using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Tracker.Controls.ViewModels;
using Tracker.Helpers;
using Tracker.Services;

namespace Tracker.Controls.Views;

/// <summary>
/// Control de usuario que muestra y gestiona la selección de productos rastreados como una lista
/// (<see cref="ListView"/>) en la columna izquierda de la ventana principal. Expone su
/// <see cref="ProductListViewModel"/> mediante una <see cref="DependencyProperty"/> para que la ventana
/// principal pueda inyectar el ViewModel desde el exterior.
///
/// La selección se sincroniza a mano con <see cref="SharedDataService.SelectedProduct"/> (en vez de un binding
/// TwoWay) para poder IGNORAR las deselecciones transitorias del ListView al reordenar la lista alfabéticamente
/// (un <c>Move</c> cuando un producto recién añadido carga su título), que si no borrarían la selección.
/// </summary>
public sealed partial class ProductListControl : UserControl
{
    #region Attributes
    /// <summary>
    /// Filtros del desplegable: clave del texto localizado y propiedad de <c>ProductFilters</c> a la que se enlaza.
    /// El ORDEN de esta tabla es indiferente: el menú se ordena por el texto ya traducido.
    /// </summary>
    private static readonly (string LocKey, string Property)[] FilterEntries =
    {
        (LocKeys.ProductList_FilterFavorites_Label, nameof(Models.ProductFilters.Favorites)),
        (LocKeys.ProductList_FilterIssues_Label, nameof(Models.ProductFilters.WithIssues)),
        (LocKeys.ProductList_FilterPriceChange_Label, nameof(Models.ProductFilters.WithPriceChange)),
        (LocKeys.ProductList_FilterHistoricalLow_Label, nameof(Models.ProductFilters.WithHistoricalLow)),
        (LocKeys.ProductList_FilterAlert_Label, nameof(Models.ProductFilters.WithAlert)),
        (LocKeys.ProductList_FilterWithoutPrice_Label, nameof(Models.ProductFilters.WithoutPrice)),
        (LocKeys.ProductList_FilterPurchased_Label, nameof(Models.ProductFilters.WithPurchased)),
    };

    private readonly LocalizationService _localizationService = App.GetService<LocalizationService>();

    private SharedDataService? _sharedDataService;

    /// <summary>Evita la reentrada al sincronizar la selección del ListView con el modelo (y viceversa).</summary>
    private bool _syncingSelection;
    #endregion

