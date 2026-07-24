using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.UI.Controls;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model for the ImageAudit control.
/// </summary>
public class ImageAuditViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly ProgressService _progressService;
    private readonly FileSystemService _fileSystemService;
    private readonly ImageBinariesCacheService _imageBinariesCacheService;
    private readonly ImageLoadingService _imageLoadingService;
    private readonly IStatisticsService _statisticsService;
    private readonly ThemeService _themeService;
    private readonly WindowService _windowService;
    private readonly DialogsService _dialogsService;
    private readonly ExceptionService _exceptionService;

    // Gráfica "Image set characteristics" (página 2 del FlipView): 4 columnas apiladas del TIPO seleccionado.
    // Las dos primeras siempre son Extensión / Dimensiones; las dos últimas dependen del tipo: Región / Sin región
    // para imágenes, o Calidad / rango de Duración para vídeos. Cada columna apila su valor más frecuente (moda) +
    // "Others" sumando siempre el total de imágenes. Dimensiones: solo las ya decodificadas; el resto en "Others".
    // Las dimensiones se leen de forma perezosa al mostrarse la página (EnsureImageDimensionsLoadedAsync).
    private IEnumerable<ISeries> _imageCharacteristicsSeries = Array.Empty<ISeries>();
    private IEnumerable<ICartesianAxis> _imageCharacteristicsXAxes = Array.Empty<ICartesianAxis>();
    private IEnumerable<ICartesianAxis> _imageCharacteristicsYAxes = Array.Empty<ICartesianAxis>();
    private bool _hasImageCharacteristicsData;
    private bool _loadingDimensions;   // evita lanzar dos lecturas de dimensiones a la vez
    private bool _characteristicsPageVisible;

    private RelayCommand? _deleteOrphanImagesCommand;
    private RelayCommand? _filterImagesCommand;
    private RelayCommand? _getImageDimensionsCommand;

    /// <summary>
    /// Coalesces the burst of per-image binary loads that happen while scrolling the gallery into a
    /// single statistics refresh, so the dimension stats update progressively without recomputing on
    /// every decoded image.
    /// </summary>
    private readonly DispatcherQueueTimer _statsRefreshTimer;

    private string _columnLastOrderedBy;
    private DataGridSortDirection? _columnLastOrderedDirection;
    private bool _isEnabled;
    private bool _isGridView = true;
    private bool _isLoaded;
    private bool _pendingRefresh;
    private bool _showRegionStats = true;
    private bool _isVideoSet;
    private ImageAuditStats? _statisticsImages;
    #endregion


    #region Properties (Observable)
    /// <summary>
    /// The source collection of images.
    /// </summary>
    public ObservableCollection<GameImage> ImagesCollection { get; protected set; } = new();

    /// <summary>
    /// The collection of images filtered.
    /// </summary>
    public ObservableRangeCollection<GameImage> ImagesCollectionFiltered { get; set; } = new();

    /// <summary>
    /// Statistics of the images of the collection.
    /// </summary>
    public ImageAuditStats? StatisticsImages
    {
        get => _statisticsImages;
        set => SetProperty(ref _statisticsImages, value);
    }

    /// <summary>
    /// Whether the region statistics pills (Region / No region) apply to the selected media type. Videos have no
    /// region (they live under Videos\{Platform}\ with no region subfolder), so those pills are hidden for them and
    /// the remaining pills (Extension / Dimensions) spread to fill the row.
    /// </summary>
    public bool ShowRegionStats
    {
        get => _showRegionStats;
        private set => SetProperty(ref _showRegionStats, value);
    }

    /// <summary>
    /// Whether the selected media type is a video. Complement of <see cref="ShowRegionStats"/>: when true the
    /// audit shows the video-specific pills/columns (Quality, Duration) and hides the region ones. Updated by
    /// <see cref="UpdateRegionStatsVisibility"/>.
    /// </summary>
    public bool IsVideoSet
    {
        get => _isVideoSet;
        private set => SetProperty(ref _isVideoSet, value);
    }
    #endregion


    #region Properties (image-characteristics chart data)
    /// <summary>Columnas apiladas (Extensión / Dimensiones / Región / Sin región) con el reparto de cada atributo de las imágenes del tipo seleccionado.</summary>
    public IEnumerable<ISeries> ImageCharacteristicsSeries
    {
        get => _imageCharacteristicsSeries;
        private set => SetProperty(ref _imageCharacteristicsSeries, value);
    }

    /// <summary>Eje X: valores (nº de imágenes).</summary>
    public IEnumerable<ICartesianAxis> ImageCharacteristicsXAxes
    {
        get => _imageCharacteristicsXAxes;
        private set => SetProperty(ref _imageCharacteristicsXAxes, value);
    }

    /// <summary>Eje Y: 4 categorías, cada etiqueta es el valor más repetido (moda) de su columna.</summary>
    public IEnumerable<ICartesianAxis> ImageCharacteristicsYAxes
    {
        get => _imageCharacteristicsYAxes;
        private set => SetProperty(ref _imageCharacteristicsYAxes, value);
    }

    /// <summary>True cuando hay imágenes en el set seleccionado para dibujar la gráfica de características.</summary>
    public bool HasImageCharacteristicsData
    {
        get => _hasImageCharacteristicsData;
        private set => SetProperty(ref _hasImageCharacteristicsData, value);
    }

    /// <summary>
    /// Si la página de "Image set characteristics" (página 2 del FlipView) está visible ahora mismo. El control la
    /// mantiene sincronizada. Al pasar a visible se leen las dimensiones de forma perezosa; estando ya visible, un
    /// cambio de set (ver <see cref="SetImages"/>) también las lee, para que la gráfica se rellene sin tener que
    /// salir y volver a entrar en la página.
    /// </summary>
    public bool CharacteristicsPageVisible
    {
        get => _characteristicsPageVisible;
        set
        {
            if (SetProperty(ref _characteristicsPageVisible, value) && value)
                _ = EnsureImageDimensionsLoadedAsync();
        }
    }
    #endregion


    #region Properties
    /// <summary>
    /// Status of the filters by game image count.
    /// </summary>
    public Filters ActiveCountFilters { get; protected set; } = new();

    /// <summary>
    /// View model to control the image grid component inside the image audit component.
    /// </summary>
    public ImageGridViewModel IgViewModel { get; protected set; }

    /// <summary>
    /// Indicates whether the images are shown in the image grid (gallery) view.
    /// </summary>
    public bool IsGridView
    {
        get => _isGridView;
        set { if (value) { SelectGridView(); } else { SelectListView(); } }
    }

    /// <summary>
    /// Indicates whether the images are shown in the data grid (list) view.
    /// </summary>
    public bool IsListView
    {
        get => !_isGridView;
        set { if (value) { SelectListView(); } else { SelectGridView(); } }
    }

    /// <summary>Vista activa como cadena, para el <c>ExclusiveOptionsControl</c> (TwoWay).</summary>
    public string SelectedViewValue
    {
        get => _isGridView ? "Grid" : "List";
        set { if (value == "Grid") { SelectGridView(); } else { SelectListView(); } }
    }
    #endregion


    #region Published events
    /// <summary>
    /// Delegate event handler when the selected image changes.
    /// </summary>
    public delegate void ImageSelectionChangedEventHandler(GameImage image);
    public event ImageSelectionChangedEventHandler? ImageSelectionChanged;
    protected virtual void OnImageSelectionChanged(GameImage image) => ImageSelectionChanged?.Invoke(image);
    #endregion


    #region Commands
    /// <summary>
    /// Deleting the orphan images.
    /// </summary>
    public RelayCommand DeleteOrphanImagesCommand => _deleteOrphanImagesCommand ??= new RelayCommand(OnDeleteOrphanClickedAsync, CanExecuteDeleteOrphanImagesCommand);

    protected async void OnDeleteOrphanClickedAsync()
    {
        try
        {
            await DeleteOrphanImagesCoreAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_DeleteOrphanImages_Error] ?? "Error deleting orphan images.");
            // Operación bloqueante: si algo lanzó a mitad, desbloqueamos la UI (FinishBlockingOperation solo
            // pone IsUIEnabled=true; es inocuo si no se llegó a bloquear).
            _progressService.FinishBlockingOperation();
        }
    }

    private async Task DeleteOrphanImagesCoreAsync()
    {
        Platform? platform = SharedDataService.SelectedPlatform;
        PlatformImageSet? set = platform?.SelectedImageSet;
        List<GameImage> orphanImages = set?.Images.FindAll(x => x.LinkedGames.Count == 0) ?? new List<GameImage>();

        // Nada que borrar: salir sin molestar al usuario (el comando no debería estar habilitado en este caso).
        if (orphanImages.Count == 0)
            return;

        // Confirmación antes de borrar. La operación crea backups y es deshacible desde el ACTIVITY LOG, pero
        // afecta a ficheros en disco, así que pedimos confirmación explícita.
        string message = $"{orphanImages.Count} orphan {set!.Type.Value} image(s) of \"{platform!.Name}\" will be deleted from disk. You can undo this from the activity log. Do you want to continue?";
        bool confirmed = await _dialogsService.ConfirmAsync(_windowService.ActiveXamlRoot!, "Delete orphan images", message, "Delete", "Cancel");
        if (!confirmed)
            return;

        ProgressNotifier progressNotifier = _progressService.StartBlockingOperation(false);

        // Registro para el undo: imagen + ruta de su backup (solo las que realmente se borraron).
        var deleted = new List<(GameImage image, string backupPath)>();

        foreach (GameImage image in orphanImages)
        {
            set.RemoveImage(image);
            RemoveImage(image);
            string? backupPath = await _fileSystemService.DeleteImageFileAsync(image);
            if (backupPath != null)
                deleted.Add((image, backupPath));
        }

        // Una sola notificación tras el lote: las vistas agregadas (totales del widget de stats) recalculan
        // el total de plataforma a partir de los sets ya actualizados (ImagesCount / SizeOnDiskKb).
        if (orphanImages.Count > 0)
            _imageLoadingService.NotifyPlatformImagesChanged();
        progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_OrphanFilesDeleted_Progress] ?? "{0}  |  {1} {2} orphan media files deleted", platform.Name, orphanImages.Count, set.Type.Value);

        // Undo: restaura cada fichero desde su backup y vuelve a registrar la imagen en el set y en el grid.
        if (deleted.Count > 0)
        {
            progressNotifier.UndoNeedsBackup = true;
            progressNotifier.UndoAction = async () =>
            {
                foreach (var (image, backupPath) in deleted)
                {
                    await _fileSystemService.RestoreImageFileAsync(backupPath, image.File);
                    set.AddImage(image);
                    AddImage(image);
                }
                _imageLoadingService.NotifyPlatformImagesChanged();
            };
        }

        progressNotifier.FinishOperation();
        _progressService.ProgressNotifier.Report(progressNotifier);
        _progressService.FinishBlockingOperation();
    }

    protected virtual bool CanExecuteDeleteOrphanImagesCommand() => ImagesCollection.Any(x => x.LinkedGames.Count == 0);

    /// <summary>
    /// Filtering the images.
    /// </summary>
    public RelayCommand FilterImagesCommand => _filterImagesCommand ??= new RelayCommand(OnFilterImages);

    protected void OnFilterImages()
    {
        GameImage? selectedImage = SharedDataService.SelectedImage;
        SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection);
        SharedDataService.SelectedImage = selectedImage;
    }

    /// <summary>
    /// Retrieves the dimensions of all the images in the collection.
    /// </summary>
    public RelayCommand GetImageDimensionsCommand => _getImageDimensionsCommand ??= new RelayCommand(OnGetImageDimensions, CanExecuteGetImageDimensionsCommand);

    protected async void OnGetImageDimensions()
    {
        try
        {
            await GetImageDimensionsCoreAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_RetrieveDimensions_Error] ?? "Error retrieving image dimensions.");
            RaiseCanExecuteCommands();
        }
    }

    private async Task GetImageDimensionsCoreAsync()
    {
        if (ImagesCollection.Count > 0)
        {
            ProgressNotifier progressNotifier = _progressService.StartOperation();
            string imageType = ImagesCollection.First().Type?.Value ?? string.Empty;
            string platformName = SharedDataService.SelectedPlatform?.Name ?? string.Empty;

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // The service reads the dimensions (in bounded parallel) and reports progress as a 0-100
            // percentage; surface that on the progress bar/console.
            Progress<int> progress = new(percent =>
            {
                progressNotifier.Progress = percent;
                progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_RetrievingDimensions_Progress] ?? "{2}  |  Retrieving dimensions of {0} {1} media files", ImagesCollection.Count, imageType, platformName);
                _progressService.ProgressNotifier.Report(progressNotifier);
            });

            int fallbackCount = await _fileSystemService.LoadImageDimensionsAsync(ImagesCollection.ToList(), progress);

            stopwatch.Stop();
            StatisticsImages = GetImageStatistics();
            BuildImageCharacteristicsChart();   // ya hay dimensiones: refresca esa columna
            progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_DimensionsRetrieved_Progress] ?? "{0}  |  {1} {2} media files' dimensions retrieved in {3} ms ({4} slow fallback)", platformName, ImagesCollection.Count, imageType, stopwatch.ElapsedMilliseconds, fallbackCount);
            progressNotifier.FinishOperation();
            _progressService.ProgressNotifier.Report(progressNotifier);
            _progressService.FinishOperation();
        }
        RaiseCanExecuteCommands();
    }

    protected virtual bool CanExecuteGetImageDimensionsCommand() => ImagesCollection.Any(x => x.Width == 0 || x.Height == 0);
    #endregion


    #region Constructors
    public ImageAuditViewModel(SharedDataService sharedDataService, FileSystemService fileSystemService, ProgressService progressService, ImageBinariesCacheService imageBinariesCacheService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, IStatisticsService statisticsService, ThemeService themeService, WindowService windowService, DialogsService dialogsService, ExceptionService exceptionService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        _fileSystemService = fileSystemService;
        _imageBinariesCacheService = imageBinariesCacheService;
        _progressService = progressService;
        _imageLoadingService = imageLoadingService;
        _statisticsService = statisticsService;
        _themeService = themeService;
        _windowService = windowService;
        _dialogsService = dialogsService;
        _exceptionService = exceptionService;

        IgViewModel = new(sharedDataService, progressService, imageLoadingService, imageBinaryLoadingService, dialogsService, windowService, appSettings)
        {
            // The audit grid no longer has a manual "(Re)load images" button: decode each image binary
            // lazily as its container scrolls into view, the same way the import gallery does.
            LazyLoadBinariesOnScroll = true,
            // Allow deleting the selected media from the audit grid (even when its game is not the selected one:
            // DeleteImageAsync unlinks the image from all its LinkedGames).
            CanDeleteImages = true,
        };

        _columnLastOrderedBy = "FileName";

        // Coalesce the binary-load bursts into ~4 statistics refreshes per second. The view model is
        // created on the UI thread, so the timer runs on it and StatisticsImages can be set directly.
        _statsRefreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _statsRefreshTimer.Interval = TimeSpan.FromMilliseconds(250);
        _statsRefreshTimer.IsRepeating = false;
        _statsRefreshTimer.Tick += OnStatsRefreshTimerTick;

        // Subscribing to events
        IgViewModel.ImageSelectionChanged += OnGridImageSelectionChanged;
        IgViewModel.ImageBinaryLoaded += OnGalleryImageBinaryLoaded;
        SharedDataService.SelectedImageChanged += OnSelectedImageChanged;
        SharedDataService.SelectedGameImagesChanged += OnSelectedGameImagesChanged;
        SharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
        _fileSystemService.ImageDimensionsChanged += OnImageDimensionsChanged;
        _imageLoadingService.ImageAddedToGame += OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame += OnImageRemovedFromGame;
        _themeService.ThemeChanged += OnThemeChanged;
        PropertyChanged += ImageAuditViewModel_PropertyChanged;

        // An image set may already be selected by the time the widget is created (typically on
        // startup), in which case no SelectedGameImagesChanged notification will arrive. Stage the
        // images now so they show as soon as the widget becomes active (SlotIndex >= 0).
        if (SharedDataService.SelectedImageSet != null)
        {
            SetImages(new ObservableCollection<GameImage>(SharedDataService.SelectedImageSet.Images));
        }

        BuildImageCharacteristicsChart();
    }
    #endregion


    #region Subscribed events
    /// <summary>
    /// Inserta en la galería del audit una imagen recién añadida a un juego (drag&amp;drop, descarga web o
    /// menú/clic del WebView) cuando pertenece al image set (tipo) que el audit está mostrando, para que
    /// aparezca sin recargar — igual que ya hace la galería del juego (ImageGridGameViewModel).
    /// </summary>
    private void OnImageAddedToGame(Game game, GameImage image)
    {
        if (image is null || ImagesCollection.Contains(image))
        {
            return;
        }

        // La imagen ya se metió en su image set en el servicio (imageSet.AddImage). Solo la mostramos si ese
        // set es el que el audit tiene en pantalla; si se añadió a otro tipo, no debe aparecer aquí.
        PlatformImageSet? currentSet = SharedDataService.SelectedPlatform?.SelectedImageSet;
        if (currentSet is null || !currentSet.Images.Contains(image))
        {
            return;
        }

        AddImage(image);

        // Colócala en su posición ordenada (no al final): re-aplica el orden actual (FileName ascendente por
        // defecto si no hay columna ordenada activa) y conserva la imagen seleccionada, igual que OnFilterImages.
        GameImage? selectedImage = SharedDataService.SelectedImage;
        SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection ?? DataGridSortDirection.Ascending);
        SharedDataService.SelectedImage = selectedImage;
    }

    /// <summary>
    /// Baja de una imagen (p. ej. al deshacer un alta): la quita del grid del audit si está, simétrico a
    /// <see cref="OnImageAddedToGame"/>. <see cref="RemoveImage"/> recalcula stats y gráfica.
    /// </summary>
    private void OnImageRemovedFromGame(Game game, GameImage image)
    {
        if (image is null || !ImagesCollection.Contains(image))
        {
            return;
        }

        RemoveImage(image);
    }

    /// <summary>
    /// The selected image changes.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void IaControl_OnImageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.Count > 0 && e.AddedItems.First() is GameImage image)
            {
                // Orphan images cannot be selected. The DataGrid's SelectedItem is bound OneWay, so the
                // click only moved the control's selection, not SharedDataService.SelectedImage (which is
                // still the valid one the dashboard shows). Revert the row highlight back to it; deferred
                // because the DataGrid ignores a SelectedItem change made from inside its own
                // SelectionChanged.
                if (image.LinkedGames.Count == 0)
                {
                    DataGrid dataGrid = (DataGrid)sender!;
                    _ = dataGrid.DispatcherQueue.TryEnqueue(() => dataGrid.SelectedItem = SharedDataService.SelectedImage);
                    return;
                }

                (sender as DataGrid)?.ScrollIntoView(image, null);
                SelectImage(image);
                OnImageSelectionChanged(image);
            }
            StatisticsImages = GetImageStatistics();
        }
        catch
        {
        }
    }

    /// <summary>
    /// The selected image changes from the image grid (gallery) view. Mirrors the selection into the
    /// shared data service (image and, when needed, game) and re-raises the control's selection event.
    /// </summary>
    /// <param name="image"></param>
    private void OnGridImageSelectionChanged(GameImage image)
    {
        if (image == null) { return; }

        // Orphan images cannot be selected. The grid's SelectedItem is TwoWay-bound to
        // IgViewModel.SelectedImage, so revert it to the current shared selection.
        if (image.LinkedGames.Count == 0)
        {
            IgViewModel.SelectedImage = SharedDataService.SelectedImage!;
            return;
        }

        SelectImage(image);
        OnImageSelectionChanged(image);
    }

    /// <summary>
    /// An image binary was decoded on demand while scrolling the gallery, which also filled that image's
    /// dimensions. Schedule a (coalesced) statistics refresh so the dimension stats update progressively
    /// as more binaries load, instead of waiting for the manual "Dimensions" command.
    /// </summary>
    /// <param name="image">The image whose binary was just loaded.</param>
    private void OnGalleryImageBinaryLoaded(GameImage image)
    {
        if (!_statsRefreshTimer.IsRunning)
        {
            _statsRefreshTimer.Start();
        }
    }

    /// <summary>
    /// Recomputes the image statistics after a burst of binary loads has settled.
    /// </summary>
    /// <param name="sender">The timer that elapsed.</param>
    /// <param name="args">The tick event data.</param>
    private void OnStatsRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        StatisticsImages = GetImageStatistics();
    }

    /// <summary>
    /// Dimensions were read elsewhere (e.g. the platform stats widget reads them lazily). Refresh the statistics
    /// pill (dimensions row) and the "Dimensions" command availability so this control stays in sync.
    /// </summary>
    private void OnImageDimensionsChanged()
    {
        StatisticsImages = GetImageStatistics();
        BuildImageCharacteristicsChart();   // la columna de dimensiones puede haber cambiado
        RaiseCanExecuteCommands();
    }

    /// <summary>
    /// The selected image type changes: rebuild the characteristics chart for the new set (its images, extensions,
    /// regions and dimensions are different).
    /// </summary>
    private void OnSelectedImageSetChanged(object? sender, ImageSetChangedEventArgs e)
    {
        UpdateRegionStatsVisibility();
        BuildImageCharacteristicsChart();
    }

    /// <summary>
    /// IsEnabled property of the control changes. The DataGrid, with lots of images, blocks the UI for a few seconds so the data is only rendered if the component is visible.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void IaControl_IsEnabledChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        _isEnabled = (bool)e.NewValue;
        if (_isEnabled && !_isLoaded) { SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection); }
    }

    /// <summary>
    /// Propagates the shared selected image to the gallery view model so the grid highlights it.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnSelectedImageChanged(object? sender, SharedDataService.GameImageChangedEventArgs e)
    {
        IgViewModel.SelectedImage = e.NewImage!;
    }

    /// <summary>
    /// The selected image set (or its game matching) changes. Reloads all the images of the currently
    /// selected image type for the selected platform. This fires after the images have been matched
    /// with the games, so each image's LinkedGames are already populated.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnSelectedGameImagesChanged(object? sender, SharedDataService.GameImagesChangedEventArgs e)
    {
        List<GameImage> images = e.ImageSet?.Images ?? new List<GameImage>();
        SetImages(new ObservableCollection<GameImage>(images));
    }

    /// <summary>
    /// Reacts to the widget becoming active. While the widget is not assigned to a slot
    /// (SlotIndex &lt; 0) the images are never pushed to the DataGrid; the refresh is deferred until
    /// the widget becomes visible. Building the bound collection for thousands of images is expensive,
    /// so it only happens for an active widget.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ImageAuditViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SlotIndex)) { return; }
        if (SlotIndex >= 0 && _pendingRefresh) { RefreshImages(); }
    }
    #endregion


    #region Methods
    /// <summary>
    /// Selects the given image in the shared data service (used by both the data grid and the image
    /// grid views). When the image does not belong to the currently selected game, the selected game is
    /// switched to one of the games the image is linked to, so the rest of the application follows.
    /// </summary>
    /// <param name="image"></param>
    public void SelectImage(GameImage? image)
    {
        // Orphan images (not associated with any game) are not selectable: keep the current selection.
        if (image == null || image.LinkedGames.Count == 0) { return; }

        // If the picked image does not belong to the currently selected game, switch the selected game
        // to one of the games the image is linked to. The game is changed BEFORE the image so the image
        // dashboard's (async) game refresh keeps this image selected: SelectInitialImage preserves the
        // current SelectedImage when it belongs to the (new) selected game, which it does here.
        if (!image.LinkedGames.Contains(SharedDataService.SelectedGame!))
        {
            SharedDataService.SelectedGame = image.LinkedGames.First();
        }

        SharedDataService.SelectedImage = image;
    }

    /// <summary>
    /// Adds an image to the collection (called when drag & dropping images).
    /// </summary>
    /// <param name="image"></param>
    public void AddImage(GameImage image)
    {
        ImagesCollection.Add(image);
        ImagesCollectionFiltered.Add(image);
        IgViewModel.Images.Add(image);
        StatisticsImages = GetImageStatistics();
        BuildImageCharacteristicsChart();
        RaiseCanExecuteCommands();
    }

    /// <summary>
    /// Removes an image from the collection (called when processing a game).
    /// </summary>
    /// <param name="image"></param>
    public void RemoveImage(GameImage image)
    {
        _ = ImagesCollection.Remove(image);
        _ = ImagesCollectionFiltered.Remove(image);
        _ = IgViewModel.Images.Remove(image);
        _imageBinariesCacheService.RemoveImage(image);
        StatisticsImages = GetImageStatistics();
        BuildImageCharacteristicsChart();
        RaiseCanExecuteCommands();
    }

    /// <summary>
    /// Sets the collection of images for the control (called normally when changing platform or image type).
    /// </summary>
    /// <param name="images"></param>
    public void SetImages(ObservableCollection<GameImage> images)
    {
        _isLoaded = false;
        ImagesCollection = images;
        StatisticsImages = GetImageStatistics();
        UpdateRegionStatsVisibility();      // el tipo (imagen/vídeo) pudo cambiar: oculta las pastillas de región para vídeo
        BuildImageCharacteristicsChart();   // nuevo set: extensiones/regiones/dimensiones distintas
        // Si la página de la gráfica ya está visible (p. ej. al cambiar de plataforma estando en ella), lee las
        // dimensiones del nuevo set de inmediato; el guard de EnsureImageDimensionsLoadedAsync evita relecturas.
        if (_characteristicsPageVisible)
            _ = EnsureImageDimensionsLoadedAsync();
        RaiseCanExecuteCommands();

        // Pushing thousands of images to the DataGrid freezes the UI for a few seconds, so the data is
        // only rendered when the widget is active (SlotIndex >= 0); otherwise the refresh is deferred
        // until the widget becomes visible (see ImageAuditViewModel_PropertyChanged).
        _pendingRefresh = true;
        if (SlotIndex >= 0) { RefreshImages(); }
    }

    /// <summary>
    /// Pushes the staged images to the DataGrid (and image grid) using the current sort/filter state.
    /// </summary>
    public void RefreshImages()
    {
        _pendingRefresh = false;
        SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection);
    }

    /// <summary>
    /// Sorts the collection based on the column and order selected.
    /// </summary>
    /// <param name="columnTag"></param>
    /// <param name="sortDirection"></param>
    public void SortCollection(string columnTag, DataGridSortDirection? sortDirection)
    {
        List<GameImage> data = new();
        _columnLastOrderedBy = columnTag; _columnLastOrderedDirection = sortDirection;
        if (sortDirection != null)
        {
            if (columnTag == "FileName")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.Name ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.Name descending
                                          select item);
            }
            if (columnTag == "FileSize")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.FileSize ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.FileSize descending
                                          select item);
            }
            if (columnTag == "Dimensions")
            {
                // Sort numerically by width then height, not lexicographically by the "WxH" string
                // (otherwise "1920x1080" would sort before "640x480").
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.Width ascending, item.Height ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.Width descending, item.Height descending
                                          select item);
            }
            if (columnTag == "FileExtension")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.FileExtension ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.FileExtension descending
                                          select item);
            }
            if (columnTag == "Quality")
            {
                // Quality = vertical resolution (height); sort numerically, not by the "1080p" label.
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.Height ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.Height descending
                                          select item);
            }
            if (columnTag == "Duration")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.Duration ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.Duration descending
                                          select item);
            }
            if (columnTag == "Region")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.Region ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.Region descending
                                          select item);
            }
            if (columnTag == "LinkedGames")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                ? new List<GameImage>(from item in ImagesCollection
                                      orderby item.LinkedGames.Count ascending
                                      select item)
                : new List<GameImage>(from item in ImagesCollection
                                      orderby item.LinkedGames.Count descending
                                      select item);
            }
            if (columnTag == "LinkedGamesToString")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<GameImage>(from item in ImagesCollection
                                          orderby item.LinkedGamesToString ascending
                                          select item)
                    : new List<GameImage>(from item in ImagesCollection
                                          orderby item.LinkedGamesToString descending
                                          select item);
            }
            SetCollection(data);
        }
        else
        {
            SetCollection(ImagesCollection.ToList());
        }
    }
    #endregion


    #region Methods (image-characteristics chart)
    /// <summary>
    /// Construye la gráfica "Image set characteristics": 4 categorías en el eje Y como barras horizontales APILADAS
    /// de 2 series que SIEMPRE suman el total de imágenes del set seleccionado (misma longitud en las 4): una serie
    /// "destacado" (el valor más repetido de cada columna, en acento claro) y una "Others" (el resto, en acento
    /// oscuro). La etiqueta de cada categoría es ese valor destacado (la moda), y el tooltip lo muestra también.
    /// Las dos primeras categorías son siempre Extensión y Dimensiones; las dos últimas dependen del tipo. Reglas:
    /// <list type="bullet">
    /// <item>Extensión: la extensión más usada; el resto en Others.</item>
    /// <item>Dimensiones: la dimensión más usada (de las ya decodificadas); el resto (incl. no decodificadas) en
    /// Others. Si NO hay ninguna dimensión leída, la columna se etiqueta "Dimensions".</item>
    /// <item>Imágenes — Región: la región más usada; el resto (otras regiones + sin región) en Others. Sin región:
    /// destacado = imágenes sin región ("No region"); Others = las que sí tienen región.</item>
    /// <item>Vídeos — Calidad: la resolución vertical más usada ("1080p"); Duración: el rango de 10 s más frecuente
    /// ("11-20s"). En ambas, las que aún no tienen altura/duración leída van a Others (etiqueta "Quality"/"Duration").</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// Al cambiar el tema en caliente, reconstruye la gráfica de características: sus series se pintan con colores del
    /// tema (SKColor) horneados al construirla. Reutiliza el set de imágenes ya seleccionado (cómputo barato).
    /// </summary>
    private void OnThemeChanged(object? sender, System.EventArgs e)
    {
        if (SlotIndex < 0) return;
        BuildImageCharacteristicsChart();
    }

    private void BuildImageCharacteristicsChart()
    {
        // Solo del TIPO DE IMAGEN seleccionado: no tiene sentido comparar dimensiones/etc. entre tipos distintos.
        PlatformImageSet? imageSet = SharedDataService.SelectedImageSet;
        int total = imageSet?.ImagesCount ?? 0;
        if (imageSet == null || total == 0)
        {
            ClearImageCharacteristics();
            return;
        }

        IReadOnlyDictionary<string, int> extension = _statisticsService.GetImageSetCountByExtension(imageSet);
        IReadOnlyDictionary<string, int> dimensions = _statisticsService.GetImageSetCountByDimensions(imageSet);  // solo decodificadas

        // Valor destacado (etiqueta + cuenta) de cada columna; "Others" = total − destacado (las 4 suman 'total').
        (string Label, int Count) extTop = TopValue(extension);
        (string Label, int Count) dimTop = TopValue(dimensions);
        if (dimTop.Count == 0)
            dimTop = (MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_Dimensions_Label] ?? "Dimensions", 0);   // sin dimensiones leídas: la columna entera va a "Others", etiqueta "Dimensions"

        // Las dos últimas categorías dependen del tipo: para vídeos, Calidad y rango de Duración; para imágenes,
        // Región y Sin región (un vídeo no tiene región, así que esas dos no aportan nada para ellos).
        bool isVideo = imageSet.Type != null && MediaType.IsVideo(imageSet.Type.Key);
        (string Label, int Count) thirdTop;
        (string Label, int Count) fourthTop;
        if (isVideo)
        {
            thirdTop = TopValue(_statisticsService.GetImageSetCountByQuality(imageSet));
            if (thirdTop.Count == 0)
                thirdTop = (MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_Quality_Label] ?? "Quality", 0);    // sin alturas leídas: toda la columna va a "Others", etiqueta "Quality"
            fourthTop = TopValue(_statisticsService.GetImageSetCountByDurationRange(imageSet));
            if (fourthTop.Count == 0)
                fourthTop = (MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_Duration_Label] ?? "Duration", 0);  // sin duraciones leídas: toda la columna va a "Others", etiqueta "Duration"
        }
        else
        {
            IReadOnlyDictionary<string, int> region = _statisticsService.GetImageSetCountByRegion(imageSet);
            string noRegionKey = ImageRegion.NoRegion.Value;
            region.TryGetValue(noRegionKey, out int noRegionCount);
            thirdTop = TopValue(region.Where(kv => kv.Key != noRegionKey));
            fourthTop = (MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Common_NoRegion_Label] ?? "No region", noRegionCount);   // NoRegion.Value es "", etiqueta explícita
        }

        // Orden visual deseado de arriba a abajo: imágenes → extensión, dimensiones, región, sin región;
        // vídeos → extensión, calidad, dimensiones, duración (calidad por encima de dimensiones). Las barras
        // horizontales (RowSeries) dibujan el índice 0 ABAJO, así que el array va al revés (la fila inferior primero).
        (string Label, int Count)[] tops = isVideo
            ? new (string Label, int Count)[] { fourthTop, dimTop, thirdTop, extTop }
            : new (string Label, int Count)[] { fourthTop, thirdTop, dimTop, extTop };

        var labels = new string[tops.Length];
        var topValues = new double[tops.Length];
        var othersValues = new double[tops.Length];
        for (int c = 0; c < tops.Length; c++)
        {
            labels[c] = tops[c].Label;
            topValues[c] = tops[c].Count;
            othersValues[c] = total - tops[c].Count;
        }

        (SKColor _, SKColor accentDark, SKColor accentLight, SKColor text) = ResolveThemeColors();
        string[] labelsSnapshot = labels;   // para el tooltip de la serie destacada

        // Barras horizontales apiladas: una fila por columna (categoría en el eje Y), valor en el eje X.
        ImageCharacteristicsSeries = new ISeries[]
        {
            new StackedRowSeries<double>
            {
                Values = topValues,
                Name = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_MostUsed_Label] ?? "Most used",
                Rx = 2,
                Ry = 2,
                Fill = new SolidColorPaint(accentLight),
                XToolTipLabelFormatter = point =>
                {
                    int i = point.Index;
                    string label = i >= 0 && i < labelsSnapshot.Length ? labelsSnapshot[i] : string.Empty;
                    return $"{label}: {point.Coordinate.PrimaryValue:0}";
                },
            },
            new StackedRowSeries<double>
            {
                Values = othersValues,
                Name = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_Others_Label] ?? "Others",
                Rx = 2,
                Ry = 2,
                Fill = new SolidColorPaint(accentDark),
                XToolTipLabelFormatter = point => $"{MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_Others_Label] ?? "Others"}: {point.Coordinate.PrimaryValue:0}",
            },
        };
        // Categorías (modas) en el eje Y; valores (nº de imágenes) en el eje X.
        ImageCharacteristicsXAxes = new ICartesianAxis[]
        {
            new Axis { MinLimit = 0, TextSize = 11, LabelsPaint = new SolidColorPaint(text) }
        };
        ImageCharacteristicsYAxes = new ICartesianAxis[]
        {
            new Axis { Labels = labels, TextSize = 11, LabelsPaint = new SolidColorPaint(text) }
        };
        HasImageCharacteristicsData = true;
    }

    /// <summary>
    /// Lectura PEREZOSA de dimensiones: al hacerse visible la gráfica de características, lee en segundo plano las
    /// dimensiones (cabeceras de fichero) de las imágenes del TIPO DE IMAGEN seleccionado que aún no las tienen y
    /// reconstruye la gráfica. Idempotente y barato si ya están todas leídas (no relee las conocidas). No bloquea la UI.
    /// </summary>
    public async Task EnsureImageDimensionsLoadedAsync()
    {
        if (_loadingDimensions)
            return;

        PlatformImageSet? imageSet = SharedDataService.SelectedImageSet;
        if (imageSet == null)
            return;

        // Imágenes del set sin dimensiones leídas.
        List<GameImage> pending = imageSet.Images.Where(img => img.Width <= 0 || img.Height <= 0).ToList();
        if (pending.Count == 0)
        {
            BuildImageCharacteristicsChart();   // ya están todas; refresca por si cambió algo
            return;
        }

        _loadingDimensions = true;

        // Feedback en la barra/consola de progreso, igual que el botón "Dimensions" del Images Audit.
        string platformName = SharedDataService.SelectedPlatform?.Name ?? string.Empty;
        string imageType = imageSet.Type?.Value ?? string.Empty;
        ProgressNotifier progressNotifier = _progressService.StartOperation();
        var progress = new Progress<int>(percent =>
        {
            progressNotifier.Progress = percent;
            progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_RetrievingDimensions_Progress] ?? "{2}  |  Retrieving dimensions of {0} {1} media files", pending.Count, imageType, platformName);
            _progressService.ProgressNotifier.Report(progressNotifier);
        });

        try
        {
            await _fileSystemService.LoadImageDimensionsAsync(pending, progress);
        }
        catch
        {
            // Lectura de solo lectura; si fallara no rompemos la UI.
        }
        finally
        {
            _loadingDimensions = false;
            progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageAudit_DimensionsRetrievedShort_Progress] ?? "{0}  |  {1} dimensions retrieved", platformName, imageType);
            progressNotifier.FinishOperation();
            _progressService.ProgressNotifier.Report(progressNotifier);
            _progressService.FinishOperation();
        }

        // Reconstruye SIEMPRE tras la lectura (relee el set seleccionado actual), para que la columna de
        // dimensiones se pinte en cuanto terminan de leerse las cabeceras. La lectura también dispara
        // ImageDimensionsChanged, que refresca las pastillas de estadísticas.
        BuildImageCharacteristicsChart();
    }

    /// <summary>Valor más frecuente (etiqueta + cuenta) de una distribución; ("—", 0) si está vacía.</summary>
    private static (string Label, int Count) TopValue(IEnumerable<KeyValuePair<string, int>> distribution)
    {
        var ordered = distribution.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
        return ordered.Count > 0 ? (ordered[0].Key, ordered[0].Value) : ("—", 0);
    }

    /// <summary>Vacía la gráfica de características de imagen.</summary>
    private void ClearImageCharacteristics()
    {
        ImageCharacteristicsSeries = Array.Empty<ISeries>();
        ImageCharacteristicsXAxes = Array.Empty<ICartesianAxis>();
        ImageCharacteristicsYAxes = Array.Empty<ICartesianAxis>();
        HasImageCharacteristicsData = false;
    }

    /// <summary>Resuelve los colores de acento y texto del tema activo para tematizar la gráfica.</summary>
    private (SKColor accent, SKColor accentDark, SKColor accentLight, SKColor text) ResolveThemeColors()
        => (ToSk(_themeService.AccentColor), ToSk(_themeService.AccentDarkColor), ToSk(_themeService.AccentLightColor), ToSk(_themeService.TextColor));

    private static SKColor ToSk(Windows.UI.Color c) => new(c.R, c.G, c.B, c.A);
    #endregion


    #region Methods (private)
    /// <summary>
    /// Calculates the statistics related to the images loaded for the games of the platform.
    /// </summary>
    /// <returns>An ImageAuditStats instance</returns>
    private ImageAuditStats GetImageStatistics() => _statisticsService.GetImageCollectionStatistics(ImagesCollection);

    /// <summary>
    /// Region only makes sense for images: videos have no region. Hide the region pills (Region / No region) when
    /// the selected media type is a video so they don't show a meaningless "No region" for every file.
    /// </summary>
    private void UpdateRegionStatsVisibility()
    {
        int? key = SharedDataService.SelectedImageSet?.Type?.Key;
        bool isVideo = key != null && MediaType.IsVideo(key.Value);
        ShowRegionStats = !isVideo;
        IsVideoSet = isVideo;
    }

    /// <summary>
    /// Checks the different conditions for executing the commands and raises the NotifyCanExecuteChanged event for each of the them.
    /// </summary>
    private void RaiseCanExecuteCommands()
    {
        DeleteOrphanImagesCommand.NotifyCanExecuteChanged();
        GetImageDimensionsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Filters the collection of images based on the active filters.
    /// </summary>
    /// <param name="sourceList"></param>
    private void SetCollection(List<GameImage> sourceList)
    {
        // Replace the whole filtered set in a single Reset notification instead of one Add per item,
        // so the bound DataGrid re-runs layout once rather than O(N) times.
        ImagesCollectionFiltered.ReplaceAll(ActiveCountFilters.ApplyImageFilters(sourceList));
        _ = IgViewModel.SetGalleryAsync(ImagesCollectionFiltered.ToList());
        _isLoaded = true;
    }

    /// <summary>
    /// Limpia suscripciones globales al destruir el ViewModel.
    /// </summary>
    public override void Dispose()
    {
        _statsRefreshTimer.Stop();
        _statsRefreshTimer.Tick -= OnStatsRefreshTimerTick;
        IgViewModel.ImageSelectionChanged -= OnGridImageSelectionChanged;
        IgViewModel.ImageBinaryLoaded -= OnGalleryImageBinaryLoaded;
        SharedDataService.SelectedImageChanged -= OnSelectedImageChanged;
        SharedDataService.SelectedGameImagesChanged -= OnSelectedGameImagesChanged;
        SharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
        _fileSystemService.ImageDimensionsChanged -= OnImageDimensionsChanged;
        _imageLoadingService.ImageAddedToGame -= OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame -= OnImageRemovedFromGame;
        PropertyChanged -= ImageAuditViewModel_PropertyChanged;
    }

    /// <summary>
    /// Switches the control to the image grid (gallery) view.
    /// </summary>
    public void SelectGridView()
    {
        _isGridView = true;
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(SelectedViewValue));
    }

    /// <summary>
    /// Switches the control to the data grid (list) view.
    /// </summary>
    public void SelectListView()
    {
        _isGridView = false;
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(SelectedViewValue));
    }

    /// <summary>
    /// Loads the persisted configuration of the control: the active view (list/grid) and the gallery's
    /// aspect ratio and resolution. Called by the control once the application settings have been
    /// restored from disk.
    /// </summary>
    public override void LoadConfig()
    {
        AppSettings.ImageAuditControlSettings config = _appSettings.ImageAuditControl;
        if (config == null) { return; }

        if (config.ListView) { SelectListView(); } else { SelectGridView(); }

        IgViewModel.ApplyAspectRatio(config.AspectRatio?.Value);
        IgViewModel.ApplyResolution(config.Resolution?.Value);

        // Ignore a non-positive persisted size (a bad 0 could have been saved by an earlier session); keep the
        // view model's default so the gallery self-heals instead of restoring a collapsed thumbnail size.
        if (config.ItemSize > 0) { IgViewModel.Width = config.ItemSize; }
    }

    /// <summary>
    /// Saves the control's configuration back into the application settings: the active view (list/grid)
    /// and the gallery's current aspect ratio and resolution.
    /// </summary>
    public override void SaveConfig()
    {
        AppSettings.ImageAuditControlSettings config = _appSettings.ImageAuditControl;

        config.ListView = IsListView;
        config.GridView = IsGridView;
        config.AspectRatio = Enumeration.FromValue<AspectRatioSettings>(IgViewModel.SelectedAspectRatio.Name) ?? config.AspectRatio;
        config.Resolution = Enumeration.FromValue<ImageResolutionSettings>(IgViewModel.SelectedImageResolution.Name) ?? config.Resolution;
        config.ItemSize = IgViewModel.Width;
    }
    #endregion
}
