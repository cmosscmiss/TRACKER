using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MM4LB.Helpers;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel asociado a <see cref="Views.ProductListControl"/>.
///
/// Expone (vía <see cref="SharedDataService"/>) la colección de productos rastreados y el producto
/// seleccionado. Sobre esa colección aplica un filtro por texto (nombre) y por variables (favoritos, con avisos,
/// con cambio de precio, con alerta), publicando el resultado en <see cref="FilteredProducts"/>, que es lo que
/// muestra el ListView. El estilo (texto + <c>ToggleSplitButton</c> con toggles) recupera el de la antigua lista
/// de juegos.
/// </summary>
public partial class ProductListViewModel : WidgetViewModelBase
{
    #region Attributes
    private string _filterBy = string.Empty;
    private bool _filtersEnabled;
    private bool _sortByPrice;
    private bool _sortDescending;
    private RelayCommand? _filtersChangedCommand;

    /// <summary>Propiedades de un producto que, al cambiar, pueden alterar si pasa los filtros o el orden por precio.</summary>
    private static readonly HashSet<string> FilterAffectingProperties = new()
    {
        nameof(Product.Name), nameof(Product.IsFavorite), nameof(Product.HasIssues),
        nameof(Product.Trend), nameof(Product.HasAlert), nameof(Product.BestPrice), nameof(Product.IsPurchased),
    };
    #endregion

    #region Properties
    /// <summary>Productos que pasan los filtros actuales, en el mismo orden (alfabético) que la fuente. Lo muestra el ListView.</summary>
    public ObservableCollection<Product> FilteredProducts { get; } = new();

    /// <summary>Estado de los filtros por variable (favoritos, avisos, cambio de precio, alerta).</summary>
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

    /// <summary>Dirección del orden por precio: false = ascendente (por defecto), true = descendente. Al cambiar, reordena.</summary>
    public bool SortDescending
    {
        get => _sortDescending;
        set { if (SetProperty(ref _sortDescending, value)) ApplyFilters(); }
    }

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
        IEnumerable<Product> source = SharedDataService.ProductSet.Products;

        // Los comprados solo se muestran si el toggle del footer está activo.
        if (!SharedDataService.ShowPurchased)
            source = source.Where(product => !product.IsPurchased);

        if (FiltersEnabled)
            source = ActiveFilters.Apply(source);

        if (!string.IsNullOrWhiteSpace(FilterBy))
            source = source.Where(product => product.Name.Contains(FilterBy, StringComparison.CurrentCultureIgnoreCase));

        // Orden: si el maestro está activo, por mejor precio (sin precio => al final); si no, se conserva el alfabético.
        if (SortByPrice)
            source = SortDescending
                ? source.OrderByDescending(product => product.BestPrice ?? decimal.MinValue)
                : source.OrderBy(product => product.BestPrice ?? decimal.MaxValue);

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

        ApplyFilters();
    }

    /// <summary>Cambió una propiedad de un producto: si afecta a los filtros (nombre, favorito, avisos, tendencia, alerta, comprado), refiltra.</summary>
    private void OnProductPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
