using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Enums;
using MM4LB.Helpers;
using MM4LB.Models;
using MM4LB.Services;
using Windows.UI;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>Una tienda del producto para el "strip" de la cabecera: etiqueta, precio actual y badge de Prime.</summary>
public sealed class StoreChip
{
    public string Label { get; init; } = string.Empty;
    public string PriceText { get; init; } = string.Empty;

    /// <summary>Muestra el badge verde de Prime (solo si la tienda es Amazon y es Prime).</summary>
    public bool ShowPrimeBadge { get; init; }

    /// <summary>Coste de envío formateado ("+3,99 €"), o vacío si no hay coste.</summary>
    public string ShippingText { get; init; } = string.Empty;

    /// <summary>Hay coste de envío detectado (&gt; 0) para mostrar la pastilla.</summary>
    public bool ShowShipping { get; init; }
}

/// <summary>
/// ViewModel del widget de gráfica de precios: expone la evolución del precio del producto seleccionado como
/// multi-serie (una serie por tienda) para <c>ChartTypeSelectorControl</c>, más la cabecera del producto (imagen,
/// título y pills de precio actual / mínimo histórico, indicando de qué tienda es cada uno). Se recalcula al cambiar
/// de producto y al registrarse un precio nuevo (<see cref="Product.PriceRecorded"/>, p. ej. desde el scheduler).
/// </summary>
public partial class PriceChartViewModel : WidgetViewModelBase
{
    #region Attributes
    private Product? _product;
    private bool _followSelection = true;

    private ChartType _selectedChartType = ChartType.Line;
    private SortMode _sortOrder = SortMode.None;
    private int _topN;
    #endregion

    #region Properties
    /// <summary>Una serie de precios por tienda (eje Y); alineadas por ronda de lectura (eje X = <see cref="Labels"/>).</summary>
    public IReadOnlyList<IReadOnlyList<double>> SeriesValues { get; private set; } = Array.Empty<IReadOnlyList<double>>();

    /// <summary>Nombre de cada serie (etiqueta de tienda), en el mismo orden que <see cref="SeriesValues"/>.</summary>
    public IReadOnlyList<string> SeriesNames { get; private set; } = Array.Empty<string>();

    /// <summary>Fechas de las rondas de lectura formateadas (eje X de la gráfica).</summary>
    public IReadOnlyList<string> Labels { get; private set; } = Array.Empty<string>();

    /// <summary>Símbolo de moneda a añadir a los valores (del mejor precio / primera tienda), o vacío.</summary>
    public string ValueSuffix { get; private set; } = string.Empty;

    /// <summary>Nombre del producto seleccionado (cabecera del widget).</summary>
    public string ProductName { get; private set; } = string.Empty;

    /// <summary>Imagen del producto (o null si no hay producto/imagen).</summary>
    public ImageSource? Image { get; private set; }

    /// <summary>Hay un producto seleccionado (para mostrar/ocultar la cabecera).</summary>
    public bool HasProduct { get; private set; }

    /// <summary>Precio actual (mejor precio) con su tienda, o "—".</summary>
    public string CurrentPriceText { get; private set; } = "—";

    /// <summary>Precio más bajo del histórico con la tienda donde se dio, o "—".</summary>
    public string LowestPriceText { get; private set; } = "—";

    /// <summary>Se muestra el pill de Prime (solo si el mejor precio es de una tienda Amazon).</summary>
    public bool ShowPrime { get; private set; }

    /// <summary>El producto tiene alguna promoción / oferta / cupón / voucher en alguna tienda (pill de promo).</summary>
    public bool ShowPromo { get; private set; }

    /// <summary>El producto tiene algún problema (no disponible / sin precio en alguna tienda): pill de aviso.</summary>
    public bool ShowIssues { get; private set; }

    /// <summary>El producto es una reserva / pre-order (pill).</summary>
    public bool ShowPreorder { get; private set; }

    /// <summary>Hay un precio de alerta configurado para el producto (cambia el icono del botón de alerta).</summary>
    public bool HasAlert { get; private set; }

    /// <summary>El mejor precio está en/por debajo del precio de alerta configurado (pill de "objetivo").</summary>
    public bool ShowBelowAlert { get; private set; }

    /// <summary>Precio de alerta formateado con su moneda (para el pill/tooltip), o "—".</summary>
    public string AlertPriceText { get; private set; } = "—";

    /// <summary>Texto del pill de Prime ("Prime" / "No Prime") de la tienda del mejor precio.</summary>
    public string PrimeText { get; private set; } = string.Empty;

