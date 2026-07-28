using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del widget de favoritos: expone un <see cref="PriceChartViewModel"/> por cada producto favorito (cada
/// uno FIJADO a su producto vía <see cref="PriceChartViewModel.PinTo"/>), para mostrarlos en un FlipView reutilizando
/// el mismo control que el widget del producto seleccionado. Se reconstruye al cambiar la lista de productos o el
/// conjunto de favoritos.
/// </summary>
public partial class FavoritesViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly IOptions<AppSettings> _appSettingsOptions;

    private int _selectedIndex;
    #endregion

    #region Properties
    /// <summary>Un ViewModel de vista de producto por favorito (páginas del FlipView).</summary>
    public ObservableCollection<PriceChartViewModel> FavoriteViews { get; } = new();

    /// <summary>Hay al menos un favorito (para mostrar el FlipView o el placeholder).</summary>
    public bool HasFavorites => FavoriteViews.Count > 0;

    /// <summary>Número de favoritos (número de puntitos del PipsPager).</summary>
    public int FavoritesCount => FavoriteViews.Count;

    /// <summary>Página (favorito) activa del FlipView; enlazada TwoWay y persistida en el .ini.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }
    #endregion

    #region Constructor
    public FavoritesViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        _appSettingsOptions = appSettings;

        SharedDataService.ProductSet.Products.CollectionChanged += OnProductsChanged;
        SharedDataService.FavoritesChanged += OnFavoritesChanged;

        Rebuild();
    }
    #endregion

    #region Subscribed events
    private void OnProductsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnFavoritesChanged(object? sender, EventArgs e) => Rebuild();
    #endregion

    #region Methods (private)
    private void Rebuild()
    {
        // Antes de reconstruir, vuelca la config de gráfica actual de cada favorito al diccionario persistente para
        // que los cambios en sesión sobrevivan al recreado de las vistas (al añadir/quitar favoritos).
        CaptureChartConfigs();

        foreach (PriceChartViewModel view in FavoriteViews)
            view.Dispose();
        FavoriteViews.Clear();

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

        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(FavoritesCount));
    }

    /// <summary>Vuelca la config de gráfica de cada vista de favorito al diccionario persistente (por Id de producto).</summary>
    private void CaptureChartConfigs()
    {
        foreach (PriceChartViewModel view in FavoriteViews)
        {
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

        foreach (PriceChartViewModel view in FavoriteViews)
            view.Dispose();
        FavoriteViews.Clear();
    }

    public override void LoadConfig()
    {
        // Restaura la página activa del FlipView (acotada al nº de favoritos). Se invoca al cargarse el control, con
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
