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
    #endregion

    #region Properties
    /// <summary>Páginas del FlipView: [0] = producto seleccionado; el resto, un favorito cada una.</summary>
    public ObservableCollection<PriceChartViewModel> FavoriteViews { get; } = new();

    /// <summary>Número de páginas del FlipView (número de puntitos del PipsPager).</summary>
    public int FavoritesCount => FavoriteViews.Count;

    /// <summary>Mostrar el PipsPager solo si hay más de una página (el seleccionado + al menos un favorito).</summary>
    public bool ShowPager => FavoriteViews.Count > 1;

    /// <summary>Página activa del FlipView; enlazada TwoWay y persistida en el .ini.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
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
    #endregion

    #region Methods (private)
    private void Rebuild()
    {
        // Antes de reconstruir, vuelca la config de gráfica actual de cada favorito al diccionario persistente para
        // que los cambios en sesión sobrevivan al recreado de las vistas (al añadir/quitar favoritos).
        CaptureChartConfigs();

        // Solo se disponen/recrean las páginas de FAVORITOS; la página del seleccionado (_selectedView) es un
        // singleton compartido que se mantiene entre reconstrucciones.
        foreach (PriceChartViewModel view in FavoriteViews)
            if (!ReferenceEquals(view, _selectedView))
                view.Dispose();
        FavoriteViews.Clear();

        // Página 0: el producto seleccionado (sigue la selección global).
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

        // No se dispone _selectedView (página 0): es un singleton que dispone MainWindowViewModel.
        foreach (PriceChartViewModel view in FavoriteViews)
            if (!ReferenceEquals(view, _selectedView))
                view.Dispose();
        FavoriteViews.Clear();
    }

    public override void LoadConfig()
    {
        // Restaura la página activa del FlipView (acotada al nº de páginas). Se invoca al cargarse el control, con
        // las vistas ya construidas, para no chocar con el reajuste del índice del FlipView vacío.
        int saved = _appSettings.FavoritesControl.SelectedIndex;
        SelectedIndex = FavoriteViews.Count == 0 ? 0 : Math.Clamp(saved, 0, FavoriteViews.Count - 1);
    }

    public override void SaveConfig()
    {
        CaptureChartConfigs();
        _appSettings.FavoritesControl.SelectedIndex = SelectedIndex;
    }
    #endregion
}
