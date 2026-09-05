using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Tracker.Helpers;
using Tracker.Models;
using Tracker.Services;

namespace Tracker.Controls.ViewModels;

/// <summary>Criterio por el que se ordena la lista de productos cuando el orden está activo.</summary>
public enum ProductSortField
{
    /// <summary>Por el mejor precio actual.</summary>
    Price,

    /// <summary>Por antigüedad: la fecha de alta en la base de datos (ver <see cref="Tracker.Models.Product.SortDate"/>).</summary>
    Created
}

/// <summary>
/// ViewModel asociado a <see cref="Views.ProductListControl"/>.
///
/// Expone (vía <see cref="SharedDataService"/>) la colección de productos rastreados y el producto
/// seleccionado. Sobre esa colección aplica un filtro por texto (nombre) y por variables (favoritos, con avisos,
/// con cambio de precio, en mínimo histórico, con alerta), publicando el resultado en <see cref="FilteredProducts"/>,
/// que es lo que muestra el ListView. El estilo (texto + <c>ToggleSplitButton</c> con toggles) recupera el de la
/// antigua lista de juegos.
/// </summary>
public partial class ProductListViewModel : WidgetViewModelBase
{
    #region Attributes
    private string _filterBy = string.Empty;
    private bool _filtersEnabled;
    private bool _sortByPrice;
    private bool _sortDescending;
    private RelayCommand? _filtersChangedCommand;
    private bool _applyingFilters;
    private bool _filtersDirty;
    private double _priceFloor;
    private double _priceCeiling = 1;
    private double _minPrice;
    private double _maxPrice = 1;
    private ProductSortField _sortField = ProductSortField.Price;
    private IReadOnlyList<int> _priceHistogram = System.Array.Empty<int>();

    /// <summary>Nº de tramos (barras) del histograma de precios que se dibuja sobre el slider de rango.</summary>
    private const int PriceHistogramBuckets = 48;

    /// <summary>Propiedades de un producto que, al cambiar, pueden alterar si pasa los filtros o el orden por precio.</summary>
    private static readonly HashSet<string> FilterAffectingProperties = new()
    {
        nameof(Product.Name), nameof(Product.IsFavorite), nameof(Product.HasIssues),
        nameof(Product.Trend), nameof(Product.IsHistoricalLow), nameof(Product.HasAlert), nameof(Product.BestPrice),
        nameof(Product.IsPurchased), nameof(Product.PurchasePrice),
    };

    /// <summary>Propiedades de un producto que, al cambiar, pueden mover los extremos del rango de precio.</summary>
    private static readonly HashSet<string> PriceBoundsProperties = new()
    {
        nameof(Product.BestPrice), nameof(Product.PurchasePrice), nameof(Product.IsPurchased),
    };
    #endregion

    #region Properties
    /// <summary>Productos que pasan los filtros actuales, en el mismo orden (alfabético) que la fuente. Lo muestra el ListView.</summary>
    public ObservableCollection<Product> FilteredProducts { get; } = new();

    /// <summary>Estado de los filtros por variable (favoritos, avisos, cambio de precio, mínimo histórico, alerta).</summary>
    public ProductFilters ActiveFilters { get; } = new();

    /// <summary>Texto del filtro por nombre (subcadena, sin distinguir mayúsculas). Al cambiar, refiltra en el acto.</summary>
    public string FilterBy
    {
        get => _filterBy;
        set { if (SetProperty(ref _filterBy, value)) ApplyFilters(); }
    }

    /// <summary>Interruptor maestro de los filtros por variable (la cara del <c>ToggleSplitButton</c>). Al cambiar, refiltra.</summary>
    public bool FiltersEnabled
    {
        get => _filtersEnabled;
        set { if (SetProperty(ref _filtersEnabled, value)) ApplyFilters(); }
    }

    /// <summary>Comando que dispara cada toggle de variable: sincroniza el interruptor maestro y refiltra.</summary>
    public RelayCommand FiltersChangedCommand => _filtersChangedCommand ??= new RelayCommand(OnFiltersChanged);

    /// <summary>
    /// Interruptor maestro del orden (la cara del <c>ToggleSplitButton</c>, igual que el filtro): si está activo la
    /// lista se ordena por el mejor precio (según <see cref="SortDescending"/>); si no, se mantiene el orden alfabético
    /// de la fuente. Al cambiar, reordena en el acto.
    /// </summary>
    public bool SortByPrice
    {
        get => _sortByPrice;
        set { if (SetProperty(ref _sortByPrice, value)) ApplyFilters(); }
    }

