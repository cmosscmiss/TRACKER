using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model class for the GameImagesDashboard control.
/// </summary>
public class GameImagesDashboardViewModel : WidgetViewModelBase
{
    #region Subclasses
    /// <summary>
    /// Available layouts for the game images dashboard.
    /// </summary>
    public enum GameImagesDashboardLayout
    {
        Horizontal,
        Vertical
    }
    #endregion

    #region Constants
    private const double DefaultThumbnailPanelWidth = 200;
    private const double MinThumbnailPanelWidth = 200;
    private const double MaxThumbnailPanelWidth = 600;

    private const double DefaultThumbnailPanelHeight = 400;
    private const double MinThumbnailPanelHeight = 200;
    private const double MaxThumbnailPanelHeight = 500;

    private const double DefaultVideoVolume = 0;
    private const double MinVideoVolume = 0;
    private const double MaxVideoVolume = 100;

    #endregion

    #region Attributes
    private readonly ImageLoadingService _imageLoadingService;
    private readonly ImageBinaryLoadingService _imageBinaryLoadingService;
    private readonly ExceptionService _exceptionService;
    private readonly DialogsService _dialogsService;
    private readonly WindowService _windowService;

    private AsyncRelayCommand? _deleteSelectedImageCommand;
    private AsyncRelayCommand? _processPreviousGameCommand;
    private AsyncRelayCommand? _processNextGameCommand;
    private AsyncRelayCommand? _openSettingsCommand;

    private bool _isImportingDrop;
    private bool _isDragActive;
    private double _thumbnailPanelWidth = DefaultThumbnailPanelWidth;
    private double _thumbnailPanelHeight = DefaultThumbnailPanelHeight;
    private double _videoVolume = DefaultVideoVolume;
    private bool _isMuted;
    private bool _isSearchStringsPanelVisible;
    private int _selectedGameLoadVersion;
    private Game? _inFlightGameLoad;
    private GameImagesDashboardLayout _selectedLayout = GameImagesDashboardLayout.Horizontal;
    private VideoDownloadQualitySettings _videoDownloadQuality = VideoDownloadQualitySettings.P1080;
    #endregion

    #region Properties
    /// <summary>
    /// True mientras se resuelve un drop de imágenes (copia local o descarga web). Gobierna el overlay
    /// "Importing image..." del dashboard. Lo conmuta <see cref="HandleImageDropAsync"/>.
    /// </summary>
    public bool IsImportingDrop
    {
        get => _isImportingDrop;
        set => SetProperty(ref _isImportingDrop, value);
    }

    /// <summary>
    /// True mientras hay una operación de drag activa sobre el dashboard. Gobierna la visibilidad de
    /// las zonas de drop (imagen seleccionada y lista), garantizando una superficie donde soltar
    /// aunque no haya imagen seleccionada ni miniaturas. Lo conmutan <see cref="Dashboard_DragEnter"/>,
    /// <see cref="Dashboard_DragLeave"/> y el fin del drop en <see cref="HandleImageDropAsync"/>.
    /// </summary>
    public bool IsDragActive
    {
        get => _isDragActive;
        set => SetProperty(ref _isDragActive, value);
    }

    /// <summary>
    /// Title shown on the left ("process &amp; previous") half: the previous game in the filtered list, or the
    /// CURRENT game when already at the list start (there the pill processes and stays on the current game).
    /// </summary>
    public string PreviousGameTitle => (GetAdjacentGame(-1) ?? SharedDataService.SelectedGame)?.Title ?? string.Empty;

    /// <summary>
    /// Title shown on the right ("process &amp; next") half: the next game, or the CURRENT game at the list end.
    /// </summary>
    public string NextGameTitle => (GetAdjacentGame(1) ?? SharedDataService.SelectedGame)?.Title ?? string.Empty;

    /// <summary>True when the selected game is at the start/end of the filtered list (no previous/next game).</summary>
    private bool IsAtListStart => GetAdjacentGame(-1) is null;
    private bool IsAtListEnd => GetAdjacentGame(1) is null;

    /// <summary>
    /// True when the selected game has more than one media file, i.e. processing is meaningful (keep the preferred
    /// one, delete the rest). With 0 or 1 media there is nothing to reduce.
    /// </summary>
    private bool HasMultipleMedia => (SharedDataService.SelectedGame?.Images.Count ?? 0) > 1;

    /// <summary>
    /// Whether the "process &amp; previous" half is enabled. It is disabled ONLY in the useless corner: at the FIRST
    /// game of the list (nowhere previous to go) AND with &lt;=1 media (nothing to process). Otherwise enabled:
    /// mid-list it still navigates to the previous game, and at the first game with &gt;1 media it processes and stays.
    /// </summary>
    public bool IsProcessPreviousEnabled => SharedDataService.SelectedGame != null && (HasMultipleMedia || !IsAtListStart);

    /// <summary>Whether the "process &amp; next" half is enabled. Disabled only at the LAST game with &lt;=1 media.</summary>
    public bool IsProcessNextEnabled => SharedDataService.SelectedGame != null && (HasMultipleMedia || !IsAtListEnd);

    /// <summary>Per-half disabled flags (inverse of the enabled ones) used to dim each half independently.</summary>
    public bool IsProcessPreviousDisabled => !IsProcessPreviousEnabled;
    public bool IsProcessNextDisabled => !IsProcessNextEnabled;

