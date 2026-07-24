using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.Dialogs;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MM4LB.Controls.ViewModels;

public class ImageCollectionImportViewModel : ImageGridViewModel
{
    #region Attributes
    // _windowService and _dialogsService are inherited (protected) from ImageGridViewModel.
    private readonly IStatisticsService _statisticsService;
    private readonly ExceptionService _exceptionService;

    private RelayCommand? _importImagesCommand;
    private RelayCommand? _selectFolderCommand;

    private bool _isImagesView = true;
    private bool _isGamesView;
    private FolderImportStats? _statisticsImages;

    // Tipo de medio del set seleccionado en el momento de cargar la carpeta. Sirve para detectar el desajuste
    // imagen/vídeo: si luego se cambia a un tipo de la otra clase, el import deja de poder ejecutarse.
    private MediaType? _loadedMediaType;
    #endregion

    #region Properties
    public GamesAuditInGalleryViewModel GaViewModel { get; protected set; }

    /// <summary>
    /// Folder-import pills (extension / dimensions / matched images / matched games) produced by the service,
    /// ready to display. Null until a folder is loaded (x:Bind cuts the path).
    /// </summary>
    public FolderImportStats? StatisticsImages
    {
        get => _statisticsImages;
        private set => SetProperty(ref _statisticsImages, value);
    }

    /// <summary>
    /// Whether the folder images gallery is shown. Both views are independent and can be visible at the
    /// same time (the control only guarantees that at least one stays visible).
    /// </summary>
    public bool IsImagesView
    {
        get => _isImagesView;
        set => SetProperty(ref _isImagesView, value);
    }

    /// <summary>
    /// Whether the embedded games audit view is shown.
    /// </summary>
    public bool IsGamesView
    {
        get => _isGamesView;
        set => SetProperty(ref _isGamesView, value);
    }

    /// <summary>
    /// Whether a source image folder has already been selected. Gates both the Import action and the
    /// visibility of the statistics bar, since neither makes sense until the user has picked a folder.
    /// Computed from <see cref="ImageGridViewModel.SelectedFolder"/>; change notifications are raised
    /// manually in <see cref="OnSelectFolder"/>.
    /// </summary>
    public bool HasSelectedFolder => !string.IsNullOrEmpty(SelectedFolder);

    /// <summary>
    /// Whether the currently selected image set is a video media type (vs an image one). Drives both what the
    /// folder picker loads and whether the loaded media can be imported into the selected set.
    /// </summary>
    private bool IsCurrentSetVideo =>
        SharedDataService.SelectedImageSet?.Type is { } type && MediaType.IsVideo(type.Key);
    #endregion

    #region Published events
    /// <summary>
    /// Delegate event handler when the selected game changes.
    /// </summary>
    public delegate void GameSelectionChangedEventHandler(Game game);
    public event GameSelectionChangedEventHandler? GameSelectionChanged;
    protected virtual void OnGameSelectionChanged(Game game) => GameSelectionChanged?.Invoke(game);

    /// <summary>
    /// Delegate event handler to tell the subscriber to import the matched images in the collection.
    /// </summary>
    public delegate void ImportImagesRequestedEventHandler(List<Game> games, bool keepCollectionImages);
    public event ImportImagesRequestedEventHandler? ImportImagesRequested;
    protected virtual void OnImportImagesRequested(List<Game> games, bool keepCollectionImages) => ImportImagesRequested?.Invoke(games, keepCollectionImages);
    #endregion

    #region Commands
    /// <summary>
    /// Imports the matched images for the selected image type.
    /// </summary>
    public override RelayCommand ImportImagesCommand => _importImagesCommand ??= new RelayCommand(OnImportImagesAsync, CanExecuteImportImagesCommand);

