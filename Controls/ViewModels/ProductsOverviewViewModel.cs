using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del widget de resumen de productos: una gráfica de columnas con un producto por columna (eje X) y su
/// mejor precio actual (eje Y). La columna del producto seleccionado se resalta (<see cref="HighlightIndex"/>), y al
/// pulsar una columna el control selecciona ese producto (<see cref="ProductAt"/>). Se recalcula al cambiar la lista
/// de productos, la selección o los precios.
/// </summary>
public partial class ProductsOverviewViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly List<Product> _subscribed = new();

    private ChartType _selectedChartType = ChartType.Column;
    private SortMode _sortOrder = SortMode.None;
    private int _topN;
    #endregion

    #region Properties
    /// <summary>Mejor precio actual de cada producto (eje Y), en el orden de la lista.</summary>
    public IReadOnlyList<double> Values { get; private set; } = Array.Empty<double>();

    /// <summary>
    /// Precio MÍNIMO histórico (efectivo) de cada producto, en el mismo orden que <see cref="Values"/>. Es la parte baja
    /// de cada barra: se pinta de 0 a este valor con un color y de aquí al precio actual con otro (ver <c>BaseValues</c>
    /// del <c>ChartTypeSelectorControl</c>).
    /// </summary>
    public IReadOnlyList<double> BaseValues { get; private set; } = Array.Empty<double>();

    /// <summary>
    /// Base efectiva que recibe la gráfica: el mínimo histórico (<see cref="BaseValues"/>) solo si el toggle global
    /// <see cref="SharedDataService.ShowMinPriceChart"/> está activo; si no, vacío (barra de un solo color, precio actual).
    /// </summary>
    public IReadOnlyList<double> EffectiveBaseValues
        => SharedDataService.ShowMinPriceChart ? BaseValues : Array.Empty<double>();

    /// <summary>Nombre de cada producto (eje X), en el orden de la lista.</summary>
    public IReadOnlyList<string> Labels { get; private set; } = Array.Empty<string>();

    /// <summary>Moneda a añadir a los valores (la primera disponible entre los productos), o vacío.</summary>
    public string ValueSuffix { get; private set; } = string.Empty;

    /// <summary>Índice del producto seleccionado (columna resaltada), o -1.</summary>
    public int HighlightIndex { get; private set; } = -1;

    /// <summary>Tipo de gráfica (enlazado TwoWay al <c>ChartTypeSelectorControl</c>); se persiste en el .ini.</summary>
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

    #region Constructor
    public ProductsOverviewViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        // Config de gráfica persistida (el .ini ya está restaurado en este punto).
        _selectedChartType = _appSettings.ProductsOverviewControl.ChartType;
        _sortOrder = _appSettings.ProductsOverviewControl.SortOrder;
        _topN = _appSettings.ProductsOverviewControl.TopN;

        SharedDataService.ProductSet.Products.CollectionChanged += OnProductsChanged;
        SharedDataService.SelectedProductChanged += OnSelectedProductChanged;
        SharedDataService.PropertyChanged += OnSharedDataChanged;

        Subscribe();
        Recompute();
    }
    #endregion

    #region Subscribed events
    private void OnProductsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Subscribe();
        Recompute();
    }

    private void OnSelectedProductChanged(object? sender, ProductChangedEventArgs e)
    {
        UpdateHighlight();
        OnPropertyChanged(nameof(HighlightIndex));
    }

    private void OnProductPriceRecorded(object? sender, EventArgs e) => Recompute();

    /// <summary>
    /// Reacciona a ajustes globales: al incluir/excluir envío recalcula los valores (precio efectivo); al alternar el
    /// precio mínimo re-notifica solo la base efectiva (los datos no cambian) para mostrar/ocultar el tramo del mínimo.
    /// </summary>
    private void OnSharedDataChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.IncludeShippingInPrice))
            Recompute();
        else if (e.PropertyName == nameof(SharedDataService.ShowMinPriceChart))
            OnPropertyChanged(nameof(EffectiveBaseValues));
    }
    #endregion

    #region Methods (public)
    /// <summary>Producto en la posición <paramref name="index"/> de la lista (para el clic en columna), o null.</summary>
    public Product? ProductAt(int index)
    {
        var products = SharedDataService.ProductSet.Products;
        return index >= 0 && index < products.Count ? products[index] : null;
    }
    #endregion

    #region Methods (private)
    /// <summary>Se (re)suscribe a los precios de los productos actuales.</summary>
    private void Subscribe()
    {
        foreach (Product product in _subscribed)
            product.PriceRecorded -= OnProductPriceRecorded;
        _subscribed.Clear();

        foreach (Product product in SharedDataService.ProductSet.Products)
        {
            product.PriceRecorded += OnProductPriceRecorded;
            _subscribed.Add(product);
        }
    }

    private void Recompute()
    {
        var products = SharedDataService.ProductSet.Products;

        bool includeShipping = SharedDataService.IncludeShippingInPrice;
        Values = products.Select(product => (double)(product.BestPrice ?? 0m)).ToList();
        BaseValues = products.Select(product => (double)HistoricalMin(product, includeShipping)).ToList();
        Labels = products.Select(product => product.Name).ToList();
        ValueSuffix = products
            .Select(product => product.BestStore?.Currency)
            .FirstOrDefault(currency => !string.IsNullOrEmpty(currency)) ?? string.Empty;

        UpdateHighlight();

        OnPropertyChanged(nameof(Values));
        OnPropertyChanged(nameof(BaseValues));
        OnPropertyChanged(nameof(EffectiveBaseValues));
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(ValueSuffix));
        OnPropertyChanged(nameof(HighlightIndex));
    }

    private void UpdateHighlight()
    {
        Product? selected = SharedDataService.SelectedProduct;
        HighlightIndex = selected is null ? -1 : SharedDataService.ProductSet.Products.IndexOf(selected);
    }

    /// <summary>
    /// Precio mínimo histórico EFECTIVO de un producto (con envío de la tienda si el ajuste global lo incluye), sobre
    /// todo su histórico de precios. Si no hay histórico, cae al mejor precio actual (o 0).
    /// </summary>
    private static decimal HistoricalMin(Product product, bool includeShipping)
    {
        if (product.PriceHistory.Count == 0)
            return product.BestPrice ?? 0m;

        decimal EffectiveOf(PricePoint point) => point.Price
            + (includeShipping && product.Stores.FirstOrDefault(store => store.Label == point.StoreLabel)?.ShippingCost is decimal cost ? cost : 0m);

        return product.PriceHistory.Min(EffectiveOf);
    }
    #endregion

    #region Methods (public)
    public override void Dispose()
    {
        SharedDataService.ProductSet.Products.CollectionChanged -= OnProductsChanged;
        SharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
        SharedDataService.PropertyChanged -= OnSharedDataChanged;

        foreach (Product product in _subscribed)
            product.PriceRecorded -= OnProductPriceRecorded;
        _subscribed.Clear();
    }

    public override void LoadConfig()
    {
        SelectedChartType = _appSettings.ProductsOverviewControl.ChartType;
        SortOrder = _appSettings.ProductsOverviewControl.SortOrder;
        TopN = _appSettings.ProductsOverviewControl.TopN;
    }

    public override void SaveConfig()
    {
        _appSettings.ProductsOverviewControl.ChartType = SelectedChartType;
        _appSettings.ProductsOverviewControl.SortOrder = SortOrder;
        _appSettings.ProductsOverviewControl.TopN = TopN;
    }
    #endregion
}
