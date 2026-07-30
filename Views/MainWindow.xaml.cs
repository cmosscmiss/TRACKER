using Microsoft.UI.Xaml;
using MM4LB.Controls.Templates;
using MM4LB.Controls.Views;
using MM4LB.Services;
using MM4LB.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MM4LB.Views;

public sealed partial class MainWindow : Window
{
    #region Constants
    private const int DefaultWindowWidth = 2800;
    private const int DefaultWindowHeight = 1400;

    /// <summary>
    /// Nº de navegadores WebView2 ocultos del pool de scraping. A más navegadores, más tiendas se leen en paralelo
    /// (una por navegador), a costa de más memoria (cada uno es un proceso de Chromium). 4 equilibra velocidad y RAM
    /// para los 5 marketplaces de Amazon; se puede subir/bajar aquí.
    /// </summary>
    private const int ScraperPoolSize = 4;
    #endregion

    #region Attributes
    private readonly MainWindowViewModel _viewModel;
    private readonly WindowService _windowService;
    private readonly ThemeService _themeService;
    private readonly TrayIcon _trayIcon = new();

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

        // Minimizar a la bandeja del sistema: al minimizar se oculta de la barra de tareas y el icono de la bandeja
        // la restaura. Mantener el proceso vivo en la bandeja es lo que permite al scheduler seguir leyendo precios.
        _trayIcon.Initialize(WinRT.Interop.WindowNative.GetWindowHandle(this), "Tracker");

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
            new(ucProductsOverviewControl.ViewModel!, ucProductsOverviewWidget),
            new(ucFavoritesControl.ViewModel!, ucFavoritesWidget),
        };

        WidgetPanel.SetWidgets(widgetEntries);

        _viewModel.Widgets = widgetEntries.Select(w => new WidgetInfo(w.ViewModel, w.Control.Title, w.Control.Content?.GetType().Name ?? "DefaultWidget")).ToList();

        _viewModel.RestoreWidgetSlots();

        if (sender is FrameworkElement fe)
            fe.Loaded -= OnWindowLoaded;

        // Deja terminar los bindings y el primer layout pass.
        await Task.Yield();

        await InitializeScraperAsync();
    }

    /// <summary>
    /// Inicializa el WebView2 oculto de scraping (mismo user data folder explícito que el WebView visible, para no
    /// caer en el fallback de ApplicationData que revienta en apps unpackaged) y lo entrega al ProductParsingService.
    /// </summary>
    private async Task InitializeScraperAsync()
    {
        try
        {
            string userDataFolder = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Tracker", "WebView2");
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);

            ProductParsingService parsing = App.GetService<ProductParsingService>();

            // Primer navegador del pool: el declarado en XAML.
            await ScraperWebView.EnsureCoreWebView2Async(environment);
            parsing.Attach(ScraperWebView);

            // Resto del pool: navegadores 1x1 ocultos creados por código, que COMPARTEN el mismo entorno (misma
            // carpeta de datos de usuario), para poder leer varias tiendas en paralelo (ver ProductParsingService).
            if (ScraperWebView.Parent is Microsoft.UI.Xaml.Controls.Panel host)
            {
                for (int i = 1; i < ScraperPoolSize; i++)
                {
                    // Cada navegador del pool va en su propio try/catch: si uno falla al inicializar, se registra y se
                    // sigue con los demás, sin abortar el resto del pool ni el arranque del scheduler (más abajo).
                    try
                    {
                        var webView = new Microsoft.UI.Xaml.Controls.WebView2
                        {
                            Width = 1,
                            Height = 1,
                            Opacity = 0,
                            IsHitTestVisible = false,
                            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
                            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top
                        };
                        host.Children.Add(webView);
                        await webView.EnsureCoreWebView2Async(environment);
                        parsing.Attach(webView);
                    }
                    catch (Exception ex)
                    {
                        ExceptionService.LogToFile(ex, $"Could not initialize scraper WebView2 #{i} of the pool; continuing with fewer.");
                    }
                }
            }

            // El scraper ya está listo: arranca el planificador de precios (catch-up + cada 12 h) en este hilo de UI.
            App.GetService<PriceSchedulerService>().Start(DispatcherQueue);
        }
        catch (System.Exception ex)
        {
            App.GetService<ExceptionService>().Handle(ex, "The price scraper browser could not be initialized.");
        }
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        var placement = _windowService.GetWindowPlacement(this);

        _viewModel.SaveWindowPlacement(placement);
        _viewModel.SaveConfig();

        Activated -= OnWindowActivated;
        Closed -= OnClosed;

        DisposeToolbarBehavior();
        _trayIcon.Dispose();

        _viewModel.Dispose();
    }

    #endregion

    #region Footer actions
    /// <summary>
    /// Botón "Añadir producto" del footer: pide una URL y crea un producto rastreado desde ella (una tienda), que
    /// se persiste en la BD, se añade a la lista y queda seleccionado. Nombre/precio se rellenan luego al parsear.
    /// </summary>
    private async void OnAddProductClick(object sender, RoutedEventArgs e)
    {
        if (Content is not FrameworkElement root)
            return;

        DialogsService dialogs = App.GetService<DialogsService>();
        string? url = await dialogs.PromptAsync(
            root.XamlRoot,
            L(MM4LB.Helpers.LocKeys.AddProduct_Dialog_Title),
            L(MM4LB.Helpers.LocKeys.AddProduct_Dialog_Message),
            L(MM4LB.Helpers.LocKeys.AddProduct_Url_Placeholder),
            L(MM4LB.Helpers.LocKeys.Common_Add_Label),
            L(MM4LB.Helpers.LocKeys.Common_Cancel_Label));

        if (string.IsNullOrWhiteSpace(url))
            return;

        var product = App.GetService<ProductService>().AddProductFromUrl(url);
        if (product is not null)
            await App.GetService<ProductService>().RefreshProductAsync(product);
    }

    /// <summary>Botón "Refrescar todo" del footer: fuerza el refresco de precios de todos los productos (como el scheduler).</summary>
    private void OnRefreshAllClick(object sender, RoutedEventArgs e)
    {
        _ = App.GetService<PriceSchedulerService>().RefreshAllAsync();
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => MM4LB.Services.LocalizationService.Instance?[key] ?? key;
    #endregion

}