    /// <summary>Dirección del orden: false = ascendente (por defecto), true = descendente. Al cambiar, reordena.</summary>
    public bool SortDescending
    {
        get => _sortDescending;
        set { if (SetProperty(ref _sortDescending, value)) ApplyFilters(); }
    }

    /// <summary>Criterio del orden mientras el interruptor maestro está activo: por precio o por antigüedad.</summary>
    public ProductSortField SortField
    {
        get => _sortField;
        set { if (SetProperty(ref _sortField, value)) ApplyFilters(); }
    }

    /// <summary>
    /// Suelo del rango de precio: el precio más BAJO entre TODOS los productos (el que muestra cada uno en la lista),
    /// redondeado al euro inferior. Es el mínimo del slider: arrancar siempre en 0 desperdiciaba todo el tramo por
    /// debajo del producto más barato. Vale 0 mientras no hay ningún precio leído.
    /// </summary>
    public double PriceFloor
    {
        get => _priceFloor;
        private set
        {
            if (SetProperty(ref _priceFloor, value))
                OnPropertyChanged(nameof(PriceFloorText));
        }
    }

    /// <summary>
    /// Techo del rango de precio: el precio más alto entre TODOS los productos (el que muestra cada uno en la lista),
    /// redondeado al euro superior. Es el máximo del slider; el mínimo es <see cref="PriceFloor"/>. Siempre queda al
    /// menos un euro por encima del suelo, para que el control conserve recorrido aunque aún no haya precios leídos.
    /// </summary>
    public double PriceCeiling
    {
        get => _priceCeiling;
        private set
        {
            if (SetProperty(ref _priceCeiling, value))
                OnPropertyChanged(nameof(PriceCeilingText));
        }
    }

    /// <summary>
    /// Distribución de precios para el histograma que se dibuja sobre el slider: cuántos productos con precio caen en
    /// cada uno de los <see cref="PriceHistogramBuckets"/> tramos iguales en que se reparte
    /// <see cref="PriceFloor"/>..<see cref="PriceCeiling"/>. Se reemplaza entera al recalcularse (el binding es OneWay
    /// sobre la lista, no sobre sus elementos).
    /// </summary>
    public IReadOnlyList<int> PriceHistogram
    {
        get => _priceHistogram;
        private set => SetProperty(ref _priceHistogram, value);
    }

    /// <summary>Extremo INFERIOR del rango de precio elegido con el slider (igual al suelo = sin límite por abajo).</summary>
    public double MinPrice
    {
        get => _minPrice;
        set
        {
            if (SetProperty(ref _minPrice, value))
            {
                OnPropertyChanged(nameof(MinPriceText));
                OnPropertyChanged(nameof(IsPriceRangeNarrowed));
                ApplyFilters();
            }
        }
    }