    /// <summary>
    /// Settings to be used to pre-select the image once a game is selected.
    /// </summary>
    public List<GameImageCriterion> GameImageSelectionCriteria { get; set; } = new()
    {
        new GameImageCriterion
        {
            Type = SettingsType.Image,
            CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_First_Label] ?? "1st:",
            IsActive = true,
            ID = 1
        },
        new GameImageCriterion
        {
            Type = SettingsType.Image,
            CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_Second_Label] ?? "2nd:",
            IsActive = true,
            ID = 2
        },
    };

    /// <summary>
    /// Settings to be used to process the images of the selected game.
    /// </summary>
    public List<GameImageCriterion> GameImageProcessingCriteria { get; set; } = new()
    {
        new GameImageCriterion
        {
            Type = SettingsType.Region,
            CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_Region_Label] ?? "Region:",
            IsActive = true,
            ID = 2
        },
        new GameImageCriterion
        {
            Type = SettingsType.FileNameSuffix,
            CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_Suffix_Label] ?? "Suffix:",
            IsActive = true,
            ID = 2
        },
        new GameImageCriterion
        {
            Type = SettingsType.FileName,
            CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_FileName_Label] ?? "File Name:",
            IsActive = true,
            ID = 4
        }
    };

    /// <summary>
    /// Indicates whether the dashboard is displayed in horizontal view.
    /// </summary>
    public bool IsHorizontalView
    {
        get => _selectedLayout == GameImagesDashboardLayout.Horizontal;
        set
        {
            if (value)
            {
                SelectHorizontalView();
            }
            else
            {
                SelectVerticalView();
            }
        }
    }

    /// <summary>
    /// Indicates whether the dashboard is displayed in vertical view.
    /// </summary>
    public bool IsVerticalView
    {
        get => _selectedLayout == GameImagesDashboardLayout.Vertical;
        set
        {
            if (value)
            {
                SelectVerticalView();
            }
            else
            {
                SelectHorizontalView();
            }
        }
    }

    public bool IsSearchStringsPanelVisible
    {
        get => _isSearchStringsPanelVisible;
        set
        {
            SetProperty(ref _isSearchStringsPanelVisible, value);
        }
    }

    /// <summary>
    /// Resolución de descarga de vídeo seleccionada (excluyente: 240p / 360p / 480p / 720p / 1080p, siempre con
    /// audio). Las propiedades reflejan <see cref="_videoDownloadQuality"/> para los toggles de la toolbar.
    /// </summary>
    public bool IsVideoQuality240 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P240);

    /// <summary>Excluyente: 360p (SD).</summary>
    public bool IsVideoQuality360 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P360);

    /// <summary>Excluyente: 480p (ED).</summary>
    public bool IsVideoQuality480 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P480);

    /// <summary>Excluyente: 720p (HD).</summary>
    public bool IsVideoQuality720 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P720);

    /// <summary>Excluyente: 1080p (Full HD).</summary>
    public bool IsVideoQuality1080 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P1080);

    /// <summary>
    /// Valor del grupo de layout para el <see cref="Views.ExclusiveOptionsControl"/> ("Horizontal"/"Vertical").
    /// TwoWay: el control lo fija al pulsar; el getter lo deriva de <see cref="_selectedLayout"/>.
    /// </summary>
    public string SelectedLayoutValue
    {
        get => _selectedLayout == GameImagesDashboardLayout.Horizontal ? "Horizontal" : "Vertical";
        set
        {
            GameImagesDashboardLayout layout = value == "Vertical"
                ? GameImagesDashboardLayout.Vertical
                : GameImagesDashboardLayout.Horizontal;
            if (_selectedLayout != layout)
            {
                SelectLayout(layout);
                OnPropertyChanged(nameof(SelectedLayoutValue));
            }
        }
    }

    /// <summary>
    /// Valor del grupo de calidad de vídeo para el <see cref="Views.ExclusiveOptionsControl"/> ("240".."1080",
    /// el Key del <see cref="VideoDownloadQualitySettings"/>). TwoWay.
    /// </summary>
    public string SelectedVideoQualityValue
    {
        get => _videoDownloadQuality.Key.ToString();
        set
        {
            if (int.TryParse(value, out int key)
                && Enumeration.FromKey<VideoDownloadQualitySettings>(key) is { } quality
                && !Equals(quality, _videoDownloadQuality))
            {
                SelectVideoQuality(quality);
                OnPropertyChanged(nameof(SelectedVideoQualityValue));
            }
        }
    }

    /// <summary>
    /// Whether the selected image set is a video type (Video Snap / Theme Video). Governs the visibility of the
    /// video download-quality options in the toolbar, which are irrelevant (and confusing) for image types.
    /// </summary>
    public bool IsVideoSetSelected => IsSelectedSetVideo();

    /// <summary>
    /// Volumen (0–100) de la reproducción de vídeo. 0 = silencio. Es el setting global
    /// (<see cref="GeneralSettings.VideoVolume"/>) y gobierna tanto el preview del dashboard como el vídeo de la
    /// ficha de plataforma. TwoWay desde el slider de ajustes.
    /// </summary>
    public double VideoVolume
    {
        get => _videoVolume;
        set
        {
            double normalizedValue = NormalizeVideoVolume(value);
            if (SetProperty(ref _videoVolume, normalizedValue))
            {
                OnPropertyChanged(nameof(EffectiveVideoVolume));
                OnPropertyChanged(nameof(IsSoundOff));
            }
        }
    }

    /// <summary>
    /// Silencio global de la reproducción de vídeo (setting <see cref="GeneralSettings.IsMuted"/>). Conserva el nivel
    /// de <see cref="VideoVolume"/>; al desactivarlo se recupera. TwoWay desde el botón de mute del footer.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(EffectiveVideoVolume));
                OnPropertyChanged(nameof(IsSoundOff));
            }
        }
    }

    /// <summary>
    /// Volumen efectivo (0–100) aplicado a los reproductores: 0 si está silenciado, si no el nivel de
    /// <see cref="VideoVolume"/>. Es el que consume el preview del dashboard.
    /// </summary>
    public double EffectiveVideoVolume => IsMuted ? 0 : VideoVolume;

    /// <summary>True cuando no hay sonido (silenciado o nivel 0); gobierna el glyph del botón de sonido del footer.</summary>
    public bool IsSoundOff => IsMuted || VideoVolume <= 0;

    /// <summary>Si el preview de vídeo del dashboard muestra los controles de reproducción (setting, false por defecto).</summary>
    public bool ShowVideoControls => _appSettings.GameImagesDashboardControl?.ShowVideoControls ?? false;

    /// <summary>
    /// Width of the thumbnails panel when the dashboard is displayed in vertical view.
    /// </summary>
    public double ThumbnailPanelWidth
    {
        get => _thumbnailPanelWidth;
        set
        {
            double normalizedValue = NormalizeThumbnailPanelWidth(value);
            SetProperty(ref _thumbnailPanelWidth, normalizedValue);
        }
    }

    /// <summary>
    /// Height of the thumbnails panel when the dashboard is displayed in horizontal view.
    /// </summary>
    public double ThumbnailPanelHeight
    {
        get => _thumbnailPanelHeight;
        set
        {
            double normalizedValue = NormalizeThumbnailPanelHeight(value);
            SetProperty(ref _thumbnailPanelHeight, normalizedValue);
        }
    }
    #endregion

    #region Published events
    /// <summary>
    /// Image dropped event.
    /// </summary>
    /// <param name="e">The drag event arguments.</param>
    /// <param name="droppedOnSelectedImage">Whether the image was dropped on the selected image preview.</param>
    public delegate void ImageDroppedEventHandler(DragEventArgs e, bool droppedOnSelectedImage);

    /// <summary>
    /// Raised when an image is dropped on the dashboard.
    /// </summary>
    public event ImageDroppedEventHandler? ImageDropped;

    /// <summary>
    /// Raises the image dropped event.
    /// </summary>
    /// <param name="e">The drag event arguments.</param>
    /// <param name="droppedOnSelectedImage">Whether the image was dropped on the selected image preview.</param>
    protected virtual void OnImageDropped(DragEventArgs e, bool droppedOnSelectedImage)
    {
        ImageDropped?.Invoke(e, droppedOnSelectedImage);
    }

    /// <summary>
    /// Image selection changed event.
    /// </summary>
    /// <param name="image">The selected image.</param>
    public delegate void ImageSelectionChangedEventHandler(GameImage image);

    /// <summary>
    /// Raised when the selected image changes.
    /// </summary>
    public event ImageSelectionChangedEventHandler? ImageSelectionChanged;

    /// <summary>
    /// Raises the image selection changed event.
    /// </summary>
    /// <param name="image">The selected image.</param>
    protected virtual void OnImageSelectionChanged(GameImage image)
    {
        ImageSelectionChanged?.Invoke(image);
    }
    #endregion

    #region Constructors
    /// <summary>
    /// Creates a new view model for the game images dashboard.
    /// </summary>
    /// <param name="sharedDataService">The shared application data service.</param>
    /// <param name="exceptionService">The exception handling service.</param>
    /// <param name="imageLoadingService">The image loading service.</param>
    /// <param name="appSettings">The application settings.</param>
    public GameImagesDashboardViewModel(SharedDataService sharedDataService, ExceptionService exceptionService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, DialogsService dialogsService, WindowService windowService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _exceptionService = exceptionService;
        _imageLoadingService = imageLoadingService;
        _imageBinaryLoadingService = imageBinaryLoadingService;
        _dialogsService = dialogsService;
        _windowService = windowService;

        SharedDataService.SelectedGameChanged += OnSelectedGameChanged;
        SharedDataService.SelectedGameImagesChanged += OnSelectedGameImagesChanged;
        SharedDataService.PropertyChanged += OnSharedDataServicePropertyChanged;
        SharedDataService.GamesFiltered.CollectionChanged += OnGamesFilteredChanged;
        _imageLoadingService.ImageAddedToGame += OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame += OnImageRemovedFromGame;
    }

    /// <summary>
    /// Gets the command that processes the current game (keeps the criteria-preferred media, deletes the rest and
    /// renames the kept one) and then navigates to the PREVIOUS game in the filtered list. With progress and undo.
    /// </summary>
    public AsyncRelayCommand ProcessPreviousGameCommand =>
        _processPreviousGameCommand ??= new AsyncRelayCommand(() => ProcessAndNavigateAsync(-1), () => IsProcessPreviousEnabled);

    /// <summary>
    /// Gets the command that processes the current game and then navigates to the NEXT game in the filtered list.
    /// </summary>
    public AsyncRelayCommand ProcessNextGameCommand =>
        _processNextGameCommand ??= new AsyncRelayCommand(() => ProcessAndNavigateAsync(1), () => IsProcessNextEnabled);

    // La condición de habilitación es por mitad: ver IsProcessPreviousEnabled / IsProcessNextEnabled.

    /// <summary>
    /// Processes the current game (keeps the criteria-preferred media via <see cref="PreselectGameImage"/>, deletes
    /// the rest and renames/moves the kept one) and then moves the selection to the previous/next game in the
    /// filtered list. At the list edge it processes and stays on the current game.
    /// </summary>
    /// <param name="direction">-1 for previous, +1 for next.</param>
    private async Task ProcessAndNavigateAsync(int direction)
    {
        Game? game = SharedDataService.SelectedGame;
        if (game == null)
        {
            return;
        }

        GameImage keep = PreselectGameImage();
        if (keep != null && !string.IsNullOrEmpty(keep.File))
        {
            await _imageLoadingService.ProcessGameAsync(game, keep, GameImageProcessingCriteria);
        }

        // Navigate to the previous/next game (stay put at the list edge).
        int index = SharedDataService.GamesFiltered.IndexOf(game);
        if (index >= 0)
        {
            int target = index + direction;
            if (target >= 0 && target < SharedDataService.GamesFiltered.Count)
            {
                SharedDataService.SelectedGame = SharedDataService.GamesFiltered[target];
            }
        }
    }

    /// <summary>Refreshes the process-game pill when the filtered game list changes (edges/titles may shift).</summary>
    private void OnGamesFilteredChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshProcessNavigation();
    }

    /// <summary>
    /// Returns the game at the given offset from the selected game in the filtered list, or null when there is
    /// no selected game or the offset falls outside the list (i.e. at either edge).
    /// </summary>
    /// <param name="direction">-1 for the previous game, +1 for the next game.</param>
    private Game? GetAdjacentGame(int direction)
    {
        Game? game = SharedDataService.SelectedGame;
        if (game is null)
        {
            return null;
        }

        int index = SharedDataService.GamesFiltered.IndexOf(game);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= SharedDataService.GamesFiltered.Count)
        {
            return null;
        }

        return SharedDataService.GamesFiltered[target];
    }

    /// <summary>
    /// Raises change notifications for the process-game pill (adjacent/current titles and enablement) and
    /// re-evaluates the process commands so the halves enable/disable with the current game's media count.
    /// </summary>
    private void RefreshProcessNavigation()
    {
        OnPropertyChanged(nameof(PreviousGameTitle));
        OnPropertyChanged(nameof(NextGameTitle));
        OnPropertyChanged(nameof(IsProcessPreviousEnabled));
        OnPropertyChanged(nameof(IsProcessNextEnabled));
        OnPropertyChanged(nameof(IsProcessPreviousDisabled));
        OnPropertyChanged(nameof(IsProcessNextDisabled));
        _processPreviousGameCommand?.NotifyCanExecuteChanged();
        _processNextGameCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Gets the command that deletes the currently selected media of the dashboard (with confirmation and undo).
    /// </summary>
    public AsyncRelayCommand DeleteSelectedImageCommand =>
        _deleteSelectedImageCommand ??= new AsyncRelayCommand(DeleteSelectedImageAsync, () => SharedDataService.SelectedImage != null);

    /// <summary>Abre el diálogo de settings del dashboard (criterios de preselección y de proceso).</summary>
    public AsyncRelayCommand OpenSettingsCommand => _openSettingsCommand ??= new AsyncRelayCommand(ShowSettingsAsync);

    private async Task ShowSettingsAsync()
    {
        var result = await _dialogsService.ShowDashboardSettingsAsync(
            _windowService.ActiveXamlRoot!, GameImageSelectionCriteria, GameImageProcessingCriteria);

        if (result is not { } edited)
        {
            return;
        }

        GameImageSelectionCriteria = edited.Selection;
        GameImageProcessingCriteria = edited.Processing;

        // Persistencia inmediata: vuelca a AppSettings y guarda a disco (no espera al cierre).
        _appSettings.GameImagesDashboardControl.ImageSelectionCriteria = GameImageSelectionCriteria.ToArray();
        _appSettings.GameImagesDashboardControl.ImageProcessingCriteria = GameImageProcessingCriteria.ToArray();
        App.GetService<PersistAndRestoreService>().PersistData();
    }

    /// <summary>
    /// Confirms (honoring the PromptBeforeDeleteImage setting) and deletes the selected media from disk via
    /// <see cref="ImageLoadingService.DeleteImageAsync"/>. Undoable from the activity log; the dashboard refreshes
    /// through the service events.
    /// </summary>
    private async Task DeleteSelectedImageAsync()
    {
        GameImage? image = SharedDataService.SelectedImage;
        if (image is null)
        {
            return;
        }

        string message = $"\"{image.Name}{image.FileExtension}\" will be deleted from disk. You can undo this from the activity log. Do you want to continue?";
        bool confirmed = await _dialogsService.ConfirmImageDeletionAsync(_windowService.ActiveXamlRoot!, message);
        if (!confirmed)
        {
            return;
        }

        await _imageLoadingService.DeleteImageAsync(image);
    }

    /// <summary>Re-evaluates the delete command when the shared selected media changes.</summary>
    private void OnSharedDataServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.SelectedImage) || e.PropertyName == nameof(SharedDataService.SelectedGame))
        {
            _deleteSelectedImageCommand?.NotifyCanExecuteChanged();
            _processPreviousGameCommand?.NotifyCanExecuteChanged();
            _processNextGameCommand?.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(SharedDataService.SelectedGame))
        {
            RefreshProcessNavigation();
        }
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles drag over events on the dashboard.
    /// </summary>
    /// <param name="sender">The control receiving the drag operation.</param>
    /// <param name="e">The drag event arguments.</param>
    public void GameImages_DragOver(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    /// <summary>
    /// Activa las zonas de drop cuando un drag entra en el dashboard. Se cablea en el contenedor raíz,
    /// cuyo fondo capta el drag aunque las áreas internas estén vacías, de modo que las zonas de drop
    /// aparezcan al comenzar el arrastre.
    /// </summary>
    /// <param name="sender">El contenedor que recibe el drag.</param>
    /// <param name="e">The drag event arguments.</param>
    public void Dashboard_DragEnter(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        IsDragActive = true;
    }

    /// <summary>
    /// Oculta las zonas de drop cuando el drag abandona el dashboard sin soltar.
    /// </summary>
    /// <param name="sender">El contenedor que recibe el drag.</param>
    /// <param name="e">The drag event arguments.</param>
    public void Dashboard_DragLeave(object? sender, DragEventArgs e)
    {
        IsDragActive = false;
    }

    /// <summary>
    /// Cierra las zonas de drop cuando se suelta sobre una zona muerta del dashboard (fuera de las dos
    /// zonas de drop), sin importar imagen alguna.
    /// </summary>
    /// <param name="sender">El contenedor que recibe el drop.</param>
    /// <param name="e">The drag event arguments.</param>
    public void Dashboard_Drop(object? sender, DragEventArgs e)
    {
        IsDragActive = false;
    }

    /// <summary>
    /// Handles drop events raised when an image is dropped on the selected image area.
    /// The dropped images are added to the list and the first one becomes the selected image.
    /// </summary>
    /// <param name="sender">The control that received the drop.</param>
    /// <param name="e">The drag event arguments.</param>
    public async void SelectedImage_Drop(object? sender, DragEventArgs e)
    {
        // Evita que el drop burbujee al contenedor raíz (Dashboard_Drop).
        e.Handled = true;
        try
        {
            await HandleImageDropAsync(e, droppedOnSelectedImage: true);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameImagesDashboard_AddDroppedMedia_Error] ?? "Error adding the dropped media.");
        }
    }

    /// <summary>
    /// Refreshes the dashboard when the selected game changes.
    /// If the selected image set is not ready yet, the refresh is postponed until
    /// SelectedGameImagesChanged is raised.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selected game change event arguments.</param>
    private async void OnSelectedGameChanged(object? sender, GameChangedEventArgs e)
    {
        try
        {
            await OnSelectedGameChangedCoreAsync(sender, e);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameImagesDashboard_RefreshGame_Error] ?? "Error refreshing the selected game.");
        }
    }

    private async Task OnSelectedGameChangedCoreAsync(object? sender, GameChangedEventArgs e)
    {
        if (SharedDataService.SelectedImageSet?.IsLoaded != true)
        {
            return;
        }

        await RefreshSelectedGameImagesAsync();
    }

    /// <summary>
    /// Refreshes the dashboard when the images of the selected game have changed.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selected game images change event arguments.</param>
    private async void OnSelectedGameImagesChanged(object? sender, GameImagesChangedEventArgs e)
    {
        try
        {
            await OnSelectedGameImagesChangedCoreAsync(sender, e);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameImagesDashboard_RefreshImages_Error] ?? "Error refreshing the game images.");
        }
    }

    private async Task OnSelectedGameImagesChangedCoreAsync(object? sender, GameImagesChangedEventArgs e)
    {
        // El set puede haber cambiado (cambio de tipo de medio) → refresca la visibilidad de las opciones de
        // resolución de vídeo de la toolbar, que solo aplican a los tipos de vídeo.
        OnPropertyChanged(nameof(IsVideoSetSelected));

        if (!ReferenceEquals(e.Game, SharedDataService.SelectedGame))
        {
            return;
        }

        await RefreshSelectedGameImagesAsync();
        RefreshProcessNavigation();
    }
    #endregion

    #region Methods private    
    /// <summary>
    /// Normalizes the thumbnails panel width to keep it within the supported visual range.
    /// </summary>
    /// <param name="value">The width value to normalize.</param>
    /// <returns>A valid thumbnails panel width.</returns>
    private static double NormalizeThumbnailPanelWidth(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultThumbnailPanelWidth;
        }

        return Math.Clamp(value, MinThumbnailPanelWidth, MaxThumbnailPanelWidth);
    }

    /// <summary>
    /// Normaliza el volumen de vídeo (0–100) para evitar valores inválidos procedentes de configuración o entrada externa.
    /// </summary>
    private static double NormalizeVideoVolume(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultVideoVolume;
        }

        return Math.Clamp(value, MinVideoVolume, MaxVideoVolume);
    }

    /// <summary>
    /// Normalizes the thumbnails panel height to keep it within the supported visual range.
    /// </summary>
    /// <param name="value">The height value to normalize.</param>
    /// <returns>A valid thumbnails panel height.</returns>
    private static double NormalizeThumbnailPanelHeight(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultThumbnailPanelHeight;
        }

        return Math.Clamp(value, MinThumbnailPanelHeight, MaxThumbnailPanelHeight);
    }

    /// <summary>
    /// Selects an image based on the configured preselection criteria.
    /// The criteria are applied progressively: each active setting filters
    /// the current candidate list. If a criterion produces no result,
    /// the previous candidate list is kept.
    /// </summary>
    /// <returns>The preselected game image, or a new empty image if none exists.</returns>
    private GameImage PreselectGameImage()
    {
        if (SharedDataService.GameImages.Count == 0)
        {
            return new();
        }

        List<GameImage> candidates = SharedDataService.GameImages
            .Where(image => image != null)
            .ToList();

        if (candidates.Count == 0)
        {
            return new();
        }

        foreach (GameImageCriterion criterion in GameImageSelectionCriteria)
        {
            if (!criterion.IsActive || candidates.Count == 0 || string.IsNullOrWhiteSpace(criterion.Name))
            {
                continue;
            }

            List<GameImage> filteredCandidates;

            if (criterion.Name == ImageSettings.FileDimensions.Value)
            {
                long maxDimensions = candidates
                    .Max(image => (long)image.Width * image.Height);

                filteredCandidates = candidates
                    .Where(image => (long)image.Width * image.Height == maxDimensions)
                    .ToList();
            }
            else if (criterion.Name == ImageSettings.FileSize.Value)
            {
                long maxFileSize = candidates
                    .Max(image => image.FileSize);

                filteredCandidates = candidates
                    .Where(image => image.FileSize == maxFileSize)
                    .ToList();
            }
            else
            {
                string expectedExtension = $".{criterion.Name}";

                filteredCandidates = candidates
                    .Where(image =>
                        !string.IsNullOrWhiteSpace(image.FileExtension)
                        && string.Equals(
                            image.FileExtension,
                            expectedExtension,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (filteredCandidates.Count > 0)
            {
                candidates = filteredCandidates;
            }
        }

        return candidates.FirstOrDefault()
            ?? SharedDataService.GameImages.FirstOrDefault()
            ?? new();
    }

    /// <summary>
    /// Refreshes the images displayed in the dashboard for the currently selected game.
    /// </summary>
    private async Task RefreshSelectedGameImagesAsync()
    {
        Game? selectedGame = SharedDataService.SelectedGame;

        // Coalesce redundant refreshes: on startup the same game is signalled by both
        // SelectedGameChanged and SelectedGameImagesChanged, which would otherwise decode the same
        // high-res binaries twice concurrently. Skip the duplicate while a load for this game runs.
        if (selectedGame != null && ReferenceEquals(_inFlightGameLoad, selectedGame))
        {
            return;
        }

        int loadVersion = ++_selectedGameLoadVersion;

        SharedDataService.GameImages.Clear();

        if (selectedGame == null)
        {
            _inFlightGameLoad = null;
            SharedDataService.SelectedImage = new();
            return;
        }

        _inFlightGameLoad = selectedGame;

        try
        {
            await _imageBinaryLoadingService.LoadGameHighResImageBinariesAsync(selectedGame);

            if (loadVersion != _selectedGameLoadVersion)
            {
                return;
            }

            foreach (GameImage gameImage in selectedGame.Images)
            {
                SharedDataService.GameImages.Add(gameImage);
            }

            if (SharedDataService.GameImages.Count == 0)
            {
                SharedDataService.SelectedImage = new();
                return;
            }

            SharedDataService.SelectedImage = SelectInitialImage(selectedGame);
        }
        catch (Exception ex)
        {
            if (loadVersion == _selectedGameLoadVersion)
            {
                SharedDataService.SelectedImage = new();
                _exceptionService.Handle(ex, ex.Message);
            }
        }
        finally
        {
            // Only the load that still owns the in-flight slot clears it, so a newer load for a
            // different game (which already took ownership) is not cancelled out.
            if (ReferenceEquals(_inFlightGameLoad, selectedGame))
            {
                _inFlightGameLoad = null;
            }
        }
    }

    /// <summary>
    /// Selects the initial image for the selected game.
    /// Keeps the current SelectedImage if it already belongs to the selected game.
    /// Otherwise, preselects an image based on the configured criteria.
    /// </summary>
    /// <param name="selectedGame">The currently selected game.</param>
    /// <returns>The image that should become the selected image.</returns>
    private GameImage SelectInitialImage(Game selectedGame)
    {
        if (SharedDataService.GameImages.Count == 0)
        {
            return new();
        }

        if (SharedDataService.SelectedImage != null && selectedGame.Images.IndexOf(SharedDataService.SelectedImage) != -1)
        {
            return SharedDataService.SelectedImage;
        }

        if (SharedDataService.GameImages.Count == 1)
        {
            return SharedDataService.GameImages.FirstOrDefault() ?? new();
        }

        return PreselectGameImage();
    }

    /// <summary>
    /// Handles an image drop by adding the dropped images to the dashboard list and,
    /// when the drop occurred on the selected image preview, selecting the first one.
    /// </summary>
    /// <param name="e">The drag event arguments carrying the dropped data.</param>
    /// <param name="droppedOnSelectedImage">Whether the images were dropped on the selected image preview.</param>
    private async Task HandleImageDropAsync(DragEventArgs e, bool droppedOnSelectedImage)
    {
        // The data is read asynchronously, so the drop operation must be kept alive with a deferral.
        var deferral = e.GetDeferral();
        IsDragActive = false;
        IsImportingDrop = true;

        try
        {
            List<GameImage> droppedImages = await ResolveDroppedImagesAsync(e.DataView);

            foreach (GameImage image in droppedImages)
            {
                if (!SharedDataService.GameImages.Contains(image))
                {
                    SharedDataService.GameImages.Add(image);
                }
            }

            if (droppedOnSelectedImage && droppedImages.FirstOrDefault() is GameImage imageToSelect)
            {
                SelectImage(imageToSelect);
            }

            OnImageDropped(e, droppedOnSelectedImage);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, ex.Message);
        }
        finally
        {
            IsImportingDrop = false;
            deferral.Complete();
        }
    }

    /// <summary>
    /// Resolves the images carried by a drop. Three drag sources are supported: files dragged from a
    /// Windows folder (Explorer), which arrive as storage items; the local image gallery, which provides
    /// file paths as text; and the in-app web browser, which provides the dragged image as a URL (text,
    /// web link or HTML). Files and browser images are copied into the selected game image folder so they
    /// persist in the application.
    /// </summary>
    /// <param name="data">The data package carried by the drop.</param>
    /// <returns>The resolved game images, preserving the dropped order.</returns>
    private async Task<List<GameImage>> ResolveDroppedImagesAsync(DataPackageView data)
    {
        List<GameImage> resolvedImages = new();

        // 0) Storage items: real media files dragged from a Windows folder. Their paths flow through the
        // same local-file pipeline as the gallery, which copies them into the selected type's folder. This is
        // the only source for video types (videos are dragged from Explorer, not downloaded from the browser).
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            foreach (IStorageItem item in await data.GetStorageItemsAsync())
            {
                if (item is StorageFile file && IsDroppableFile(file.Path))
                {
                    await AddResolvedImageAsync(resolvedImages, file.Path);
                }
            }
        }

        // The text/web/HTML sources below feed the image pipeline (gallery paths, browser URLs). They never
        // apply to video types, so they are skipped to avoid downloading a web image into a video folder.
        if (IsSelectedSetVideo())
        {
            return resolvedImages;
        }

        // 1) Text: either local file paths (gallery) or an image URL (web browser). Skipped when storage
        // items already resolved, so an Explorer drop is not processed twice.
        if (resolvedImages.Count == 0 && data.Contains(StandardDataFormats.Text))
        {
            string droppedText = await data.GetTextAsync();

            foreach (string token in droppedText.Split(','))
            {
                await AddResolvedImageAsync(resolvedImages, token.Trim());
            }
        }

        // 2) Web/application link exposed by the browser when no usable text is available.
        if (resolvedImages.Count == 0)
        {
            foreach (string url in await GetDroppedWebUrlsAsync(data))
            {
                await AddResolvedImageAsync(resolvedImages, url);
            }
        }

        // 3) HTML fragment: use the source of the first <img> tag.
        if (resolvedImages.Count == 0 && data.Contains(StandardDataFormats.Html))
        {
            string html = await data.GetHtmlFormatAsync();
            string? imageSource = Utilities.GetImageTagSource(html).FirstOrDefault();

            await AddResolvedImageAsync(resolvedImages, imageSource);
        }

        return resolvedImages;
    }

    /// <summary>
    /// Whether the given path points to an image the application supports, based on its extension.
    /// </summary>
    private bool IsImageFile(string path) =>
        _appSettings.LaunchBox.AllowedImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Whether the given path points to a video container the application supports, based on its extension.
    /// </summary>
    private bool IsVideoFile(string path) =>
        _appSettings.LaunchBox.AllowedVideoExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Whether the currently selected image set is a video type (Video Snap / Theme Video). Determines whether
    /// a drop targets the video pipeline (copy a video into the type's folder) or the image one.
    /// </summary>
    private bool IsSelectedSetVideo()
    {
        var type = SharedDataService.SelectedImageSet?.Type;
        return type != null && (Enums.MediaType.IsVideo(type.Key) || Enums.MediaType.IsPlatformVideo(type.Key));
    }

    /// <summary>
    /// Whether a dropped file is acceptable for the currently selected set: a video when the set is a video
    /// type, an image otherwise. Keeps an image out of a video folder (and vice versa).
    /// </summary>
    private bool IsDroppableFile(string path) => IsSelectedSetVideo() ? IsVideoFile(path) : IsImageFile(path);

    /// <summary>
    /// Resolves a single dropped token (a local file path or a web URL) and, when it produces an
    /// image, appends it to the provided list avoiding duplicates.
    /// </summary>
    /// <param name="resolvedImages">The list being built.</param>
    /// <param name="token">The dropped token to resolve.</param>
    private async Task AddResolvedImageAsync(List<GameImage> resolvedImages, string? token)
    {
        GameImage? image = await ResolveDroppedTokenAsync(token);

        if (image != null && !resolvedImages.Contains(image))
        {
            resolvedImages.Add(image);
        }
    }

    /// <summary>
    /// Resolves a single dropped token into a game image. HTTP(S) URLs are downloaded from the
    /// browser into the selected game image folder; everything else is treated as a local file path
    /// and matched against the images already known to the application.
    /// </summary>
    /// <param name="token">The dropped token to resolve.</param>
    /// <returns>The resolved image, or <c>null</c> when the token cannot be resolved.</returns>
    private async Task<GameImage?> ResolveDroppedTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (IsHttpUrl(token))
        {
            return await DownloadWebImageAsync(token);
        }

        return await ResolveLocalImageAsync(token);
    }

    /// <summary>
    /// Resolves a local file path (dragged from the image gallery) into a game image by copying the
    /// file into the selected game image folder, persisting it and registering it on the game model.
    /// If there is no game/image set selected to persist into, the already loaded instance is reused.
    /// </summary>
    /// <param name="filePath">The local file path to resolve.</param>
    /// <returns>The persisted or reused image, or <c>null</c> when the file does not exist.</returns>
    private async Task<GameImage?> ResolveLocalImageAsync(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return null;
        }

        Game? game = SharedDataService.SelectedGame;
        PlatformImageSet? imageSet = SharedDataService.SelectedImageSet;

        // Without a game/image set there is no folder to persist into; reuse the existing instance.
        if (game is null || imageSet is null || string.IsNullOrWhiteSpace(imageSet.FolderPath))
        {
            return FindImageByFile(SharedDataService.GameImages, filePath)
                ?? FindImageByFile(game?.AllImages ?? Enumerable.Empty<GameImage>(), filePath);
        }

        return await _imageLoadingService.AddImageFromFileToGameAsync(filePath, game, imageSet);
    }

    /// <summary>
    /// Downloads an image dragged from the in-app browser into the selected game image folder,
    /// registers it on the game model and returns it. Returns <c>null</c> when there is no
    /// game/image set selected to determine the destination folder.
    /// </summary>
    /// <param name="url">The image URL provided by the browser drag operation.</param>
    /// <returns>The downloaded game image, or <c>null</c> when it could not be created.</returns>
    private async Task<GameImage?> DownloadWebImageAsync(string url)
    {
        Game? game = SharedDataService.SelectedGame;
        PlatformImageSet? imageSet = SharedDataService.SelectedImageSet;

        if (game is null || imageSet is null || string.IsNullOrWhiteSpace(imageSet.FolderPath))
        {
            return null;
        }

        return await _imageLoadingService.AddImageFromUrlToGameAsync(url, game, imageSet);
    }

    /// <summary>
    /// Extracts the web and application links carried by a drop, if any.
    /// </summary>
    /// <param name="data">The data package carried by the drop.</param>
    /// <returns>The links found, as absolute URL strings.</returns>
    private static async Task<List<string>> GetDroppedWebUrlsAsync(DataPackageView data)
    {
        List<string> urls = new();

        if (data.Contains(StandardDataFormats.WebLink))
        {
            Uri webLink = await data.GetWebLinkAsync();

            if (webLink != null)
            {
                urls.Add(webLink.ToString());
            }
        }

        if (data.Contains(StandardDataFormats.ApplicationLink))
        {
            Uri applicationLink = await data.GetApplicationLinkAsync();

            if (applicationLink != null)
            {
                urls.Add(applicationLink.ToString());
            }
        }

        return urls;
    }

    /// <summary>
    /// Determines whether the provided value is an absolute HTTP or HTTPS URL.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><c>true</c> when the value is an HTTP(S) URL; otherwise, <c>false</c>.</returns>
    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Finds the first image whose file path matches the provided one, ignoring case.
    /// </summary>
    /// <param name="images">The images to search.</param>
    /// <param name="filePath">The file path to match.</param>
    /// <returns>The matching image, or <c>null</c> when none is found.</returns>
    private static GameImage? FindImageByFile(IEnumerable<GameImage> images, string filePath)
    {
        return images.FirstOrDefault(image =>
            string.Equals(image?.File, filePath, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectLayout(GameImagesDashboardLayout layout)
    {
        if (_selectedLayout == layout)
        {
            return;
        }

        _selectedLayout = layout;

        OnPropertyChanged(nameof(IsHorizontalView));
        OnPropertyChanged(nameof(IsVerticalView));
    }
    #endregion

    #region Methods public
    /// <summary>
    /// Selects the provided game image and notifies subscribers.
    /// </summary>
    /// <param name="image">The image to select.</param>
    public void SelectImage(GameImage image)
    {
        if (image == null)
        {
            return;
        }

        if (ReferenceEquals(SharedDataService.SelectedImage, image))
        {
            return;
        }

        SharedDataService.SelectedImage = image;
        OnImageSelectionChanged(image);
    }

    /// <summary>
    /// Switches the dashboard to horizontal view.
    /// </summary>
    public void SelectHorizontalView()
    {
        SelectLayout(GameImagesDashboardLayout.Horizontal);
    }

    /// <summary>
    /// Switches the dashboard to vertical view.
    /// </summary>
    public void SelectVerticalView()
    {
        SelectLayout(GameImagesDashboardLayout.Vertical);
    }

    /// <summary>Selecciona la resolución de descarga 240p.</summary>
    public void SelectVideoQuality240() => SelectVideoQuality(VideoDownloadQualitySettings.P240);

    /// <summary>Selecciona la resolución de descarga 360p.</summary>
    public void SelectVideoQuality360() => SelectVideoQuality(VideoDownloadQualitySettings.P360);

    /// <summary>Selecciona la resolución de descarga 480p.</summary>
    public void SelectVideoQuality480() => SelectVideoQuality(VideoDownloadQualitySettings.P480);

    /// <summary>Selecciona la resolución de descarga 720p.</summary>
    public void SelectVideoQuality720() => SelectVideoQuality(VideoDownloadQualitySettings.P720);

    /// <summary>Selecciona la resolución de descarga 1080p.</summary>
    public void SelectVideoQuality1080() => SelectVideoQuality(VideoDownloadQualitySettings.P1080);

    private void SelectVideoQuality(VideoDownloadQualitySettings quality)
    {
        if (Equals(_videoDownloadQuality, quality))
        {
            return;
        }

        _videoDownloadQuality = quality;

        // Se vuelca de inmediato en settings: la descarga (ImageLoadingService) lee este valor en vivo, y
        // SaveConfig solo se ejecuta al cerrar la app, así que sin esto la selección no surtiría efecto hasta
        // el siguiente arranque.
        if (_appSettings.GameImagesDashboardControl is not null)
        {
            _appSettings.GameImagesDashboardControl.VideoDownloadQuality = quality;
        }

        OnPropertyChanged(nameof(IsVideoQuality240));
        OnPropertyChanged(nameof(IsVideoQuality360));
        OnPropertyChanged(nameof(IsVideoQuality480));
        OnPropertyChanged(nameof(IsVideoQuality720));
        OnPropertyChanged(nameof(IsVideoQuality1080));
    }

    /// <summary>
    /// Loads the dashboard configuration from application settings.
    /// </summary>
    public override void LoadConfig()
    {
        _isSearchStringsPanelVisible = _appSettings.GameImagesDashboardControl?.IsSearchStringsPanelVisible ?? true;
        bool isHorizontalView = _appSettings.GameImagesDashboardControl?.IsHorizontalView ?? true;
        _videoDownloadQuality = _appSettings.GameImagesDashboardControl?.VideoDownloadQuality ?? VideoDownloadQualitySettings.P1080;

        // Criterios de preselección y de procesado persistidos (si los hay); si no, se conservan los del
        // inicializador por defecto.
        GameImageCriterion[]? savedSelectionCriteria = _appSettings.GameImagesDashboardControl?.ImageSelectionCriteria;
        if (savedSelectionCriteria is { Length: > 0 })
        {
            GameImageSelectionCriteria = savedSelectionCriteria.ToList();
        }

        GameImageCriterion[]? savedProcessingCriteria = _appSettings.GameImagesDashboardControl?.ImageProcessingCriteria;
        if (savedProcessingCriteria is { Length: > 0 })
        {
            GameImageProcessingCriteria = savedProcessingCriteria.ToList();
        }

        ThumbnailPanelWidth = _appSettings.GameImagesDashboardControl?.Width ?? DefaultThumbnailPanelWidth;
        ThumbnailPanelHeight = _appSettings.GameImagesDashboardControl?.Height ?? DefaultThumbnailPanelHeight;
        VideoVolume = _appSettings.General?.VideoVolume ?? DefaultVideoVolume;
        IsMuted = _appSettings.General?.IsMuted ?? false;

        if (isHorizontalView)
        {
            SelectHorizontalView();
        }
        else
        {
            SelectVerticalView();
        }

        OnPropertyChanged(nameof(IsHorizontalView));
        OnPropertyChanged(nameof(IsVerticalView));
        OnPropertyChanged(nameof(IsSearchStringsPanelVisible));
        OnPropertyChanged(nameof(IsVideoQuality240));
        OnPropertyChanged(nameof(IsVideoQuality360));
        OnPropertyChanged(nameof(IsVideoQuality480));
        OnPropertyChanged(nameof(IsVideoQuality720));
        OnPropertyChanged(nameof(IsVideoQuality1080));
        OnPropertyChanged(nameof(SelectedLayoutValue));
        OnPropertyChanged(nameof(SelectedVideoQualityValue));
        OnPropertyChanged(nameof(IsVideoSetSelected));
        OnPropertyChanged(nameof(VideoVolume));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(EffectiveVideoVolume));
        OnPropertyChanged(nameof(IsSoundOff));
        OnPropertyChanged(nameof(ShowVideoControls));
    }

    /// <summary>
    /// Saves the dashboard configuration into application settings.
    /// </summary>
    public override void SaveConfig()
    {
        _appSettings.GameImagesDashboardControl.IsHorizontalView = IsHorizontalView;
        _appSettings.GameImagesDashboardControl.IsSearchStringsPanelVisible = IsSearchStringsPanelVisible;
        _appSettings.GameImagesDashboardControl.Width = ThumbnailPanelWidth;
        _appSettings.GameImagesDashboardControl.Height = ThumbnailPanelHeight;
        _appSettings.GameImagesDashboardControl.VideoDownloadQuality = _videoDownloadQuality;
        _appSettings.GameImagesDashboardControl.ImageSelectionCriteria = GameImageSelectionCriteria.ToArray();
        _appSettings.GameImagesDashboardControl.ImageProcessingCriteria = GameImageProcessingCriteria.ToArray();
        _appSettings.General.VideoVolume = VideoVolume;
        _appSettings.General.IsMuted = IsMuted;
    }

    /// <summary>
    /// Releases resources and unsubscribes from external events.
    /// </summary>
    public override void Dispose()
    {
        SharedDataService.SelectedGameChanged -= OnSelectedGameChanged;
        SharedDataService.SelectedGameImagesChanged -= OnSelectedGameImagesChanged;
        _imageLoadingService.ImageAddedToGame -= OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame -= OnImageRemovedFromGame;
    }
    #endregion

    /// <summary>
    /// Alta de una imagen en el juego seleccionado (p. ej. import): carga su binario alta-res (las importadas
    /// llegan sin binario) y la añade a la lista del dashboard si no está. Simétrico a
    /// <see cref="OnImageRemovedFromGame"/>; el <c>Contains</c> evita duplicar el alta del drag&amp;drop, que
    /// además añade la imagen a <see cref="SharedDataService.GameImages"/> por su cuenta.
    /// </summary>
    private async void OnImageAddedToGame(Game game, GameImage image)
    {
        try
        {
            await OnImageAddedToGameCoreAsync(game, image);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameImagesDashboard_LoadAddedImage_Error] ?? "Error loading the added game image.");
        }
    }

    private async Task OnImageAddedToGameCoreAsync(Game game, GameImage image)
    {
        if (image is null || !ReferenceEquals(game, SharedDataService.SelectedGame))
        {
            return;
        }

        if (ContainsByFile(SharedDataService.GameImages, image.File))
        {
            return;
        }

        await _imageBinaryLoadingService.LoadGameImageBinaryAsync(image, ImageResolutionSettings.High);

        // Dedup by file path, not by reference: undo can re-emit the same file as a different GameImage instance
        // (per-game copy vs canonical set image); adding both would show a duplicate thumbnail.
        if (!ReferenceEquals(game, SharedDataService.SelectedGame) || ContainsByFile(SharedDataService.GameImages, image.File))
        {
            return;
        }

        SharedDataService.GameImages.Add(image);

        // If there was no valid selection (e.g. after deleting the last media the viewer was left with an empty
        // GameImage), select the just-(re)added one so the StandAlone viewer shows it. This covers undo of the
        // last deletion. A normal add with a valid selection leaves it untouched.
        GameImage? current = SharedDataService.SelectedImage;
        if (current == null || string.IsNullOrEmpty(current.File) || !ContainsByFile(SharedDataService.GameImages, current.File))
        {
            SelectImage(image);
        }

        // El nº de medios del juego pudo cruzar el umbral (>1) que habilita la pastilla de procesar.
        RefreshProcessNavigation();
    }

    /// <summary>True if the collection already holds an image with the given file path.</summary>
    private static bool ContainsByFile(System.Collections.ObjectModel.ObservableCollection<GameImage> images, string file)
    {
        for (int i = 0; i < images.Count; i++)
        {
            if (images[i].File == file)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Baja de una imagen del juego (p. ej. al deshacer un alta): la quita de la lista del dashboard y, si era
    /// la seleccionada, pasa a la siguiente disponible (o ninguna).
    /// </summary>
    private void OnImageRemovedFromGame(Game game, GameImage image)
    {
        if (image is null)
        {
            return;
        }

        // Match by file path, not by reference: the same file may be a different GameImage instance in the
        // dashboard list than the one carried by the event (canonical set image vs per-game AllImages copy).
        string file = image.File;
        GameImage? inList = null;
        for (int i = 0; i < SharedDataService.GameImages.Count; i++)
        {
            if (SharedDataService.GameImages[i].File == file)
            {
                inList = SharedDataService.GameImages[i];
                break;
            }
        }

        if (inList == null)
        {
            return;
        }

        bool wasSelected = SharedDataService.SelectedImage != null && SharedDataService.SelectedImage.File == file;
        _ = SharedDataService.GameImages.Remove(inList);

        if (wasSelected)
        {
            GameImage? next = SharedDataService.GameImages.FirstOrDefault();
            if (next != null)
            {
                SelectImage(next);
            }
            else
            {
                // Last media removed: reset to an empty GameImage so the StandAlone viewer empties. The dashboard
                // uses an empty instance (not null) for the "no image" state (see RefreshSelectedGameImagesAsync).
                SharedDataService.SelectedImage = new();
            }
        }

        // El nº de medios del juego pudo bajar de >1 a 1/0, lo que deshabilita la pastilla de procesar.
        RefreshProcessNavigation();
    }
}