using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del widget <c>GameImagesRegionDashboard</c>: gestiona las imágenes del juego seleccionado POR
/// REGIÓN. Casi idéntico al <see cref="GameImagesDashboardViewModel"/>, pero las miniaturas son las de la REGIÓN
/// activa (elegida en un selector de buckets: regiones favoritas de appSettings + "otras regiones" + "sin
/// región") y la preselección de la imagen principal se hace por región. La imagen principal se SINCRONIZA con el
/// estado global (<see cref="SharedDataService.SelectedImage"/>).
///
/// FASE C: miniaturas + preview + preselección por región + layout/vídeo + borrar. El drag&amp;drop de import y el
/// procesado por región llegan después (fases C.2 / D del plan).
/// </summary>
public class GameImagesRegionDashboardViewModel : WidgetViewModelBase
{
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
    private AsyncRelayCommand? _processRegionCommand;
    private AsyncRelayCommand? _processPreviousGameCommand;
    private AsyncRelayCommand? _processNextGameCommand;
    private AsyncRelayCommand? _openSettingsCommand;

    private double _thumbnailPanelWidth = DefaultThumbnailPanelWidth;
    private double _thumbnailPanelHeight = DefaultThumbnailPanelHeight;
    private double _videoVolume = DefaultVideoVolume;
    private bool _isMuted;
    private bool _isImportingDrop;
    private bool _isDragActive;
    private bool _isSearchStringsPanelVisible;
    private GameImagesDashboardViewModel.GameImagesDashboardLayout _selectedLayout = GameImagesDashboardViewModel.GameImagesDashboardLayout.Horizontal;
    private VideoDownloadQualitySettings _videoDownloadQuality = VideoDownloadQualitySettings.P1080;

    private int _regionLoadVersion;
    private Game? _inFlightGameLoad;

    /// <summary>Regiones favoritas (máx. 3), leídas de la configuración en <see cref="LoadConfig"/>.</summary>
    private ImageRegion[] _favouriteRegions = Array.Empty<ImageRegion>();

    /// <summary>Criterios de preselección del medio principal (por región). Propios de este widget.</summary>
    private List<GameImageCriterion> _selectionCriteria = new();

    /// <summary>Criterios de procesado del conservado (keep-region fijado). Propios de este widget.</summary>
    private List<GameImageCriterion> _processingCriteria = new();

    /// <summary>Si al procesar se borran TODAS las imágenes del bucket "otras regiones" (no favoritas).</summary>
    private bool _purgeNonFavouriteRegions = true;
    #endregion

    #region Properties (region)
    /// <summary>Los elementos fijos del selector: favoritas + "otras regiones" + "sin región".</summary>
    public ObservableCollection<RegionBucket> RegionBuckets { get; } = new();

    /// <summary>
    /// Máximo de columnas del selector = nº de buckets. Limita el UniformGridLayout para que, con el widget ancho,
    /// no cree más columnas que items (dejando huecos): así los items siempre reparten TODO el ancho por igual, y
    /// solo hacen wrap cuando no caben a su ancho mínimo.
    /// </summary>
    public int SelectorMaxColumns => Math.Max(1, RegionBuckets.Count);

    private RegionBucket? _selectedBucket;
    /// <summary>Bucket (región) activo. Lo escribe el selector (TwoWay) y se fija por defecto al refrescar.</summary>
    public RegionBucket? SelectedBucket
    {
        get => _selectedBucket;
        set
        {
            if (!ReferenceEquals(_selectedBucket, value))
            {
                ApplyActiveBucket(value, preselect: true);
            }
        }
    }

    /// <summary>Imágenes de la región activa (fuente de las miniaturas).</summary>
    public ObservableCollection<GameImage> RegionGameImages { get; } = new();
    #endregion

    #region Properties (drag & drop)
    /// <summary>True mientras se resuelve un drop (copia local o descarga web). Gobierna el overlay de import.</summary>
    public bool IsImportingDrop
    {
        get => _isImportingDrop;
        set => SetProperty(ref _isImportingDrop, value);
    }

    /// <summary>True mientras hay un drag activo sobre el widget (muestra la zona de drop).</summary>
    public bool IsDragActive
    {
        get => _isDragActive;
        set => SetProperty(ref _isDragActive, value);
    }
    #endregion

    #region Properties (viewing/video, espejo del dashboard)
    public bool IsHorizontalView
    {
        get => _selectedLayout == GameImagesDashboardViewModel.GameImagesDashboardLayout.Horizontal;
        set { if (value) { SelectHorizontalView(); } else { SelectVerticalView(); } }
    }

    public bool IsVerticalView
    {
        get => _selectedLayout == GameImagesDashboardViewModel.GameImagesDashboardLayout.Vertical;
        set { if (value) { SelectVerticalView(); } else { SelectHorizontalView(); } }
    }

    public bool IsSearchStringsPanelVisible
    {
        get => _isSearchStringsPanelVisible;
        set => SetProperty(ref _isSearchStringsPanelVisible, value);
    }

