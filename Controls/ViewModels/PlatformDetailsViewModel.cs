using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace MM4LB.Controls.ViewModels;

public class PlatformDetailsViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly ImageLoadingService _imageLoadingService;
    private readonly ImageBinaryLoadingService _imageBinaryLoadingService;
    private readonly WindowService _windowService;
    private readonly DialogsService _dialogsService;
    private readonly IStatisticsService _statisticsService;
    private readonly ThemeService _themeService;
    private readonly ExceptionService _exceptionService;
    private GameImage? _selectedOwnImage;
    private bool _isDragActive;
    private bool _isImportingDrop;

    // Gráfica "Coverage - Platforms (own image types)": cobertura (0..100) de los tipos de imagen especiales de
    // CADA plataforma (los gestionados aquí), un punto por plataforma en área, con la plataforma seleccionada
    // resaltada. Misma forma que la gráfica "Coverage - Games" del StatsPlatformControl, pero por plataforma.
    private IEnumerable<ISeries> _coverageByPlatformSeries = Array.Empty<ISeries>();
    private IEnumerable<ICartesianAxis> _coverageByPlatformXAxes = Array.Empty<ICartesianAxis>();
    private IEnumerable<ICartesianAxis> _coverageByPlatformYAxes = Array.Empty<ICartesianAxis>();
    private IEnumerable<RectangularSection> _coverageByPlatformSections = Array.Empty<RectangularSection>();
    private bool _hasCoverageByPlatformData;
    private bool _animateCoverageByPlatformChart = true;

    // Gráfica "Coverage distribution - Platforms": nº de plataformas por tramo de cobertura de tipos propios.
    private IReadOnlyList<double> _coverageDistributionValues = Array.Empty<double>();
    private IReadOnlyList<string> _coverageDistributionLabels = Array.Empty<string>();

    // Gráfica "Coverage - Platform image type": % de plataformas con cada tipo de imagen propia.
    private IReadOnlyList<double> _coverageByTypeValues = Array.Empty<double>();
    private IReadOnlyList<string> _coverageByTypeLabels = Array.Empty<string>();

    // Gráfica "Own images - Platform image type": nº total de imágenes propias por tipo en todas las plataformas.
    private IReadOnlyList<double> _imagesByTypeValues = Array.Empty<double>();
    private IReadOnlyList<string> _imagesByTypeLabels = Array.Empty<string>();
    private double _platformCount;
    private bool _globalCoverageChartsBuilt;

    private int _selectedChartIndex;

    // Tipos de imagen propios (los gestionados aquí), en orden: los 7 tipos de imagen + el vídeo. Eje X de las
    // gráficas por tipo y denominador de la cobertura.
    private static readonly IReadOnlyList<MediaType> OwnImageTypes =
        MediaType.PlatformImageTypes.Append(MediaType.PlatformVideo).ToList();
    #endregion

    #region Properties
    public new SharedDataService SharedDataService => _sharedDataService;

    /// <summary>
    /// Volumen (0–100) de la reproducción de vídeo, compartido con el resto de la app
    /// (<see cref="GeneralSettings.VideoVolume"/>). 0 = silencio. El control lo aplica al crear el reproductor del
    /// vídeo de la ficha, así que un cambio se refleja en la siguiente reproducción.
    /// </summary>
    public double VideoVolume => _appSettings.General?.VideoVolume ?? 0;

    /// <summary>
    /// True cuando el set de medios seleccionado es de vídeo (Video Snap / Theme Video). En ese caso el dashboard
    /// reproduce su propio vídeo y tiene prioridad de audio, así que el vídeo de la ficha debe ir en silencio para
    /// no solapar dos audios (ver <see cref="EffectiveVideoVolume"/>).
    /// </summary>
    public bool IsVideoSetSelected
        => _sharedDataService.SelectedImageSet?.Type is { } type && MediaType.IsVideo(type.Key);

    /// <summary>
    /// Volumen efectivo (0–100) del vídeo de la ficha de plataforma: el global <see cref="VideoVolume"/>, salvo que
    /// esté silenciado globalmente (<see cref="GeneralSettings.IsMuted"/>) o haya un set de vídeo seleccionado
    /// (<see cref="IsVideoSetSelected"/>), en cuyo caso es 0 (mute global, o cede el audio al vídeo del dashboard).
    /// </summary>
    public double EffectiveVideoVolume => (_appSettings.General?.IsMuted ?? false) || IsVideoSetSelected ? 0 : VideoVolume;

    /// <summary>
    /// The selected platform's own images (banner, boxes, device, fanart...). Exposed via the VM so the
    /// list and the framed preview share a single source whose updates are sequenced (see
    /// <see cref="RefreshOwnImages"/>), avoiding a selection-vs-itemssource race on platform change.
    /// Returns a fresh snapshot each time: <see cref="Platform.OwnImages"/> is refreshed in place
    /// (Clear + Add on the same List), so a new reference is needed for x:Bind to rebuild the strip.
    /// </summary>
    public IReadOnlyList<GameImage>? OwnImages => _sharedDataService.SelectedPlatform?.OwnImages?.ToList();

    /// <summary>
    /// The own image currently selected in the list and shown in the framed preview. Defaults to the
    /// video, then the Fanart, then the first available image.
    /// </summary>
    public GameImage? SelectedOwnImage
    {
        get => _selectedOwnImage;
        set
        {
            var previous = _selectedOwnImage;

            if (SetProperty(ref _selectedOwnImage, value))
            {
                // Keep the binary-derived preview properties (below) in sync with the selected image's own
                // async binary load: hop off the old image's notifications and onto the new one's.
                if (previous != null)
                    previous.PropertyChanged -= OnSelectedOwnImagePropertyChanged;
                if (value != null)
                    value.PropertyChanged += OnSelectedOwnImagePropertyChanged;

                OnPropertyChanged(nameof(IsSelectedOwnImageVideo));
                OnPropertyChanged(nameof(SelectedOwnImageBinary));
                OnPropertyChanged(nameof(SelectedOwnImageHasBinary));
                OnPropertyChanged(nameof(ShowEmptyDropHint));
            }
        }
    }

    /// <summary>
    /// Whether to show the resting empty-drop hint (the dashed border) on the preview: only when the platform
    /// has no own image selected and no drag is in progress. While dragging, the accent drop overlay (with its
    /// own dashed border and icon) takes over, so the resting border is hidden to avoid doubling it.
    /// </summary>
    public bool ShowEmptyDropHint => _selectedOwnImage == null && !_isDragActive;

    /// <summary>
    /// The selected own image's decoded bitmap, surfaced on the VM (whose instance is the binding root and
    /// never null) so the preview clears reliably when the selection becomes null. Binding the preview image
    /// straight to <c>SelectedOwnImage.Binary</c> with x:Bind retains the last value when the intermediate
    /// <see cref="SelectedOwnImage"/> goes null — so switching to a platform with no own images kept showing
    /// the previous platform's picture.
    /// </summary>
    public BitmapImage? SelectedOwnImageBinary => _selectedOwnImage?.Binary;

    /// <summary>Whether the selected own image has a decoded bitmap (drives the preview image's visibility).</summary>
    public bool SelectedOwnImageHasBinary => _selectedOwnImage?.HasBinary ?? false;

    /// <summary>
    /// Whether the selected own image is the platform video (drives the preview: player vs image).
    /// </summary>
    public bool IsSelectedOwnImageVideo =>
        _selectedOwnImage != null && MediaType.IsPlatformVideo(_selectedOwnImage.Type?.Key ?? -1);

    /// <summary>True while a file is being dragged over the preview, to show the drop-zone overlay.</summary>
    public bool IsDragActive
    {
        get => _isDragActive;
        set
        {
            if (SetProperty(ref _isDragActive, value))
                OnPropertyChanged(nameof(ShowEmptyDropHint));
        }
    }

    /// <summary>True while a dropped file is being imported (copy/replace), to show the importing overlay.</summary>
    public bool IsImportingDrop
    {
        get => _isImportingDrop;
        set => SetProperty(ref _isImportingDrop, value);
    }
    #endregion

    #region Properties (coverage-by-platform chart data)
    /// <summary>Gráfica de área: cobertura de imágenes propias (0..100) de cada plataforma; la seleccionada, resaltada.</summary>
    public IEnumerable<ISeries> CoverageByPlatformSeries
    {
        get => _coverageByPlatformSeries;
        private set => SetProperty(ref _coverageByPlatformSeries, value);
    }

    public IEnumerable<ICartesianAxis> CoverageByPlatformXAxes
    {
        get => _coverageByPlatformXAxes;
        private set => SetProperty(ref _coverageByPlatformXAxes, value);
    }

    public IEnumerable<ICartesianAxis> CoverageByPlatformYAxes
    {
        get => _coverageByPlatformYAxes;
        private set => SetProperty(ref _coverageByPlatformYAxes, value);
    }

    /// <summary>Línea vertical punteada desde el punto de la plataforma seleccionada hasta el eje X.</summary>
    public IEnumerable<RectangularSection> CoverageByPlatformSections
    {
        get => _coverageByPlatformSections;
        private set => SetProperty(ref _coverageByPlatformSections, value);
    }

    /// <summary>
    /// Si la próxima actualización de la gráfica debe animarse. False al cambiar de plataforma (los valores no
    /// cambian: solo se mueve el resaltado) para que sea instantáneo; true cuando hay recálculo real (alta/baja
    /// de imágenes propias). El code-behind la consume para ajustar la velocidad de animación antes de redibujar.
    /// </summary>
    public bool AnimateCoverageByPlatformChart
    {
        get => _animateCoverageByPlatformChart;
        private set => SetProperty(ref _animateCoverageByPlatformChart, value);
    }

    /// <summary>True cuando hay plataformas que dibujar en la gráfica de cobertura por plataforma.</summary>
    public bool HasCoverageByPlatformData
    {
        get => _hasCoverageByPlatformData;
        private set => SetProperty(ref _hasCoverageByPlatformData, value);
    }
    #endregion

    #region Properties (coverage charts — config + data)
    /// <summary>Índice de la gráfica visible en el FlipView (enlazado TwoWay a <c>CoverageChartFlipView.SelectedIndex</c>).</summary>
    public int SelectedChartIndex
    {
        get => _selectedChartIndex;
        set => SetProperty(ref _selectedChartIndex, value);
    }

    /// <summary>Tipo / orden / Top X de la gráfica "Coverage distribution - Platforms". Enlazado TwoWay y persistido.</summary>
    public ChartViewState CoverageDistributionChart { get; } = new ChartViewState();

    /// <summary>Tipo / orden / Top X de la gráfica "Coverage - Platform image type". Enlazado TwoWay y persistido.</summary>
    public ChartViewState CoverageByTypeChart { get; } = new ChartViewState();

    /// <summary>Tipo / orden / Top X de la gráfica "Own images - Platform image type". Enlazado TwoWay y persistido.</summary>
    public ChartViewState ImagesByTypeChart { get; } = new ChartViewState();

    /// <summary>Valores (nº de plataformas) por cada tramo de cobertura de tipos propios.</summary>
    public IReadOnlyList<double> CoverageDistributionValues
    {
        get => _coverageDistributionValues;
        private set => SetProperty(ref _coverageDistributionValues, value);
    }

    /// <summary>Etiquetas de los tramos (p. ej. "0–20%") alineadas con <see cref="CoverageDistributionValues"/>.</summary>
    public IReadOnlyList<string> CoverageDistributionLabels
    {
        get => _coverageDistributionLabels;
        private set => SetProperty(ref _coverageDistributionLabels, value);
    }

    /// <summary>Valores (% de plataformas) por cada tipo de imagen propia.</summary>
    public IReadOnlyList<double> CoverageByTypeValues
    {
        get => _coverageByTypeValues;
        private set => SetProperty(ref _coverageByTypeValues, value);
    }

    /// <summary>Etiquetas (nombre del tipo propio) alineadas con <see cref="CoverageByTypeValues"/>.</summary>
    public IReadOnlyList<string> CoverageByTypeLabels
    {
        get => _coverageByTypeLabels;
        private set => SetProperty(ref _coverageByTypeLabels, value);
    }

    /// <summary>Valores (nº total de imágenes propias) por cada tipo de imagen propia.</summary>
    public IReadOnlyList<double> ImagesByTypeValues
    {
        get => _imagesByTypeValues;
        private set => SetProperty(ref _imagesByTypeValues, value);
    }

    /// <summary>Etiquetas (nombre del tipo propio) alineadas con <see cref="ImagesByTypeValues"/>.</summary>
    public IReadOnlyList<string> ImagesByTypeLabels
    {
        get => _imagesByTypeLabels;
        private set => SetProperty(ref _imagesByTypeLabels, value);
    }

    /// <summary>Nº de plataformas (línea de referencia "una imagen por plataforma" en "Own images by type").</summary>
    public double PlatformCount
    {
        get => _platformCount;
        private set => SetProperty(ref _platformCount, value);
    }
    #endregion

    #region Constructors
    public PlatformDetailsViewModel(SharedDataService sharedDataService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, WindowService windowService, DialogsService dialogsService, IStatisticsService statisticsService, ThemeService themeService, ExceptionService exceptionService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _imageLoadingService = imageLoadingService;
        _imageBinaryLoadingService = imageBinaryLoadingService;
        _windowService = windowService;
        _dialogsService = dialogsService;
        _statisticsService = statisticsService;
        _themeService = themeService;
        _exceptionService = exceptionService;

        // Suscribirse a eventos globales para mantener el VM sincronizado con cambios en SharedDataService.
        _sharedDataService.SelectedPlatformChanged += OnSelectedPlatformChanged;
        _imageLoadingService.PlatformImagesChanged += OnPlatformImagesChanged;
        _themeService.ThemeChanged += OnThemeChanged;

        // Estado inicial: NotifyInitialState re-emite SelectedPlatformChanged, así que OnSelectedPlatformChanged
        // ya hace TODO el refresco inicial (own images + gráficas + carga de imágenes de la plataforma) una sola
        // vez. Antes esto se hacía TAMBIÉN aquí manualmente, decodificando las imágenes de la plataforma dos veces
        // de forma concurrente en el arranque. El estado final es idéntico (el handler ya reconstruía la gráfica
        // por plataforma con animate:false justo después del build con animate:true del constructor).
        _sharedDataService.NotifyInitialState();
    }
    #endregion

    #region Subscribed events
    private async void OnSelectedPlatformChanged(object? sender, SharedDataService.PlatformChangedEventArgs e)
    {
        try
        {
            await OnSelectedPlatformChangedCoreAsync(sender, e);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformDetails_RefreshView_Error] ?? "Error refreshing the platform view.");
        }
    }

    private async Task OnSelectedPlatformChangedCoreAsync(object? sender, SharedDataService.PlatformChangedEventArgs e)
    {
        RefreshOwnImages();
        // Cambio de plataforma: los valores globales (distribución, por tipo) no cambian; solo se mueve el resaltado
        // de la gráfica por plataforma (sin animación).
        BuildCoverageByPlatformChart(animate: false);
        EnsureGlobalCoverageChartsBuilt();   // 1ª vez con plataformas ya cargadas (si el VM se creó antes)
        await LoadSelectedPlatformImagesAsync();
    }

    /// <summary>
    /// Re-publishes the own-images list after a change on disk (e.g. a drop or an import). The service has
    /// already re-scanned <see cref="Platform.OwnImages"/>, so we keep the selection on the same type when
    /// possible (the new list holds fresh instances), then reload the binaries.
    /// </summary>
    private async void OnPlatformImagesChanged()
    {
        try
        {
            await OnPlatformImagesChangedCoreAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformDetails_RefreshImages_Error] ?? "Error refreshing the platform images.");
        }
    }

    private async Task OnPlatformImagesChangedCoreAsync()
    {
        OnPropertyChanged(nameof(OwnImages));

        var images = _sharedDataService.SelectedPlatform?.OwnImages;
        var currentTypeKey = _selectedOwnImage?.Type?.Key;
        var sameType = currentTypeKey != null ? images?.FirstOrDefault(i => i.Type?.Key == currentTypeKey) : null;

        if (sameType != null)
            SelectedOwnImage = sameType;
        else
            SelectDefaultOwnImage();

        // Alta/baja de imágenes propias: cambia la cobertura de la plataforma → recalcular todo (y animar el área).
        RefreshCoverageStats(animate: true);

        await LoadSelectedPlatformImagesAsync();
    }

    /// <summary>
    /// Republishes the binary-derived preview properties when the selected image decodes (or clears) its
    /// bitmap asynchronously, so the framed preview shows up once <see cref="LoadSelectedPlatformImagesAsync"/>
    /// finishes loading it.
    /// </summary>
    private void OnSelectedOwnImagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageAsset.Binary) || e.PropertyName == nameof(ImageAsset.HasBinary))
        {
            OnPropertyChanged(nameof(SelectedOwnImageBinary));
            OnPropertyChanged(nameof(SelectedOwnImageHasBinary));
        }
    }

    #region Drag & drop
    public void Preview_DragEnter(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        IsDragActive = true;
    }

    public void Preview_DragOver(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    public void Preview_DragLeave(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        IsDragActive = false;
    }

    public async void Preview_Drop(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        try
        {
            await HandlePlatformDropAsync(e);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformDetails_AddDroppedMedia_Error] ?? "Error adding the dropped platform media.");
        }
    }
    #endregion
    #endregion

    #region Methods (private)
    /// <summary>
    /// Refreshes the own-images list and resets the default selection when the platform changes. The list
    /// is republished BEFORE the selection so the bound control already contains the item being selected,
    /// which prevents the list from clearing a selection that points into the previous platform's items.
    /// </summary>
    private void RefreshOwnImages()
    {
        OnPropertyChanged(nameof(OwnImages));
        SelectDefaultOwnImage();
    }

    /// <summary>
    /// Selects the Fanart by default, or the first available own image when the platform has no Fanart.
    /// </summary>
    private void SelectDefaultOwnImage()
    {
        var images = _sharedDataService.SelectedPlatform?.OwnImages;

        SelectedOwnImage = images?.FirstOrDefault(i => i.Type?.Key == MediaType.PlatformVideo.Key)
                           ?? images?.FirstOrDefault(i => i.Type?.Key == MediaType.PlatformFanart.Key)
                           ?? images?.FirstOrDefault();
    }

    /// <summary>Selects the own image of the given type, if present (used to show the just-dropped item).</summary>
    private void SelectOwnImageByType(MediaType type)
    {
        var match = _sharedDataService.SelectedPlatform?.OwnImages?.FirstOrDefault(i => i.Type?.Key == type?.Key);
        if (match != null)
            SelectedOwnImage = match;
    }

    /// <summary>Selects the own image with the given file path, if present (shows the just-added image).</summary>
    private void SelectOwnImageByFile(string? file)
    {
        if (string.IsNullOrEmpty(file))
            return;

        var match = _sharedDataService.SelectedPlatform?.OwnImages?
            .FirstOrDefault(i => string.Equals(i.File, file, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            SelectedOwnImage = match;
    }

    /// <summary>
    /// Resolves the dropped file(s): files from a Windows folder arrive as storage items, files dragged from
    /// an ImageGrid arrive as comma-separated paths in text.
    /// </summary>
    private static async Task<List<string>> ResolveDroppedPathsAsync(DataPackageView data)
    {
        var paths = new List<string>();

        if (data.Contains(StandardDataFormats.StorageItems))
        {
            foreach (var item in await data.GetStorageItemsAsync())
                if (item is StorageFile file)
                    paths.Add(file.Path);
        }

        if (paths.Count == 0 && data.Contains(StandardDataFormats.Text))
        {
            foreach (var token in (await data.GetTextAsync()).Split(','))
            {
                var trimmed = token.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    paths.Add(trimmed);
            }
        }

        return paths;
    }

    /// <summary>
    /// Handles a drop on the platform preview: a single supported video replaces the platform video; a single
    /// supported image prompts for the platform image type + Keep/Discard and is added/replaced; anything else
    /// (or more than one file) raises an informational message. Both operations report progress and are undoable.
    /// </summary>
    private async Task HandlePlatformDropAsync(Microsoft.UI.Xaml.DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        IsDragActive = false;

        var xamlRoot = _windowService.ActiveXamlRoot;
        var platform = _sharedDataService.SelectedPlatform;

        try
        {
            var paths = await ResolveDroppedPathsAsync(e.DataView);

            if (paths.Count == 0 || platform == null || xamlRoot == null)
                return;

            if (paths.Count > 1)
            {
                await _dialogsService.AlertAsync(xamlRoot, "Drop", "Only one file can be dropped at a time.", "OK");
                return;
            }

            var path = paths[0];
            var extension = Path.GetExtension(path).ToLowerInvariant();

            if (_appSettings.LaunchBox.AllowedVideoExtensions.Contains(extension))
            {
                IsImportingDrop = true;
                await _imageLoadingService.ReplacePlatformVideoAsync(platform, path);
                SelectOwnImageByType(MediaType.PlatformVideo);
            }
            else if (_appSettings.LaunchBox.AllowedImageExtensions.Contains(extension))
            {
                var choice = await _dialogsService.ShowPlatformImageDropAsync(xamlRoot);
                if (choice == null)
                    return;

                IsImportingDrop = true;
                var added = await _imageLoadingService.AddPlatformImageAsync(platform, choice.Value.Type, path, choice.Value.Discard);
                SelectOwnImageByFile(added?.File);
            }
            else
            {
                await _dialogsService.AlertAsync(xamlRoot, "Unsupported file", "The dropped file is not a supported image or video.", "OK");
            }
        }
        catch
        {
            // The copy/replace operations report their own progress/exception; swallow drop-resolution errors.
        }
        finally
        {
            IsImportingDrop = false;
            deferral.Complete();
        }
    }

    private async Task LoadSelectedPlatformImagesAsync()
    {
        var platform = _sharedDataService.SelectedPlatform;

        if (platform?.Logo != null)
        {
            await _imageBinaryLoadingService.LoadImageAsync(platform.Logo, ImageResolutionSettings.High);
            if (platform.Fanart != null)
            {
                await _imageBinaryLoadingService.LoadImageAsync(platform.Fanart, ImageResolutionSettings.High);
            }
        }

        // The platform's own images (banner, boxes, device, fanart...) feed both the thumbnail strip and
        // the framed preview, so they are decoded at high resolution for the larger preview. The platform
        // video is skipped: it is played, not decoded as a bitmap (LoadImageAsync would throw on it).
        if (platform != null)
        {
            foreach (var image in platform.OwnImages.Where(i => !MediaType.IsPlatformVideo(i.Type?.Key ?? -1)))
                await _imageBinaryLoadingService.LoadImageAsync(image, ImageResolutionSettings.High);

            // The video shows a still frame in the strip (extracted once); it is played in the preview.
            var video = platform.OwnImages.FirstOrDefault(i => MediaType.IsPlatformVideo(i.Type?.Key ?? -1));
            if (video != null)
                await _imageBinaryLoadingService.LoadVideoThumbnailAsync(video);
        }
    }

    /// <summary>
    /// Recalcula todos los datos de cobertura de tipos propios: el resumen (cobertura de la plataforma + media de
    /// todas) y las cuatro gráficas (área por plataforma, distribución, % por tipo y nº de imágenes por tipo). Todo
    /// es cómputo barato en memoria sobre <see cref="Platform.OwnImages"/> (ya poblado en la carga inicial), así que
    /// no hace falta hilo de fondo. <paramref name="animate"/> solo afecta a la gráfica de área.
    /// </summary>
    private void RefreshCoverageStats(bool animate)
    {
        BuildCoverageByPlatformChart(animate);
        BuildGlobalCoverageCharts();
    }

    /// <summary>
    /// Al cambiar el tema en caliente, reconstruye las gráficas: sus series se pintan con colores del tema (SKColor)
    /// horneados al construirlas. Reutiliza los datos ya cacheados (cómputo barato en memoria).
    /// </summary>
    private void OnThemeChanged(object? sender, System.EventArgs e)
    {
        // PlatformDetails no es un widget de slot (es un panel fijo), así que aquí no se filtra por SlotIndex: se
        // reconstruyen las series (que hornean los colores del tema) siempre. Cómputo barato en memoria.
        RefreshCoverageStats(animate: false);
    }

    /// <summary>
    /// Construye las 3 gráficas que NO dependen de la plataforma seleccionada (distribución, % por tipo y nº de
    /// imágenes por tipo) y marca que ya se han construido si había plataformas.
    /// </summary>
    private void BuildGlobalCoverageCharts()
    {
        BuildCoverageDistributionChart();
        BuildCoverageByTypeChart();
        BuildImagesByTypeChart();

        IReadOnlyList<Platform> platforms = _sharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();
        _globalCoverageChartsBuilt = platforms.Count > 0;
    }

    /// <summary>
    /// Construye las gráficas globales si aún no se han construido y ya hay plataformas. Cubre el caso de que el VM
    /// se cree antes de cargar las plataformas: las gráficas globales no cambian al seleccionar plataforma, así que
    /// solo hay que poblarlas la primera vez que haya datos.
    /// </summary>
    private void EnsureGlobalCoverageChartsBuilt()
    {
        if (_globalCoverageChartsBuilt)
            return;

        IReadOnlyList<Platform> platforms = _sharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();
        if (platforms.Count == 0)
            return;

        BuildGlobalCoverageCharts();
    }

    /// <summary>Cobertura (0..1) de tipos propios de cada plataforma cargada, en el orden de <c>PlatformSet.Platforms</c>.</summary>
    private IReadOnlyList<(Platform Platform, double Coverage)> GetPerPlatformOwnCoverage()
    {
        IReadOnlyList<Platform> platforms = _sharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();
        var result = new (Platform, double)[platforms.Count];
        for (int i = 0; i < platforms.Count; i++)
            result[i] = (platforms[i], _statisticsService.GetPlatformOwnImageCoverageRatio(platforms[i]));
        return result;
    }

    /// <summary>
    /// Construye la gráfica de área "Coverage - Platforms (own image types)": un punto por plataforma en el eje X
    /// (sin etiquetas), cobertura 0..100 de los tipos de imagen propios de cada plataforma en el eje Y (fracción de
    /// los 8 tipos especiales —banner, default 3D/flat boxes y carts, device, fanart y vídeo— de los que tiene al
    /// menos una imagen propia), y la plataforma seleccionada resaltada con un punto y una línea vertical punteada.
    /// Misma forma que <c>StatsPlatformViewModel.BuildCoverageByGameChart</c>, pero por plataforma. Cómputo barato
    /// en memoria (lee <see cref="Platform.OwnImages"/>, ya poblado para todas las plataformas en la carga inicial).
    /// </summary>
    private void BuildCoverageByPlatformChart(bool animate)
    {
        // Se fija ANTES de tocar las series: el code-behind ajusta la velocidad de animación al recibir esta
        // propiedad, de modo que la actualización de Series/Sections que viene a continuación se dibuje (o no) con animación.
        AnimateCoverageByPlatformChart = animate;

        IReadOnlyList<(Platform Platform, double Coverage)> perPlatform = GetPerPlatformOwnCoverage();
        if (perPlatform.Count == 0)
        {
            ClearCoverageByPlatformChart();
            return;
        }

        Platform? selected = _sharedDataService.SelectedPlatform;

        // Snapshot que coincide con los valores de estas series, para resolver el nombre de la plataforma en el tooltip.
        IReadOnlyList<(Platform Platform, double Coverage)> snapshot = perPlatform;

        double[] percents = new double[snapshot.Count];
        int selectedIndex = -1;
        for (int i = 0; i < snapshot.Count; i++)
        {
            percents[i] = snapshot[i].Coverage * 100.0;
            if (selected != null && ReferenceEquals(snapshot[i].Platform, selected))
            {
                selectedIndex = i;
            }
        }

        (SKColor accent, SKColor accentLight, SKColor text) = ResolveThemeColors();

        CoverageChartVisual visual = CoverageChartBuilder.Build(
            percents, selectedIndex, i => snapshot[i].Platform?.Name ?? string.Empty, accent, accentLight, text);

        CoverageByPlatformSeries = visual.Series;
        CoverageByPlatformXAxes = visual.XAxes;
        CoverageByPlatformYAxes = visual.YAxes;
        CoverageByPlatformSections = visual.Sections;
        HasCoverageByPlatformData = true;
    }

    /// <summary>Vacía la gráfica de cobertura por plataforma (sin plataformas cargadas).</summary>
    private void ClearCoverageByPlatformChart()
    {
        CoverageByPlatformSeries = Array.Empty<ISeries>();
        CoverageByPlatformXAxes = Array.Empty<ICartesianAxis>();
        CoverageByPlatformYAxes = Array.Empty<ICartesianAxis>();
        CoverageByPlatformSections = Array.Empty<RectangularSection>();
        HasCoverageByPlatformData = false;
    }

    /// <summary>
    /// Histograma de distribución de cobertura: cuántas plataformas caen en cada tramo de cobertura de tipos propios
    /// (0–20 %, …, 80–100 %). Análogo al "Coverage distribution" del StatsPlatform, pero por plataforma.
    /// </summary>
    private void BuildCoverageDistributionChart()
    {
        IReadOnlyList<(Platform Platform, double Coverage)> perPlatform = GetPerPlatformOwnCoverage();
        if (perPlatform.Count == 0)
        {
            CoverageDistributionValues = Array.Empty<double>();
            CoverageDistributionLabels = Array.Empty<string>();
            return;
        }

        string[] labels = { "0–20%", "20–40%", "40–60%", "60–80%", "80–100%" };
        var counts = new double[labels.Length];
        foreach ((Platform _, double coverage) in perPlatform)
        {
            int bucket = (int)(coverage * 100 / 20);   // [0,20)->0, …; 100 % cae en el último tramo
            if (bucket < 0) bucket = 0;
            if (bucket >= labels.Length) bucket = labels.Length - 1;
            counts[bucket]++;
        }

        CoverageDistributionValues = counts;
        CoverageDistributionLabels = labels;
    }

    /// <summary>
    /// Datos de "Coverage - Platform image type": por cada tipo de imagen propia (eje X fijo), el % de plataformas
    /// que tienen al menos una imagen propia de ese tipo. Análogo al "Coverage - Image type" del StatsPlatform, pero
    /// con plataformas como población y los tipos propios como eje.
    /// </summary>
    private void BuildCoverageByTypeChart()
    {
        IReadOnlyList<Platform> platforms = _sharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();
        if (platforms.Count == 0)
        {
            CoverageByTypeValues = Array.Empty<double>();
            CoverageByTypeLabels = Array.Empty<string>();
            return;
        }

        Dictionary<int, int> platformCountByType = _statisticsService.GetOwnImagePlatformCountByType(platforms);

        var values = new double[OwnImageTypes.Count];
        var labels = new string[OwnImageTypes.Count];
        for (int i = 0; i < OwnImageTypes.Count; i++)
        {
            int covered = platformCountByType.TryGetValue(OwnImageTypes[i].Key, out int c) ? c : 0;
            values[i] = 100.0 * covered / platforms.Count;
            labels[i] = OwnImageTypes[i].Value;
        }

        CoverageByTypeValues = values;
        CoverageByTypeLabels = labels;
    }

    /// <summary>
    /// Datos de "Own images - Platform image type": por cada tipo de imagen propia (eje X fijo), el nº total de
    /// ficheros de imagen propios de ese tipo en TODAS las plataformas. La línea de referencia (nº de plataformas)
    /// marca "una imagen por plataforma". Análogo al "Image set - Image type" del StatsPlatform.
    /// </summary>
    private void BuildImagesByTypeChart()
    {
        IReadOnlyList<Platform> platforms = _sharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();
        PlatformCount = platforms.Count;

        if (platforms.Count == 0)
        {
            ImagesByTypeValues = Array.Empty<double>();
            ImagesByTypeLabels = Array.Empty<string>();
            return;
        }

        Dictionary<int, int> fileCountByType = _statisticsService.GetOwnImageFileCountByType(platforms);

        var values = new double[OwnImageTypes.Count];
        var labels = new string[OwnImageTypes.Count];
        for (int i = 0; i < OwnImageTypes.Count; i++)
        {
            values[i] = fileCountByType.TryGetValue(OwnImageTypes[i].Key, out int count) ? count : 0;
            labels[i] = OwnImageTypes[i].Value;
        }

        ImagesByTypeValues = values;
        ImagesByTypeLabels = labels;
    }

    /// <summary>Resuelve los colores de acento y texto del tema activo para tematizar la gráfica.</summary>
    private (SKColor accent, SKColor accentLight, SKColor text) ResolveThemeColors()
        => (ToSk(_themeService.AccentColor), ToSk(_themeService.AccentLightColor), ToSk(_themeService.TextColor));

    private static SKColor ToSk(Windows.UI.Color c) => new(c.R, c.G, c.B, c.A);
    #endregion

    #region Methods (public)
    /// <summary>
    /// Restaura la gráfica activa del FlipView y la configuración (tipo / orden / Top X) de las tres gráficas con
    /// toolbar. La llama el control una vez restaurados los ajustes de disco.
    /// </summary>
    public override void LoadConfig()
    {
        AppSettings.PlatformDetailsControlSettings config = _appSettings.PlatformDetailsControl;
        if (config != null)
        {
            SelectedChartIndex = config.SelectedChartIndex;
            CoverageDistributionChart.ApplyFrom(config.CoverageDistributionChart);
            CoverageByTypeChart.ApplyFrom(config.CoverageByTypeChart);
            ImagesByTypeChart.ApplyFrom(config.ImagesByTypeChart);
        }
    }

    /// <summary>
    /// Vuelca la gráfica activa y la configuración de las tres gráficas con toolbar en los ajustes.
    /// </summary>
    public override void SaveConfig()
    {
        AppSettings.PlatformDetailsControlSettings config = _appSettings.PlatformDetailsControl;
        config.SelectedChartIndex = SelectedChartIndex;
        CoverageDistributionChart.StoreTo(config.CoverageDistributionChart);
        CoverageByTypeChart.StoreTo(config.CoverageByTypeChart);
        ImagesByTypeChart.StoreTo(config.ImagesByTypeChart);
    }

    /// <summary>
    /// Libera recursos y desuscribe eventos.
    /// </summary>
    public override void Dispose()
    {
        _sharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged;
        _imageLoadingService.PlatformImagesChanged -= OnPlatformImagesChanged;
    }
    #endregion
}