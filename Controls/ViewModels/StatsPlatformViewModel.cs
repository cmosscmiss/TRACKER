using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using SkiaSharp;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model del widget de estadísticas de la <b>plataforma seleccionada</b>. Calcula los DATOS de cada gráfica
/// (valores + etiquetas); el dibujo y el tipo
/// de gráfica los gestiona cada <c>ChartTypeSelectorControl</c> (toolbar de tipo + chart) al que se enlazan esos
/// datos. Partes:
///
/// - <b>Pastillas inferiores</b>: imágenes / juegos de la plataforma frente al total de todas las plataformas.
/// - <b>Gráficas</b> (FlipView). De la plataforma seleccionada: cobertura por tipo de imagen (% de juegos con ≥1
///   imagen de ese tipo; eje X seleccionable vía <see cref="CoverageTypeScope"/>), distribución de cobertura (nº de
///   juegos por tramo de cobertura de favoritos) e imágenes por tipo (conteo bruto). Comparación entre plataformas:
///   una gráfica cuya métrica se elige con una toolbar (<see cref="PlatformMetric"/>): juegos, imágenes o tamaño en
///   disco por plataforma, con la plataforma seleccionada resaltada.
///
/// Solo se consideran tipos de imagen (key &lt; 100): vídeo, manual y música quedan fuera.
/// </summary>
public class StatsPlatformViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly IStatisticsService _statisticsService;
    private readonly ImageLoadingService _imageLoadingService;
    private readonly ThemeService _themeService;

    // Pastillas: "valor de la plataforma / valor de todas las plataformas".
    // Pastillas producidas por el servicio: las de imagen (nº / tipos / tamaño) y la de juegos (plataforma / todas).
    private ImagePills? _imagePills;
    private Stat? _gamesPill;
    private bool _hasData;
    private double _platformGameCount;
    private bool _isCoverageVisible = true;   // panel de resumen de cobertura visible por defecto
    private int _selectedChartIndex;

    /// <summary>
    /// Número de gráficas del FlipView (debe coincidir con los FlipViewItem de StatsPlatformControl.xaml y el
    /// PipsPager). Se usa para acotar <see cref="SelectedChartIndex"/>: restaurar un índice fuera de rango (p. ej.
    /// tras retirar una página) hacía que FlipView.SelectedIndex lanzara y la app se cerrara al arrancar.
    /// </summary>
    private const int ChartCount = 4;

    // Resumen de cobertura (barra de progreso): cobertura media de la plataforma seleccionada y media de TODAS
    // las plataformas, AMBAS según el ámbito activo (favoritos / tipos presentes). Se derivan de los conteos
    // "juegos cubiertos por tipo": el de la plataforma seleccionada es _coverageCountByType; el del resto se
    // cachea en _allPlatformsCounts (cómputo caro en segundo plano, invalidado al cambiar imágenes).
    private double _platformCoveragePercent;
    private string _platformCoverageText = "0%";
    private string _allPlatformsCoverageText = string.Empty;
    private bool _allPlatformsComputed;
    private bool _allPlatformsRunning;
    private IReadOnlyList<(IReadOnlyDictionary<int, int> Counts, int Games)> _allPlatformsCounts = Array.Empty<(IReadOnlyDictionary<int, int>, int)>();

    // Datos de la gráfica de cobertura por tipo (valores en %, etiquetas = nombre del tipo).
    private IReadOnlyList<double> _coverageByTypeValues = Array.Empty<double>();
    private IReadOnlyList<string> _coverageByTypeLabels = Array.Empty<string>();
    private int _coverageByTypeHighlightIndex = -1;
    private CoverageTypeScope _coverageScope = CoverageTypeScope.Favourites;
    private RelayCommand<string>? _setCoverageScopeCommand;

    // Datos del histograma de distribución de cobertura (nº de juegos por tramo).
    private IReadOnlyList<double> _coverageDistributionValues = Array.Empty<double>();
    private IReadOnlyList<string> _coverageDistributionLabels = Array.Empty<string>();

    // Datos de la gráfica de imágenes por tipo (conteo bruto).
    private IReadOnlyList<double> _imagesByTypeValues = Array.Empty<double>();
    private IReadOnlyList<string> _imagesByTypeLabels = Array.Empty<string>();
    private int _imagesByTypeHighlightIndex = -1;

    // Gráfica "Image types coverage by game" (idéntica a la de GameStatsControl): cobertura (0..100) de cada juego
    // de la plataforma sobre los tipos del ámbito activo (favoritos / presentes), en área, con el juego
    // seleccionado resaltado. Reutiliza la cobertura por juego cacheada del histograma de distribución
    // (_coveragePerGame), por lo que sigue el filtro de ámbito sin recalcular aparte.
    private IEnumerable<ISeries> _coverageByGameSeries = Array.Empty<ISeries>();
    private IEnumerable<ICartesianAxis> _coverageByGameXAxes = Array.Empty<ICartesianAxis>();
    private IEnumerable<ICartesianAxis> _coverageByGameYAxes = Array.Empty<ICartesianAxis>();
    private IEnumerable<RectangularSection> _coverageByGameSections = Array.Empty<RectangularSection>();
    private bool _hasCoverageByGameData;
    private bool _animateCoverageByGameChart = true;

    /// <summary>Plataforma cuya cobertura por tipo ya se calculó (cache), el nº de juegos con cada tipo y el total de juegos, para no recalcular al cambiar de ámbito (favoritos/presentes/todas).</summary>
    private Platform? _coveragePlatform;
    private IReadOnlyDictionary<int, int> _coverageCountByType = new Dictionary<int, int>();
    private int _coverageTotalGames;

    /// <summary>Plataforma y ámbito cuya cobertura por juego ya se calculó (cache), usada por el histograma de distribución.</summary>
    private Platform? _coveragePerGamePlatform;
    private CoverageTypeScope _coveragePerGameScope;
    private IReadOnlyList<(Game Game, double Coverage)> _coveragePerGame = Array.Empty<(Game, double)>();
    #endregion

    #region Properties (pills)
    /// <summary>
    /// Pastillas de imagen (Image set / Image types / Image set size) de la plataforma / todas, producidas por el
    /// servicio (label + descripción + valor ya formateado). Null hasta el primer refresco (x:Bind corta el path).
    /// </summary>
    public ImagePills? ImagePills
    {
        get => _imagePills;
        private set => SetProperty(ref _imagePills, value);
    }

    /// <summary>Pastilla "Games": nº de juegos de la plataforma / de todas, producida por el servicio.</summary>
    public Stat? GamesPill
    {
        get => _gamesPill;
        private set => SetProperty(ref _gamesPill, value);
    }

    /// <summary>True cuando hay una plataforma seleccionada con datos (controla el estado vacío del control).</summary>
    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }

    /// <summary>Nº de juegos de la plataforma seleccionada (línea de referencia "1 imagen por juego" en "Images by type").</summary>
    public double PlatformGameCount
    {
        get => _platformGameCount;
        private set => SetProperty(ref _platformGameCount, value);
    }
    #endregion

    #region Properties (coverage summary)
    /// <summary>Si el panel de resumen de cobertura es visible. Lo alterna un toggle de la barra superior (TwoWay).</summary>
    public bool IsCoverageVisible
    {
        get => _isCoverageVisible;
        set => SetProperty(ref _isCoverageVisible, value);
    }

    /// <summary>Cobertura media de favoritos de la plataforma seleccionada (0..100), para la barra de progreso.</summary>
    public double PlatformCoveragePercent
    {
        get => _platformCoveragePercent;
        private set => SetProperty(ref _platformCoveragePercent, value);
    }

    /// <summary>Cobertura media de la plataforma como texto (p. ej. "62%").</summary>
    public string PlatformCoverageText
    {
        get => _platformCoverageText;
        private set => SetProperty(ref _platformCoverageText, value);
    }

    /// <summary>Media de cobertura de favoritos de TODAS las plataformas como texto ("…" mientras se calcula).</summary>
    public string AllPlatformsCoverageText
    {
        get => _allPlatformsCoverageText;
        private set => SetProperty(ref _allPlatformsCoverageText, value);
    }
    #endregion

    #region Properties (chart config — persisted)
    /// <summary>Índice de la gráfica visible en el FlipView (enlazado TwoWay a <c>ChartFlipView.SelectedIndex</c>).</summary>
    public int SelectedChartIndex
    {
        get => _selectedChartIndex;
        // Acotado a [0, ChartCount-1]: un índice persistido fuera de rango (al quitar una página del FlipView)
        // hacía que FlipView.SelectedIndex lanzara ArgumentException al inicializar el binding → crash silencioso.
        set => SetProperty(ref _selectedChartIndex, Math.Clamp(value, 0, ChartCount - 1));
    }

    /// <summary>Tipo / orden / Top X de la gráfica "Coverage distribution". Enlazado TwoWay y persistido.</summary>
    public ChartViewState CoverageDistributionChart { get; } = new ChartViewState();

    /// <summary>Tipo / orden / Top X de la gráfica "Coverage - Image type". Enlazado TwoWay y persistido.</summary>
    public ChartViewState CoverageByTypeChart { get; } = new ChartViewState();

    /// <summary>Tipo / orden / Top X de la gráfica "Image set - Image type". Enlazado TwoWay y persistido.</summary>
    public ChartViewState ImagesByTypeChart { get; } = new ChartViewState();
    #endregion

    #region Properties (coverage-by-type chart data)
    /// <summary>Valores (cobertura 0..100 %) por cada tipo del ámbito seleccionado.</summary>
    public IReadOnlyList<double> CoverageByTypeValues
    {
        get => _coverageByTypeValues;
        private set => SetProperty(ref _coverageByTypeValues, value);
    }

    /// <summary>Etiquetas (nombre de cada tipo) alineadas con <see cref="CoverageByTypeValues"/>.</summary>
    public IReadOnlyList<string> CoverageByTypeLabels
    {
        get => _coverageByTypeLabels;
        private set => SetProperty(ref _coverageByTypeLabels, value);
    }

    /// <summary>Índice del tipo de imagen seleccionado (a resaltar y mantener siempre visible), o -1.</summary>
    public int CoverageByTypeHighlightIndex
    {
        get => _coverageByTypeHighlightIndex;
        private set => SetProperty(ref _coverageByTypeHighlightIndex, value);
    }

    /// <summary>Ámbito de tipos del eje X de la gráfica de cobertura (favoritos por defecto).</summary>
    public CoverageTypeScope CoverageScope
    {
        get => _coverageScope;
        private set
        {
            if (SetProperty(ref _coverageScope, value))
            {
                OnPropertyChanged(nameof(IsFavouritesScope));
                OnPropertyChanged(nameof(IsPresentScope));
                OnPropertyChanged(nameof(SelectedScopeValue));
            }
        }
    }

    public bool IsFavouritesScope => _coverageScope == CoverageTypeScope.Favourites;
    public bool IsPresentScope => _coverageScope == CoverageTypeScope.Present;

    /// <summary>Ámbito activo como cadena, para el <c>ExclusiveOptionsControl</c> (TwoWay).</summary>
    public string SelectedScopeValue
    {
        get => _coverageScope.ToString();
        set => OnSetCoverageScope(value);
    }

    /// <summary>Cambia el ámbito de tipos del eje X. El parámetro es el nombre del valor de <see cref="CoverageTypeScope"/>.</summary>
    public RelayCommand<string> SetCoverageScopeCommand =>
        _setCoverageScopeCommand ??= new RelayCommand<string>(OnSetCoverageScope);
    #endregion

    #region Properties (coverage-distribution chart data)
    /// <summary>Valores (nº de juegos) por cada tramo de cobertura de favoritos.</summary>
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
    #endregion

    #region Properties (images-by-type chart data)
    /// <summary>Valores (nº de imágenes) por cada tipo presente en la plataforma.</summary>
    public IReadOnlyList<double> ImagesByTypeValues
    {
        get => _imagesByTypeValues;
        private set => SetProperty(ref _imagesByTypeValues, value);
    }

    /// <summary>Etiquetas (nombre del tipo) alineadas con <see cref="ImagesByTypeValues"/>.</summary>
    public IReadOnlyList<string> ImagesByTypeLabels
    {
        get => _imagesByTypeLabels;
        private set => SetProperty(ref _imagesByTypeLabels, value);
    }

    /// <summary>Índice del tipo de imagen seleccionado (a resaltar y mantener siempre visible), o -1.</summary>
    public int ImagesByTypeHighlightIndex
    {
        get => _imagesByTypeHighlightIndex;
        private set => SetProperty(ref _imagesByTypeHighlightIndex, value);
    }
    #endregion

    #region Properties (coverage-by-game chart data)
    /// <summary>Gráfica de área: cobertura de favoritos (0..100) de cada juego de la plataforma; el juego seleccionado, resaltado.</summary>
    public IEnumerable<ISeries> CoverageByGameSeries
    {
        get => _coverageByGameSeries;
        private set => SetProperty(ref _coverageByGameSeries, value);
    }

    public IEnumerable<ICartesianAxis> CoverageByGameXAxes
    {
        get => _coverageByGameXAxes;
        private set => SetProperty(ref _coverageByGameXAxes, value);
    }

    public IEnumerable<ICartesianAxis> CoverageByGameYAxes
    {
        get => _coverageByGameYAxes;
        private set => SetProperty(ref _coverageByGameYAxes, value);
    }

    /// <summary>Línea vertical punteada desde el punto del juego seleccionado hasta el eje X.</summary>
    public IEnumerable<RectangularSection> CoverageByGameSections
    {
        get => _coverageByGameSections;
        private set => SetProperty(ref _coverageByGameSections, value);
    }

    /// <summary>
    /// Si la próxima actualización de la gráfica debe animarse. False en cambios de juego (cache hit: solo se
    /// mueve el resaltado) para que sea instantáneo; true cuando hay recálculo real. El code-behind la consume
    /// para ajustar <c>AnimationsSpeed</c> del chart antes de redibujar.
    /// </summary>
    public bool AnimateCoverageByGameChart
    {
        get => _animateCoverageByGameChart;
        private set => SetProperty(ref _animateCoverageByGameChart, value);
    }

    /// <summary>True cuando hay una plataforma con juegos para dibujar la gráfica de cobertura por juego.</summary>
    public bool HasCoverageByGameData
    {
        get => _hasCoverageByGameData;
        private set => SetProperty(ref _hasCoverageByGameData, value);
    }
    #endregion

    #region Constructor
    public StatsPlatformViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings, IStatisticsService statisticsService, ImageLoadingService imageLoadingService, ThemeService themeService) : base(sharedDataService, appSettings)
    {
        _statisticsService = statisticsService;
        _imageLoadingService = imageLoadingService;
        _themeService = themeService;

        SharedDataService.SelectedPlatformChanged += OnSelectedPlatformChanged;
        SharedDataService.SelectedGameChanged += OnSelectedGameChanged;
        SharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
        _imageLoadingService.ImageAddedToGame += OnImageAddedToGame;
        _imageLoadingService.PlatformImagesChanged += OnPlatformImagesChanged;
        _themeService.ThemeChanged += OnThemeChanged;
        PropertyChanged += OnSelfPropertyChanged;
    }
    #endregion

    /// <summary>
    /// Al cambiar el tema en caliente, reconstruye las gráficas: sus series se pintan con colores del tema (SKColor)
    /// horneados al construirlas, así que no se propagan solos. Reutiliza los datos ya cacheados (cómputo barato).
    /// </summary>
    private void OnThemeChanged(object? sender, System.EventArgs e)
    {
        if (SlotIndex < 0) return;
        BuildCoverageChart();
        BuildCoverageDistributionChart();
        BuildCoverageByGameChart(animate: false);
        BuildImagesByTypeChart();
    }

    #region Subscribed events
    /// <summary>Al cambiar de plataforma se recalcula todo.</summary>
    private void OnSelectedPlatformChanged(object? sender, PlatformChangedEventArgs e)
    {
        if (SlotIndex < 0) return;
        Refresh();
    }

    /// <summary>Al cambiar de juego solo se mueve el resaltado de la gráfica de cobertura por juego (cache hit, sin animación).</summary>
    private void OnSelectedGameChanged(object? sender, GameChangedEventArgs e)
    {
        if (SlotIndex < 0) return;
        BuildCoverageByGameChart(animate: false);
    }

    /// <summary>Recalcula todo cuando el widget pasa a estar visible (asignado a un slot).</summary>
    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlotIndex) && SlotIndex >= 0)
            Refresh();
    }

    /// <summary>Al cambiar el tipo de imagen seleccionado, reubica el resaltado de las gráficas por tipo (cache hit).</summary>
    private void OnSelectedImageSetChanged(object? sender, ImageSetChangedEventArgs e)
    {
        if (SlotIndex < 0) return;
        BuildCoverageChart();
        BuildImagesByTypeChart();
    }

    /// <summary>Alta de una imagen en un juego (drag&amp;drop).</summary>
    private void OnImageAddedToGame(Game game, GameImage image)
    {
        if (SlotIndex < 0) return;
        RefreshAfterImageMutation();
    }

    /// <summary>Baja de imágenes (borrado de huérfanas).</summary>
    private void OnPlatformImagesChanged()
    {
        if (SlotIndex < 0) return;
        RefreshAfterImageMutation();
    }

    /// <summary>
    /// Refresco común tras un alta/baja de imágenes: pastillas + los datos que dependen de imágenes (imágenes por
    /// tipo y la comparación entre plataformas, por si su métrica es imágenes/tamaño) + recálculo de las dos
    /// coberturas (caché invalidada).
    /// </summary>
    private void RefreshAfterImageMutation()
    {
        RefreshPills();
        BuildImagesByTypeChart();
        InvalidateCoverage();
        RefreshCoverage();               // → barra de cobertura de la plataforma (UpdatePlatformCoverage), según ámbito
        RefreshCoverageDistribution();   // recalcula la cobertura por juego (caché invalidada) → también la gráfica de cobertura por juego

        // Las imágenes han cambiado: la media de todas las plataformas ya no vale.
        _allPlatformsComputed = false;
        RefreshAllPlatformsCoverage();
    }
    #endregion

    #region Methods (private)
    /// <summary>Recalcula pastillas y los datos de todas las gráficas.</summary>
    private void Refresh()
    {
        if (SlotIndex < 0)
            return;

        RefreshPills();
        RefreshCoverage();               // → barra de cobertura de la plataforma (UpdatePlatformCoverage), según ámbito
        RefreshCoverageDistribution();   // histograma + gráfica de cobertura por juego (misma cobertura cacheada)
        RefreshAllPlatformsCoverage();   // media de todas las plataformas (cacheada; solo calcula la 1ª vez)
        BuildImagesByTypeChart();
    }

    /// <summary>
    /// Reconstruye las pastillas: valor de la plataforma seleccionada (X) y total de todas las plataformas (Y),
    /// para imágenes y juegos. Todo en memoria (conteos ya cacheados en cada <see cref="PlatformImageSet"/>).
    /// </summary>
    private void RefreshPills()
    {
        Platform? platform = SharedDataService.SelectedPlatform;
        IReadOnlyList<Platform> allPlatforms = SharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();

        if (platform == null) { HasData = false; return; }

        // Pastillas de imagen: compuestas y formateadas por el servicio (plataforma / todas).
        ImagePills = _statisticsService.GetPlatformImagePills(platform, allPlatforms);

        // Pastilla de juegos (no es de imagen): nº de la plataforma / total de todas, producida por el servicio.
        GamesPill = _statisticsService.GetPlatformGamesPill(platform, allPlatforms);
        PlatformGameCount = GamesPill.Value;

        HasData = ImagePills.Images.Value > 0 || GamesPill.Value > 0;
    }

    /// <summary>
    /// Gráfica de cobertura por tipo: asegura (cacheada por plataforma) el nº de juegos cubiertos por cada tipo,
    /// calculado en segundo plano por ser un cómputo puro (recorre los ficheros para emparejar), y con él
    /// construye los datos del ámbito actual. Cambiar de ámbito (favoritos/presentes/todas) es cache hit.
    /// </summary>
    private async void RefreshCoverage()
    {
        Platform? platform = SharedDataService.SelectedPlatform;

        if (platform == null || platform.Games.Count == 0)
        {
            ClearCoverage();
            return;
        }

        if (!ReferenceEquals(platform, _coveragePlatform))
        {
            IReadOnlyDictionary<int, int> countByType;
            try
            {
                countByType = await Task.Run(() => _statisticsService.GetPlatformGameCountByImageType(platform));
            }
            catch (Exception ex)
            {
                // Cómputo de solo lectura; si fallara (p. ej. colección mutada a la vez) no mostramos nada.
                MM4LB.Services.ExceptionService.LogToFile(ex, "Error computing platform coverage.");
                ClearCoverage();
                return;
            }

            // Descartar si la plataforma cambió mientras se calculaba.
            if (!ReferenceEquals(platform, SharedDataService.SelectedPlatform))
                return;

            _coveragePlatform = platform;
            _coverageCountByType = countByType;
            _coverageTotalGames = platform.Games.Count;
        }

        BuildCoverageChart();
    }

    /// <summary>Reconstruye los datos de cobertura por tipo a partir de la cobertura cacheada y el ámbito actual.</summary>
    private void BuildCoverageChart()
    {
        List<MediaType> types = _coverageTotalGames == 0 ? new List<MediaType>() : ResolveScopeTypes();
        UpdatePlatformCoverage();   // la barra refleja el ámbito activo

        if (types.Count == 0)
        {
            CoverageByTypeValues = Array.Empty<double>();
            CoverageByTypeLabels = Array.Empty<string>();
            CoverageByTypeHighlightIndex = -1;
            return;
        }

        int? selectedKey = SharedDataService.SelectedImageSet?.Type?.Key;
        var values = new double[types.Count];
        var labels = new string[types.Count];
        int highlight = -1;
        for (int i = 0; i < types.Count; i++)
        {
            int covered = _coverageCountByType.TryGetValue(types[i].Key, out int c) ? c : 0;
            values[i] = 100.0 * covered / _coverageTotalGames;
            labels[i] = types[i].Value;
            if (selectedKey.HasValue && types[i].Key == selectedKey.Value)
                highlight = i;
        }

        CoverageByTypeValues = values;
        CoverageByTypeLabels = labels;
        CoverageByTypeHighlightIndex = highlight;
    }

    /// <summary>
    /// Histograma de distribución de cobertura: cuántos juegos de la plataforma caen en cada tramo de cobertura
    /// de favoritos (0–20 %, …, 80–100 %), a partir de la cobertura por juego cacheada.
    /// </summary>
    private void BuildCoverageDistributionChart()
    {
        if (_coveragePerGame.Count == 0)
        {
            CoverageDistributionValues = Array.Empty<double>();
            CoverageDistributionLabels = Array.Empty<string>();
            return;
        }

        string[] labels = { "0–20%", "20–40%", "40–60%", "60–80%", "80–100%" };
        var counts = new double[labels.Length];
        foreach ((Game _, double coverage) in _coveragePerGame)
        {
            int bucket = (int)(coverage * 100 / 20);   // [0,20)->0, [20,40)->1, …; 100 % cae en el último tramo
            if (bucket < 0) bucket = 0;
            if (bucket >= labels.Length) bucket = labels.Length - 1;
            counts[bucket]++;
        }

        CoverageDistributionValues = counts;
        CoverageDistributionLabels = labels;
    }

    /// <summary>
    /// Datos de imágenes por tipo (conteo bruto) de la plataforma seleccionada: nº de ficheros de imagen de cada
    /// tipo del ámbito activo (favoritos / tipos presentes, igual que las otras dos gráficas), sumado sobre los
    /// imageset ya escaneados. Un tipo favorito sin imágenes aparece con valor 0. Cómputo barato en memoria.
    /// </summary>
    private void BuildImagesByTypeChart()
    {
        Platform? platform = SharedDataService.SelectedPlatform;
        List<MediaType> types = ResolveScopeTypes();   // favoritos o tipos presentes, según el ámbito
        if (platform == null || types.Count == 0)
        {
            ImagesByTypeValues = Array.Empty<double>();
            ImagesByTypeLabels = Array.Empty<string>();
            ImagesByTypeHighlightIndex = -1;
            return;
        }

        Dictionary<int, int> countByType = _statisticsService.GetPlatformImageCountsByType(platform);

        int? selectedKey = SharedDataService.SelectedImageSet?.Type?.Key;
        var values = new double[types.Count];
        var labels = new string[types.Count];
        int highlight = -1;
        for (int i = 0; i < types.Count; i++)
        {
            values[i] = countByType.TryGetValue(types[i].Key, out int count) ? count : 0;
            labels[i] = types[i].Value;
            if (selectedKey.HasValue && types[i].Key == selectedKey.Value)
                highlight = i;
        }

        ImagesByTypeValues = values;
        ImagesByTypeLabels = labels;
        ImagesByTypeHighlightIndex = highlight;
    }

    /// <summary>
    /// Asegura (cacheada por plataforma + ámbito, calculada en segundo plano por recorrer ficheros) la cobertura
    /// por juego sobre el conjunto de tipos del ámbito actual (favoritos / tipos presentes) y reconstruye el
    /// histograma de distribución. Cambiar de ámbito recomputa (el conjunto de tipos cambia).
    /// </summary>
    private async void RefreshCoverageDistribution()
    {
        Platform? platform = SharedDataService.SelectedPlatform;
        List<MediaType> types = ResolveScopeTypes();   // favoritos o tipos presentes, según el ámbito

        if (platform == null || platform.Games.Count == 0 || types.Count == 0)
        {
            ClearCoverageDistribution();
            return;
        }

        bool recomputed = false;

        if (!ReferenceEquals(platform, _coveragePerGamePlatform) || _coverageScope != _coveragePerGameScope)
        {
            CoverageTypeScope scope = _coverageScope;
            IReadOnlyList<(Game Game, double Coverage)> perGame;
            try
            {
                perGame = await Task.Run(() => _statisticsService.GetPlatformCoveragePerGame(platform, types));
            }
            catch (Exception ex)
            {
                MM4LB.Services.ExceptionService.LogToFile(ex, "Error computing coverage distribution.");
                ClearCoverageDistribution();
                return;
            }

            // Descartar si la plataforma o el ámbito cambiaron mientras se calculaba.
            if (!ReferenceEquals(platform, SharedDataService.SelectedPlatform) || scope != _coverageScope)
                return;

            _coveragePerGamePlatform = platform;
            _coveragePerGameScope = scope;
            _coveragePerGame = perGame;
            recomputed = true;
        }

        BuildCoverageDistributionChart();
        // La gráfica de cobertura por juego usa la MISMA cobertura por juego (según ámbito): se anima solo si hubo
        // recálculo real; un cambio de juego (cache hit) solo mueve el resaltado (ver OnSelectedGameChanged).
        BuildCoverageByGameChart(animate: recomputed);
    }

    /// <summary>Cobertura media de la plataforma seleccionada según el ámbito activo, para la barra de progreso.</summary>
    private void UpdatePlatformCoverage()
    {
        IReadOnlyCollection<MediaType> favourites = _appSettings.ImageTypeControl.FavouriteImageTypes ?? Array.Empty<MediaType>();
        double coverage = _statisticsService.GetTypeCoverageRatio(_coverageCountByType, _coverageTotalGames, _coverageScope, favourites);
        PlatformCoveragePercent = coverage * 100.0;
        PlatformCoverageText = _statisticsService.FormatPercent(coverage);
    }

    /// <summary>
    /// Media de cobertura de TODAS las plataformas (media de la cobertura media de cada una) para el ámbito
    /// actual. El cómputo caro (conteos por tipo de cada plataforma, empareja ficheros) se hace UNA vez en
    /// segundo plano y se cachea en <see cref="_allPlatformsCounts"/> (invalidado al cambiar imágenes); el texto
    /// se deriva del ámbito de forma barata. Muestra "…" mientras calcula.
    /// </summary>
    private async void RefreshAllPlatformsCoverage()
    {
        if (_allPlatformsComputed)
        {
            UpdateAllPlatformsCoverageText();
            return;
        }
        if (_allPlatformsRunning)
            return;

        IReadOnlyList<Platform> platforms = SharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();
        if (platforms.Count == 0)
        {
            _allPlatformsCounts = Array.Empty<(IReadOnlyDictionary<int, int>, int)>();
            _allPlatformsComputed = true;
            AllPlatformsCoverageText = "0%";
            return;
        }

        _allPlatformsRunning = true;
        AllPlatformsCoverageText = "…";

        var snapshot = platforms.ToList();   // copia para el hilo de fondo
        List<(IReadOnlyDictionary<int, int> Counts, int Games)> counts;
        try
        {
            counts = await Task.Run(() =>
            {
                var list = new List<(IReadOnlyDictionary<int, int>, int)>(snapshot.Count);
                foreach (Platform p in snapshot)
                    list.Add((_statisticsService.GetPlatformGameCountByImageType(p), p.Games.Count));
                return list;
            });
        }
        catch (Exception ex)
        {
            MM4LB.Services.ExceptionService.LogToFile(ex, "Error computing all-platforms coverage.");
            _allPlatformsRunning = false;
            AllPlatformsCoverageText = string.Empty;
            return;
        }

        _allPlatformsCounts = counts;
        _allPlatformsComputed = true;
        _allPlatformsRunning = false;
        UpdateAllPlatformsCoverageText();
    }

    /// <summary>Deriva (barato) el texto de la media de todas las plataformas para el ámbito actual desde la cache.</summary>
    private void UpdateAllPlatformsCoverageText()
    {
        if (!_allPlatformsComputed)
            return;   // aún calculando: se mantiene "…"

        IReadOnlyCollection<MediaType> favourites = _appSettings.ImageTypeControl.FavouriteImageTypes ?? Array.Empty<MediaType>();
        double sum = 0;
        int n = 0;
        foreach ((IReadOnlyDictionary<int, int> countByType, int games) in _allPlatformsCounts)
        {
            if (games == 0)
                continue;   // plataforma sin juegos: cobertura indefinida, no cuenta en la media
            sum += _statisticsService.GetTypeCoverageRatio(countByType, games, _coverageScope, favourites);
            n++;
        }

        AllPlatformsCoverageText = n == 0 ? "0%" : _statisticsService.FormatPercent(sum / n);
    }

    /// <summary>Lista de tipos de imagen del eje X según <see cref="CoverageScope"/>.</summary>
    private List<MediaType> ResolveScopeTypes()
    {
        switch (_coverageScope)
        {
            case CoverageTypeScope.Present:
                // Tipos de imagen y de vídeo de juego (Video Snap, Theme Video) con al menos un fichero en la
                // plataforma (ImagesCount > 0), ordenados por clave. Se deriva directamente de los image sets (no
                // de la cache async), para no depender de su orden.
                Platform? platform = SharedDataService.SelectedPlatform;
                if (platform?.Images?.ImageSets == null)
                    return new List<MediaType>();
                return platform.Images.ImageSets
                    .Where(s => s.Type != null && (MediaType.IsImage(s.Type.Key) || MediaType.IsVideo(s.Type.Key)) && s.ImagesCount > 0)
                    .Select(s => s.Type)
                    .Distinct()
                    .OrderBy(t => t.Key)
                    .ToList();

            case CoverageTypeScope.Favourites:
            default:
                IReadOnlyCollection<MediaType> favourites = _appSettings.ImageTypeControl.FavouriteImageTypes ?? Array.Empty<MediaType>();
                return favourites.ToList();
        }
    }

    /// <summary>Aplica el ámbito elegido por el usuario (botones de la gráfica) y reconstruye los datos (sin recalcular).</summary>
    private void OnSetCoverageScope(string? scopeName)
    {
        if (!Enum.TryParse(scopeName, out CoverageTypeScope scope))
            return;

        if (scope == _coverageScope)
        {
            // Clic sobre el ámbito ya activo: re-emitimos los estados para que el binding OneWay lo vuelva a marcar.
            OnPropertyChanged(nameof(IsFavouritesScope));
            OnPropertyChanged(nameof(IsPresentScope));
            return;
        }

        CoverageScope = scope;   // emite los booleanos de estado
        BuildCoverageChart();              // reconstruye la gráfica + barra de la plataforma (UpdatePlatformCoverage)
        RefreshCoverageDistribution();     // recomputa el histograma sobre el conjunto de tipos del nuevo ámbito
        BuildImagesByTypeChart();          // imágenes por tipo: mismo ámbito que las otras dos gráficas
        UpdateAllPlatformsCoverageText();  // y la media de todas, según el nuevo ámbito (barato, desde cache)
    }

    /// <summary>Invalida las caches de cobertura (por tipo y por juego) para forzar el recálculo en el próximo refresco.</summary>
    private void InvalidateCoverage()
    {
        _coveragePlatform = null;
        _coveragePerGamePlatform = null;
    }

    /// <summary>Limpia la cobertura por tipo: invalida la cache y vacía los datos.</summary>
    private void ClearCoverage()
    {
        _coveragePlatform = null;
        _coverageCountByType = new Dictionary<int, int>();
        _coverageTotalGames = 0;
        CoverageByTypeValues = Array.Empty<double>();
        CoverageByTypeLabels = Array.Empty<string>();
        UpdatePlatformCoverage();   // sin datos → 0%
    }

    /// <summary>Limpia el histograma de distribución de cobertura (y la gráfica de cobertura por juego, que comparte su cache): invalida la cache y vacía los datos.</summary>
    private void ClearCoverageDistribution()
    {
        _coveragePerGamePlatform = null;
        _coveragePerGame = Array.Empty<(Game, double)>();
        CoverageDistributionValues = Array.Empty<double>();
        CoverageDistributionLabels = Array.Empty<string>();
        ClearCoverageByGameChartOnly();
    }

    /// <summary>
    /// Construye la gráfica de área "Image types coverage by game" a partir de la cobertura por juego cacheada
    /// (<see cref="_coveragePerGame"/>, la MISMA que el histograma de distribución y por tanto sujeta al ámbito
    /// favoritos/presentes): un punto por juego en el eje X (sin etiquetas), cobertura 0..100 en el eje Y, y el
    /// juego seleccionado resaltado con un punto ámbar y una línea vertical punteada. Idéntica en aspecto a
    /// <c>GameStatsViewModel.BuildCoverageChart</c>.
    /// </summary>
    private void BuildCoverageByGameChart(bool animate)
    {
        // Se fija ANTES de tocar las series: el code-behind ajusta la velocidad de animación al recibir esta
        // propiedad, de modo que la actualización de Series/Sections que viene a continuación se dibuje (o no)
        // con animación.
        AnimateCoverageByGameChart = animate;

        if (_coveragePerGame.Count == 0)
        {
            ClearCoverageByGameChartOnly();
            return;
        }

        Game? selected = SharedDataService.SelectedGame;

        // Snapshot que coincide con los valores de estas series, para resolver el nombre del juego en el tooltip.
        IReadOnlyList<(Game Game, double Coverage)> perGame = _coveragePerGame;

        double[] percents = new double[perGame.Count];
        int selectedIndex = -1;
        for (int i = 0; i < perGame.Count; i++)
        {
            percents[i] = perGame[i].Coverage * 100.0;
            if (selected != null && ReferenceEquals(perGame[i].Game, selected))
            {
                selectedIndex = i;
            }
        }

        (SKColor accent, SKColor _, SKColor accentLight, SKColor text) = ResolveThemeColors();

        CoverageChartVisual visual = CoverageChartBuilder.Build(
            percents, selectedIndex, i => perGame[i].Game?.Title ?? string.Empty, accent, accentLight, text);

        CoverageByGameSeries = visual.Series;
        CoverageByGameXAxes = visual.XAxes;
        CoverageByGameYAxes = visual.YAxes;
        CoverageByGameSections = visual.Sections;
        HasCoverageByGameData = true;
    }

    /// <summary>Vacía la gráfica de cobertura por juego (la cobertura cacheada la gestiona el histograma de distribución).</summary>
    private void ClearCoverageByGameChartOnly()
    {
        CoverageByGameSeries = Array.Empty<ISeries>();
        CoverageByGameXAxes = Array.Empty<ICartesianAxis>();
        CoverageByGameYAxes = Array.Empty<ICartesianAxis>();
        CoverageByGameSections = Array.Empty<RectangularSection>();
        HasCoverageByGameData = false;
    }

    /// <summary>Resuelve los colores de acento y texto del tema activo para tematizar la gráfica.</summary>
    private (SKColor accent, SKColor accentDark, SKColor accentLight, SKColor text) ResolveThemeColors()
        => (ToSk(_themeService.AccentColor), ToSk(_themeService.AccentDarkColor), ToSk(_themeService.AccentLightColor), ToSk(_themeService.TextColor));

    private static SKColor ToSk(Windows.UI.Color c) => new(c.R, c.G, c.B, c.A);
    #endregion

    #region Methods (public)
    /// <summary>
    /// Restaura la gráfica activa del FlipView y la configuración (tipo / orden / Top X) de las tres gráficas con
    /// toolbar, y recalcula. La llama el control una vez restaurados los ajustes de disco.
    /// </summary>
    public override void LoadConfig()
    {
        AppSettings.StatsPlatformControlSettings config = _appSettings.StatsPlatformControl;
        if (config != null)
        {
            SelectedChartIndex = config.SelectedChartIndex;
            IsCoverageVisible = config.IsCoverageVisible;
            CoverageScope = config.CoverageScope;   // se lee en ResolveScopeTypes durante el Refresh de abajo
            CoverageDistributionChart.ApplyFrom(config.CoverageDistributionChart);
            CoverageByTypeChart.ApplyFrom(config.CoverageByTypeChart);
            ImagesByTypeChart.ApplyFrom(config.ImagesByTypeChart);
        }

        Refresh();
    }

    /// <summary>Vuelca la gráfica activa y la configuración de las tres gráficas con toolbar en los ajustes.</summary>
    public override void SaveConfig()
    {
        AppSettings.StatsPlatformControlSettings config = _appSettings.StatsPlatformControl;
        config.SelectedChartIndex = SelectedChartIndex;
        config.IsCoverageVisible = IsCoverageVisible;
        config.CoverageScope = CoverageScope;
        CoverageDistributionChart.StoreTo(config.CoverageDistributionChart);
        CoverageByTypeChart.StoreTo(config.CoverageByTypeChart);
        ImagesByTypeChart.StoreTo(config.ImagesByTypeChart);
    }

    public override void Dispose()
    {
        SharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged;
        SharedDataService.SelectedGameChanged -= OnSelectedGameChanged;
        SharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
        _imageLoadingService.ImageAddedToGame -= OnImageAddedToGame;
        _imageLoadingService.PlatformImagesChanged -= OnPlatformImagesChanged;
        PropertyChanged -= OnSelfPropertyChanged;
    }
    #endregion
}
