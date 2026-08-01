using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MM4LB.Services;

namespace MM4LB.Models;

/// <summary>Tendencia del precio de un producto respecto a la lectura anterior.</summary>
public enum PriceTrend
{
    /// <summary>Sin datos suficientes (menos de dos lecturas).</summary>
    Unknown,

    /// <summary>El precio ha bajado.</summary>
    Down,

    /// <summary>El precio se mantiene igual.</summary>
    Same,

    /// <summary>El precio ha subido.</summary>
    Up,
}

/// <summary>Cómo se resalta el recuadro de precio en la lista de productos.</summary>
public enum PriceHighlight
{
    /// <summary>Neutro (sin cambio reciente y no es el mínimo histórico).</summary>
    None,

    /// <summary>El precio ha bajado (verde).</summary>
    Down,

    /// <summary>El precio ha subido (rojo).</summary>
    Up,

    /// <summary>Sin subida/bajada reciente, pero el precio actual es el mínimo histórico (azul).</summary>
    Low,
}

/// <summary>
/// A tracked product whose price is monitored across one or more store URLs (mainly Amazon
/// marketplaces in different countries). Exposes the current best price across its stores and a
/// timestamped price history used to chart the price evolution.
///
/// It is the item shown in the left-hand ListView of the main window (see ProductListControl),
/// mirroring the structural role the old Platform had.
/// </summary>
public partial class Product : ObservableObject
{
    #region Constants
    /// <summary>Tiempo que se mantiene el indicador de subida/bajada de precio tras un cambio, aunque luego no varíe.</summary>
    private static readonly TimeSpan TrendStickyWindow = TimeSpan.FromDays(3);
    #endregion

    #region Identity
    /// <summary>Primary key in the local database. 0 until the product has been persisted.</summary>
    public long Id { get; set; }
    #endregion

    #region Observable properties
    /// <summary>Display name, parsed from the product page. Empty until the first successful parse.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>URL of the main product image, if parsed. Optional.</summary>
    [ObservableProperty]
    private string? _imageUrl;

    /// <summary>Whether the product is marked as a favourite (shown in the favourites flip widget and the list).</summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Optional alert price: when the best price drops to/below it, the product is flagged as a good deal. Null = no alert.</summary>
    [ObservableProperty]
    private decimal? _alertPrice;
    #endregion

    #region Properties
    /// <summary>
    /// The store URLs being tracked for this product, one per marketplace/country (e.g. Amazon.es,
    /// Amazon.de). The lowest current price among them is the product's <see cref="BestPrice"/>.
    /// </summary>
    public ObservableCollection<ProductStore> Stores { get; } = new();

    /// <summary>
    /// Timestamped price observations across all stores, in the order they were recorded. Feeds the
    /// price-evolution chart. Each point records which store it came from.
    /// </summary>
    public List<PricePoint> PriceHistory { get; } = new();

    /// <summary>Mejor precio EFECTIVO actual (con envío si el ajuste global lo incluye) entre las tiendas de la moneda de referencia, o <c>null</c>.</summary>
    public decimal? BestPrice => BestStore?.EffectivePrice;

