using Microsoft.UI.Xaml;
using MM4LB.Controls.Templates;
using MM4LB.Controls.Views;
using MM4LB.Services;
using MM4LB.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MM4LB.Views;

public sealed partial class MainWindow : Window
{
    #region Constants
    private const int DefaultWindowWidth = 2800;
    private const int DefaultWindowHeight = 1400;
    #endregion

    #region Attributes
    private readonly MainWindowViewModel _viewModel;
    private readonly WindowService _windowService;
    private readonly ThemeService _themeService;

    private bool _initialized;
    #endregion

    #region Properties
    // Expuesto para los x:Bind del XAML (x:Bind resuelve contra el code-behind, no contra el DataContext).
    public MainWindowViewModel ViewModel => _viewModel;

    /// <summary>
    /// Marca de compilación que se muestra junto al título de la plataforma, para saber a simple vista si la versión
    /// en ejecución es la recién compilada. Se deriva de la fecha de última escritura del ensamblado (cada
    /// compilación reescribe el DLL), así que no hay que fijarla a mano.
    /// </summary>
    public string BuildTimestamp
    {
        get
        {
            try
            {
                // La ruta del .exe en ejecución es fiable en .NET 6+ (y se reescribe en cada compilación); si por lo
                // que sea viene vacía, se cae al Location del ensamblado. El .exe/.dll se reescriben al compilar, así
                // que su fecha de última escritura ES la hora de compilación.
                string? path = System.Environment.ProcessPath;
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                    path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                    return "build ?";

                return "build " + System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "build ?";
            }
        }
    }
    #endregion

    #region Constructor

    public MainWindow(MainWindowViewModel viewModel, WindowService windowService)
    {
        _viewModel = viewModel;
        _windowService = windowService;

        InitializeComponent();

        // Marca de compilación junto al título de la plataforma (para saber si la versión en ejecución es la recién
        // compilada). Asignada por código, sin binding, para que no dependa de nada del árbol visual.
        BuildTimestampText.Text = BuildTimestamp;

        // El logo, el fondo/borde del progreso de cache y el título de plataforma usan recursos de tipo Uri/Color que
        // no se propagan solos al cambiar de tema en caliente: se refrescan por código al recibir ThemeChanged.
        _themeService = App.GetService<ThemeService>();
        _themeService.ThemeChanged += OnThemeChanged;

        if (Content is FrameworkElement root)
        {
            root.DataContext = _viewModel;
            root.Loaded += OnWindowLoaded;
        }

        InitializeToolbarBehavior();

        Activated += OnWindowActivated;
        Closed += OnClosed;
    }

    #endregion

    #region Subscribed events

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;

        var placement = _viewModel.GetSavedWindowPlacement();

        _windowService.MainWindow(
            this,
            DefaultWindowWidth,
            DefaultWindowHeight,
            false,
            placement: placement);
    }

    /// <summary>
    /// Refresca en caliente los elementos cuyos recursos son de tipo Uri/Color (no brushes) y por tanto no se
    /// propagan solos: el logo de la app y el overlay tintado del fondo.
    /// </summary>
    private void OnThemeChanged(object? sender, System.EventArgs e)
    {
        if (_themeService.LogoImageUri is System.Uri logo)
            AppLogoImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(logo);

        // Overlay tintado del fondo: sus propiedades vienen de recursos Uri/Color/double (no brushes), así que se
        // reasignan aquí al cambiar de tema o sus parámetros de tinte (ventana de configuración -> ApplyTheme).
        TintedImage.Source = _themeService.OverlayImageUri?.ToString() ?? string.Empty;
        TintedImage.TintColor = _themeService.AccentColor;
        TintedImage.TintOpacity = _themeService.TintOpacity;
        TintedImage.TintSaturation = _themeService.TintSaturation;
        TintedImage.TintBrightness = _themeService.TintBrightness;
        TintedImage.Blur = _themeService.OverlayImageBlur;
        TintedImage.Opacity = _themeService.OverlayImageOpacity;
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var widgetEntries = new List<WidgetPanelControl.WidgetEntry>
        {
            new(ucConsoleControl.ViewModel!, ucConsoleWidget),
            new(ucWebViewControl.ViewModel!, ucWebViewWidget),
        };

        WidgetPanel.SetWidgets(widgetEntries);

        _viewModel.Widgets = widgetEntries.Select(w => new WidgetInfo(w.ViewModel, w.Control.Title, w.Control.Content?.GetType().Name ?? "DefaultWidget")).ToList();

        _viewModel.RestoreWidgetSlots();

        if (sender is FrameworkElement fe)
            fe.Loaded -= OnWindowLoaded;

        // Deja terminar los bindings y el primer layout pass.
        await Task.Yield();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        var placement = _windowService.GetWindowPlacement(this);

        _viewModel.SaveWindowPlacement(placement);
        _viewModel.SaveConfig();

        Activated -= OnWindowActivated;
        Closed -= OnClosed;

        DisposeToolbarBehavior();

        _viewModel.Dispose();
    }

    #endregion

}