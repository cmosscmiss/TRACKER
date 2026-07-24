using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Specialized view model for displaying the images of the currently selected game
/// inside an ImageGrid control.
/// 
/// This view model listens to changes in the selected game and refreshes the image
/// gallery only when the widget is currently visible in a valid slot.
/// 
/// If the selected game changes while the widget is not visible, the refresh is marked
/// as pending and executed later when the widget becomes visible again.
/// </summary>
public class ImageGridGameViewModel : ImageGridViewModel
{
    #region Attributes
    private bool _pendingGameSelectionRefresh;

    private readonly IStatisticsService _statisticsService;
    private readonly ExceptionService _exceptionService;
    private readonly ImageMatchingService _imageMatchingService;

    // Pastillas inferiores (solo en modo galería de juego): "valor del juego seleccionado / valor de la plataforma".
    // Las de imagen (ImagePills) las produce el servicio ya formateadas; la de cobertura (CoverageText) aún se
    // compone aquí (centralización aplazada). Ambas viven en la base ImageGridViewModel para bindearlas con x:Bind.

    /// <summary>Plataforma cuya media de cobertura de favoritos ya se calculó (cache); evita recalcular al cambiar de juego.</summary>
    private Platform? _coveragePlatform;
    // Coberturas como ratio 0..1 (el servicio las formatea a "%"): media de favoritos de la plataforma y del juego.
    private double _platformAverageCoverageRatio;
    private double _gameCoverageRatio;
    #endregion

