using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model del widget de estadísticas <b>globales</b> (entre plataformas). Expone los DATOS de tres gráficas
/// comparativas por plataforma —juegos, imágenes y tamaño en disco (GB)— que dibuja cada
/// <c>ChartTypeSelectorControl</c>. Etiquetas (nombres de plataforma) e índice resaltado (la plataforma
/// seleccionada) son comunes a las tres. Todo cómputo de agregación vive en <see cref="IStatisticsService"/>.
/// </summary>
public class StatsGlobalViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly IStatisticsService _statisticsService;
    private readonly ImageLoadingService _imageLoadingService;

    private IReadOnlyList<double> _gamesByPlatformValues = Array.Empty<double>();
    private IReadOnlyList<double> _imagesByPlatformValues = Array.Empty<double>();
    private IReadOnlyList<double> _sizeByPlatformValues = Array.Empty<double>();
    private IReadOnlyList<IReadOnlyList<double>> _gamesAuditByPlatformSeries = Array.Empty<IReadOnlyList<double>>();
    private IReadOnlyList<string> _platformLabels = Array.Empty<string>();
    private int _highlightIndex = -1;

    // Pastillas inferiores: "valor de la plataforma seleccionada / total de todas las plataformas". Las produce el
    // servicio ya formateadas (label + descripción + valor): las de juegos (estáticas) y las de imagen (dinámicas).
    // Ambas null hasta el primer Refresh (x:Bind corta el path con seguridad).
    private GameAuditStats? _gamesAuditStats;
    private ImagePills? _imagePills;

    private int _selectedChartIndex;

    /// <summary>Nombres de las tres series apiladas de la gráfica "Games by platform" (colección / LaunchBox / sólo colección).</summary>
    private static readonly IReadOnlyList<string> GamesAuditSeriesNames =
        new[] { "In my collection", "In LaunchBox", "In collection, not in LB" };
    #endregion

    #region Properties
    /// <summary>Nº de juegos por plataforma.</summary>
    public IReadOnlyList<double> GamesByPlatformValues
    {
        get => _gamesByPlatformValues;
        private set => SetProperty(ref _gamesByPlatformValues, value);
    }

    /// <summary>Nº de imágenes por plataforma.</summary>
    public IReadOnlyList<double> ImagesByPlatformValues
    {
        get => _imagesByPlatformValues;
        private set => SetProperty(ref _imagesByPlatformValues, value);
    }

    /// <summary>Tamaño en disco (GB) por plataforma.</summary>
    public IReadOnlyList<double> SizeByPlatformValues
    {
        get => _sizeByPlatformValues;
        private set => SetProperty(ref _sizeByPlatformValues, value);
    }

    /// <summary>
    /// Tres series 100% APILADAS de juegos por plataforma, en PORCENTAJE (cada plataforma normalizada a 100%
    /// sobre la suma de las tres): (0) en mi colección, (1) total en LaunchBox, (2) en mi colección y no en
    /// LaunchBox. Se renderiza como barras/área apiladas. Mismas etiquetas (<see cref="PlatformLabels"/>) y
    /// nombres de serie (<see cref="GamesAuditSeriesNames"/>).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<double>> GamesAuditByPlatformSeries
    {
        get => _gamesAuditByPlatformSeries;
        private set => SetProperty(ref _gamesAuditByPlatformSeries, value);
    }

    /// <summary>Nombres de las tres series de <see cref="GamesAuditByPlatformSeries"/> (para la leyenda).</summary>
    public IReadOnlyList<string> GamesAuditSeries => GamesAuditSeriesNames;

    /// <summary>Etiquetas (nombre de plataforma) comunes a las tres gráficas.</summary>
    public IReadOnlyList<string> PlatformLabels
    {
        get => _platformLabels;
        private set => SetProperty(ref _platformLabels, value);
    }

    /// <summary>Índice de la plataforma seleccionada (a resaltar en las tres gráficas), o -1 si ninguna.</summary>
    public int HighlightIndex
    {
        get => _highlightIndex;
        private set => SetProperty(ref _highlightIndex, value);
    }
    #endregion

    #region Properties (pills)
    /// <summary>
    /// Tres pastillas de juegos (en colección / en LaunchBox / no en LaunchBox) ya formateadas por el servicio:
    /// cada ítem trae label (<see cref="Stat.Title"/>), descripción y <c>Value</c>=plataforma seleccionada /
    /// <c>Total</c>=suma de todas. Estáticas (los conteos no cambian en caliente); el XAML bindea por índice.
    /// </summary>
    public GameAuditStats? GamesAuditStats
    {
        get => _gamesAuditStats;
        private set => SetProperty(ref _gamesAuditStats, value);
    }

    /// <summary>
    /// Pastillas de imagen (Image set / Image set size) de la plataforma seleccionada / total de todas, producidas
    /// por el servicio ya formateadas. Solo se muestran Images y Size. Null hasta el primer Refresh.
    /// </summary>
    public ImagePills? ImagePills
    {
        get => _imagePills;
        private set => SetProperty(ref _imagePills, value);
    }
    #endregion

    #region Properties (chart config — persisted)
    /// <summary>Índice de la gráfica visible en el FlipView (enlazado TwoWay a <c>ChartFlipView.SelectedIndex</c>).</summary>
    public int SelectedChartIndex
    {
        get => _selectedChartIndex;
        set => SetProperty(ref _selectedChartIndex, value);
    }

    /// <summary>Tipo / orden / Top X de la gráfica 1 (juegos por plataforma, apilada). Enlazado TwoWay y persistido.</summary>
    public ChartViewState GameSetChart { get; } = new ChartViewState();

    /// <summary>Tipo / orden / Top X de la gráfica 2 (nº de juegos por plataforma). Enlazado TwoWay y persistido.</summary>
    public ChartViewState GameCountChart { get; } = new ChartViewState();

    /// <summary>Tipo / orden / Top X de la gráfica 3 (nº de imágenes por plataforma). Enlazado TwoWay y persistido.</summary>
    public ChartViewState ImageCountChart { get; } = new ChartViewState();

    /// <summary>Tipo / orden / Top X de la gráfica 4 (tamaño en disco por plataforma). Enlazado TwoWay y persistido.</summary>
    public ChartViewState ImageSizeChart { get; } = new ChartViewState();
    #endregion

    #region Constructor
    public StatsGlobalViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings, IStatisticsService statisticsService, ImageLoadingService imageLoadingService) : base(sharedDataService, appSettings)
    {
        _statisticsService = statisticsService;
        _imageLoadingService = imageLoadingService;

        SharedDataService.SelectedPlatformChanged += OnSelectedPlatformChanged;
        _imageLoadingService.ImageAddedToGame += OnImageAddedToGame;
        _imageLoadingService.PlatformImagesChanged += OnPlatformImagesChanged;
        PropertyChanged += OnSelfPropertyChanged;
    }
    #endregion

    #region Subscribed events
    /// <summary>Al cambiar de plataforma cambia solo el resaltado; se reconstruye (barato).</summary>
    private void OnSelectedPlatformChanged(object? sender, PlatformChangedEventArgs e)
    {
        if (SlotIndex < 0) return;
        Refresh();
    }

    /// <summary>Recalcula cuando el widget pasa a estar visible (asignado a un slot).</summary>
    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlotIndex) && SlotIndex >= 0)
            Refresh();
    }

    /// <summary>Alta de una imagen (drag&amp;drop): cambian imágenes y tamaño por plataforma.</summary>
    private void OnImageAddedToGame(Game game, GameImage image)
    {
        if (SlotIndex < 0) return;
        Refresh();
    }

    /// <summary>Baja de imágenes (borrado de huérfanas): cambian imágenes y tamaño por plataforma.</summary>
    private void OnPlatformImagesChanged()
    {
        if (SlotIndex < 0) return;
        Refresh();
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Reconstruye las tres gráficas comparativas (juegos, imágenes, tamaño en GB) con sus etiquetas comunes
    /// (nombres de plataforma) y el índice de la plataforma seleccionada para resaltarla. Agregación en memoria.
    /// </summary>
    private void Refresh()
    {
        if (SlotIndex < 0)
            return;

        IReadOnlyList<Platform> platforms = SharedDataService.PlatformSet?.Platforms ?? (IReadOnlyList<Platform>)Array.Empty<Platform>();
        if (platforms.Count == 0)
        {
            GamesByPlatformValues = Array.Empty<double>();
            ImagesByPlatformValues = Array.Empty<double>();
            SizeByPlatformValues = Array.Empty<double>();
            GamesAuditByPlatformSeries = Array.Empty<IReadOnlyList<double>>();
            PlatformLabels = Array.Empty<string>();
            HighlightIndex = -1;
            ClearPills();
            return;
        }

        Platform? selected = SharedDataService.SelectedPlatform;
        var games = new double[platforms.Count];
        var images = new double[platforms.Count];
        var size = new double[platforms.Count];
        // Tres series apiladas: en colección, en LaunchBox, sólo en colección (no en LaunchBox).
        var inCollection = new double[platforms.Count];
        var inLaunchbox = new double[platforms.Count];
        var notInLaunchbox = new double[platforms.Count];
        var labels = new string[platforms.Count];
        int selectedIndex = -1;

        for (int i = 0; i < platforms.Count; i++)
        {
            games[i] = platforms[i].Games.Count;

            // Conteo y tamaño por plataforma para las series de las gráficas (agregación centralizada en el servicio).
            PlatformImageStats imageStats = _statisticsService.GetPlatformImageStats(platforms[i]);
            images[i] = imageStats.ImageCount;
            size[i] = imageStats.SizeKb / 1_000_000.0;        // KB → GB (gráfica)

            // Serie apilada de juegos: mismas métricas que el GamesAuditControl pero por plataforma, leídas de la
            // caché del servicio (GamesInLauchboxDb es estático tras la carga, así que se computa una sola vez).
            GameAuditStats audit = _statisticsService.GetGameCollectionStatistics(platforms[i]);
            double c0 = audit.InCollection.Value;     // Games in my collection
            double c1 = audit.InLaunchBox.Value;      // Games in LB DB
            double c2 = audit.NotInLaunchBox.Value;   // Games not in LB DB

            // Eje Y en porcentaje: gráfica 100% apilada (cada plataforma se normaliza a 100% sobre la suma
            // de las tres series). Plataforma sin juegos → 0%.
            double sum = c0 + c1 + c2;
            inCollection[i] = sum > 0 ? c0 / sum * 100.0 : 0;
            inLaunchbox[i] = sum > 0 ? c1 / sum * 100.0 : 0;
            notInLaunchbox[i] = sum > 0 ? c2 / sum * 100.0 : 0;

            if (selected != null && ReferenceEquals(platforms[i], selected))
                selectedIndex = i;

            labels[i] = platforms[i].Name;
        }

        GamesByPlatformValues = games;
        ImagesByPlatformValues = images;
        SizeByPlatformValues = size;
        GamesAuditByPlatformSeries = new IReadOnlyList<double>[] { inCollection, inLaunchbox, notInLaunchbox };
        PlatformLabels = labels;
        HighlightIndex = selectedIndex;

        // Pastillas de juegos: producidas por el servicio (label + descripción + plataforma/total), memoizadas.
        GamesAuditStats = _statisticsService.GetGlobalGameCollectionStatistics(platforms, selected);

        // Pastillas de imágenes: compuestas y formateadas por el servicio (plataforma seleccionada / todas).
        ImagePills = _statisticsService.GetPlatformImagePills(selected, platforms);
    }

    /// <summary>Restablece las pastillas a vacío (sin plataformas cargadas).</summary>
    private void ClearPills()
    {
        // Ítems con sus labels/descripciones a cero, para que los bindings del XAML sigan válidos.
        GamesAuditStats = _statisticsService.GetGlobalGameCollectionStatistics(Array.Empty<Platform>(), null);
        ImagePills = _statisticsService.GetPlatformImagePills(null, Array.Empty<Platform>());
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Restaura la gráfica activa del FlipView y la configuración (tipo / orden / Top X) de las cuatro gráficas,
    /// y recalcula los datos. La llama el control una vez restaurados los ajustes de disco.
    /// </summary>
    public override void LoadConfig()
    {
        AppSettings.StatsGlobalControlSettings config = _appSettings.StatsGlobalControl;
        if (config != null)
        {
            SelectedChartIndex = config.SelectedChartIndex;
            GameSetChart.ApplyFrom(config.GameSetChart);
            GameCountChart.ApplyFrom(config.GameCountChart);
            ImageCountChart.ApplyFrom(config.ImageCountChart);
            ImageSizeChart.ApplyFrom(config.ImageSizeChart);
        }

        Refresh();
    }

    /// <summary>Vuelca la gráfica activa y la configuración de las cuatro gráficas en los ajustes de la aplicación.</summary>
    public override void SaveConfig()
    {
        AppSettings.StatsGlobalControlSettings config = _appSettings.StatsGlobalControl;
        config.SelectedChartIndex = SelectedChartIndex;
        GameSetChart.StoreTo(config.GameSetChart);
        GameCountChart.StoreTo(config.GameCountChart);
        ImageCountChart.StoreTo(config.ImageCountChart);
        ImageSizeChart.StoreTo(config.ImageSizeChart);
    }

    public override void Dispose()
    {
        SharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged;
        _imageLoadingService.ImageAddedToGame -= OnImageAddedToGame;
        _imageLoadingService.PlatformImagesChanged -= OnPlatformImagesChanged;
        PropertyChanged -= OnSelfPropertyChanged;
    }
    #endregion
}