    protected virtual async void OnImportImagesAsync()
    {
        try
        {
            await ImportImagesCoreAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageCollectionImport_Import_Error] ?? "Error importing the matched images.");
            // Re-habilita los botones aunque haya fallado (si no, quedaban deshabilitados para siempre).
            RaiseCanExecuteCommands(false);
        }
    }

    private async Task ImportImagesCoreAsync()
    {
        RaiseCanExecuteCommands(true);

        // Con el dashboard de REGIONES activo, se pregunta primero a qué región importar (favoritas + "No region").
        // El destino es la subcarpeta de esa región (o la raíz del set para "No region"). Con el dashboard estándar
        // no hay pregunta y se importa a la raíz (destino null).
        string? destinationFolder = null;
        if (SharedDataService.ActiveDashboardMode == DashboardMode.Region)
        {
            var options = (_appSettings.GameImagesRegionDashboardControl.FavouriteRegions ?? System.Array.Empty<ImageRegion>())
                .Take(3)
                .Append(ImageRegion.NoRegion)
                .ToList();

            ImageRegion? region = await _dialogsService.ShowSelectRegionAsync(_windowService.ActiveXamlRoot!, options);
            if (region is null)
            {
                RaiseCanExecuteCommands(false);
                return; // cancelado
            }

            if (!string.IsNullOrEmpty(region.Value))
            {
                destinationFolder = System.IO.Path.Combine(SharedDataService.SelectedImageSet!.FolderPath, region.Value);
            }
        }

        // Diálogo "Keep"/"Discard": devuelve KeepCollectionImages (true = Keep; false = Discard, reemplaza las
        // existentes) o null si se canceló. El origen siempre se copia (la opción de mover se eliminó).
        bool? keepCollectionImages = await _dialogsService.ShowImportImagesAsync(_windowService.ActiveXamlRoot!);
        if (keepCollectionImages is bool keep)
        {
            bool discardExisting = !keep;

            await _imageLoadingService.ImportMatchedImagesAsync(
                GaViewModel.GamesCollection, SharedDataService.SelectedImageSet!, discardExisting, destinationFolder);

            OnImportImagesRequested(GaViewModel.GamesCollection, keep);

            // Tras importar, vacía la colección de imágenes de origen y el emparejado: esas imágenes ya están
            // en la colección del juego, así que el control vuelve a su estado inicial (sin carpeta cargada).
            await SetGalleryAsync(new List<GameImage>());
            GaViewModel.ClearGameImages();
            SelectedFolder = null;
            _loadedMediaType = null;
            OnPropertyChanged(nameof(HasSelectedFolder));
            StatisticsImages = GetImageStatistics();
        }
        RaiseCanExecuteCommands(false);
    }

    // Disabled by default; enabled only once the import's own MatchImages produced at least one match.
    // (Uses MatchedImagesCount, set solely by the import's MatchImages, not the shared game.Images which the
    // main image-loading flow also mutates on platform/game changes.)
    // Además, la clase del medio cargado (imagen/vídeo) debe coincidir con la del tipo seleccionado: no se puede
    // importar vídeo en un tipo de imágenes ni viceversa (p. ej. si se cambia el tipo tras cargar la carpeta).
    protected virtual bool CanExecuteImportImagesCommand()
    {
        bool loadedIsVideo = _loadedMediaType != null && MediaType.IsVideo(_loadedMediaType.Key);
        return !_isLoadingInProgress
            && GaViewModel.MatchedImagesCount > 0
            && loadedIsVideo == IsCurrentSetVideo
            // Debe haber un dashboard visible (estándar o regiones) al que importar; si no, se deshabilita.
            && SharedDataService.ActiveDashboardMode != DashboardMode.None;
    }

    /// <summary>
    /// Matches the loaded folder images against the current platform's game collection and refreshes the stats.
    /// There is no longer a manual "Match images" command: this runs automatically when a folder is loaded
    /// (see <see cref="OnSelectFolder"/>) and on every platform change while images are loaded
    /// (see <see cref="SharedDataService_PropertyChanged"/>).
    /// </summary>
    private void MatchLoadedImages()
    {
        GaViewModel.MatchImages(Images.ToList(), SelectedFolder!);
        StatisticsImages = GetImageStatistics();

        // Matching is exactly when the Import command's availability can change: enabled iff there is at least
        // one match (a game with images), disabled otherwise.
        ImportImagesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Selecting a folder from the file system.
    /// </summary>
    public override RelayCommand SelectFolderCommand => _selectFolderCommand ??= new RelayCommand(OnSelectFolder, CanExecuteSelectFolderCommand);

    private async void OnSelectFolder()
    {
        try
        {
            await SelectFolderCoreAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageCollectionImport_SelectFolder_Error] ?? "Error selecting the folder.");
            RaiseCanExecuteCommands(false);
        }
    }

    private async Task SelectFolderCoreAsync()
    {
        FolderPicker folderPicker = new();
        folderPicker.FileTypeFilter.Add("*");

        // A FolderPicker must be associated with the owning window's HWND in WinUI 3.
        IntPtr hwnd = WindowNative.GetWindowHandle(_windowService.ActiveWindow);
        InitializeWithWindow.Initialize(folderPicker, hwnd);

        StorageFolder folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null || folder.Path == SelectedFolder) { return; }

        RaiseCanExecuteCommands(true);

        // Selecting a new source folder invalidates any previous match against the game collection.
        GaViewModel.ClearGameImages();
        SelectedFolder = folder.Path;
        OnPropertyChanged(nameof(HasSelectedFolder));

        // Load the folder media following the selected set's type: images for an image type, videos for a video
        // type (only that class of files is scanned). Remember which class was loaded for the import gate.
        MediaType? setType = SharedDataService.SelectedImageSet?.Type;
        _loadedMediaType = setType;
        List<GameImage> images = await _imageLoadingService.LoadFolderMediaAsync(folder.Path, setType!);
        await SetGalleryAsync(images);

        // Match the just-loaded images against the collection automatically (no manual command anymore).
        MatchLoadedImages();
        RaiseCanExecuteCommands(false);
    }

    protected virtual bool CanExecuteSelectFolderCommand() => !_isLoadingInProgress;
    #endregion

    #region Constructor
    public ImageCollectionImportViewModel(SharedDataService sharedDataService, ProgressService progressService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, WindowService windowService, DialogsService dialogsService, IStatisticsService statisticsService, ExceptionService exceptionService, IOptions<AppSettings> appSettings) : base(sharedDataService, progressService, imageLoadingService, imageBinaryLoadingService, dialogsService, windowService, appSettings)
    {
        _statisticsService = statisticsService;
        _exceptionService = exceptionService;

        IsFolderView = true;

        // This gallery decodes image binaries lazily as they scroll into view (the imported folder can
        // hold thousands of images), instead of eagerly loading every binary up front.
        LazyLoadBinariesOnScroll = true;

        GaViewModel = new(sharedDataService, progressService, statisticsService, appSettings);

        // Subscribing to events
        GaViewModel.GameSelectionChanged += GaViewModel_GameSelectionChanged;

        // GaViewModel subscribed to SharedDataService.PropertyChanged in its own constructor (just above), so on
        // a platform change its handler rebuilds AND clears its games (ClearGameImages). Re-register OUR handler
        // last so our re-match runs AFTER that clear; otherwise GaViewModel.SetGames would wipe the match.
        SharedDataService.PropertyChanged -= SharedDataService_PropertyChanged;
        SharedDataService.PropertyChanged += SharedDataService_PropertyChanged;

        // Al cambiar el tipo de medio seleccionado, reevalúa si el medio ya cargado se puede importar (imagen
        // vs vídeo): cambiar a un tipo de la otra clase deshabilita el import.
        SharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// A game has been selected in the list of games, so we try to set as selected image the one that matches the game selected.
    /// </summary>
    /// <param name="e"></param>
    private void GaViewModel_GameSelectionChanged(Game game)
    {
        if (GaViewModel.SelectedGame?.Images.Count > 0) { SelectedImage = game.Images.First(); }
        OnGameSelectionChanged(game);
    }

    /// <summary>
    /// The selected media type changed: the import is only valid when the loaded media is of the same class
    /// (image vs video) as the new type, so re-evaluate the Import command's availability.
    /// </summary>
    private void OnSelectedImageSetChanged(object? sender, SharedDataService.ImageSetChangedEventArgs e)
    {
        ImportImagesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The selected image changes.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public override void IgControl_OnImageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        base.IgControl_OnImageSelectionChanged(sender, e);
        if (e.AddedItems.Count > 0 && e.AddedItems.First() is GameImage selectedImage)
        {
            GaViewModel.SelectedGame = GaViewModel.GamesCollection.Find(x => x.Images.Contains(selectedImage));
        }
    }

    /// <summary>
    /// Checking changes on the selected platform or game.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    protected override void SharedDataService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.SharedDataService_PropertyChanged(sender, e);
        switch (e.PropertyName)
        {
            case "SelectedGame":
                GaViewModel.SelectedGame = GaViewModel.GamesCollection.Find(x => x.Equals(SharedDataService.SelectedGame!));
                break;
            case "SelectedPlatform":
                if (SlotIndex >= 0)
                {
                    // GaViewModel already rebuilt its games for the new platform (its handler runs before ours).
                    // Re-match the already-loaded folder images against them; if none loaded, just refresh stats.
                    RaiseCanExecuteCommands(true);
                    if (Images.Count > 0) { MatchLoadedImages(); }
                    else { StatisticsImages = GetImageStatistics(); }
                    RaiseCanExecuteCommands(false);
                }
                break;
            case nameof(SharedDataService.ActiveDashboardMode):
                // Cambió el dashboard visible: el import se habilita/deshabilita según haya uno visible.
                ImportImagesCommand.NotifyCanExecuteChanged();
                break;
        }
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Calculates the statistics related to the images loaded from the folder.
    /// </summary>
    /// <returns>A FolderImportStats instance</returns>
    private FolderImportStats GetImageStatistics() => _statisticsService.GetFolderImportStatistics(
        Images, GaViewModel.MatchedImagesCount, GaViewModel.MatchedGamesCount, GaViewModel.GamesCollection.Count);

    /// <summary>
    /// Checks the different conditions for executing the commands and raises the NotifyCanExecuteChanged event for each of the them.
    /// </summary>
    protected override void RaiseCanExecuteCommands(bool isLoadingInProgress)
    {
        base.RaiseCanExecuteCommands(isLoadingInProgress);
        ImportImagesCommand.NotifyCanExecuteChanged();
        SelectFolderCommand.NotifyCanExecuteChanged();
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Restores the persisted configuration: the folder gallery's aspect ratio, resolution and item size,
    /// which views (images and/or games) are active, and the image-match filters of the embedded games
    /// audit. Called by the control once the application settings have been restored from disk.
    /// </summary>
    public override void LoadConfig()
    {
        AppSettings.ImageCollectionImportControlSettings config = _appSettings.ImageCollectionImportControl;
        if (config == null) { return; }

        // Folder images gallery view settings.
        ApplyAspectRatio(config.AspectRatio?.Value);
        ApplyResolution(config.Resolution?.Value);

        // Ignore a non-positive persisted size (a bad 0 could have been saved by an earlier session); keep the
        // view model's default so the gallery self-heals instead of restoring a collapsed thumbnail size.
        if (config.ItemSize > 0) { Width = config.ItemSize; }

        // Active view(s).
        IsImagesView = config.ImagesView;
        IsGamesView = config.GamesView;

        // Image-match filters of the embedded games audit.
        GaViewModel.ActiveCountFilters.Images.Missing = config.MissingImages;
        GaViewModel.ActiveCountFilters.Images.OneImage = config.OneImage;
        GaViewModel.ActiveCountFilters.Images.MoreThanOneImage = config.MoreThanOneImage;
        GaViewModel.FilterGamesCommand.Execute(null);
    }

    /// <summary>
    /// Saves the control's configuration into the application settings.
    /// </summary>
    public override void SaveConfig()
    {
        AppSettings.ImageCollectionImportControlSettings config = _appSettings.ImageCollectionImportControl;

        config.AspectRatio = Enumeration.FromValue<AspectRatioSettings>(SelectedAspectRatio.Name) ?? config.AspectRatio;
        config.Resolution = Enumeration.FromValue<ImageResolutionSettings>(SelectedImageResolution.Name) ?? config.Resolution;
        config.ItemSize = Width;
        config.ImagesView = IsImagesView;
        config.GamesView = IsGamesView;
        config.MissingImages = GaViewModel.ActiveCountFilters.Images.Missing;
        config.OneImage = GaViewModel.ActiveCountFilters.Images.OneImage;
        config.MoreThanOneImage = GaViewModel.ActiveCountFilters.Images.MoreThanOneImage;
    }

    /// <summary>
    /// Libera recursos: desuscribe del view model de juegos embebido y delega en la base.
    /// </summary>
    public override void Dispose()
    {
        GaViewModel.GameSelectionChanged -= GaViewModel_GameSelectionChanged;
        SharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
        base.Dispose();
    }
    #endregion
}
