using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del FlipView de productos: su PRIMERA página es el producto SELECCIONADO (un
/// <see cref="PriceChartViewModel"/> que sigue la selección global, inyectado como singleton) y a continuación una
/// página por cada producto favorito (cada una FIJADA a su producto vía <see cref="PriceChartViewModel.PinTo"/>),
/// reutilizando el mismo control de vista de producto. Se reconstruye la parte de favoritos al cambiar la lista de
/// productos o el conjunto de favoritos; la página del seleccionado se mantiene fija y se actualiza sola.
/// </summary>
public partial class FavoritesViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly IOptions<AppSettings> _appSettingsOptions;

    /// <summary>Vista del producto seleccionado (primera página, sigue la selección). Es un singleton que NO poseemos
    /// (lo construye/dispone la DI vía <see cref="MM4LB.ViewModels.MainWindowViewModel"/>), así que nunca se dispone aquí.</summary>
    private readonly PriceChartViewModel _selectedView;

    private int _selectedIndex;

    /// <summary>Evita la reentrada al sincronizar la página del FlipView con la selección global (y viceversa).</summary>
    private bool _syncingSelection;
    #endregion

    #region Properties
    /// <summary>Páginas del FlipView: [0] = producto seleccionado; el resto, un favorito cada una.</summary>
    public ObservableCollection<PriceChartViewModel> FavoriteViews { get; } = new();

    /// <summary>Número de páginas del FlipView (número de puntitos del PipsPager).</summary>
    public int FavoritesCount => FavoriteViews.Count;

    /// <summary>Mostrar el PipsPager solo si hay más de una página (el seleccionado + al menos un favorito).</summary>
    public bool ShowPager => FavoriteViews.Count > 1;

    /// <summary>
    /// Página activa del FlipView; enlazada TwoWay y persistida en el .ini. Al cambiar por navegación del usuario,
    /// selecciona en la lista el producto de esa página (página 0 = producto seleccionado; el resto, un favorito).
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
                PropagateIndexToSelection();
        }
    }
    #endregion

    #region Constructor
    public FavoritesViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings, PriceChartViewModel selectedProductView)
        : base(sharedDataService, appSettings)
    {
        _appSettingsOptions = appSettings;
        _selectedView = selectedProductView;

        SharedDataService.ProductSet.Products.CollectionChanged += OnProductsChanged;
        SharedDataService.FavoritesChanged += OnFavoritesChanged;
        SharedDataService.SelectedProductChanged += OnSelectedProductChanged;

        Rebuild();
    }
    #endregion

    #region Subscribed events
    private void OnProductsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Reordenar la lista (orden alfabético) no cambia el conjunto de favoritos: no hace falta reconstruir el FlipView.
        if (e.Action == NotifyCollectionChangedAction.Move)
            return;
        Rebuild();
    }

    private void OnFavoritesChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>
    /// Cambió el producto seleccionado (lista, alta, gráfico): si cambia si debe existir la página del seleccionado
    /// (aparece para no favoritos, se omite para favoritos), reconstruye el conjunto de páginas; si no, solo reposiciona.
    /// </summary>
    private void OnSelectedProductChanged(object? sender, SharedDataService.ProductChangedEventArgs e)
    {
        if (_syncingSelection)
            return;

        bool shouldHaveSelectedPage = !(SharedDataService.SelectedProduct?.IsFavorite ?? false);
        bool hasSelectedPage = FavoriteViews.Contains(_selectedView);
        if (shouldHaveSelectedPage != hasSelectedPage)
            Rebuild();   // añade o quita la página del seleccionado; Rebuild reposiciona el FlipView al final
        else
            SyncIndexToSelection();
    }
    #endregion

    #region Selection <-> FlipView sync
    /// <summary>
    /// Al navegar el usuario a una página, selecciona en la lista el producto de esa página. La página 0 (producto
    /// seleccionado) ya muestra la selección actual, así que fijarla es un no-op; las demás fijan su favorito.
    /// </summary>
    private void PropagateIndexToSelection()
    {
        if (_syncingSelection)
            return;
        if (_selectedIndex < 0 || _selectedIndex >= FavoriteViews.Count)
            return;
        if (FavoriteViews[_selectedIndex].Product is not Product product)
            return;

        _syncingSelection = true;
        SharedDataService.SelectedProduct = product;
        _syncingSelection = false;
    }

    /// <summary>
    /// Posiciona el FlipView según el producto seleccionado: si es FAVORITO, en su página; si no (o no hay selección),
    /// en la página 0 (la del producto seleccionado). No propaga de vuelta a la selección (guard).
    /// </summary>
    private void SyncIndexToSelection()
    {
        int target = IndexForProduct(SharedDataService.SelectedProduct);
        if (target == _selectedIndex)
            return;

        _syncingSelection = true;
        SelectedIndex = target;
        _syncingSelection = false;
    }

    /// <summary>
    /// Índice de la página del FlipView para un producto: la de su favorito si lo tiene; si no (no favorito o sin
    /// selección), la página del producto seleccionado (que puede no existir si el seleccionado es favorito → 0).
    /// </summary>
    private int IndexForProduct(Product? product)
    {
        int selectedIndex = FavoriteViews.IndexOf(_selectedView);

        // Página PROPIA del producto (favorito): se busca en todas las páginas salvo la del seleccionado.
        if (product is not null)
            for (int i = 0; i < FavoriteViews.Count; i++)
                if (!ReferenceEquals(FavoriteViews[i], _selectedView) && ReferenceEquals(FavoriteViews[i].Product, product))
                    return i;

        return selectedIndex >= 0 ? selectedIndex : 0;
    }
    #endregion

    #region Methods (private)
    private void Rebuild()
    {
        // Antes de reconstruir, vuelca la config de gráfica actual de cada favorito al diccionario persistente para
        // que los cambios en sesión sobrevivan al recreado de las vistas (al añadir/quitar favoritos).
        CaptureChartConfigs();

        // Durante el churn de la colección el FlipView escribe de vuelta su SelectedIndex (a -1 al vaciarse, a 0 al
        // rellenarse): se pone el guard para que esos valores transitorios NO cambien la selección global.
        _syncingSelection = true;
        try
        {
            // Solo se disponen/recrean las páginas de FAVORITOS; la página del seleccionado (_selectedView) es un
            // singleton compartido que se mantiene entre reconstrucciones.
            foreach (PriceChartViewModel view in FavoriteViews)
                if (!ReferenceEquals(view, _selectedView))
                    view.Dispose();
            FavoriteViews.Clear();

            // Página 0: el producto seleccionado (sigue la selección global), PERO solo si NO es favorito. Si el
            // seleccionado es favorito, su propia página de favorito ya lo muestra, así que se omite para no duplicarlo.
            if (!(SharedDataService.SelectedProduct?.IsFavorite ?? false))
                FavoriteViews.Add(_selectedView);

            foreach (Product product in SharedDataService.ProductSet.Products.Where(product => product.IsFavorite))
            {
                var view = new PriceChartViewModel(SharedDataService, _appSettingsOptions);
                view.PinTo(product);

                // Aplica la config de gráfica persistida de ESTE producto (por Id), si la hay.
                if (_appSettings.FavoritesControl.Charts.TryGetValue(product.Id, out AppSettings.ChartConfig? config) && config is not null)
                {
                    view.SelectedChartType = config.ChartType;
                    view.SortOrder = config.SortOrder;
                    view.TopN = config.TopN;
                }

                FavoriteViews.Add(view);
            }

            OnPropertyChanged(nameof(FavoritesCount));
            OnPropertyChanged(nameof(ShowPager));

            // Reposiciona el FlipView en la página del producto seleccionado (pudo cambiar de índice) y FUERZA la
            // notificación de SelectedIndex aunque el valor no cambie: así el PipsPager reevalúa su pip activo contra
            // el nuevo nº de páginas y no deja un pip "fantasma" encendido (dos seleccionados) al quitar un favorito.
            _selectedIndex = IndexForProduct(SharedDataService.SelectedProduct);
            OnPropertyChanged(nameof(SelectedIndex));
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Vuelca la config de gráfica de cada página de FAVORITO al diccionario persistente (por Id de producto).
    /// Excluye la página del seleccionado, cuya config la persiste el propio <see cref="PriceChartViewModel"/> (sección PriceChartControl).</summary>
    private void CaptureChartConfigs()
    {
        foreach (PriceChartViewModel view in FavoriteViews)
        {
            if (ReferenceEquals(view, _selectedView))
                continue;
            if (view.Product is not Product product || product.Id == 0)
                continue;

            _appSettings.FavoritesControl.Charts[product.Id] = new AppSettings.ChartConfig
            {
                ChartType = view.SelectedChartType,
                SortOrder = view.SortOrder,
                TopN = view.TopN,
            };
        }
    }
    #endregion

    #region Methods (public)
    public override void Dispose()
    {
        SharedDataService.ProductSet.Products.CollectionChanged -= OnProductsChanged;
        SharedDataService.FavoritesChanged -= OnFavoritesChanged;
        SharedDataService.SelectedProductChanged -= OnSelectedProductChanged;

        // No se dispone _selectedView (página 0): es un singleton que dispone MainWindowViewModel.
        foreach (PriceChartViewModel view in FavoriteViews)
            if (!ReferenceEquals(view, _selectedView))
                view.Dispose();
        FavoriteViews.Clear();
    }

    public override void LoadConfig()
    {
        // Se invoca al cargarse el control, con las vistas ya construidas. Si ya hay un producto seleccionado, el
        // FlipView se posiciona en SU página (coherente con el enlace selección<->FlipView); si no hay selección, se
        // restaura la última página guardada (acotada al nº de páginas). En ningún caso se propaga a la selección.
        if (SharedDataService.SelectedProduct is not null)
        {
            SyncIndexToSelection();
            return;
        }

        int saved = _appSettings.FavoritesControl.SelectedIndex;
        _syncingSelection = true;
        SelectedIndex = FavoriteViews.Count == 0 ? 0 : Math.Clamp(saved, 0, FavoriteViews.Count - 1);
        _syncingSelection = false;
    }

    public override void SaveConfig()
    {
        CaptureChartConfigs();
        _appSettings.FavoritesControl.SelectedIndex = SelectedIndex;
    }
    #endregion
}
