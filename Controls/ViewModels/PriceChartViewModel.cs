using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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

    /// <summary>URL del producto en esta tienda: para abrirla en el navegador y casar la selección con la abierta.</summary>
    public string Url { get; init; } = string.Empty;

    public string PriceText { get; init; } = string.Empty;

    /// <summary>La tienda es Amazon: se muestra el indicador Prime (sí/no).</summary>
    public bool ShowPrime { get; init; }

    /// <summary>La tienda es Prime (solo relevante cuando <see cref="ShowPrime"/> es true).</summary>
    public bool IsPrime { get; init; }

    /// <summary>Mostrar la pastilla verde "Prime" (Amazon y Prime).</summary>
    public bool ShowPrimeYes => ShowPrime && IsPrime;

    /// <summary>Mostrar la pastilla gris "No Prime" (Amazon y no Prime).</summary>
    public bool ShowPrimeNo => ShowPrime && !IsPrime;

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

    /// <summary>
    /// Evolución del precio MÍNIMO por ronda de lectura: el menor precio (efectivo) entre todas las tiendas en cada
    /// ronda. Serie ÚNICA alineada a <see cref="Labels"/>, para la gráfica de área del precio mínimo (ver <see cref="ShowMinPriceChart"/>).
    /// </summary>
    public IReadOnlyList<double> MinPriceValues { get; private set; } = Array.Empty<double>();

    /// <summary>Límite inferior del eje Y de la gráfica por tienda (mismo ±10% que su auto-ajuste), o NaN si no hay datos.
    /// La gráfica de precio mínimo lo usa para compartir EXACTAMENTE el mismo eje Y que la de precios por tienda.</summary>
    public double MinChartAxisMin { get; private set; } = double.NaN;

    /// <summary>Límite superior del eje Y de la gráfica por tienda (mismo ±10% que su auto-ajuste), o NaN si no hay datos.</summary>
    public double MinChartAxisMax { get; private set; } = double.NaN;

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

    /// <summary>
    /// Etiqueta de la tarjeta de precio de la izquierda: "Precio actual" normalmente, o "Precio de compra" si el
    /// producto está comprado (en cuyo caso la tarjeta muestra el precio de compra en vez del mejor precio actual).
    /// </summary>
    public string CurrentPriceLabel { get; private set; } = string.Empty;

    /// <summary>Precio de la tarjeta izquierda: mejor precio actual, o el precio de COMPRA si el producto está comprado. "—" si no hay.</summary>
    public string CurrentPriceText { get; private set; } = "—";

    /// <summary>Tienda del mejor precio actual (descripción de la tarjeta); vacío si el producto está comprado.</summary>
    public string CurrentPriceStore { get; private set; } = string.Empty;

    /// <summary>Precio más bajo del histórico formateado, o "—".</summary>
    public string LowestPriceText { get; private set; } = "—";

    /// <summary>Tienda donde se dio el precio más bajo del histórico (para la descripción de la tarjeta), o vacío.</summary>
    public string LowestPriceStore { get; private set; } = string.Empty;

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

    /// <summary>Color del BORDE del pill de Prime (color completo): verde si es Prime, gris si no.</summary>
    public Brush PrimeBrush { get; private set; } = PrimeGray;

    /// <summary>Color de FONDO del pill de Prime (el mismo color al 60% de opacidad).</summary>
    public Brush PrimeBackgroundBrush { get; private set; } = PrimeGray;

    /// <summary>Una entrada por tienda del producto (etiqueta + precio actual + Prime) para el strip de la cabecera.</summary>
    public IReadOnlyList<StoreChip> Stores { get; private set; } = Array.Empty<StoreChip>();

    /// <summary>Producto que se está mostrando (el seleccionado global, o el fijado con <see cref="PinTo"/>).</summary>
    public Product? Product => _product;

    /// <summary>El producto mostrado está marcado como favorito.</summary>
    public bool IsFavorite { get; private set; }

    /// <summary>El producto mostrado está marcado como comprado (pill + estado del botón de comprado).</summary>
    public bool IsPurchased { get; private set; }

    /// <summary>Se puede alternar el favorito: hay producto, NO está comprado, y o ya es favorito o no se alcanzó el máximo.</summary>
    public bool CanToggleFavorite { get; private set; }

    /// <summary>Se puede eliminar el enlace actual: hay producto y tiene MÁS de una tienda (nunca se borra el último enlace).</summary>
    public bool CanRemoveStore { get; private set; }

    /// <summary>Tipo de gráfica (enlazado TwoWay al <c>ChartTypeSelectorControl</c>); se persiste por widget/favorito.</summary>
    public ChartType SelectedChartType
    {
        get => _selectedChartType;
        set { if (SetProperty(ref _selectedChartType, value)) OnPropertyChanged(nameof(EffectiveChartType)); }
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

    // --- Entradas EFECTIVAS de la ÚNICA gráfica de la vista, según el toggle global ShowMinPriceChart. ---
    // Con el toggle activo se muestra la evolución del precio MÍNIMO (serie única, área, eje Y compartido); si no, la
    // de precios POR TIENDA (multi-serie, con auto-ajuste de eje). Al cambiar el toggle solo se re-notifican estas
    // propiedades (sin recalcular datos) y la gráfica se reconstruye una vez.

    /// <summary>Tipo de gráfica efectivo: área en modo precio mínimo; si no, el tipo seleccionado (persistido).</summary>
    public ChartType EffectiveChartType => SharedDataService.ShowMinPriceChart ? ChartType.Area : SelectedChartType;

    /// <summary>Series multi (por tienda) que recibe la gráfica: vacío en modo precio mínimo (pasa a serie única).</summary>
    public IReadOnlyList<IReadOnlyList<double>> EffectiveSeriesValues
        => SharedDataService.ShowMinPriceChart ? Array.Empty<IReadOnlyList<double>>() : SeriesValues;

    /// <summary>Serie única que recibe la gráfica: la del precio mínimo en modo precio mínimo; vacía en modo por tienda.</summary>
    public IReadOnlyList<double> EffectiveValues
        => SharedDataService.ShowMinPriceChart ? MinPriceValues : Array.Empty<double>();

    /// <summary>Límite inferior del eje Y: el dominio compartido en modo precio mínimo; NaN (auto-ajuste) en modo por tienda.</summary>
    public double EffectiveValueMin => SharedDataService.ShowMinPriceChart ? MinChartAxisMin : double.NaN;

    /// <summary>Límite superior del eje Y: el dominio compartido en modo precio mínimo; NaN (auto-ajuste) en modo por tienda.</summary>
    public double EffectiveValueMax => SharedDataService.ShowMinPriceChart ? MinChartAxisMax : double.NaN;

    #endregion

    #region Constants
    /// <summary>Fallback del pill Prime si el brush del tema no estuviera disponible (gris genérico).</summary>
    private static readonly Brush PrimeGray = new SolidColorBrush(Color.FromArgb(0xFF, 0x75, 0x75, 0x75));

    /// <summary>Brush VIVO del recurso del tema (mutado in situ, se actualiza en caliente), o el gris de fallback.</summary>
    private static Brush ResolveBrush(string key)
        => Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out object? b) && b is Brush brush ? brush : PrimeGray;
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

    /// <summary>Se añadió o eliminó una tienda del producto (p. ej. "eliminar enlace"): recalcula el strip de tiendas, la gráfica y los derivados.</summary>
    private void OnStoresCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Recompute();

    private void OnFavoritesChanged(object? sender, EventArgs e) => UpdateFavoriteState();

    /// <summary>
    /// Reacciona a ajustes globales: al incluir/excluir envío recalcula la gráfica y las pastillas (precio efectivo); al
    /// alternar la gráfica de precio mínimo re-notifica solo las entradas EFECTIVAS (los datos no cambian) para que la
    /// única gráfica se reconstruya en el modo correcto.
    /// </summary>
    private void OnSharedDataChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.IncludeShippingInPrice))
        {
            Recompute();
        }
        else if (e.PropertyName == nameof(SharedDataService.ShowMinPriceChart))
        {
            OnPropertyChanged(nameof(EffectiveChartType));
            OnPropertyChanged(nameof(EffectiveSeriesValues));
            OnPropertyChanged(nameof(EffectiveValues));
            OnPropertyChanged(nameof(EffectiveValueMin));
            OnPropertyChanged(nameof(EffectiveValueMax));
        }
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
            _product.Stores.CollectionChanged -= OnStoresCollectionChanged;
        }

        _product = product;

        if (_product is not null)
        {
            _product.PriceRecorded += OnPriceRecorded;
            _product.PropertyChanged += OnProductPropertyChanged;
            _product.Stores.CollectionChanged += OnStoresCollectionChanged;
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
        else if (e.PropertyName == nameof(Product.IsPurchased))
        {
            UpdateFavoriteState();     // refresca IsPurchased (pill), y CanToggleFavorite
            UpdateCurrentPriceCard();  // "Precio actual" <-> "Precio de compra"
        }
    }

    /// <summary>
    /// Actualiza la tarjeta de precio izquierda según el estado de compra: si el producto está COMPRADO muestra
    /// "Precio de compra" + el precio de compra (sin tienda); si no, "Precio actual" + el mejor precio y su tienda.
    /// </summary>
    private void UpdateCurrentPriceCard()
    {
        Product? product = _product;
        bool purchased = product?.IsPurchased ?? false;

        CurrentPriceLabel = L(purchased ? LocKeys.PriceChart_PurchasedPrice_Label : LocKeys.PriceChart_CurrentPrice_Label);
        CurrentPriceText = FormatPrice(purchased ? product?.PurchasePrice : product?.BestPrice);
        // Descripción: si está comprado, la FECHA de compra (local, formato corto); si no, la tienda del mejor precio.
        CurrentPriceStore = purchased
            ? (product?.PurchasedAt is DateTime at ? at.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : string.Empty)
            : (product?.BestStore?.Label ?? string.Empty);

        OnPropertyChanged(nameof(CurrentPriceLabel));
        OnPropertyChanged(nameof(CurrentPriceText));
        OnPropertyChanged(nameof(CurrentPriceStore));
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
            MinPriceValues = Array.Empty<double>();
            MinChartAxisMin = double.NaN;
            MinChartAxisMax = double.NaN;
        }
        else
        {
            List<DateTime> rounds = history.Select(point => point.Timestamp).Distinct().OrderBy(t => t).ToList();
            Labels = rounds.Select(t => t.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.CurrentCulture)).ToList();

            // Una serie por TIENDA (identificada por Id, NO por etiqueta: dos enlaces al mismo host son series distintas)
            // que tenga al menos un precio, en el orden de las tiendas del producto.
            List<ProductStore> seriesStores = product.Stores
                .Where(store => history.Any(point => point.StoreId == store.Id))
                .ToList();

            // Si el ajuste incluye el envío, se suma el envío ACTUAL de cada tienda a toda su serie (aproximación del
            // histórico, que no guarda el envío de cada momento).
            bool includeShipping = SharedDataService.IncludeShippingInPrice;
            SeriesNames = seriesStores.Select(store => store.Label).ToList();
            SeriesValues = seriesStores
                .Select(store =>
                {
                    double shipping = includeShipping && store.ShippingCost is decimal cost ? (double)cost : 0;
                    return (IReadOnlyList<double>)BuildStoreSeries(store.Id, rounds, history, shipping);
                })
                .ToList();

            // Evolución del precio mínimo: por ronda, el menor precio (efectivo) entre las series de tienda.
            MinPriceValues = BuildMinSeries(SeriesValues, rounds.Count);

            // Dominio del eje Y de la gráfica POR TIENDA (mismo ±10% que su FitValueAxis), para que la gráfica de mínimo
            // comparta EXACTAMENTE el mismo eje Y (misma referencia visual de precios).
            (MinChartAxisMin, MinChartAxisMax) = ComputeSharedAxis(SeriesValues);
        }

        HasProduct = product is not null;
        ProductName = product?.Name ?? string.Empty;
        Image = BuildImage(product?.ImageUrl);

        ProductStore? bestStore = product?.BestStore;
        UpdateCurrentPriceCard();

        // Precio más bajo del histórico, por precio EFECTIVO (suma el envío actual de la tienda si el ajuste lo incluye).
        if (product is not null && history is { Count: > 0 })
        {
            bool includeShippingLow = SharedDataService.IncludeShippingInPrice;
            decimal EffectiveOf(PricePoint p) => p.Price + (includeShippingLow && product.Stores.FirstOrDefault(s => s.Id == p.StoreId)?.ShippingCost is decimal c ? c : 0);
            PricePoint lowest = history.OrderBy(EffectiveOf).First();
            LowestPriceText = FormatPrice(EffectiveOf(lowest));
            LowestPriceStore = lowest.StoreLabel;
        }
        else
        {
            LowestPriceText = "—";
            LowestPriceStore = string.Empty;
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
        PrimeBrush = ResolveBrush(prime ? "ExtraColor4Brush" : "DangerBrush");
        PrimeBackgroundBrush = ResolveBrush(prime ? "ExtraColor4BrushOpacity60" : "DangerBrushOpacity60");

        Stores = product is null
            ? Array.Empty<StoreChip>()
            : product.Stores.Select(store => new StoreChip
            {
                Label = store.Label,
                Url = store.Url,
                PriceText = store.EffectivePrice is decimal price ? FormatStorePrice(price, store.Currency) : "—",
                ShowPrime = store.Label.StartsWith("Amazon", StringComparison.OrdinalIgnoreCase),
                IsPrime = store.IsPrime,
                ShippingText = store.ShippingCost is decimal shipping ? "+" + FormatStorePrice(shipping, store.Currency) : string.Empty,
                // Si el envío ya va incluido en el precio, no se muestra aparte.
                ShowShipping = !SharedDataService.IncludeShippingInPrice && store.ShippingCost is decimal shippingCost && shippingCost > 0
            }).ToList();

        OnPropertyChanged(nameof(SeriesValues));
        OnPropertyChanged(nameof(SeriesNames));
        OnPropertyChanged(nameof(MinPriceValues));
        OnPropertyChanged(nameof(MinChartAxisMin));
        OnPropertyChanged(nameof(MinChartAxisMax));
        // Entradas efectivas de la gráfica (dependen de los datos recién calculados).
        OnPropertyChanged(nameof(EffectiveSeriesValues));
        OnPropertyChanged(nameof(EffectiveValues));
        OnPropertyChanged(nameof(EffectiveValueMin));
        OnPropertyChanged(nameof(EffectiveValueMax));
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(ValueSuffix));
        OnPropertyChanged(nameof(HasProduct));
        OnPropertyChanged(nameof(ProductName));
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(CurrentPriceText));
        OnPropertyChanged(nameof(CurrentPriceStore));
        OnPropertyChanged(nameof(LowestPriceText));
        OnPropertyChanged(nameof(LowestPriceStore));
        OnPropertyChanged(nameof(ShowPrime));
        OnPropertyChanged(nameof(ShowPromo));
        OnPropertyChanged(nameof(ShowIssues));
        OnPropertyChanged(nameof(ShowPreorder));
        OnPropertyChanged(nameof(PrimeText));
        OnPropertyChanged(nameof(PrimeBrush));
        OnPropertyChanged(nameof(PrimeBackgroundBrush));
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
        IsPurchased = _product?.IsPurchased ?? false;
        int favoriteCount = SharedDataService.ProductSet.Products.Count(product => product.IsFavorite);
        // Un producto comprado no puede ser favorito: el botón se deshabilita.
        CanToggleFavorite = _product is not null && !_product.IsPurchased && (_product.IsFavorite || favoriteCount < ProductService.MaxFavorites);
        CanRemoveStore = _product is not null && _product.Stores.Count > 1;
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(IsPurchased));
        OnPropertyChanged(nameof(CanToggleFavorite));
        OnPropertyChanged(nameof(CanRemoveStore));
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
    private static double[] BuildStoreSeries(long storeId, List<DateTime> rounds, List<PricePoint> history, double shippingOffset = 0)
    {
        var byTimestamp = new Dictionary<DateTime, double>();
        foreach (PricePoint point in history)
            if (point.StoreId == storeId)
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

    /// <summary>
    /// Serie del precio MÍNIMO por ronda: en cada índice, el menor valor entre todas las series de tienda (que ya vienen
    /// alineadas a las rondas y con huecos rellenados). Si en una ronda no hay ningún valor, arrastra 0.
    /// </summary>
    private static double[] BuildMinSeries(IReadOnlyList<IReadOnlyList<double>> series, int roundCount)
    {
        var min = new double[roundCount];
        for (int i = 0; i < roundCount; i++)
        {
            double best = double.MaxValue;
            foreach (IReadOnlyList<double> serie in series)
                if (i < serie.Count && serie[i] < best)
                    best = serie[i];
            min[i] = best == double.MaxValue ? 0 : best;
        }
        return min;
    }

    /// <summary>
    /// Dominio (min, max) del eje Y que usaría la gráfica por tienda con FitValueAxis: [max(0, dataMin*0.9), dataMax*1.1]
    /// sobre TODOS los valores de todas las series. (NaN, NaN) si no hay datos. Debe replicar el cálculo de
    /// <c>ChartTypeSelectorControl.ResolveValueAxisLimits</c> para que ambas gráficas compartan el mismo eje.
    /// </summary>
    private static (double Min, double Max) ComputeSharedAxis(IReadOnlyList<IReadOnlyList<double>> series)
    {
        double dataMin = double.MaxValue, dataMax = double.MinValue;
        foreach (IReadOnlyList<double> serie in series)
            foreach (double v in serie)
            {
                if (v < dataMin) dataMin = v;
                if (v > dataMax) dataMax = v;
            }

        if (dataMax < dataMin)
            return (double.NaN, double.NaN);

        double min = Math.Max(0, dataMin * 0.9);
        double max = dataMax * 1.1;
        if (max <= min)
            max = min + 1;
        return (min, max);
    }

    /// <summary>Formatea un precio con la moneda ("39,99 €"), o "—" si es null. La tienda va aparte (descripción).</summary>
    private string FormatPrice(decimal? price)
    {
        if (price is not decimal value)
            return "—";

        string text = value.ToString("0.00", CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(ValueSuffix) ? text : text + " " + ValueSuffix;
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
            _product.Stores.CollectionChanged -= OnStoresCollectionChanged;
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
