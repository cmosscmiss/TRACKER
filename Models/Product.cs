using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

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

    /// <summary>Lowest current price across the stores that have one, or <c>null</c> if none is known yet.</summary>
    public decimal? BestPrice
    {
        get
        {
            decimal? best = null;
            foreach (ProductStore store in Stores)
                if (store.CurrentPrice is decimal price && (best is null || price < best))
                    best = price;
            return best;
        }
    }

    /// <summary>The store currently offering <see cref="BestPrice"/>, or <c>null</c> if no price is known.</summary>
    public ProductStore? BestStore
    {
        get
        {
            ProductStore? best = null;
            foreach (ProductStore store in Stores)
                if (store.CurrentPrice is decimal price && (best?.CurrentPrice is not decimal b || price < b))
                    best = store;
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
    /// Tendencia del mejor precio respecto a la lectura anterior: compara el mínimo entre tiendas de la última ronda
    /// con el de la penúltima. <see cref="PriceTrend.Unknown"/> si hay menos de dos rondas.
    /// </summary>
    public PriceTrend Trend
    {
        get
        {
            if (PriceHistory.Count == 0)
                return PriceTrend.Unknown;

            var rounds = PriceHistory.GroupBy(point => point.Timestamp).OrderBy(group => group.Key).ToList();
            if (rounds.Count < 2)
                return PriceTrend.Unknown;

            decimal latest = rounds[^1].Min(point => point.Price);
            decimal previous = rounds[^2].Min(point => point.Price);
            return latest < previous ? PriceTrend.Down : latest > previous ? PriceTrend.Up : PriceTrend.Same;
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
        if (e.PropertyName is nameof(ProductStore.CurrentPrice) or nameof(ProductStore.LastChecked))
            RaiseDerivedChanged();
    }

    private void RaiseDerivedChanged()
    {
        OnPropertyChanged(nameof(BestPrice));
        OnPropertyChanged(nameof(BestStore));
        OnPropertyChanged(nameof(LastChecked));
        OnPropertyChanged(nameof(BestPriceText));
        OnPropertyChanged(nameof(Trend));
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

    /// <summary>ISO currency code / symbol of <see cref="CurrentPrice"/>, if known.</summary>
    [ObservableProperty]
    private string? _currency;

    /// <summary>Last observed price on this store, or <c>null</c> if never read.</summary>
    [ObservableProperty]
    private decimal? _currentPrice;

    /// <summary>Whether the product is Amazon Prime on this store (as of the last read). Amazon-only.</summary>
    [ObservableProperty]
    private bool _isPrime;

    /// <summary>When <see cref="CurrentPrice"/> was last read, or <c>null</c> if never read.</summary>
    [ObservableProperty]
    private DateTime? _lastChecked;

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