    /// <summary>Extremo SUPERIOR del rango de precio elegido con el slider (igual al techo = sin límite por arriba).</summary>
    public double MaxPrice
    {
        get => _maxPrice;
        set
        {
            if (SetProperty(ref _maxPrice, value))
            {
                OnPropertyChanged(nameof(MaxPriceText));
                OnPropertyChanged(nameof(IsPriceRangeNarrowed));
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// El rango de precio está ACOTADO: alguno de los dos pulgares se ha movido de su extremo. Mientras es falso, el
    /// slider no descarta ningún producto (ni siquiera los que aún no tienen precio).
    /// </summary>
    public bool IsPriceRangeNarrowed => MinPrice > PriceFloor || MaxPrice < PriceCeiling;

    /// <summary>Extremo inferior del rango, formateado para la etiqueta del slider.</summary>
    public string MinPriceText => FormatPrice(MinPrice);

    /// <summary>Extremo superior del rango, formateado para la etiqueta del slider.</summary>
    public string MaxPriceText => FormatPrice(MaxPrice);

    /// <summary>Suelo del rango, formateado.</summary>
    public string PriceFloorText => FormatPrice(PriceFloor);

    /// <summary>Techo del rango, formateado.</summary>
    public string PriceCeilingText => FormatPrice(PriceCeiling);

    /// <summary>
    /// Texto "N / Total" del pie de la lista (productos mostrados frente al total). El total EXCLUYE los comprados si
    /// el toggle de mostrar comprados está desactivado (no se cuentan los que no se muestran).
    /// </summary>
    public string CountText => string.Format(
        LocalizationService.Instance?[LocKeys.ProductList_Count_Format] ?? "{0} / {1}",
        FilteredProducts.Count,
        SharedDataService.ShowPurchased
            ? SharedDataService.ProductSet.Products.Count
            : SharedDataService.ProductSet.Products.Count(product => !product.IsPurchased));
    #endregion

    #region Constructor
    public ProductListViewModel(
        SharedDataService sharedDataService,
        IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        // El VM es singleton: se suscribe a la colección fuente y a los productos existentes para refiltrar en vivo.
        ObservableCollection<Product> products = SharedDataService.ProductSet.Products;
        products.CollectionChanged += OnProductsCollectionChanged;
        foreach (Product product in products)
            product.PropertyChanged += OnProductPropertyChanged;

        // Mostrar/ocultar comprados es un ajuste global del footer: al cambiar, se refiltra (y se limpia el filtro
        // "Comprados" cuando se dejan de mostrar).
        SharedDataService.PropertyChanged += OnSharedDataChanged;

        RefreshPriceBounds();
        ApplyFilters();
    }
    #endregion

    #region Filtering
    /// <summary>Un toggle de variable cambió: reajusta el interruptor maestro según haya o no filtros activos y refiltra.</summary>
    private void OnFiltersChanged()
    {
        // Mantiene el interruptor maestro coherente con los sub-toggles (cualquiera activo => filtros activados).
        bool anyActive = ActiveFilters.HasAny;
        if (anyActive && !FiltersEnabled)
            FiltersEnabled = true;       // el setter ya refiltra
        else if (!anyActive && FiltersEnabled)
            FiltersEnabled = false;      // el setter ya refiltra
        else
            ApplyFilters();
    }

    /// <summary>
    /// Recalcula <see cref="FilteredProducts"/> a partir de la fuente: primero los filtros por variable (si el
    /// interruptor maestro está activo) y luego el filtro por texto; reconcilia la colección en su sitio (sin
    /// Clear) para no perturbar la selección del ListView.
    /// </summary>
    public void ApplyFilters()
    {
        // Reconciliar la colección notifica al ListView de forma SÍNCRONA, y esa notificación puede acabar pidiendo
        // otro refiltrado (el cambio de selección mueve el gráfico, que toca propiedades del producto). Reconciliar
        // de forma anidada deja al ListView con índices obsoletos, así que la llamada reentrante solo marca la lista
        // como sucia y la de fuera repite hasta converger.
        if (_applyingFilters)
        {
            _filtersDirty = true;
            return;
        }

        _applyingFilters = true;
        try
        {
            do
            {
                _filtersDirty = false;
                ApplyFiltersCore();
            }
            while (_filtersDirty);
        }
        finally
        {
            _applyingFilters = false;
        }
    }

    /// <summary>Cuerpo real del refiltrado. Siempre a través de <see cref="ApplyFilters"/> (protege de la reentrada).</summary>
    private void ApplyFiltersCore()
    {
        IEnumerable<Product> source = SharedDataService.ProductSet.Products;

        // Los comprados solo se muestran si el toggle del footer está activo.
        if (!SharedDataService.ShowPurchased)
            source = source.Where(product => !product.IsPurchased);

        if (FiltersEnabled)
            source = ActiveFilters.Apply(source);

        if (!string.IsNullOrWhiteSpace(FilterBy))
            source = source.Where(product => product.Name.Contains(FilterBy, StringComparison.CurrentCultureIgnoreCase));

        // Rango de precio (el slider bajo el filtro de texto): mientras abarca de 0 al techo no descarta nada. En
        // cuanto se acota, los productos SIN precio quedan fuera: no se puede saber si caen dentro del rango.
        if (IsPriceRangeNarrowed)
        {
            decimal min = (decimal)MinPrice;
            decimal max = (decimal)MaxPrice;
            source = source.Where(product => product.ListPrice is decimal price && price >= min && price <= max);
        }

        // Orden: si el maestro está activo, por el criterio elegido (los productos sin dato quedan al final en ambas
        // direcciones); si no, se conserva el alfabético de la fuente.
        if (SortByPrice)
        {
            source = SortField switch
            {
                ProductSortField.Created => SortDescending
                    ? source.OrderByDescending(product => product.SortDate ?? DateTime.MinValue)
                    : source.OrderBy(product => product.SortDate ?? DateTime.MaxValue),
                _ => SortDescending
                    ? source.OrderByDescending(product => product.BestPrice ?? decimal.MinValue)
                    : source.OrderBy(product => product.BestPrice ?? decimal.MaxValue),
            };
        }

        List<Product> desired = source.ToList();

        // Si el producto seleccionado desaparece de la lista por haberse marcado como comprado (y el toggle oculta los
        // comprados), la selección quedaría huérfana: se adelanta al siguiente elemento visible (o el último si era el
        // final; null si la lista queda vacía) antes de reconciliar.
        Product? selected = SharedDataService.SelectedProduct;
        int oldIndex = selected is null ? -1 : FilteredProducts.IndexOf(selected);
        bool selectionDropped = selected is not null && oldIndex >= 0 && !desired.Contains(selected)
            && selected.IsPurchased && !SharedDataService.ShowPurchased;

        Reconcile(desired);

        if (selectionDropped)
            SharedDataService.SelectedProduct = desired.Count == 0
                ? null
                : desired[Math.Min(oldIndex, desired.Count - 1)];

        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>
    /// Recalcula los dos extremos del rango de precio a partir de TODOS los productos (no de los filtrados): el suelo
    /// es el precio más bajo y el techo, el más alto. Cada pulgar sigue pegado a su extremo mientras el usuario no lo
    /// haya movido, de modo que leer precios nuevos amplía el recorrido sin dejar productos fuera; si el recorrido se
    /// estrecha, los extremos elegidos se recortan a él.
    /// </summary>
    private void RefreshPriceBounds()
    {
        double lowest = double.MaxValue;
        double highest = 0;
        foreach (Product product in SharedDataService.ProductSet.Products)
            if (product.ListPrice is decimal listPrice)
            {
                double price = (double)listPrice;
                if (price < lowest)
                    lowest = price;
                if (price > highest)
                    highest = price;
            }

        double floor = lowest == double.MaxValue ? 0 : Math.Floor(lowest);
        double ceiling = Math.Max(floor + 1, Math.Ceiling(highest));
        if (Math.Abs(floor - PriceFloor) < 0.0001 && Math.Abs(ceiling - PriceCeiling) < 0.0001)
        {
            RefreshPriceHistogram();   // el recorrido no se mueve, pero los conteos por tramo sí pueden haber cambiado
            return;
        }

        bool minWasAtFloor = MinPrice <= PriceFloor;
        bool maxWasAtCeiling = MaxPrice >= PriceCeiling;

        // Al ensanchar el recorrido se sube el techo antes de subir el suelo (y al revés al estrecharlo): así los dos
        // límites del RangeSelector nunca se cruzan en un estado intermedio.
        if (ceiling > PriceCeiling)
        {
            PriceCeiling = ceiling;
            PriceFloor = floor;
        }
        else
        {
            PriceFloor = floor;
            PriceCeiling = ceiling;
        }

        if (maxWasAtCeiling || MaxPrice > ceiling)
            MaxPrice = ceiling;        // el setter ya refiltra
        if (minWasAtFloor || MinPrice < floor)
            MinPrice = floor;
        if (MinPrice > ceiling)
            MinPrice = ceiling;

        OnPropertyChanged(nameof(IsPriceRangeNarrowed));
        RefreshPriceHistogram();
    }

    /// <summary>
    /// Recalcula la distribución de precios del histograma: reparte <see cref="PriceFloor"/>..<see cref="PriceCeiling"/>
    /// en <see cref="PriceHistogramBuckets"/> tramos iguales y cuenta en cuál cae cada producto CON precio (los que aún
    /// no tienen no suman en ninguno). Se llama junto a <see cref="RefreshPriceBounds"/>, que puede salir antes de
    /// tiempo si los extremos no se mueven: los conteos sí cambian aunque el recorrido siga igual.
    /// </summary>
    private void RefreshPriceHistogram()
    {
        double span = PriceCeiling - PriceFloor;
        if (span <= 0)
        {
            PriceHistogram = System.Array.Empty<int>();
            return;
        }

        int[] buckets = new int[PriceHistogramBuckets];
        foreach (Product product in SharedDataService.ProductSet.Products)
        {
            if (product.ListPrice is not decimal listPrice)
                continue;

            // El techo cae en el último tramo (si no, se saldría del array por un tramo).
            int index = (int)(((double)listPrice - PriceFloor) / span * PriceHistogramBuckets);
            index = Math.Clamp(index, 0, PriceHistogramBuckets - 1);
            buckets[index]++;
        }

        PriceHistogram = buckets;
    }

    /// <summary>Formatea un extremo del rango para su etiqueta: importe entero con la moneda de referencia ("120 €").</summary>
    private string FormatPrice(double value)
    {
        string text = value.ToString("0", System.Globalization.CultureInfo.CurrentCulture);
        string currency = SharedDataService.ProductSet.Products
            .Select(product => product.BestStore?.DisplayCurrency)
            .FirstOrDefault(symbol => !string.IsNullOrEmpty(symbol)) ?? string.Empty;

        return string.IsNullOrEmpty(currency) ? text : $"{text} {currency}";
    }

    /// <summary>
    /// Ajusta <see cref="FilteredProducts"/> a la lista deseada mutándola en su sitio: quita los que sobran (de atrás
    /// hacia delante) e inserta/mueve para casar el orden. Evita un Clear+Add que anularía la selección del ListView.
    /// </summary>
    private void Reconcile(List<Product> desired)
    {
        for (int i = FilteredProducts.Count - 1; i >= 0; i--)
            if (!desired.Contains(FilteredProducts[i]))
                FilteredProducts.RemoveAt(i);

        for (int target = 0; target < desired.Count; target++)
        {
            Product product = desired[target];
            int current = FilteredProducts.IndexOf(product);
            if (current < 0)
                FilteredProducts.Insert(target, product);
            else if (current != target)
                FilteredProducts.Move(current, target);
        }
    }
    #endregion

    #region Source subscriptions
    /// <summary>La colección fuente cambió: mantiene las suscripciones por producto al día y refiltra.</summary>
    private void OnProductsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (Product product in e.OldItems)
                product.PropertyChanged -= OnProductPropertyChanged;

        if (e.NewItems is not null)
            foreach (Product product in e.NewItems)
                product.PropertyChanged += OnProductPropertyChanged;

        RefreshPriceBounds();
        ApplyFilters();
    }

    /// <summary>Cambió una propiedad de un producto: si afecta a los filtros (nombre, favorito, avisos, tendencia, alerta, comprado), refiltra.</summary>
    private void OnProductPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || PriceBoundsProperties.Contains(e.PropertyName))
            RefreshPriceBounds();

        if (e.PropertyName is null || FilterAffectingProperties.Contains(e.PropertyName))
            ApplyFilters();
    }

    /// <summary>Cambió el ajuste global de mostrar comprados: si se ocultan, quita el filtro "Comprados"; siempre refiltra.</summary>
    private void OnSharedDataChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SharedDataService.ShowPurchased))
            return;

        if (!SharedDataService.ShowPurchased && ActiveFilters.WithPurchased)
        {
            ActiveFilters.WithPurchased = false;
            OnFiltersChanged();   // reajusta el interruptor maestro y refiltra
        }
        else
        {
            ApplyFilters();
        }
    }
    #endregion

    #region Methods public
    /// <summary>Libera los recursos asociados al ViewModel (desuscribe de la colección y los productos).</summary>
    public override void Dispose()
    {
        ObservableCollection<Product> products = SharedDataService.ProductSet.Products;
        products.CollectionChanged -= OnProductsCollectionChanged;
        foreach (Product product in products)
            product.PropertyChanged -= OnProductPropertyChanged;
        SharedDataService.PropertyChanged -= OnSharedDataChanged;
    }

    /// <summary>Carga desde la configuración el estado visual guardado del control.</summary>
    public override void LoadConfig()
    {
    }

    /// <summary>Guarda en la configuración el Id del producto seleccionado, para re-seleccionarlo al arrancar.</summary>
    public override void SaveConfig()
    {
        _appSettings.ProductListControl.SelectedProductId = SharedDataService.SelectedProduct?.Id ?? 0;
    }
    #endregion
}