    /// <summary>Whether any tracked store currently shows a promotion / deal / coupon / voucher on the product.</summary>
    public bool HasPromo
    {
        get
        {
            foreach (ProductStore store in Stores)
                if (store.HasPromo)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Whether the product has an issue in some store: it was checked but is not available there, or it was checked
    /// and no price could be read (e.g. it doesn't exist in that marketplace). Used to flag it in the UI.
    /// </summary>
    public bool HasIssues
    {
        get
        {
            foreach (ProductStore store in Stores)
                if (!store.IsAvailable || (store.LastChecked is not null && store.CurrentPrice is null))
                    return true;
            return false;
        }
    }

    /// <summary>Whether the product is a pre-order (any tracked store reports it as such).</summary>
    public bool IsPreorder
    {
        get
        {
            foreach (ProductStore store in Stores)
                if (store.IsPreorder)
                    return true;
            return false;
        }
    }

    /// <summary>Whether an alert price is configured for this product.</summary>
    public bool HasAlert => AlertPrice.HasValue;

    /// <summary>
    /// La tienda del mejor precio tiene gastos de envío (&gt; 0). Para el icono de la lista (se muestra siempre que
    /// haya envío, aunque el precio ya lo incluya).
    /// </summary>
    public bool HasShipping => BestStore?.ShippingCost is decimal shipping && shipping > 0;

    /// <summary>Whether the current best price is at or below the configured alert price (a good deal to flag).</summary>
    public bool IsBelowAlert => AlertPrice is decimal alert && BestPrice is decimal best && best <= alert;

    /// <summary>Alert price formatted with the product's currency (or "—" if no alert).</summary>
    public string AlertPriceText
    {
        get
        {
            if (AlertPrice is not decimal value)
                return "—";

            string currency = BestStore?.Currency ?? (Stores.Count > 0 ? Stores[0].Currency : null) ?? string.Empty;
            string text = value.ToString("0.00", CultureInfo.CurrentCulture);
            return string.IsNullOrEmpty(currency) ? text : $"{text} {currency}";
        }
    }

    /// <summary>The store currently offering <see cref="BestPrice"/>, or <c>null</c> if no price is known.</summary>
    public ProductStore? BestStore
    {
        get
        {
            // Solo se comparan tiendas con la MISMA moneda (la de la mayoría de tiendas con precio), para no comparar
            // entre divisas distintas. Con los 5 marketplaces de Amazon (todos en euros) no cambia nada; protege el
            // caso de mezclar una tienda no-EUR.
            string referenceCurrency = Stores
                .Where(store => store.CurrentPrice is not null)
                .GroupBy(store => store.Currency ?? string.Empty)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault() ?? string.Empty;

            // Se compara por PRECIO EFECTIVO (con envío si el ajuste global lo incluye), de modo que la tienda "más
            // barata" refleje el coste final. Solo entre tiendas de la moneda de referencia.
            ProductStore? best = null;
            decimal bestEffective = 0;
            foreach (ProductStore store in Stores)
            {
                if (store.EffectivePrice is not decimal effective)
                    continue;
                if ((store.Currency ?? string.Empty) != referenceCurrency)
                    continue;
                if (best is null || effective < bestEffective)
                {
                    best = store;
                    bestEffective = effective;
                }
            }
            return best;
        }
    }

    /// <summary>Most recent time any store was checked, or <c>null</c> if never checked.</summary>
    public DateTime? LastChecked
    {
        get
        {
            DateTime? last = null;
            foreach (ProductStore store in Stores)
                if (store.LastChecked is DateTime stamp && (last is null || stamp > last))
                    last = stamp;
            return last;
        }
    }

    /// <summary>Mejor precio actual formateado con su moneda (o "—" si no hay precio). Para la lista de productos.</summary>
    public string BestPriceText
    {
        get
        {
            if (BestPrice is not decimal value)
                return "—";

            string currency = BestStore?.Currency ?? (Stores.Count > 0 ? Stores[0].Currency : null) ?? string.Empty;
            string text = value.ToString("0.00", CultureInfo.CurrentCulture);
            return string.IsNullOrEmpty(currency) ? text : $"{text} {currency}";
        }
    }

    /// <summary>
    /// Tendencia del mejor precio: dirección del ÚLTIMO cambio de precio (la última ronda cuyo mínimo difiere de la
    /// anterior). El indicador de subida/bajada se MANTIENE hasta <see cref="TrendStickyWindow"/> (3 días) desde ese
    /// cambio aunque en rondas posteriores el precio no varíe; pasado ese tiempo sin cambios pasa a neutro
    /// (<see cref="PriceTrend.Same"/>). Un cambio nuevo reinicia la dirección y la ventana.
    /// <see cref="PriceTrend.Unknown"/> si hay menos de dos rondas.
    /// </summary>
    public PriceTrend Trend
    {
        get
        {
            if (PriceHistory.Count == 0)
                return PriceTrend.Unknown;

            var rounds = PriceHistory
                .GroupBy(point => point.Timestamp)
                .OrderBy(group => group.Key)
                .Select(group => (Time: group.Key, Min: group.Min(point => EffectiveHistoricalPrice(point))))
                .ToList();
            if (rounds.Count < 2)
                return PriceTrend.Unknown;

            // Última ronda en la que el precio cambió respecto a la anterior (de atrás hacia delante).
            for (int i = rounds.Count - 1; i > 0; i--)
            {
                if (rounds[i].Min == rounds[i - 1].Min)
                    continue;

                PriceTrend direction = rounds[i].Min < rounds[i - 1].Min ? PriceTrend.Down : PriceTrend.Up;
                return DateTime.UtcNow - rounds[i].Time <= TrendStickyWindow ? direction : PriceTrend.Same;
            }

            return PriceTrend.Same;   // el precio nunca ha cambiado
        }
    }

    /// <summary>El mejor precio actual iguala (o mejora) el mínimo de todo el histórico registrado.</summary>
    public bool IsHistoricalLow
    {
        get
        {
            if (BestPrice is not decimal best || PriceHistory.Count == 0)
                return false;

            return best <= PriceHistory.Min(point => EffectiveHistoricalPrice(point));
        }
    }

    /// <summary>
    /// Resaltado del recuadro de precio en la lista: bajada/subida tienen prioridad (verde/rojo); si no hay cambio
    /// reciente pero el precio es el mínimo histórico, azul; en otro caso neutro.
    /// </summary>
    public PriceHighlight PriceHighlight
    {
        get
        {
            PriceTrend trend = Trend;
            if (trend == PriceTrend.Down)
                return PriceHighlight.Down;
            if (trend == PriceTrend.Up)
                return PriceHighlight.Up;

            return IsHistoricalLow ? PriceHighlight.Low : PriceHighlight.None;
        }
    }
    #endregion

    #region Constructor
    public Product()
    {
        Stores.CollectionChanged += OnStoresChanged;
    }
    #endregion

    /// <summary>Raised after a new price point is appended to <see cref="PriceHistory"/> (drives the price chart).</summary>
    public event EventHandler? PriceRecorded;

    #region Methods (public)
    /// <summary>
    /// Records a fresh price reading for one of the product's stores: updates the store's current
    /// price and timestamp, and appends a point to <see cref="PriceHistory"/>. The derived
    /// best-price / last-checked notifications and <see cref="PriceRecorded"/> are raised automatically.
    /// </summary>
    public void RecordPrice(ProductStore store, decimal price, DateTime timestamp)
    {
        if (!Stores.Contains(store))
            return;

        store.CurrentPrice = price;
        store.LastChecked = timestamp;
        PriceHistory.Add(new PricePoint(timestamp, price, store.Label));
        RaiseDerivedChanged();
        PriceRecorded?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    #region Methods (private)
    private void OnStoresChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ProductStore store in e.OldItems)
                store.PropertyChanged -= OnStorePropertyChanged;

        if (e.NewItems is not null)
            foreach (ProductStore store in e.NewItems)
                store.PropertyChanged += OnStorePropertyChanged;

        RaiseDerivedChanged();
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductStore.CurrentPrice) or nameof(ProductStore.LastChecked) or nameof(ProductStore.HasPromo) or nameof(ProductStore.IsAvailable) or nameof(ProductStore.IsPreorder) or nameof(ProductStore.ShippingCost))
            RaiseDerivedChanged();
    }

    /// <summary>
    /// Precio efectivo de un punto del histórico: su precio + el envío ACTUAL de su tienda si el ajuste global de
    /// incluir envío está activo (aproximación: no se registró el envío histórico de cada momento).
    /// </summary>
    private decimal EffectiveHistoricalPrice(PricePoint point)
    {
        if (!App.GetService<SharedDataService>().IncludeShippingInPrice)
            return point.Price;

        decimal shipping = Stores.FirstOrDefault(store => store.Label == point.StoreLabel)?.ShippingCost ?? 0;
        return point.Price + shipping;
    }

    /// <summary>Recalcula el mejor precio y todos los derivados (p. ej. al cambiar el toggle de incluir envío en el precio).</summary>
    public void NotifyPricingChanged() => RaiseDerivedChanged();

    private void RaiseDerivedChanged()
    {
        OnPropertyChanged(nameof(BestPrice));
        OnPropertyChanged(nameof(BestStore));
        OnPropertyChanged(nameof(LastChecked));
        OnPropertyChanged(nameof(BestPriceText));
        OnPropertyChanged(nameof(Trend));
        OnPropertyChanged(nameof(IsHistoricalLow));
        OnPropertyChanged(nameof(PriceHighlight));
        OnPropertyChanged(nameof(HasPromo));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IsPreorder));
        OnPropertyChanged(nameof(HasShipping));
        OnPropertyChanged(nameof(IsBelowAlert));
        OnPropertyChanged(nameof(AlertPriceText));
    }

    /// <summary>Al cambiar el precio de alerta, refresca las propiedades derivadas del alerta.</summary>
    partial void OnAlertPriceChanged(decimal? value)
    {
        OnPropertyChanged(nameof(HasAlert));
        OnPropertyChanged(nameof(IsBelowAlert));
        OnPropertyChanged(nameof(AlertPriceText));
    }
    #endregion
}

