using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.Options;
using Tracker.Enums;
using Tracker.Models;
using Tracker.Services;
using static Tracker.Services.SharedDataService;

namespace Tracker.Controls.ViewModels;

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

    /// <summary>Productos realmente representados en la gráfica (los de la lista filtrada CON precio &gt; 0), en su orden. Mapea el clic/resaltado.</summary>
    private List<Product> _shownProducts = new();

    /// <summary>Lista de productos FILTRADA de la lista principal: la gráfica muestra solo lo que se ve en la lista (respeta sus filtros/orden).</summary>
    private readonly ProductListViewModel _productList;

    private ChartType _selectedChartType = ChartType.Column;
    private SortMode _sortOrder = SortMode.None;
    private int _topN;
    #endregion

    /// <summary>Fuente de productos de la gráfica: la colección FILTRADA de la lista de productos (no todos los productos).</summary>
    private ObservableCollection<Product> Source => _productList.FilteredProducts;

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
    public ProductsOverviewViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings, ProductListViewModel productListViewModel)
        : base(sharedDataService, appSettings)
    {
        _productList = productListViewModel;

        // Config de gráfica persistida (el .ini ya está restaurado en este punto).
        _selectedChartType = _appSettings.ProductsOverviewControl.ChartType;
        _sortOrder = _appSettings.ProductsOverviewControl.SortOrder;
        _topN = _appSettings.ProductsOverviewControl.TopN;

        // La gráfica sigue a la lista FILTRADA: al cambiar los filtros/orden (añadir, quitar, reordenar) se recalcula.
        Source.CollectionChanged += OnProductsChanged;
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

    /// <summary>Cambió una propiedad de un producto mostrado: si afecta al valor de su barra (precio, comprado, precio de compra) o a su etiqueta, recalcula.</summary>
    private void OnProductChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Product.BestPrice) or nameof(Product.IsPurchased) or nameof(Product.PurchasePrice) or nameof(Product.Name))
            Recompute();
    }

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
    /// <summary>Producto en la posición <paramref name="index"/> de los MOSTRADOS en la gráfica (para el clic en columna), o null.</summary>
    public Product? ProductAt(int index)
        => index >= 0 && index < _shownProducts.Count ? _shownProducts[index] : null;
    #endregion

    #region Methods (private)
    /// <summary>Se (re)suscribe a los precios de los productos actualmente mostrados (lista filtrada).</summary>
    private void Subscribe()
    {
        foreach (Product product in _subscribed)
            product.PropertyChanged -= OnProductChanged;
        _subscribed.Clear();

        foreach (Product product in Source)
        {
            product.PropertyChanged += OnProductChanged;
            _subscribed.Add(product);
        }
    }

    /// <summary>Precio a representar en la barra: el de COMPRA si está comprado, o el mejor precio actual. Null si no hay.</summary>
    private static decimal? DisplayPrice(Product product) => product.IsPurchased ? product.PurchasePrice : product.BestPrice;

    private void Recompute()
    {
        // Solo los productos de la lista filtrada CON precio > 0 (se excluyen los sin precio o a 0).
        _shownProducts = Source.Where(product => DisplayPrice(product) is decimal price && price > 0).ToList();
        var products = _shownProducts;

        bool includeShipping = SharedDataService.IncludeShippingInPrice;
        // Para los productos COMPRADOS, la barra usa el precio de COMPRA (no el mejor precio actual).
        Values = products.Select(product => (double)(DisplayPrice(product) ?? 0m)).ToList();
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
        HighlightIndex = selected is null ? -1 : _shownProducts.IndexOf(selected);
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
            + (includeShipping && product.Stores.FirstOrDefault(store => store.Id == point.StoreId)?.ShippingCost is decimal cost ? cost : 0m);

        return product.PriceHistory.Min(EffectiveOf);
    }
    #endregion

    #region Methods (public)
    public override void Dispose()
    {
        Source.CollectionChanged -= OnProductsChanged;
        SharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
        SharedDataService.PropertyChanged -= OnSharedDataChanged;

        foreach (Product product in _subscribed)
            product.PropertyChanged -= OnProductChanged;
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