    /// <summary>Color del pill de Prime: verde si es Prime, gris si no.</summary>
    public Brush PrimeBrush { get; private set; } = PrimeGray;

    /// <summary>Una entrada por tienda del producto (etiqueta + precio actual + Prime) para el strip de la cabecera.</summary>
    public IReadOnlyList<StoreChip> Stores { get; private set; } = Array.Empty<StoreChip>();

    /// <summary>Producto que se está mostrando (el seleccionado global, o el fijado con <see cref="PinTo"/>).</summary>
    public Product? Product => _product;

    /// <summary>El producto mostrado está marcado como favorito.</summary>
    public bool IsFavorite { get; private set; }

    /// <summary>Se puede alternar el favorito: hay producto y, o ya es favorito, o no se alcanzó el máximo.</summary>
    public bool CanToggleFavorite { get; private set; }

    /// <summary>Tipo de gráfica (enlazado TwoWay al <c>ChartTypeSelectorControl</c>); se persiste por widget/favorito.</summary>
    public ChartType SelectedChartType
    {
        get => _selectedChartType;
        set => SetProperty(ref _selectedChartType, value);
    }

    /// <summary>Orden de los elementos de la gráfica (enlazado TwoWay); se persiste.</summary>
    public SortMode SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    /// <summary>Top N de la gráfica (0 = todos; enlazado TwoWay); se persiste.</summary>
    public int TopN
    {
        get => _topN;
        set => SetProperty(ref _topN, value);
    }
    #endregion