/// <summary>
/// A single store URL tracked for a <see cref="Product"/> (one marketplace/country). Holds the last
/// observed price and when it was read. Observable so the owning product's derived best-price updates
/// live when a new reading comes in.
/// </summary>
public partial class ProductStore : ObservableObject
{
    /// <summary>Primary key in the local database. 0 until the store has been persisted.</summary>
    public long Id { get; set; }

    /// <summary>Absolute URL of the product on this store (typically an Amazon marketplace URL).</summary>
    [ObservableProperty]
    private string _url = string.Empty;

    /// <summary>Human label for the store, e.g. "Amazon.es", "Amazon.de".</summary>
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>
    /// Optional CSS selector, picked by the user, of the element that holds the price on this (usually non-Amazon)
    /// page. When set, the parser reads the price from it instead of the built-in extraction. Null = built-in.
    /// </summary>
    public string? PriceSelector { get; set; }

    /// <summary>ISO currency code / symbol of <see cref="CurrentPrice"/>, if known.</summary>
    [ObservableProperty]
    private string? _currency;

    /// <summary>Last observed price on this store, or <c>null</c> if never read.</summary>
    [ObservableProperty]
    private decimal? _currentPrice;

    /// <summary>Whether the product is Amazon Prime on this store (as of the last read). Amazon-only.</summary>
    [ObservableProperty]
    private bool _isPrime;