    #region Constructors
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageGridGameViewModel"/> class.
    /// 
    /// The constructor sets the view mode as a game images view, subscribes to its own
    /// property changes to detect slot visibility changes, and requests an initial refresh
    /// if a selected game already exists when the view model is created.
    /// </summary>
    /// <param name="sharedDataService">Service containing the shared application selection state.</param>
    /// <param name="progressService">Service used to report loading progress.</param>
    /// <param name="imageLoadingService">Service used to load game images.</param>
    /// <param name="appSettings">Application settings injected through options.</param>
    public ImageGridGameViewModel(SharedDataService sharedDataService, ProgressService progressService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, ImageMatchingService imageMatchingService, IStatisticsService statisticsService, DialogsService dialogsService, WindowService windowService, ExceptionService exceptionService, IOptions<AppSettings> appSettings) : base(sharedDataService, progressService, imageLoadingService, imageBinaryLoadingService, dialogsService, windowService, appSettings)
    {
        _statisticsService = statisticsService;
        _exceptionService = exceptionService;
        _imageMatchingService = imageMatchingService;
        _imageLoadingService.ImageAddedToGame += OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame += OnImageRemovedFromGame;

        IsGameImagesView = true;
        CanDeleteImages = true;

        // The gallery no longer has a manual "(Re)load images" button: the selected game's images are
        // discovered up front (without binaries) and each binary is decoded lazily as it scrolls into view.
        LazyLoadBinariesOnScroll = true;

        PropertyChanged += ImageGridGameViewModel_PropertyChanged;

        if (SharedDataService.SelectedGame != null)
        {
            _ = RequestSelectedGameRefreshAsync();
        }
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles property changes raised by this view model.
    /// 
    /// The method listens specifically to <see cref="WidgetViewModelBase.SlotIndex"/> changes.
    /// When the widget becomes visible and a game refresh is pending, the gallery is refreshed.
    /// </summary>
    /// <param name="sender">The view model that raised the event.</param>
    /// <param name="e">The property change event data.</param>
    private async void ImageGridGameViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            await ImageGridGameViewModel_PropertyChangedCoreAsync(sender, e);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageGridGame_RefreshGallery_Error] ?? "Error refreshing the game gallery.");
        }
    }

    private async Task ImageGridGameViewModel_PropertyChangedCoreAsync(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SlotIndex))
        {
            return;
        }

        if (SlotIndex >= 0 && _pendingGameSelectionRefresh)
        {
            await RefreshSelectedGameAsync();
        }
    }

    /// <summary>
    /// Handles changes in the shared application data.
    /// 
    /// When the selected game changes, the method requests a gallery refresh. The refresh
    /// is executed immediately if the widget is visible, or deferred if the widget is not
    /// currently assigned to a visible slot.
    /// </summary>
    /// <param name="sender">The shared data service that raised the event.</param>
    /// <param name="e">The property change event data.</param>
    protected override async void SharedDataService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            await SharedDataService_PropertyChangedCoreAsync(sender, e);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageGridGame_SelectionChange_Error] ?? "Error handling the selection change.");
        }
    }

    private async Task SharedDataService_PropertyChangedCoreAsync(object? sender, PropertyChangedEventArgs e)
    {
        base.SharedDataService_PropertyChanged(sender, e);

        // Al cambiar de plataforma, la media de cobertura cacheada deja de valer: invalidar y recalcular.
        if (e.PropertyName == nameof(SharedDataService.SelectedPlatform))
        {
            _coveragePlatform = null;
            RefreshPlatformAverageCoverage();
            return;
        }

        if (e.PropertyName != nameof(SharedDataService.SelectedGame))
        {
            return;
        }

        await RequestSelectedGameRefreshAsync();
    }

    /// <summary>
    /// Appends to the gallery an image that has just been added to the currently selected game,
    /// so newly added images (from the dashboard drop or the web browser) appear without waiting
    /// for a full gallery refresh.
    /// </summary>
    /// <param name="game">The game the image was added to.</param>
    /// <param name="image">The image that was added.</param>
    private void OnImageAddedToGame(Game game, GameImage image)
    {
        if (!ReferenceEquals(game, SharedDataService.SelectedGame))
        {
            return;
        }

        // Dedup by file path, not by reference: undo can re-emit the same file as a different GameImage instance
        // (per-game copy vs canonical set image); adding both would duplicate the thumbnail.
        if (image is null || Images.Any(i => i.File == image.File))
        {
            return;
        }

        // Mirror the position the image takes in AllImages so the gallery keeps the same
        // type ordering. Fall back to appending if the index cannot be mapped to the gallery.
        int index = game.AllImages.IndexOf(image);

        if (index >= 0 && index <= Images.Count)
        {
            InsertImage(index, image);
        }
        else
        {
            AddImage(image);
        }

        // Las pastillas del juego cambian (nº/tamaño/tipos). Si el alta puede afectar a la cobertura de
        // favoritos, recalcula también la media de la plataforma (cara, en segundo plano).
        RefreshGameStats();
        if (CoverageAffectedByAdd(game, image))
        {
            _coveragePlatform = null;
            RefreshPlatformAverageCoverage();
        }
    }

    /// <summary>
    /// Baja de una imagen de un juego (p. ej. al deshacer un alta): la quita de la galería si pertenece al
    /// juego seleccionado, simétrico a <see cref="OnImageAddedToGame"/>.
    /// </summary>
    private void OnImageRemovedFromGame(Game game, GameImage image)
    {
        if (!ReferenceEquals(game, SharedDataService.SelectedGame))
        {
            return;
        }

        if (image is null || !Images.Contains(image))
        {
            return;
        }

        RemoveImage(image);

        RefreshGameStats();
        if (CoverageAffectedByAdd(game, image))
        {
            _coveragePlatform = null;
            RefreshPlatformAverageCoverage();
        }
    }
    #endregion

    #region Pills (game / platform stats — moved from GameStatsControl)
    /// <summary>
    /// Reconstruye las pastillas del juego seleccionado frente a los totales de la plataforma: nº de imágenes,
    /// tipos distintos, tamaño en disco y cobertura de favoritos del juego. La media de cobertura de la
    /// plataforma la rellena (asíncrona, cacheada) <see cref="RefreshPlatformAverageCoverage"/>.
    /// </summary>
    private void RefreshGameStats()
    {
        Game? game = SharedDataService.SelectedGame;
        if (game == null)
        {
            ClearGameStats();
            return;
        }

        // Pastillas de imagen (nº / tipos / tamaño): compuestas y formateadas por el servicio (juego / plataforma).
        // Con un juego seleccionado siempre hay plataforma seleccionada, de ahí el '!'.
        ImagePills = _statisticsService.GetGameImagePills(game, SharedDataService.SelectedPlatform!);

        // Pastilla de cobertura (cobertura de favoritos del juego; su centralización está aplazada).
        IReadOnlyCollection<MediaType> favourites = _appSettings.ImageTypeControl.FavouriteImageTypes ?? Array.Empty<MediaType>();
        Stat coverage = _statisticsService.GetGameImageStatistics(game, favourites).Items[2];
        _gameCoverageRatio = coverage.Total > 0 ? (double)coverage.Value / coverage.Total : 0;
        UpdateCoverageText();
    }

    /// <summary>
    /// Media de cobertura de favoritos de TODA la plataforma (cómputo caro: recorre ficheros), en segundo plano y
    /// cacheada por plataforma. Al cambiar de juego dentro de la misma plataforma es un cache hit (solo refresca el
    /// texto). Mientras calcula, la pastilla muestra la última media conocida (0% inicialmente).
    /// </summary>
    private async void RefreshPlatformAverageCoverage()
    {
        Platform? platform = SharedDataService.SelectedPlatform;
        IReadOnlyCollection<MediaType> favourites = _appSettings.ImageTypeControl.FavouriteImageTypes ?? Array.Empty<MediaType>();

        if (platform == null || favourites.Count == 0 || platform.Games.Count == 0)
        {
            _coveragePlatform = platform;
            _platformAverageCoverageRatio = 0;
            UpdateCoverageText();
            return;
        }

        if (ReferenceEquals(platform, _coveragePlatform))
        {
            UpdateCoverageText();   // cache hit
            return;
        }

        double average;
        try
        {
            average = await Task.Run(() => _statisticsService.GetPlatformAverageFavouriteCoverage(platform, favourites));
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Error computing platform average coverage.");
            return;   // cómputo de solo lectura; si fallara, mantenemos la última media
        }

        // Descartar si la plataforma cambió mientras se calculaba.
        if (!ReferenceEquals(platform, SharedDataService.SelectedPlatform))
            return;

        _coveragePlatform = platform;
        _platformAverageCoverageRatio = average;
        UpdateCoverageText();
    }

    /// <summary>Compone el texto de la pastilla de cobertura: juego / media de plataforma (formato del servicio).</summary>
    private void UpdateCoverageText() =>
        CoverageText = $"{_statisticsService.FormatPercent(_gameCoverageRatio)} / {_statisticsService.FormatPercent(_platformAverageCoverageRatio)}";

    /// <summary>Restablece las pastillas (sin juego seleccionado).</summary>
    private void ClearGameStats()
    {
        _gameCoverageRatio = 0;
        ImagePills = null;   // x:Bind corta el path con seguridad
        UpdateCoverageText();
    }

    /// <summary>
    /// True si añadir <paramref name="image"/> a <paramref name="game"/> puede cambiar su cobertura de favoritos:
    /// el tipo es favorito y es la PRIMERA imagen de ese tipo del juego (la recién añadida ya está en AllImages).
    /// </summary>
    private bool CoverageAffectedByAdd(Game game, GameImage image)
    {
        if (game?.AllImages == null || image?.Type == null)
            return false;

        IReadOnlyList<MediaType> favourites = _appSettings.ImageTypeControl.FavouriteImageTypes ?? Array.Empty<MediaType>();
        if (!favourites.Any(t => t.Key == image.Type.Key))
            return false;

        return game.AllImages.Count(i => i.Type != null && i.Type.Key == image.Type.Key) <= 1;
    }
    #endregion

    #region Methods
    /// <summary>
    /// Requests a refresh of the currently selected game.
    /// 
    /// If the widget is visible, the refresh is executed immediately. Otherwise, the refresh
    /// remains pending until the widget becomes visible.
    /// </summary>
    /// <returns>A task representing the asynchronous refresh request.</returns>
    private async Task RequestSelectedGameRefreshAsync()
    {
        _pendingGameSelectionRefresh = true;

        if (SlotIndex >= 0)
        {
            await RefreshSelectedGameAsync();
        }
    }

    /// <summary>
    /// Handles the selected game change and updates the image gallery accordingly.
    /// 
    /// If the widget is not visible, the refresh is deferred. If no game is selected,
    /// the gallery is cleared. If the selected game does not yet have images loaded,
    /// the method triggers the game image loading process.
    /// </summary>
    /// <returns>A task representing the asynchronous selected game update.</returns>
    public async Task OnGameSelectionChangedAsync()
    {
        if (SlotIndex < 0)
        {
            _pendingGameSelectionRefresh = true;
            return;
        }

        var selectedGame = SharedDataService.SelectedGame;

        if (selectedGame == null)
        {
            Images.Clear();
            SelectedImage = null;
            ClearGameStats();
            return;
        }

        // Discover the images first when they have not been matched yet, then populate the gallery in a
        // single step. Doing the "Count == 0" check before SetGalleryAsync (rather than after) avoids a
        // race with other widgets that also discover this game's images: the image-stats widget fills
        // game.AllImages while SetGalleryAsync yields on its internal delay, which used to flip the guard
        // to false and leave the gallery showing the (then empty) list. Both branches now end on a
        // SetGalleryAsync over the final, populated list.
        if (selectedGame.AllImages.Count == 0)
        {
            await LoadGameImagesAsync();
        }
        else
        {
            await SetGalleryAsync(selectedGame.AllImages);
        }

        // Pastillas inferiores (movidas desde GameStatsControl): datos del juego ya están disponibles.
        RefreshGameStats();
        RefreshPlatformAverageCoverage();   // cacheada por plataforma; solo recalcula al cambiar de plataforma
    }
    #endregion

    /// <summary>
    /// Restores the persisted gallery configuration: the aspect ratio, the resolution and the item size.
    /// Called by the control once the application settings have been restored from disk.
    /// </summary>
    public override void LoadConfig()
    {
        AppSettings.ImageGridControlSettings config = _appSettings.ImageGridControl;
        if (config == null) { return; }

        ApplyAspectRatio(config.AspectRatio?.Value);
        ApplyResolution(config.Resolution?.Value);

        // Ignore a non-positive persisted size (a bad 0 could have been saved by an earlier session); keep the
        // view model's default so the gallery self-heals instead of restoring a collapsed thumbnail size.
        if (config.ItemSize > 0) { Width = config.ItemSize; }
    }

    /// <summary>
    /// Saves the gallery configuration back into the application settings: the current aspect ratio,
    /// resolution and item size.
    /// </summary>
    public override void SaveConfig()
    {
        AppSettings.ImageGridControlSettings config = _appSettings.ImageGridControl;

        config.AspectRatio = Enumeration.FromValue<AspectRatioSettings>(SelectedAspectRatio.Name) ?? config.AspectRatio;
        config.Resolution = Enumeration.FromValue<ImageResolutionSettings>(SelectedImageResolution.Name) ?? config.Resolution;
        config.ItemSize = Width;
    }

    #region Methods private
    /// <summary>
    /// Refreshes the gallery using the currently selected game.
    /// 
    /// The method clears the pending refresh flag before delegating the actual update
    /// to <see cref="OnGameSelectionChangedAsync"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous refresh operation.</returns>
    private async Task RefreshSelectedGameAsync()
    {
        if (SlotIndex < 0)
        {
            return;
        }

        _pendingGameSelectionRefresh = false;

        await OnGameSelectionChangedAsync();
    }

    /// <summary>
    /// Discovers the images of the currently selected game and pushes them to the gallery.
    ///
    /// Only the image list is built here; no binaries are decoded up front. Each binary is
    /// loaded lazily as its container scrolls into view (see <see cref="ImageGridViewModel.LazyLoadBinariesOnScroll"/>).
    /// The selected game is captured before the asynchronous gallery update to avoid applying
    /// stale results if the selection changes while the gallery is being refreshed.
    /// </summary>
    /// <returns>A task representing the asynchronous image loading operation.</returns>
    protected virtual async Task LoadGameImagesAsync()
    {
        var selectedPlatform = SharedDataService.SelectedPlatform;
        var selectedGame = SharedDataService.SelectedGame;

        if (selectedGame == null || selectedPlatform == null)
        {
            return;
        }

        _imageMatchingService.MatchGameImages(selectedPlatform, selectedGame);

        if (SharedDataService.SelectedGame == selectedGame && SlotIndex >= 0)
        {
            await SetGalleryAsync(selectedGame.AllImages);
        }
        else
        {
            _pendingGameSelectionRefresh = true;
        }
    }

    /// <summary>
    /// Releases resources associated with the view model.
    /// 
    /// The method detaches event handlers registered by this class and then delegates
    /// the remaining cleanup to the base class.
    /// </summary>
    public override void Dispose()
    {
        PropertyChanged -= ImageGridGameViewModel_PropertyChanged;
        SharedDataService.PropertyChanged -= SharedDataService_PropertyChanged;
        _imageLoadingService.ImageAddedToGame -= OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame -= OnImageRemovedFromGame;

        base.Dispose();
    }
    #endregion
}