    #region Constants
    private static readonly Brush PrimeGreen = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x7D, 0x32));
    private static readonly Brush PrimeGray = new SolidColorBrush(Color.FromArgb(0xFF, 0x75, 0x75, 0x75));
    #endregion

    #region Constructor
    public PriceChartViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        // Config de gráfica del widget del producto seleccionado (los favoritos la sobrescriben tras PinTo). El
        // .ini ya está restaurado en este punto, así que se leen los valores persistidos directamente a los campos.
        _selectedChartType = _appSettings.PriceChartControl.ChartType;
        _sortOrder = _appSettings.PriceChartControl.SortOrder;
        _topN = _appSettings.PriceChartControl.TopN;

        SharedDataService.SelectedProductChanged += OnSelectedProductChanged;
        SharedDataService.FavoritesChanged += OnFavoritesChanged;
        SharedDataService.PropertyChanged += OnSharedDataChanged;
        Bind(SharedDataService.SelectedProduct);
    }
    #endregion

    #region Subscribed events
    private void OnSelectedProductChanged(object? sender, ProductChangedEventArgs e) => Bind(e.NewProduct);

    private void OnPriceRecorded(object? sender, EventArgs e) => Recompute();

    private void OnFavoritesChanged(object? sender, EventArgs e) => UpdateFavoriteState();

    /// <summary>Al cambiar el ajuste de incluir envío en el precio, recalcula la gráfica y las pastillas (precio efectivo).</summary>
    private void OnSharedDataChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.IncludeShippingInPrice))
            Recompute();
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Fija el ViewModel a un producto CONCRETO (para el FlipView de favoritos): deja de seguir la selección global
    /// y muestra siempre <paramref name="product"/>.
    /// </summary>
    public void PinTo(Product? product)
    {
        if (_followSelection)
        {
            SharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
            _followSelection = false;
        }

        Bind(product);
    }
    #endregion

    #region Methods (private)
    private void Bind(Product? product)
    {
        if (_product is not null)
        {
            _product.PriceRecorded -= OnPriceRecorded;
            _product.PropertyChanged -= OnProductPropertyChanged;
        }

        _product = product;

        if (_product is not null)
        {
            _product.PriceRecorded += OnPriceRecorded;
            _product.PropertyChanged += OnProductPropertyChanged;
        }

        Recompute();
    }

    /// <summary>Cambió una propiedad del producto mostrado (título o imagen al editarlos): refresca la cabecera del widget.</summary>
    private void OnProductPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Product.Name))
        {
            ProductName = _product?.Name ?? string.Empty;
            OnPropertyChanged(nameof(ProductName));
        }
        else if (e.PropertyName == nameof(Product.ImageUrl))
        {
            Image = BuildImage(_product?.ImageUrl);
            OnPropertyChanged(nameof(Image));
        }
    }

    private void Recompute()
    {
        Product? product = _product;
        List<PricePoint>? history = product?.PriceHistory;
        ValueSuffix = product?.BestStore?.Currency ?? product?.Stores.FirstOrDefault()?.Currency ?? string.Empty;

        if (product is null || history is null || history.Count == 0)
        {
            SeriesValues = Array.Empty<IReadOnlyList<double>>();
            SeriesNames = Array.Empty<string>();
            Labels = Array.Empty<string>();
        }
        else
        {
            List<DateTime> rounds = history.Select(point => point.Timestamp).Distinct().OrderBy(t => t).ToList();
            Labels = rounds.Select(t => t.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.CurrentCulture)).ToList();

            // Una serie por tienda que tenga al menos un precio, en el orden de las tiendas del producto.
            List<string> storeLabels = product.Stores
                .Select(store => store.Label)
                .Where(label => history.Any(point => point.StoreLabel == label))
                .Distinct()
                .ToList();

            // Si el ajuste incluye el envío, se suma el envío ACTUAL de cada tienda a toda su serie (aproximación del
            // histórico, que no guarda el envío de cada momento).
            bool includeShipping = SharedDataService.IncludeShippingInPrice;
            SeriesNames = storeLabels;
            SeriesValues = storeLabels
                .Select(label =>
                {
                    double shipping = includeShipping && product.Stores.FirstOrDefault(s => s.Label == label)?.ShippingCost is decimal cost ? (double)cost : 0;
                    return (IReadOnlyList<double>)BuildStoreSeries(label, rounds, history, shipping);
                })
                .ToList();
        }

        HasProduct = product is not null;
        ProductName = product?.Name ?? string.Empty;
        Image = BuildImage(product?.ImageUrl);

        ProductStore? bestStore = product?.BestStore;
        CurrentPriceText = FormatPriceWithStore(product?.BestPrice, bestStore?.Label);

        // Precio más bajo del histórico, por precio EFECTIVO (suma el envío actual de la tienda si el ajuste lo incluye).
        if (product is not null && history is { Count: > 0 })
        {
            bool includeShippingLow = SharedDataService.IncludeShippingInPrice;
            decimal EffectiveOf(PricePoint p) => p.Price + (includeShippingLow && product.Stores.FirstOrDefault(s => s.Label == p.StoreLabel)?.ShippingCost is decimal c ? c : 0);
            PricePoint lowest = history.OrderBy(EffectiveOf).First();
            LowestPriceText = FormatPriceWithStore(EffectiveOf(lowest), lowest.StoreLabel);
        }
        else
        {
            LowestPriceText = "—";
        }

        // Promoción / oferta / cupón / voucher en alguna tienda del producto.
        ShowPromo = product?.HasPromo ?? false;

        // Problema: no disponible / sin precio en alguna tienda.
        ShowIssues = product?.HasIssues ?? false;

        // Reserva / pre-order.
        ShowPreorder = product?.IsPreorder ?? false;

        // Prime del mejor precio (solo tiene sentido en Amazon).
        ShowPrime = bestStore is not null && bestStore.Label.StartsWith("Amazon", StringComparison.OrdinalIgnoreCase);
        bool prime = bestStore?.IsPrime ?? false;
        PrimeText = prime ? L(LocKeys.PriceChart_Prime_Label) : L(LocKeys.PriceChart_NoPrime_Label);
        PrimeBrush = prime ? PrimeGreen : PrimeGray;

        Stores = product is null
            ? Array.Empty<StoreChip>()
            : product.Stores.Select(store => new StoreChip
            {
                Label = store.Label,
                PriceText = store.EffectivePrice is decimal price ? FormatStorePrice(price, store.Currency) : "—",
                ShowPrimeBadge = store.IsPrime && store.Label.StartsWith("Amazon", StringComparison.OrdinalIgnoreCase),
                ShippingText = store.ShippingCost is decimal shipping ? "+" + FormatStorePrice(shipping, store.Currency) : string.Empty,
                // Si el envío ya va incluido en el precio, no se muestra aparte.
                ShowShipping = !SharedDataService.IncludeShippingInPrice && store.ShippingCost is decimal shippingCost && shippingCost > 0
            }).ToList();

        OnPropertyChanged(nameof(SeriesValues));
        OnPropertyChanged(nameof(SeriesNames));
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(ValueSuffix));
        OnPropertyChanged(nameof(HasProduct));
        OnPropertyChanged(nameof(ProductName));
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(CurrentPriceText));
        OnPropertyChanged(nameof(LowestPriceText));
        OnPropertyChanged(nameof(ShowPrime));
        OnPropertyChanged(nameof(ShowPromo));
        OnPropertyChanged(nameof(ShowIssues));
        OnPropertyChanged(nameof(ShowPreorder));
        OnPropertyChanged(nameof(PrimeText));
        OnPropertyChanged(nameof(PrimeBrush));
        OnPropertyChanged(nameof(Stores));

        UpdateFavoriteState();
        RefreshAlertState();
    }

    /// <summary>Recalcula el estado del precio de alerta (configurado / por debajo / texto). Público para refrescar tras fijarlo.</summary>
    public void RefreshAlertState()
    {
        HasAlert = _product?.HasAlert ?? false;
        ShowBelowAlert = _product?.IsBelowAlert ?? false;
        AlertPriceText = _product?.AlertPriceText ?? "—";
        OnPropertyChanged(nameof(HasAlert));
        OnPropertyChanged(nameof(ShowBelowAlert));
        OnPropertyChanged(nameof(AlertPriceText));
    }

    private void UpdateFavoriteState()
    {
        IsFavorite = _product?.IsFavorite ?? false;
        int favoriteCount = SharedDataService.ProductSet.Products.Count(product => product.IsFavorite);
        CanToggleFavorite = _product is not null && (_product.IsFavorite || favoriteCount < ProductService.MaxFavorites);
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(CanToggleFavorite));
    }

    /// <summary>Formatea el precio de una tienda con su moneda ("39,99 €").</summary>
    private static string FormatStorePrice(decimal price, string? currency)
    {
        string text = price.ToString("0.00", CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(currency) ? text : $"{text} {currency}";
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;

    /// <summary>
    /// Construye la serie de precios de una tienda alineada a las rondas: usa el precio de la tienda en cada ronda
    /// y, si falta (fallo de lectura), arrastra el último conocido; las rondas anteriores a su primera lectura se
    /// rellenan con esa primera lectura (para no dibujar caídas a 0).
    /// </summary>
    private static double[] BuildStoreSeries(string label, List<DateTime> rounds, List<PricePoint> history, double shippingOffset = 0)
    {
        var byTimestamp = new Dictionary<DateTime, double>();
        foreach (PricePoint point in history)
            if (point.StoreLabel == label)
                byTimestamp[point.Timestamp] = (double)point.Price;

        double firstKnown = 0;
        foreach (DateTime round in rounds)
            if (byTimestamp.TryGetValue(round, out double value))
            {
                firstKnown = value;
                break;
            }

        double last = firstKnown;
        var series = new double[rounds.Count];
        for (int i = 0; i < rounds.Count; i++)
        {
            if (byTimestamp.TryGetValue(rounds[i], out double value))
                last = value;
            series[i] = last + shippingOffset;   // + envío si el ajuste lo incluye (offset constante por tienda)
        }
        return series;
    }

    /// <summary>Formatea un precio con la moneda y la tienda ("39,99 € (Amazon.es)"), o "—" si es null.</summary>
    private string FormatPriceWithStore(decimal? price, string? storeLabel)
    {
        if (price is not decimal value)
            return "—";

        string text = value.ToString("0.00", CultureInfo.CurrentCulture);
        if (!string.IsNullOrEmpty(ValueSuffix))
            text += " " + ValueSuffix;
        if (!string.IsNullOrWhiteSpace(storeLabel))
            text += $" ({storeLabel})";
        return text;
    }

    /// <summary>Crea un <see cref="ImageSource"/> desde la URL de imagen, o null si no es válida.</summary>
    private static ImageSource? BuildImage(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? uri))
            return null;

        return new BitmapImage(uri);
    }
    #endregion

    #region Methods (public)
    public override void Dispose()
    {
        SharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
        SharedDataService.FavoritesChanged -= OnFavoritesChanged;
        SharedDataService.PropertyChanged -= OnSharedDataChanged;
        if (_product is not null)
        {
            _product.PriceRecorded -= OnPriceRecorded;
            _product.PropertyChanged -= OnProductPropertyChanged;
        }
    }

    public override void LoadConfig()
    {
        // Solo el widget del producto seleccionado (que sigue la selección) usa la sección PriceChartControl; las
        // instancias fijadas a un favorito gestionan su config vía FavoritesViewModel (por Id de producto).
        if (!_followSelection)
            return;

        SelectedChartType = _appSettings.PriceChartControl.ChartType;
        SortOrder = _appSettings.PriceChartControl.SortOrder;
        TopN = _appSettings.PriceChartControl.TopN;
    }

    public override void SaveConfig()
    {
        if (!_followSelection)
            return;

        _appSettings.PriceChartControl.ChartType = SelectedChartType;
        _appSettings.PriceChartControl.SortOrder = SortOrder;
        _appSettings.PriceChartControl.TopN = TopN;
    }
    #endregion
}