    /// <summary>Whether the product was available for purchase on this store as of the last read. Defaults to true.</summary>
    [ObservableProperty]
    private bool _isAvailable = true;

    /// <summary>Whether this store showed a promotion / deal / coupon / voucher as of the last read.</summary>
    [ObservableProperty]
    private bool _hasPromo;

    /// <summary>Whether the product is a pre-order on this store (not yet released) as of the last read.</summary>
    [ObservableProperty]
    private bool _isPreorder;

    /// <summary>Coste de envío detectado en esta tienda (Amazon), o <c>null</c> si es gratis/desconocido. Best-effort.</summary>
    [ObservableProperty]
    private decimal? _shippingCost;

    /// <summary>When <see cref="CurrentPrice"/> was last read, or <c>null</c> if never read.</summary>
    [ObservableProperty]
    private DateTime? _lastChecked;

    /// <summary>
    /// Precio efectivo de la tienda: <see cref="CurrentPrice"/> + <see cref="ShippingCost"/> si el ajuste global de
    /// incluir envío está activo; si no, el precio a secas. <c>null</c> si no hay precio.
    /// </summary>
    public decimal? EffectivePrice
    {
        get
        {
            if (CurrentPrice is not decimal price)
                return null;

            return App.GetService<SharedDataService>().IncludeShippingInPrice && ShippingCost is decimal shipping
                ? price + shipping
                : price;
        }
    }

    public ProductStore()
    {
    }

    public ProductStore(string url, string label)
    {
        Url = url;
        Label = label;
    }
}

/// <summary>
/// A single timestamped price observation for a product, tagged with the store it came from. The
/// ordered list of these on <see cref="Product.PriceHistory"/> drives the price-evolution chart.
/// </summary>
public record PricePoint(DateTime Timestamp, decimal Price, string StoreLabel);