    #region Dependency Properties
    /// <summary>
    /// ViewModel asociado al control: da acceso a la colección de productos y al producto seleccionado.
    /// </summary>
    public ProductListViewModel? ViewModel
    {
        get => (ProductListViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ProductListViewModel), typeof(ProductListControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    public ProductListControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Al cargarse: restaura el estado del control, sincroniza la selección inicial y se suscribe a los cambios de
    /// selección (para hacer scroll al seleccionado) y a los cambios de la colección (para re-sincronizar la
    /// selección tras un reordenado).
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
            return;

        ViewModel.LoadConfig();

        _sharedDataService = ViewModel.SharedDataService;
        _sharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
        _sharedDataService.SelectedProductChanged += OnSelectedProductChanged;
        // Se escucha la colección FILTRADA (lo que muestra el ListView): al reconciliarla (mover/insertar/quitar) el
        // ListView puede perder la selección, así que se re-sincroniza.
        ViewModel.FilteredProducts.CollectionChanged -= OnProductsChanged;
        ViewModel.FilteredProducts.CollectionChanged += OnProductsChanged;

        // Menú de filtros: se construye por código para poder ordenarlo por el texto TRADUCIDO. Se rehace al cambiar
        // de idioma (cambia el orden) y al alternar "mostrar comprados" (aparece o desaparece un filtro).
        _localizationService.LanguageChanged -= OnLanguageChanged;
        _localizationService.LanguageChanged += OnLanguageChanged;
        _sharedDataService.PropertyChanged -= OnSharedDataChanged;
        _sharedDataService.PropertyChanged += OnSharedDataChanged;
        BuildFilterMenu();

        // Refleja la selección actual en el ListView y la trae a la vista.
        SyncListViewToSelection();
        ScrollSelectedIntoView();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;

        if (_sharedDataService is null)
            return;

        _sharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
        _sharedDataService.PropertyChanged -= OnSharedDataChanged;
        if (ViewModel is not null)
            ViewModel.FilteredProducts.CollectionChanged -= OnProductsChanged;
    }

    /// <summary>Cambió el idioma: los textos son otros, así que el orden alfabético del menú de filtros también.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e) => BuildFilterMenu();

    /// <summary>El toggle de "mostrar comprados" del footer añade o quita el filtro "Comprados" del menú.</summary>
    private void OnSharedDataChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.ShowPurchased))
            BuildFilterMenu();
    }

    /// <summary>El usuario (o el código) seleccionó un item: refleja la selección en el modelo. Ignora deselecciones (null).</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _sharedDataService is null)
            return;

        // Solo se propaga una selección REAL; una deselección transitoria (SelectedItem == null, típica al reordenar
        // la lista) NO borra el producto seleccionado del modelo.
        if (ProductListView.SelectedItem is Models.Product product)
            _sharedDataService.SelectedProduct = product;
    }

    /// <summary>Cambió el producto seleccionado (lista, alta o gráfico de productos): sincroniza el ListView y hace scroll.</summary>
    private void OnSelectedProductChanged(object? sender, SharedDataService.ProductChangedEventArgs e)
    {
        SyncListViewToSelection();
        ScrollSelectedIntoView();
    }

    /// <summary>
    /// Tras reconciliar la lista filtrada (mover al reordenar, o insertar/quitar al cambiar el filtro), re-selecciona
    /// el producto en el ListView (que pudo perder la selección) y lo trae a la vista: al cargar el título de un
    /// producto recién añadido, la lista se reordena alfabéticamente y el seleccionado puede quedar fuera de pantalla.
    /// El reset es cuando cambia todo el ItemsSource.
    /// </summary>
    private void OnProductsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SyncListViewToSelection();
            ScrollSelectedIntoView();
        });
    }
    #endregion

    #region Filter menu
    /// <summary>
    /// Rehace el desplegable de filtros con sus items ORDENADOS ALFABÉTICAMENTE por el texto traducido, comparando
    /// con la cultura activa (para que acentos y eñes caigan donde corresponde en cada idioma). Cada item conserva el
    /// comportamiento que tenía en XAML: check enlazado en TwoWay a su propiedad de <c>ActiveFilters</c> —así una
    /// desactivación hecha desde el ViewModel se refleja— y el comando que refiltra al pulsarlo.
    /// </summary>
    private void BuildFilterMenu()
    {
        if (ViewModel is null || FiltersFlyout is null)
            return;

        FiltersFlyout.Items.Clear();

        // "Comprados" solo tiene sentido cuando el footer muestra los comprados.
        bool showPurchased = ViewModel.SharedDataService.ShowPurchased;

        var entries = FilterEntries
            .Where(entry => showPurchased || entry.Property != nameof(Models.ProductFilters.WithPurchased))
            .Select(entry => (entry.Property, Text: _localizationService[entry.LocKey]))
            .OrderBy(entry => entry.Text, StringComparer.Create(_localizationService.Current, ignoreCase: true));

        foreach ((string property, string text) in entries)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = text,
                Command = ViewModel.FiltersChangedCommand
            };

            item.SetBinding(ToggleMenuFlyoutItem.IsCheckedProperty, new Binding
            {
                Source = ViewModel.ActiveFilters,
                Path = new PropertyPath(property),
                Mode = BindingMode.TwoWay
            });

            FiltersFlyout.Items.Add(item);
        }
    }
    #endregion

    #region Sort UI events
    /// <summary>
    /// Clic en una opción del desplegable de orden: fija criterio y dirección (su <c>Tag</c>, "criterio-dirección") y
    /// ACTIVA la ordenación (igual que en el filtro, elegir una opción activa la función). Al desactivar la cara se
    /// vuelve al orden alfabético.
    /// </summary>
    private void OnSortDirectionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not FrameworkElement item || item.Tag is not string tag)
            return;

        bool descending = tag.EndsWith("desc", StringComparison.Ordinal);
        ViewModel.SortField = tag.StartsWith("date", StringComparison.Ordinal)
            ? ProductSortField.Created
            : ProductSortField.Price;
        ViewModel.SortDescending = descending;
        ViewModel.SortByPrice = true;

        // Exclusividad manual (los ToggleMenuFlyoutItem alternan su propio check): deja marcada solo la opción activa.
        SortPriceAscItem.IsChecked = tag == "price-asc";
        SortPriceDescItem.IsChecked = tag == "price-desc";
        SortDateAscItem.IsChecked = tag == "date-asc";
        SortDateDescItem.IsChecked = tag == "date-desc";
    }
    #endregion

    #region Methods (private)
    /// <summary>Pone el <c>SelectedItem</c> del ListView igual al producto seleccionado del modelo (sin reentrada).</summary>
    private void SyncListViewToSelection()
    {
        Models.Product? selected = _sharedDataService?.SelectedProduct;

        // El ListView NO puede seleccionar lo que no muestra: si el producto seleccionado ya no está en la lista
        // filtrada (p. ej. al marcarlo como comprado con los comprados ocultos, o al perder el favorito con el filtro
        // de favoritos activo), la vista se queda SIN selección y el modelo no se toca. Asignar un item ausente
        // desincroniza el ListView de su ItemsSource y acaba en el ArgumentOutOfRangeException del ABI ("indices
        // larger than Int32.MaxValue") que cierra el proceso.
        if (selected is not null && ViewModel?.FilteredProducts.Contains(selected) != true)
            selected = null;

        if (ReferenceEquals(ProductListView.SelectedItem, selected))
            return;

        _syncingSelection = true;
        ProductListView.SelectedItem = selected;
        _syncingSelection = false;
    }

    /// <summary>Hace scroll para que el producto seleccionado quede visible (diferido para que exista su contenedor).</summary>
    private void ScrollSelectedIntoView()
    {
        if (_sharedDataService?.SelectedProduct is null)
            return;

        // Se difiere al dispatcher: tras un alta o un reordenado alfabético, el contenedor del item puede no existir aún.
        // Alineación Default (no Leading): solo hace scroll si el elemento NO está visible, y el mínimo necesario (no lo
        // lleva siempre al principio de la lista).
        DispatcherQueue.TryEnqueue(() =>
        {
            // Entre encolar y ejecutar, la lista pudo reconciliarse y dejar fuera al seleccionado: hacer scroll a un
            // item que ya no está en el ItemsSource pide un índice inválido al ABI y revienta el proceso.
            if (_sharedDataService?.SelectedProduct is Models.Product current
                && ViewModel?.FilteredProducts.Contains(current) == true)
                ProductListView.ScrollIntoView(current, ScrollIntoViewAlignment.Default);
        });
    }
    #endregion
}
