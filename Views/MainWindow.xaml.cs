using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MM4LB.Controls.Templates;
using MM4LB.Controls.Views;
using MM4LB.Helpers;
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
    private readonly AmazonAuthService _amazonAuth;
    private readonly TrayIcon _trayIcon = new();

    private DispatcherQueueTimer? _nextUpdateTimer;

    private bool _initialized;
    private bool _amazonStartupPromptDone;
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

        // Login de Amazon: refresca el botón del footer cuando cambia el estado de sesión, y lanza la comprobación de
        // arranque en cuanto el navegador visible está listo (si no hay sesión, pide login).
        _amazonAuth = App.GetService<AmazonAuthService>();
        _amazonAuth.StateChanged += OnAmazonAuthStateChanged;
        _amazonAuth.LoginBrowserReady += OnAmazonLoginBrowserReady;

        // Al alternar los tooltips, el del botón de Amazon (que se fija por código) también debe ocultarse/mostrarse.
        _viewModel.SharedDataService.PropertyChanged += OnSharedDataChanged;

        // Notificación de Windows (resumen + destacados) tras un refresco total (manual o automático).
        App.GetService<PriceSchedulerService>().NotificationReady += OnSchedulerNotification;

        if (Content is FrameworkElement root)
        {
            root.DataContext = _viewModel;
            root.Loaded += OnWindowLoaded;
        }

        InitializeToolbarBehavior();

        // Minimizar a la bandeja del sistema: al minimizar se oculta de la barra de tareas y el icono de la bandeja
        // la restaura. Mantener el proceso vivo en la bandeja es lo que permite al scheduler seguir leyendo precios.
        _trayIcon.Initialize(WinRT.Interop.WindowNative.GetWindowHandle(this), "Tracker");

        // Si se intenta abrir una segunda instancia, la principal (esta) restaura la ventana en pantalla en vez de
        // mostrar un aviso. La señal llega en un hilo en segundo plano; se marshalea al hilo de UI.
        App.ActivationRequested = () => DispatcherQueue.TryEnqueue(() => _trayIcon.Restore());

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
            _amazonAuth.AttachCookieBrowser(ScraperWebView);   // permite consultar la sesión aunque el widget web no esté visible

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
                        _amazonAuth.AttachCookieBrowser(webView);
                    }
                    catch (Exception ex)
                    {
                        ExceptionService.LogToFile(ex, $"Could not initialize scraper WebView2 #{i} of the pool; continuing with fewer.");
                    }
                }
            }

            // El scraper ya está listo: arranca el planificador de precios (catch-up + cada 12 h) en este hilo de UI.
            App.GetService<PriceSchedulerService>().Start(DispatcherQueue);

            // Cuenta atrás del footer hacia la siguiente pasada automática.
            StartNextUpdateCountdown();
        }
        catch (System.Exception ex)
        {
            App.GetService<ExceptionService>().Handle(ex, "The price scraper browser could not be initialized.");
        }
    }

    /// <summary>Arranca la cuenta atrás (cada segundo) que refresca el mensaje del footer con el tiempo hasta la próxima pasada automática.</summary>
    private void StartNextUpdateCountdown()
    {
        if (_nextUpdateTimer is not null)
            return;

        _nextUpdateTimer = DispatcherQueue.CreateTimer();
        _nextUpdateTimer.Interval = TimeSpan.FromSeconds(1);
        _nextUpdateTimer.Tick += (_, _) => UpdateNextUpdateText();
        _nextUpdateTimer.Start();
        UpdateNextUpdateText();
    }

    /// <summary>Actualiza el reloj split-flap del footer con el tiempo que falta para la siguiente actualización automática.</summary>
    private void UpdateNextUpdateText()
    {
        if (App.GetService<PriceSchedulerService>().NextRunUtc is not DateTime nextUtc)
        {
            NextUpdateClock.Visibility = Visibility.Collapsed;
            return;
        }

        NextUpdateClock.Visibility = Visibility.Visible;
        TimeSpan remaining = nextUtc - DateTime.UtcNow;
        NextUpdateClock.Value = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;

        UpdateRefreshTooltip();
    }

    /// <summary>Última cadena fijada como tooltip del botón de refrescar todo (para no reasignarla en cada tick).</summary>
    private string? _refreshTooltipCache;

    /// <summary>
    /// Fija el tooltip del botón de refrescar todo: etiqueta base + fecha/hora del último refresco (automático o
    /// manual), en hora local. Gateado por el toggle global de tooltips (si están desactivados, se quita). Se llama en
    /// el tick de la cuenta atrás, así que recoge en caliente los cambios de idioma, del toggle y del último refresco.
    /// </summary>
    private void UpdateRefreshTooltip()
    {
        if (!_viewModel.SharedDataService.HelpTooltipsEnabled)
        {
            if (_refreshTooltipCache is not null)
            {
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(RefreshAllButton, null);
                _refreshTooltipCache = null;
            }
            return;
        }

        string baseLabel = L(MM4LB.Helpers.LocKeys.Footer_RefreshAll_Tooltip);
        string lastLine = App.GetService<PriceSchedulerService>().LastFullRefreshUtc is DateTime lastUtc
            ? string.Format(L(MM4LB.Helpers.LocKeys.Footer_RefreshAll_LastUpdate_Format), lastUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture))
            : L(MM4LB.Helpers.LocKeys.Footer_RefreshAll_LastUpdate_Never);
        string tooltip = $"{baseLabel}\n{lastLine}";

        if (tooltip == _refreshTooltipCache)
            return;

        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(RefreshAllButton, tooltip);
        _refreshTooltipCache = tooltip;
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        var placement = _windowService.GetWindowPlacement(this);

        _viewModel.SaveWindowPlacement(placement);
        _viewModel.SaveConfig();

        Activated -= OnWindowActivated;
        Closed -= OnClosed;

        App.ActivationRequested = null;
        _viewModel.SharedDataService.PropertyChanged -= OnSharedDataChanged;
        App.GetService<PriceSchedulerService>().NotificationReady -= OnSchedulerNotification;
        _nextUpdateTimer?.Stop();

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

        ProductService productService = App.GetService<ProductService>();

        // Si ya se rastrea un producto que cubre esta URL (mismo ASIN de Amazon o misma URL), se avisa y no se importa.
        if (productService.ContainsProductForUrl(url))
        {
            await dialogs.AlertAsync(
                root.XamlRoot,
                L(MM4LB.Helpers.LocKeys.AddProduct_Duplicate_Title),
                L(MM4LB.Helpers.LocKeys.AddProduct_Duplicate_Message),
                L(MM4LB.Helpers.LocKeys.Common_OK_Label));
            return;
        }

        var product = productService.AddProductFromUrl(url);
        if (product is not null)
        {
            await productService.RefreshProductAsync(product, addedProduct: true);

            // Al terminar la carga de precios, navega a la tienda con el precio más bajo (en el widget navegador).
            string? bestUrl = product.BestStore?.Url;
            if (!string.IsNullOrWhiteSpace(bestUrl))
                App.GetService<MM4LB.Controls.ViewModels.WebViewViewModel>().RequestNavigation(bestUrl);
        }
    }

    /// <summary>Botón "Refrescar todo" del footer: fuerza el refresco de precios de todos los productos (como el scheduler).</summary>
    private void OnRefreshAllClick(object sender, RoutedEventArgs e)
    {
        _ = App.GetService<PriceSchedulerService>().RefreshAllAsync();
    }

    /// <summary>
    /// Botón de sesión de Amazon del footer: si hay sesión iniciada, cierra sesión (previa confirmación); si no,
    /// abre el diálogo de login (email + contraseña) e intenta iniciar sesión en el navegador visible.
    /// </summary>
    private async void OnAmazonAuthClick(object sender, RoutedEventArgs e)
    {
        if (Content is not FrameworkElement root)
            return;

        if (await _amazonAuth.IsLoggedInAsync())
        {
            bool confirmed = await App.GetService<DialogsService>().ConfirmAsync(
                root.XamlRoot,
                L(MM4LB.Helpers.LocKeys.AmazonLogout_ConfirmTitle),
                L(MM4LB.Helpers.LocKeys.AmazonLogout_ConfirmMessage),
                L(MM4LB.Helpers.LocKeys.AmazonLogout_Confirm_Label),
                L(MM4LB.Helpers.LocKeys.Common_Cancel_Label));

            if (confirmed)
                await _amazonAuth.LogoutAsync();
        }
        else
        {
            await StartAmazonLoginFlowAsync(root.XamlRoot);
        }

        await RefreshAmazonAuthButtonAsync();
    }

    /// <summary>Abre el diálogo de login de Amazon y, si se confirma, intenta iniciar sesión en el navegador visible.</summary>
    private async Task StartAmazonLoginFlowAsync(Microsoft.UI.Xaml.XamlRoot xamlRoot)
    {
        // El login necesita el navegador VISIBLE (para que el usuario vea captcha/verificación en dos pasos).
        if (!_amazonAuth.CanLogin)
        {
            await App.GetService<DialogsService>().AlertAsync(
                xamlRoot,
                L(MM4LB.Helpers.LocKeys.AmazonLogin_NoBrowser_Title),
                L(MM4LB.Helpers.LocKeys.AmazonLogin_NoBrowser_Message),
                L(MM4LB.Helpers.LocKeys.Common_OK_Label));
            return;
        }

        (string Email, string Password)? credentials = await App.GetService<DialogsService>().ShowAmazonLoginAsync(xamlRoot);
        if (credentials is null)
            return;

        await _amazonAuth.LoginAsync(credentials.Value.Email, credentials.Value.Password);
    }

    /// <summary>El estado de sesión pudo cambiar (login/logout, o navegación manual del usuario): refresca el botón.</summary>
    private void OnAmazonAuthStateChanged(object? sender, System.EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () => await RefreshAmazonAuthButtonAsync());
    }

    /// <summary>El navegador visible está listo: comprueba UNA vez la sesión al arrancar y pide login si no la hay.</summary>
    private void OnAmazonLoginBrowserReady(object? sender, System.EventArgs e)
    {
        if (_amazonStartupPromptDone)
            return;
        _amazonStartupPromptDone = true;

        DispatcherQueue.TryEnqueue(async () =>
        {
            await RefreshAmazonAuthButtonAsync();

            // Deja constancia en la consola del estado de sesión de Amazon comprobado al arrancar.
            int loggedInStores = await _amazonAuth.CountLoggedInStoresAsync();
            App.GetService<ProgressService>().LogEvent(
                string.Format(L(MM4LB.Helpers.LocKeys.AmazonSession_Startup_Log), loggedInStores, _amazonAuth.StoreCount));

            if (Content is FrameworkElement root && !await _amazonAuth.IsLoggedInAsync())
            {
                await StartAmazonLoginFlowAsync(root.XamlRoot);
                await RefreshAmazonAuthButtonAsync();
            }
        });
    }

    /// <summary>Refresca el icono/tooltip del botón de sesión de Amazon según haya sesión iniciada o no.</summary>
    private async Task RefreshAmazonAuthButtonAsync()
    {
        bool loggedIn = await _amazonAuth.IsLoggedInAsync();
        // Icono en acento si hay sesión (es un "toggle" login/logout), secundario si no.
        AmazonAuthIcon.Foreground = ResolveBrush(loggedIn ? "AccentBrush" : "TextSecondaryBrush");
        // Tooltip gateado por el toggle global de tooltips (se fija por código, así que se aplica aquí a mano).
        bool tips = _viewModel.SharedDataService.HelpTooltipsEnabled;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(AmazonAuthButton,
            tips ? L(loggedIn ? MM4LB.Helpers.LocKeys.AmazonLogout_Tooltip : MM4LB.Helpers.LocKeys.AmazonLogin_Tooltip) : null);
    }

    /// <summary>Alterna la visibilidad global de los tooltips de los botones.</summary>
    private void OnToggleTooltipsClick(object sender, RoutedEventArgs e)
        => _viewModel.SharedDataService.HelpTooltipsEnabled = !_viewModel.SharedDataService.HelpTooltipsEnabled;

    /// <summary>Alterna la visibilidad de las etiquetas del eje de las gráficas de productos.</summary>
    private void OnToggleAxisLabelsClick(object sender, RoutedEventArgs e)
        => _viewModel.SharedDataService.ShowChartAxisLabels = !_viewModel.SharedDataService.ShowChartAxisLabels;

    /// <summary>Alterna si el precio de los productos incluye los gastos de envío (afecta a toda la app).</summary>
    private void OnToggleIncludeShippingClick(object sender, RoutedEventArgs e)
        => _viewModel.SharedDataService.IncludeShippingInPrice = !_viewModel.SharedDataService.IncludeShippingInPrice;

    /// <summary>Alterna si los productos comprados se muestran en la lista.</summary>
    private void OnTogglePurchasedClick(object sender, RoutedEventArgs e)
        => _viewModel.SharedDataService.ShowPurchased = !_viewModel.SharedDataService.ShowPurchased;

    /// <summary>Muestra la notificación de Windows del resumen del refresco (líneas + imagen ya compuestas por el scheduler).</summary>
    private void OnSchedulerNotification(string title, System.Collections.Generic.IReadOnlyList<string> lines, string? imageUri)
        => DispatcherQueue.TryEnqueue(() =>
            _ = App.GetService<NotificationService>().ShowAsync(title, lines, L(MM4LB.Helpers.LocKeys.Notify_Open_Label), imageUri));

    /// <summary>Cambió un ajuste compartido: si se alternaron los tooltips, refresca el del botón de Amazon (fijado por código).</summary>
    private void OnSharedDataChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MM4LB.Services.SharedDataService.HelpTooltipsEnabled))
            DispatcherQueue.TryEnqueue(async () => await RefreshAmazonAuthButtonAsync());
    }

    /// <summary>Resuelve un brush de los recursos de la aplicación (los genera ThemeService); null si no existe.</summary>
    private static Microsoft.UI.Xaml.Media.Brush? ResolveBrush(string key)
        => Application.Current.Resources.TryGetValue(key, out object? value) ? value as Microsoft.UI.Xaml.Media.Brush : null;

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => MM4LB.Services.LocalizationService.Instance?[key] ?? key;
    #endregion

}