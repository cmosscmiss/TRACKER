using Microsoft.Extensions.Options;
using MM4LB.Contracts.Services;
using MM4LB.Controls.Templates;
using MM4LB.Controls.ViewModels;
using MM4LB.Controls.Views;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static MM4LB.Services.WindowService;

namespace MM4LB.ViewModels;

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
    #endregion

    #region Constructors
    public MainWindowViewModel(ProgressService progressService, SharedDataService sharedDataService, ConsoleViewModel consoleViewModel, ProductListViewModel productListViewModel, LayoutSelectorViewModel layoutSelectorViewModel, WebViewViewModel webViewViewModel, PriceChartViewModel priceChartViewModel, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _progressService = progressService;
        _sharedDataService = sharedDataService;

        ConsoleViewModel = consoleViewModel;
        ProductListViewModel = productListViewModel;
        LayoutSelectorViewModel = layoutSelectorViewModel;
        WebViewViewModel = webViewViewModel;
        PriceChartViewModel = priceChartViewModel;

        LoadConfig();
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
    }

    public override void SaveConfig()
    {
        SaveWidgetSlots();
    }

    public override void Dispose()
    {
        ProductListViewModel.Dispose();
        PriceChartViewModel.Dispose();
    }
    #endregion
}