using Microsoft.Extensions.Options;
using Tracker.Contracts.Services;
using Tracker.Controls.Templates;
using Tracker.Controls.ViewModels;
using Tracker.Controls.Views;
using Tracker.Models;
using Tracker.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Tracker.Services.WindowService;

namespace Tracker.ViewModels;

public class MainWindowViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly ProgressService _progressService;
    private new readonly SharedDataService _sharedDataService;

    private bool _isAnimating;
    private IReadOnlyList<WidgetInfo> _widgets = Array.Empty<WidgetInfo>();
    #endregion

    #region Properties (Observable)
    public bool IsAnimating
    {
        get => _isAnimating;
        set
        {
            SetProperty(ref _isAnimating, value);
        }
    }

    public IReadOnlyList<WidgetInfo> Widgets
    {
        get => _widgets;
        set => SetProperty(ref _widgets, value);
    }
    #endregion

    #region Properties
    public ProgressService ProgressService => _progressService;
    public new SharedDataService SharedDataService => _sharedDataService;

    public ConsoleViewModel ConsoleViewModel
    {
        get; private set;
    }
    public ProductListViewModel ProductListViewModel
    {
        get; private set;
    }
    public LayoutSelectorViewModel LayoutSelectorViewModel
    {
        get; private set;
    }
    public WebViewViewModel WebViewViewModel
    {
        get; private set;
    }
    public PriceChartViewModel PriceChartViewModel
    {
        get; private set;
    }
    public ProductsOverviewViewModel ProductsOverviewViewModel
    {
        get; private set;
    }
    public FavoritesViewModel FavoritesViewModel
    {
        get; private set;
    }
    #endregion

    #region Constructors
    public MainWindowViewModel(ProgressService progressService, SharedDataService sharedDataService, ConsoleViewModel consoleViewModel, ProductListViewModel productListViewModel, LayoutSelectorViewModel layoutSelectorViewModel, WebViewViewModel webViewViewModel, PriceChartViewModel priceChartViewModel, ProductsOverviewViewModel productsOverviewViewModel, FavoritesViewModel favoritesViewModel, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _progressService = progressService;
        _sharedDataService = sharedDataService;

        ConsoleViewModel = consoleViewModel;
        ProductListViewModel = productListViewModel;
        LayoutSelectorViewModel = layoutSelectorViewModel;
        WebViewViewModel = webViewViewModel;
        PriceChartViewModel = priceChartViewModel;
        ProductsOverviewViewModel = productsOverviewViewModel;
        FavoritesViewModel = favoritesViewModel;

        // Templates: volcar el estado en vivo antes de grabar y re-aplicar la config al cargar (en caliente).
        _sharedDataService.SaveConfigRequested += OnSaveConfigRequested;
        _sharedDataService.SettingsReloaded += OnSettingsReloaded;

        LoadConfig();
    }
    #endregion

    #region Subscribed events (templates)
    /// <summary>
    /// Antes de GRABAR un template: vuelca a AppSettings el estado en vivo de todos los ViewModels (que normalmente
    /// solo se persiste al cerrar la app), para que el template capture el estado ACTUAL. Igual que el shutdown
    /// (SaveConfig en todos los IWidgetViewModelBase) más este VM (slots de widgets).
    /// </summary>
    private void OnSaveConfigRequested(object? sender, EventArgs e)
    {
        foreach (IWidgetViewModelBase vm in App.GetService<IEnumerable<IWidgetViewModelBase>>())
            vm.SaveConfig();
        SaveConfig();   // este VM: SaveWidgetSlots
    }

    /// <summary>
    /// Al CARGAR un template, re-aplica en vivo la configuración recargada (el tema se excluye del template): recarga
    /// cada ViewModel desde AppSettings (layout, config de gráficas, etc.) y re-coloca los widgets por slot.
    /// </summary>
    private void OnSettingsReloaded(object? sender, EventArgs e)
    {
        foreach (IWidgetViewModelBase vm in App.GetService<IEnumerable<IWidgetViewModelBase>>())
            vm.LoadConfig();
        LoadConfig();
        RestoreWidgetSlots();   // widgets visibles por slot -> el panel se re-organiza
    }
    #endregion

    #region Subscribed events

    #endregion

    #region Methods (private)

    #endregion

    #region Methods (public)

    /// <summary>
    /// Devuelve la colocación de ventana guardada en configuración.
    /// </summary>
    public WindowPlacement? GetSavedWindowPlacement()
    {
        var settings = _appSettings.Window;

        if (!settings.HasSavedPlacement)
            return null;

        return new WindowPlacement
        {
            X = settings.X,
            Y = settings.Y,
            Width = settings.Width,
            Height = settings.Height,
            IsMaximized = settings.IsMaximized
        };
    }

    /// <summary>
    /// Guarda en AppSettings la posición, tamaño y estado actual de la ventana principal.
    /// </summary>
    /// <param name="placement">
    /// Estado de ventana capturado desde WindowService.
    /// </param>
    public void SaveWindowPlacement(WindowPlacement placement)
    {
        _appSettings.Window.X = placement.X;
        _appSettings.Window.Y = placement.Y;
        _appSettings.Window.Width = placement.Width;
        _appSettings.Window.Height = placement.Height;
        _appSettings.Window.IsMaximized = placement.IsMaximized;
        _appSettings.Window.HasSavedPlacement = true;
    }

    public void SetWidgets(IEnumerable<WidgetInfo> widgets)
    {
        Widgets = widgets.ToList();

        RestoreWidgetSlots();
    }

    public void RestoreWidgetSlots()
    {
        if (Widgets.Count == 0)
            return;

        var savedSlots = _appSettings.LayoutSelectorControl.WidgetSlots;

        foreach (var widget in Widgets)
        {
            if (savedSlots.TryGetValue(widget.IconName, out int savedSlotIndex))
            {
                widget.ViewModel.SlotIndex = savedSlotIndex;
            }
            else
            {
                widget.ViewModel.SlotIndex = -1;
            }
        }
    }

    public void SaveWidgetSlots()
    {
        _appSettings.LayoutSelectorControl.WidgetSlots.Clear();

        foreach (var widget in Widgets)
        {
            _appSettings.LayoutSelectorControl.WidgetSlots[widget.IconName] =
                widget.ViewModel.SlotIndex;
        }
    }

    public override void LoadConfig()
    {
        // Ajustes globales del .ini a los observables compartidos (en caliente): leyenda del eje y tooltips.
        _sharedDataService.ShowChartAxisLabels = _appSettings.General.ShowChartAxisLabels;
        _sharedDataService.ShowMinPriceChart = _appSettings.General.ShowMinPriceChart;
        _sharedDataService.HelpTooltipsEnabled = _appSettings.General.HelpTooltipsEnabled;
        _sharedDataService.IncludeShippingInPrice = _appSettings.General.IncludeShippingInPrice;
        _sharedDataService.ShowPurchased = _appSettings.General.ShowPurchased;
    }

    public override void SaveConfig()
    {
        SaveWidgetSlots();
        _appSettings.General.ShowChartAxisLabels = _sharedDataService.ShowChartAxisLabels;
        _appSettings.General.ShowMinPriceChart = _sharedDataService.ShowMinPriceChart;
        _appSettings.General.HelpTooltipsEnabled = _sharedDataService.HelpTooltipsEnabled;
        _appSettings.General.IncludeShippingInPrice = _sharedDataService.IncludeShippingInPrice;
        _appSettings.General.ShowPurchased = _sharedDataService.ShowPurchased;
    }

    public override void Dispose()
    {
        _sharedDataService.SaveConfigRequested -= OnSaveConfigRequested;
        _sharedDataService.SettingsReloaded -= OnSettingsReloaded;

        ProductListViewModel.Dispose();
        PriceChartViewModel.Dispose();
        ProductsOverviewViewModel.Dispose();
        FavoritesViewModel.Dispose();
    }
    #endregion
}