    public bool IsVideoQuality240 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P240);
    public bool IsVideoQuality360 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P360);
    public bool IsVideoQuality480 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P480);
    public bool IsVideoQuality720 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P720);
    public bool IsVideoQuality1080 => Equals(_videoDownloadQuality, VideoDownloadQualitySettings.P1080);

    /// <summary>Valor del grupo de layout para el ExclusiveOptionsControl ("Horizontal"/"Vertical"). TwoWay.</summary>
    public string SelectedLayoutValue
    {
        get => _selectedLayout == GameImagesDashboardViewModel.GameImagesDashboardLayout.Horizontal ? "Horizontal" : "Vertical";
        set
        {
            GameImagesDashboardViewModel.GameImagesDashboardLayout layout = value == "Vertical"
                ? GameImagesDashboardViewModel.GameImagesDashboardLayout.Vertical
                : GameImagesDashboardViewModel.GameImagesDashboardLayout.Horizontal;
            if (_selectedLayout != layout)
            {
                SelectLayout(layout);
                OnPropertyChanged(nameof(SelectedLayoutValue));
            }
        }
    }

    /// <summary>Valor del grupo de calidad de vídeo para el ExclusiveOptionsControl ("240".."1080"). TwoWay.</summary>
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

    /// <summary>Si el set seleccionado es de vídeo (gobierna la visibilidad de las opciones de resolución).</summary>
    public bool IsVideoSetSelected => IsSelectedSetVideo();

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

    public double EffectiveVideoVolume => IsMuted ? 0 : VideoVolume;
    public bool IsSoundOff => IsMuted || VideoVolume <= 0;

    /// <summary>Si el preview de vídeo muestra los controles de reproducción (setting propio del widget).</summary>
    public bool ShowVideoControls => _appSettings.GameImagesRegionDashboardControl?.ShowVideoControls ?? false;

    public double ThumbnailPanelWidth
    {
        get => _thumbnailPanelWidth;
        set => SetProperty(ref _thumbnailPanelWidth, NormalizeThumbnailPanelWidth(value));
    }

    public double ThumbnailPanelHeight
    {
        get => _thumbnailPanelHeight;
        set => SetProperty(ref _thumbnailPanelHeight, NormalizeThumbnailPanelHeight(value));
    }
    #endregion

    #region Properties (process-game pill)
    /// <summary>Título de la mitad izquierda ("process &amp; previous"): juego anterior, o el actual en el borde.</summary>
    public string PreviousGameTitle => (GetAdjacentGame(-1) ?? _sharedDataService.SelectedGame)?.Title ?? string.Empty;

    /// <summary>Título de la mitad derecha ("process &amp; next"): juego siguiente, o el actual en el borde.</summary>
    public string NextGameTitle => (GetAdjacentGame(1) ?? _sharedDataService.SelectedGame)?.Title ?? string.Empty;

    private bool IsAtListStart => GetAdjacentGame(-1) is null;
    private bool IsAtListEnd => GetAdjacentGame(1) is null;

    /// <summary>True si el juego tiene más de un medio (procesar es útil).</summary>
    private bool HasMultipleMedia => (_sharedDataService.SelectedGame?.Images.Count ?? 0) > 1;

    public bool IsProcessPreviousEnabled => _sharedDataService.SelectedGame != null && (HasMultipleMedia || !IsAtListStart);
    public bool IsProcessNextEnabled => _sharedDataService.SelectedGame != null && (HasMultipleMedia || !IsAtListEnd);
    public bool IsProcessPreviousDisabled => !IsProcessPreviousEnabled;
    public bool IsProcessNextDisabled => !IsProcessNextEnabled;

    /// <summary>True si el bucket activo tiene algo que procesar: con 1 o 0 imágenes no hay nada que hacer
    /// (procesar conserva una y borra el resto), así que se exige más de 1.</summary>
    public bool IsProcessRegionEnabled => (_selectedBucket?.Count ?? 0) > 1;
    #endregion

    #region Commands
    /// <summary>Borra el medio seleccionado (con confirmación y undo), como en el dashboard normal.</summary>
    public AsyncRelayCommand DeleteSelectedImageCommand =>
        _deleteSelectedImageCommand ??= new AsyncRelayCommand(DeleteSelectedImageAsync, () => _sharedDataService.SelectedImage != null);

    /// <summary>Procesa SOLO la región activa (sin cambiar de juego): conserva una por región y borra el resto.</summary>
    public AsyncRelayCommand ProcessRegionCommand =>
        _processRegionCommand ??= new AsyncRelayCommand(ProcessActiveRegionAsync, () => IsProcessRegionEnabled);

    /// <summary>Procesa TODAS las regiones del juego y navega al anterior.</summary>
    public AsyncRelayCommand ProcessPreviousGameCommand =>
        _processPreviousGameCommand ??= new AsyncRelayCommand(() => ProcessAllRegionsAndNavigateAsync(-1), () => IsProcessPreviousEnabled);

    /// <summary>Procesa TODAS las regiones del juego y navega al siguiente.</summary>
    public AsyncRelayCommand ProcessNextGameCommand =>
        _processNextGameCommand ??= new AsyncRelayCommand(() => ProcessAllRegionsAndNavigateAsync(1), () => IsProcessNextEnabled);

    /// <summary>Abre el diálogo de settings del dashboard (criterios de preselección y de proceso por región).</summary>
    public AsyncRelayCommand OpenSettingsCommand => _openSettingsCommand ??= new AsyncRelayCommand(ShowSettingsAsync);

    private async Task ShowSettingsAsync()
    {
        // Oculta el criterio "Region": en el dashboard de regiones la región se conserva siempre (keep-region).
        var result = await _dialogsService.ShowDashboardSettingsAsync(
            _windowService.ActiveXamlRoot!, _selectionCriteria, _processingCriteria, hideRegionCriterion: true);

        if (result is not { } edited)
        {
            return;
        }

        _selectionCriteria = edited.Selection;
        _processingCriteria = edited.Processing;

        // Persistencia inmediata: vuelca a AppSettings (sección propia del widget) y guarda a disco.
        _appSettings.GameImagesRegionDashboardControl.ImageSelectionCriteria = _selectionCriteria.ToArray();
        _appSettings.GameImagesRegionDashboardControl.ImageProcessingCriteria = _processingCriteria.ToArray();
        App.GetService<PersistAndRestoreService>().PersistData();
    }
    #endregion

    #region Published events
    /// <summary>Cambio de imagen seleccionada (para el code-behind: ScrollIntoView, etc.).</summary>
    public delegate void ImageSelectionChangedEventHandler(GameImage image);
    public event ImageSelectionChangedEventHandler? ImageSelectionChanged;
    private void OnImageSelectionChanged(GameImage image) => ImageSelectionChanged?.Invoke(image);
    #endregion

    #region Constructor
    public GameImagesRegionDashboardViewModel(SharedDataService sharedDataService, ExceptionService exceptionService,
        ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService,
        DialogsService dialogsService, WindowService windowService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        _exceptionService = exceptionService;
        _imageLoadingService = imageLoadingService;
        _imageBinaryLoadingService = imageBinaryLoadingService;
        _dialogsService = dialogsService;
        _windowService = windowService;

        _sharedDataService.SelectedGameChanged += OnSelectedGameChanged;
        _sharedDataService.SelectedGameImagesChanged += OnSelectedGameImagesChanged;
        _sharedDataService.PropertyChanged += OnSharedDataServicePropertyChanged;
        _sharedDataService.GamesFiltered.CollectionChanged += OnGamesFilteredChanged;
        _sharedDataService.FavouriteRegionsChanged += OnFavouriteRegionsChanged;
        _imageLoadingService.ImageAddedToGame += OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame += OnImageRemovedFromGame;

        // Al ocultarse/mostrarse el widget (cambia su SlotIndex), re-evalúa la región activa publicada.
        PropertyChanged += OnOwnPropertyChanged;
    }
    #endregion

    #region Subscribed events
    /// <summary>Al cambiar el propio SlotIndex (mostrar/ocultar el widget), re-publica la región activa.</summary>
    private void OnOwnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlotIndex))
        {
            SyncSelectedRegion();
        }
    }

    /// <summary>
    /// Publica en <see cref="SharedDataService.SelectedRegion"/> la región de destino activa, PERO solo si este
    /// dashboard está visible (SlotIndex &gt;= 0): región favorita del bucket activo, o null (raíz) para "otras
    /// regiones"/"sin región". Si el widget no está visible, no gobierna el destino → null.
    /// </summary>
    private void SyncSelectedRegion()
    {
        bool active = SlotIndex >= 0;
        _sharedDataService.SelectedRegion = (active && _selectedBucket?.Kind == RegionBucketKind.Favourite)
            ? _selectedBucket.Region
            : null;
    }

    private async void OnSelectedGameChanged(object? sender, GameChangedEventArgs e) => await SafeRefreshRegionAsync();

    private async void OnSelectedGameImagesChanged(object? sender, GameImagesChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsVideoSetSelected));
        if (ReferenceEquals(e.Game, _sharedDataService.SelectedGame) || e.Game == null)
        {
            await SafeRefreshRegionAsync();
        }
    }

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

    /// <summary>Refresca el pill de proceso cuando cambia la lista filtrada (los bordes/títulos pueden variar).</summary>
    private void OnGamesFilteredChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshProcessNavigation();
    }

    private async void OnImageAddedToGame(Game game, GameImage image)
    {
        try
        {
            if (image is null || !ReferenceEquals(game, _sharedDataService.SelectedGame))
            {
                return;
            }

            await _imageBinaryLoadingService.LoadGameImageBinaryAsync(image, ImageResolutionSettings.High);
            ClassifyBuckets(game);
            ApplyActiveBucket(ChooseActiveBucket(), preselect: false);
            RefreshProcessNavigation();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameImagesRegionDashboard_LoadAddedImage_Error] ?? "Error loading the added game image.");
        }
    }

    private void OnImageRemovedFromGame(Game game, GameImage image)
    {
        if (image is null || !ReferenceEquals(game, _sharedDataService.SelectedGame))
        {
            return;
        }

        ClassifyBuckets(game);
        // Si la imagen borrada era la principal, hay que re-preseleccionar en la región activa.
        GameImage? current = _sharedDataService.SelectedImage;
        bool selectionStillValid = current != null && RegionGameImages.Any(i => ReferenceEquals(i, current));
        ApplyActiveBucket(ChooseActiveBucket(), preselect: !selectionStillValid);
        RefreshProcessNavigation();
    }
    #endregion

    #region Methods (region)
    /// <summary>Construye los buckets fijos del selector desde las regiones favoritas + "otras" + "sin región".</summary>
    private void BuildRegionBuckets()
    {
        RegionBuckets.Clear();
        _selectedBucket = null;

        foreach (ImageRegion region in _favouriteRegions.Take(3))
        {
            RegionBuckets.Add(new RegionBucket(region.Value, RegionBucketKind.Favourite, region));
        }

        RegionBuckets.Add(new RegionBucket(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Common_OtherRegions_Label] ?? "Other regions", RegionBucketKind.OtherRegions));
        RegionBuckets.Add(new RegionBucket(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Common_NoRegion_Label] ?? "No region", RegionBucketKind.NoRegion));

        OnPropertyChanged(nameof(SelectorMaxColumns));
    }

    /// <summary>Reparte las imágenes de <paramref name="game"/> en los buckets y actualiza sus conteos.</summary>
    private void ClassifyBuckets(Game? game)
    {
        if (RegionBuckets.Count == 0)
        {
            BuildRegionBuckets();
        }

        var favouriteByKey = new Dictionary<int, RegionBucket>();
        RegionBucket? otherBucket = null;
        RegionBucket? noRegionBucket = null;
        foreach (RegionBucket bucket in RegionBuckets)
        {
            bucket.Images.Clear();
            switch (bucket.Kind)
            {
                case RegionBucketKind.Favourite when bucket.Region != null:
                    favouriteByKey[bucket.Region.Key] = bucket;
                    break;
                case RegionBucketKind.OtherRegions:
                    otherBucket = bucket;
                    break;
                case RegionBucketKind.NoRegion:
                    noRegionBucket = bucket;
                    break;
            }
        }

        if (game != null)
        {
            foreach (GameImage image in game.Images)
            {
                ImageRegion region = image.Region;
                if (region.Key == ImageRegion.NoRegion.Key)
                {
                    noRegionBucket?.Images.Add(image);
                }
                else if (favouriteByKey.TryGetValue(region.Key, out RegionBucket? favBucket))
                {
                    favBucket.Images.Add(image);
                }
                else
                {
                    otherBucket?.Images.Add(image);
                }
            }
        }

        foreach (RegionBucket bucket in RegionBuckets)
        {
            bucket.Count = bucket.Images.Count;
        }
    }

    /// <summary>Bucket activo objetivo: se conserva el actual si aún tiene imágenes; si no, el primero con imágenes.</summary>
    private RegionBucket? ChooseActiveBucket() =>
        (_selectedBucket != null && _selectedBucket.Count > 0)
            ? _selectedBucket
            : RegionBuckets.FirstOrDefault(b => b.Count > 0) ?? RegionBuckets.FirstOrDefault();

    private async Task SafeRefreshRegionAsync()
    {
        try
        {
            await RefreshRegionAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameImagesRegionDashboard_Refresh_Error] ?? "Error refreshing the region dashboard.");
        }
    }

    /// <summary>
    /// Refresco completo al cambiar de juego o de tipo: carga los binarios alta-res del juego, reparte en buckets,
    /// elige el bucket por defecto y preselecciona la imagen principal de la región. Coalescing por juego, como el
    /// dashboard normal, para no decodificar dos veces en el arranque.
    /// </summary>
    private async Task RefreshRegionAsync()
    {
        Game? game = _sharedDataService.SelectedGame;

        if (game != null && ReferenceEquals(_inFlightGameLoad, game))
        {
            return;
        }

        int version = ++_regionLoadVersion;

        if (game == null)
        {
            ClassifyBuckets(null);
            ApplyActiveBucket(ChooseActiveBucket(), preselect: true);
            return;
        }

        _inFlightGameLoad = game;
        try
        {
            await _imageBinaryLoadingService.LoadGameHighResImageBinariesAsync(game);

            if (version != _regionLoadVersion)
            {
                return;
            }

            ClassifyBuckets(game);
            ApplyActiveBucket(ChooseActiveBucket(), preselect: true);
        }
        finally
        {
            if (ReferenceEquals(_inFlightGameLoad, game))
            {
                _inFlightGameLoad = null;
            }
        }
    }

    /// <summary>
    /// Fija el bucket activo: lo resalta, reconstruye <see cref="RegionGameImages"/> y, si <paramref name="preselect"/>,
    /// preselecciona la imagen principal de la región (sincronizando <see cref="SharedDataService.SelectedImage"/>).
    /// </summary>
    private void ApplyActiveBucket(RegionBucket? bucket, bool preselect)
    {
        _selectedBucket = bucket;
        OnPropertyChanged(nameof(SelectedBucket));

        // Publica la región activa de destino para los nuevos medios (solo si este dashboard está visible).
        SyncSelectedRegion();

        foreach (RegionBucket b in RegionBuckets)
        {
            b.IsSelected = ReferenceEquals(b, bucket);
        }

        RegionGameImages.Clear();
        if (bucket != null)
        {
            foreach (GameImage image in bucket.Images)
            {
                RegionGameImages.Add(image);
            }
        }

        if (preselect)
        {
            PreselectForActiveRegion();
        }

        OnPropertyChanged(nameof(IsProcessRegionEnabled));
        _processRegionCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>Juego a un offset del seleccionado en la lista filtrada, o null en los bordes.</summary>
    private Game? GetAdjacentGame(int direction)
    {
        Game? game = _sharedDataService.SelectedGame;
        if (game is null)
        {
            return null;
        }

        int index = _sharedDataService.GamesFiltered.IndexOf(game);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= _sharedDataService.GamesFiltered.Count)
        {
            return null;
        }

        return _sharedDataService.GamesFiltered[target];
    }

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
    /// Calcula, para un bucket, los medios a CONSERVAR (uno preseleccionado por región) y a BORRAR, según las
    /// reglas: favoritas y "sin región" conservan uno; "otras regiones" se purgan (borrar todo) o, si no,
    /// conservan uno por cada región distinta. Acumula sobre las listas recibidas.
    /// </summary>
    private void AccumulateKeepDelete(RegionBucket bucket, List<GameImage> keep, List<GameImage> delete)
    {
        if (bucket.Images.Count == 0)
        {
            return;
        }

        if (bucket.Kind == RegionBucketKind.OtherRegions)
        {
            if (_purgeNonFavouriteRegions)
            {
                delete.AddRange(bucket.Images);
            }
            else
            {
                // Conservar uno por cada región distinta del bucket.
                foreach (IGrouping<int, GameImage> group in bucket.Images.GroupBy(i => i.Region.Key))
                {
                    GameImage kept = GameImagePreselection.Preselect(group, _selectionCriteria);
                    keep.Add(kept);
                    delete.AddRange(group.Where(i => !ReferenceEquals(i, kept)));
                }
            }
            return;
        }

        // Favorita o "sin región": conservar uno, borrar el resto.
        GameImage keepImage = GameImagePreselection.Preselect(bucket.Images, _selectionCriteria);
        keep.Add(keepImage);
        delete.AddRange(bucket.Images.Where(i => !ReferenceEquals(i, keepImage)));
    }

    /// <summary>Procesa solo la región (bucket) activa, sin cambiar de juego.</summary>
    private async Task ProcessActiveRegionAsync()
    {
        Game? game = _sharedDataService.SelectedGame;
        RegionBucket? bucket = _selectedBucket;
        if (game == null || bucket == null || bucket.Count == 0)
        {
            return;
        }

        var keep = new List<GameImage>();
        var delete = new List<GameImage>();
        AccumulateKeepDelete(bucket, keep, delete);

        if (keep.Count == 0 && delete.Count == 0)
        {
            return;
        }

        await _imageLoadingService.ProcessGameMediaAsync(game, keep, delete, _processingCriteria);
    }

    /// <summary>Procesa TODAS las regiones del juego (una por región + purga de no favoritas) y navega.</summary>
    private async Task ProcessAllRegionsAndNavigateAsync(int direction)
    {
        Game? game = _sharedDataService.SelectedGame;
        if (game == null)
        {
            return;
        }

        var keep = new List<GameImage>();
        var delete = new List<GameImage>();
        foreach (RegionBucket bucket in RegionBuckets)
        {
            AccumulateKeepDelete(bucket, keep, delete);
        }

        if (keep.Count > 0 || delete.Count > 0)
        {
            await _imageLoadingService.ProcessGameMediaAsync(game, keep, delete, _processingCriteria);
        }

        // Navega al juego anterior/siguiente (se queda en el borde de la lista).
        int index = _sharedDataService.GamesFiltered.IndexOf(game);
        if (index >= 0)
        {
            int target = index + direction;
            if (target >= 0 && target < _sharedDataService.GamesFiltered.Count)
            {
                _sharedDataService.SelectedGame = _sharedDataService.GamesFiltered[target];
            }
        }
    }

    /// <summary>
    /// Preselecciona la imagen principal dentro de la región activa: si la selección global actual pertenece a la
    /// región se conserva (evita parpadeo al refrescar la misma región); si no, se elige por criterios.
    /// </summary>
    private void PreselectForActiveRegion()
    {
        if (RegionGameImages.Count == 0)
        {
            _sharedDataService.SelectedImage = new();
            return;
        }

        GameImage? current = _sharedDataService.SelectedImage;
        if (current != null && RegionGameImages.Any(i => ReferenceEquals(i, current)))
        {
            return;
        }

        _sharedDataService.SelectedImage = GameImagePreselection.Preselect(RegionGameImages, _selectionCriteria);
    }
    #endregion

    #region Methods (viewing/video helpers, espejo del dashboard)
    private static double NormalizeThumbnailPanelWidth(double value) =>
        (double.IsNaN(value) || double.IsInfinity(value)) ? DefaultThumbnailPanelWidth : Math.Clamp(value, MinThumbnailPanelWidth, MaxThumbnailPanelWidth);

    private static double NormalizeThumbnailPanelHeight(double value) =>
        (double.IsNaN(value) || double.IsInfinity(value)) ? DefaultThumbnailPanelHeight : Math.Clamp(value, MinThumbnailPanelHeight, MaxThumbnailPanelHeight);

    private static double NormalizeVideoVolume(double value) =>
        (double.IsNaN(value) || double.IsInfinity(value)) ? DefaultVideoVolume : Math.Clamp(value, MinVideoVolume, MaxVideoVolume);

    /// <summary>True si el set seleccionado es un tipo de vídeo (Video Snap / Theme Video / vídeo de plataforma).</summary>
    private bool IsSelectedSetVideo()
    {
        int? key = _sharedDataService.SelectedImageSet?.Type?.Key;
        return key.HasValue && (MediaType.IsVideo(key.Value) || MediaType.IsPlatformVideo(key.Value));
    }

    private void SelectLayout(GameImagesDashboardViewModel.GameImagesDashboardLayout layout)
    {
        if (_selectedLayout == layout)
        {
            return;
        }

        _selectedLayout = layout;
        OnPropertyChanged(nameof(IsHorizontalView));
        OnPropertyChanged(nameof(IsVerticalView));
    }

    private void SelectVideoQuality(VideoDownloadQualitySettings quality)
    {
        if (Equals(_videoDownloadQuality, quality))
        {
            return;
        }

        _videoDownloadQuality = quality;
        if (_appSettings.GameImagesRegionDashboardControl is not null)
        {
            _appSettings.GameImagesRegionDashboardControl.VideoDownloadQuality = quality;
        }

        OnPropertyChanged(nameof(IsVideoQuality240));
        OnPropertyChanged(nameof(IsVideoQuality360));
        OnPropertyChanged(nameof(IsVideoQuality480));
        OnPropertyChanged(nameof(IsVideoQuality720));
        OnPropertyChanged(nameof(IsVideoQuality1080));
    }

    private async Task DeleteSelectedImageAsync()
    {
        GameImage? image = _sharedDataService.SelectedImage;
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
    #endregion

    #region Drag & drop (import de medios, espejo del dashboard)
    public void GameImages_DragOver(object? sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;

    public void Dashboard_DragEnter(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        IsDragActive = true;
    }

    public void Dashboard_DragLeave(object? sender, DragEventArgs e) => IsDragActive = false;

    public void Dashboard_Drop(object? sender, DragEventArgs e) => IsDragActive = false;

    public async void SelectedImage_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            await HandleImageDropAsync(e, droppedOnSelectedImage: true);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameImagesRegionDashboard_AddDroppedMedia_Error] ?? "Error adding the dropped media.");
        }
    }

    /// <summary>
    /// Resuelve un drop (copia/descarga los medios y los registra en el juego, lo que dispara la reclasificación
    /// por región vía <see cref="OnImageAddedToGame"/>). Si se soltó sobre el preview, activa el bucket del primer
    /// medio y lo selecciona.
    /// </summary>
    private async Task HandleImageDropAsync(DragEventArgs e, bool droppedOnSelectedImage)
    {
        var deferral = e.GetDeferral();
        IsDragActive = false;
        IsImportingDrop = true;

        try
        {
            List<GameImage> droppedImages = await ResolveDroppedImagesAsync(e.DataView);

            // Reclasifica por si el evento de alta aún no lo hizo (idempotente con OnImageAddedToGame).
            ClassifyBuckets(_sharedDataService.SelectedGame);

            if (droppedOnSelectedImage && droppedImages.FirstOrDefault() is GameImage imageToSelect)
            {
                SelectImageAcrossRegions(imageToSelect);
            }
            else
            {
                ApplyActiveBucket(ChooseActiveBucket(), preselect: false);
            }

            RefreshProcessNavigation();
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

    /// <summary>Activa el bucket que contiene <paramref name="image"/> (por si es de otra región) y lo selecciona.</summary>
    private void SelectImageAcrossRegions(GameImage image)
    {
        RegionBucket? bucket = RegionBuckets.FirstOrDefault(b => b.Images.Any(i => ReferenceEquals(i, image)));
        if (bucket != null && !ReferenceEquals(bucket, _selectedBucket))
        {
            ApplyActiveBucket(bucket, preselect: false);
        }
        else
        {
            ApplyActiveBucket(ChooseActiveBucket(), preselect: false);
        }

        SelectImage(image);
    }

    private async Task<List<GameImage>> ResolveDroppedImagesAsync(DataPackageView data)
    {
        List<GameImage> resolvedImages = new();

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

        if (IsSelectedSetVideo())
        {
            return resolvedImages;
        }

        if (resolvedImages.Count == 0 && data.Contains(StandardDataFormats.Text))
        {
            string droppedText = await data.GetTextAsync();
            foreach (string token in droppedText.Split(','))
            {
                await AddResolvedImageAsync(resolvedImages, token.Trim());
            }
        }

        if (resolvedImages.Count == 0)
        {
            foreach (string url in await GetDroppedWebUrlsAsync(data))
            {
                await AddResolvedImageAsync(resolvedImages, url);
            }
        }

        if (resolvedImages.Count == 0 && data.Contains(StandardDataFormats.Html))
        {
            string html = await data.GetHtmlFormatAsync();
            string? imageSource = Utilities.GetImageTagSource(html).FirstOrDefault();
            await AddResolvedImageAsync(resolvedImages, imageSource);
        }

        return resolvedImages;
    }

    private bool IsImageFile(string path) =>
        _appSettings.LaunchBox.AllowedImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private bool IsVideoFile(string path) =>
        _appSettings.LaunchBox.AllowedVideoExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private bool IsDroppableFile(string path) => IsSelectedSetVideo() ? IsVideoFile(path) : IsImageFile(path);

    private async Task AddResolvedImageAsync(List<GameImage> resolvedImages, string? token)
    {
        GameImage? image = await ResolveDroppedTokenAsync(token);
        if (image != null && !resolvedImages.Contains(image))
        {
            resolvedImages.Add(image);
        }
    }

    private async Task<GameImage?> ResolveDroppedTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return IsHttpUrl(token) ? await DownloadWebImageAsync(token) : await ResolveLocalImageAsync(token);
    }

    private async Task<GameImage?> ResolveLocalImageAsync(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return null;
        }

        Game? game = _sharedDataService.SelectedGame;
        PlatformImageSet? imageSet = _sharedDataService.SelectedImageSet;

        if (game is null || imageSet is null || string.IsNullOrWhiteSpace(imageSet.FolderPath))
        {
            return FindImageByFile(RegionGameImages, filePath)
                ?? FindImageByFile(game?.AllImages ?? Enumerable.Empty<GameImage>(), filePath);
        }

        return await _imageLoadingService.AddImageFromFileToGameAsync(filePath, game, imageSet, ResolveRegionDestinationFolder(imageSet));
    }

    private async Task<GameImage?> DownloadWebImageAsync(string url)
    {
        Game? game = _sharedDataService.SelectedGame;
        PlatformImageSet? imageSet = _sharedDataService.SelectedImageSet;

        if (game is null || imageSet is null || string.IsNullOrWhiteSpace(imageSet.FolderPath))
        {
            return null;
        }

        return await _imageLoadingService.AddImageFromUrlToGameAsync(url, game, imageSet, ResolveRegionDestinationFolder(imageSet));
    }

    /// <summary>
    /// Carpeta destino del import según el bucket activo: la subcarpeta de la región FAVORITA seleccionada (para
    /// que la imagen quede en esa región), o la RAÍZ del set (=> "sin región") cuando el bucket activo es "otras
    /// regiones" o "sin región". La carpeta se crea si no existe (lo hace el propio pipeline de creación).
    /// </summary>
    private string? ResolveRegionDestinationFolder(PlatformImageSet imageSet)
    {
        if (SlotIndex >= 0
            && _selectedBucket?.Kind == RegionBucketKind.Favourite
            && _selectedBucket.Region != null
            && !string.IsNullOrEmpty(_selectedBucket.Region.Value))
        {
            return System.IO.Path.Combine(imageSet.FolderPath, _selectedBucket.Region.Value);
        }

        return null; // raíz del set => "sin región"
    }

    private static async Task<List<string>> GetDroppedWebUrlsAsync(DataPackageView data)
    {
        List<string> urls = new();

        if (data.Contains(StandardDataFormats.WebLink))
        {
            Uri webLink = await data.GetWebLinkAsync();
            if (webLink != null) { urls.Add(webLink.ToString()); }
        }

        if (data.Contains(StandardDataFormats.ApplicationLink))
        {
            Uri applicationLink = await data.GetApplicationLinkAsync();
            if (applicationLink != null) { urls.Add(applicationLink.ToString()); }
        }

        return urls;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static GameImage? FindImageByFile(IEnumerable<GameImage> images, string filePath) =>
        images.FirstOrDefault(image => string.Equals(image?.File, filePath, StringComparison.OrdinalIgnoreCase));
    #endregion

    #region Methods (public)
    /// <summary>Selecciona una miniatura (fija la selección global y notifica al code-behind).</summary>
    public void SelectImage(GameImage image)
    {
        if (image == null || ReferenceEquals(_sharedDataService.SelectedImage, image))
        {
            return;
        }

        _sharedDataService.SelectedImage = image;
        OnImageSelectionChanged(image);
    }

    public void SelectHorizontalView() => SelectLayout(GameImagesDashboardViewModel.GameImagesDashboardLayout.Horizontal);
    public void SelectVerticalView() => SelectLayout(GameImagesDashboardViewModel.GameImagesDashboardLayout.Vertical);

    public void SelectVideoQuality240() => SelectVideoQuality(VideoDownloadQualitySettings.P240);
    public void SelectVideoQuality360() => SelectVideoQuality(VideoDownloadQualitySettings.P360);
    public void SelectVideoQuality480() => SelectVideoQuality(VideoDownloadQualitySettings.P480);
    public void SelectVideoQuality720() => SelectVideoQuality(VideoDownloadQualitySettings.P720);
    public void SelectVideoQuality1080() => SelectVideoQuality(VideoDownloadQualitySettings.P1080);

    /// <inheritdoc/>
    public override void Dispose()
    {
        _sharedDataService.SelectedGameChanged -= OnSelectedGameChanged;
        _sharedDataService.SelectedGameImagesChanged -= OnSelectedGameImagesChanged;
        _sharedDataService.PropertyChanged -= OnSharedDataServicePropertyChanged;
        _sharedDataService.GamesFiltered.CollectionChanged -= OnGamesFilteredChanged;
        _sharedDataService.FavouriteRegionsChanged -= OnFavouriteRegionsChanged;
        _imageLoadingService.ImageAddedToGame -= OnImageAddedToGame;
        _imageLoadingService.ImageRemovedFromGame -= OnImageRemovedFromGame;
        PropertyChanged -= OnOwnPropertyChanged;
    }

    /// <summary>
    /// Realinea el selector de buckets con las regiones favoritas de la configuración (tras aceptar la ventana de
    /// ajustes): re-lee las favoritas y reconstruye/clasifica/reactiva los buckets en caliente.
    /// </summary>
    private void OnFavouriteRegionsChanged(object? sender, EventArgs e)
    {
        _favouriteRegions = _appSettings.GameImagesRegionDashboardControl?.FavouriteRegions ?? Array.Empty<ImageRegion>();
        BuildRegionBuckets();
        ClassifyBuckets(_sharedDataService.SelectedGame);
        ApplyActiveBucket(ChooseActiveBucket(), preselect: false);
    }

    /// <inheritdoc/>
    public override void LoadConfig()
    {
        var config = _appSettings.GameImagesRegionDashboardControl;

        _favouriteRegions = config?.FavouriteRegions ?? Array.Empty<ImageRegion>();
        _isSearchStringsPanelVisible = config?.IsSearchStringsPanelVisible ?? true;
        bool isHorizontalView = config?.IsHorizontalView ?? true;
        _videoDownloadQuality = config?.VideoDownloadQuality ?? VideoDownloadQualitySettings.P1080;

        _purgeNonFavouriteRegions = config?.PurgeNonFavouriteRegions ?? true;

        GameImageCriterion[]? savedSelectionCriteria = config?.ImageSelectionCriteria;
        _selectionCriteria = (savedSelectionCriteria is { Length: > 0 })
            ? savedSelectionCriteria.ToList()
            : new List<GameImageCriterion>
            {
                new GameImageCriterion { Type = SettingsType.Image, CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_First_Label] ?? "1st:", IsActive = true, ID = 1 },
                new GameImageCriterion { Type = SettingsType.Image, CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_Second_Label] ?? "2nd:", IsActive = true, ID = 2 },
            };

        GameImageCriterion[]? savedProcessingCriteria = config?.ImageProcessingCriteria;
        _processingCriteria = (savedProcessingCriteria is { Length: > 0 })
            ? savedProcessingCriteria.ToList()
            : new List<GameImageCriterion>
            {
                new GameImageCriterion { Type = SettingsType.Region, CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_Region_Label] ?? "Region:", IsActive = true, ID = 1 },
                new GameImageCriterion { Type = SettingsType.FileNameSuffix, CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_Suffix_Label] ?? "Suffix:", IsActive = true, ID = 2 },
                new GameImageCriterion { Type = SettingsType.FileName, CriteriaName = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.DashboardCriteria_FileName_Label] ?? "File Name:", IsActive = true, ID = 4 },
            };

        ThumbnailPanelWidth = config?.Width ?? DefaultThumbnailPanelWidth;
        ThumbnailPanelHeight = config?.Height ?? DefaultThumbnailPanelHeight;
        VideoVolume = _appSettings.General?.VideoVolume ?? DefaultVideoVolume;
        IsMuted = _appSettings.General?.IsMuted ?? false;

        if (isHorizontalView) { SelectHorizontalView(); } else { SelectVerticalView(); }

        BuildRegionBuckets();
        ClassifyBuckets(_sharedDataService.SelectedGame);
        ApplyActiveBucket(ChooseActiveBucket(), preselect: false);

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

    /// <inheritdoc/>
    public override void SaveConfig()
    {
        var config = _appSettings.GameImagesRegionDashboardControl;
        config.IsHorizontalView = IsHorizontalView;
        config.IsSearchStringsPanelVisible = IsSearchStringsPanelVisible;
        config.Width = ThumbnailPanelWidth;
        config.Height = ThumbnailPanelHeight;
        config.VideoDownloadQuality = _videoDownloadQuality;
        config.ImageSelectionCriteria = _selectionCriteria.ToArray();
        // FavouriteRegions, PurgeNonFavouriteRegions e ImageProcessingCriteria se editan solo por appSettings (v1).
    }
    #endregion
}
