using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Tracker.Contracts.Services;
using Tracker.Controls.ViewModels;
using Tracker.Controls.Views;
using Tracker.Helpers;
using Tracker.Models;
using Tracker.Services;
using Tracker.ViewModels;
using Tracker.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Tracker;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private AppSettings? _appSettings { get; set; }

    /// <summary>
    /// Mutex nombrado que garantiza una única instancia de la app: se mantiene vivo durante todo el proceso (campo
    /// estático) y el SO lo libera al terminar. Si otra instancia ya lo tiene, esta no arranca (ver el constructor).
    /// </summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>Nombre del evento (por sesión) con el que una segunda instancia pide a la principal que se traiga al frente.</summary>
    private const string ActivateEventName = @"Local\Tracker.Tracker.Activate";

    /// <summary>Evento de activación de la instancia principal (lo señala una segunda instancia al arrancar). Vivo todo el proceso.</summary>
    private static EventWaitHandle? _activateEvent;

    /// <summary>
    /// Acción que restaura/trae al frente la ventana principal. La fija la ventana principal cuando está lista; la
    /// invoca el hilo que escucha <see cref="_activateEvent"/> cuando otra instancia intenta arrancar.
    /// </summary>
    public static Action? ActivationRequested { get; set; }

    public IHost Host { get; }
    
    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    private Window? _window;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        // Identidad de taskbar ESTABLE para una app unpackaged: agrupa la ventana con su pin de la barra de tareas y da
        // atribución consistente a las notificaciones. Debe fijarse lo antes posible, antes de crear cualquier ventana.
        TrySetAppUserModelId();

        // Instancia única: si ya hay otra copia de Tracker abierta, en vez de avisar y salir, se le pide que se traiga
        // al frente (como pulsar el icono de la bandeja) y esta segunda instancia termina. El mutex se crea con nombre
        // global-por-sesión ("Local\..."); createdNew es false si otra instancia ya lo tiene. Se comprueba antes de
        // construir el host o mostrar UI para no inicializar nada de un proceso que va a terminar de inmediato.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\Tracker.Tracker.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            SignalExistingInstanceAndExit();
            return;
        }

        // Instancia principal: crea el evento de activación y escucha señales de futuras segundas instancias para
        // restaurar la ventana en pantalla (la acción real la fija la ventana principal en ActivationRequested).
        StartActivationListener();

        InitializeComponent();
        Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureServices((context, services) =>
        {
            // App Host
            services.AddHostedService<ApplicationHostService>();

            // Converters
            services.AddSingleton<LogEntrySeverityToBrushConverter>();
            services.AddSingleton<PurchasedToTextBrushConverter>();

            // Services
            services.AddSingleton<WindowService>();
            services.AddSingleton<PersistAndRestoreService>();
            services.AddSingleton<ProductDatabaseService>();
            services.AddSingleton<ProductParsingService>();
            services.AddSingleton<AmazonAuthService>();
            services.AddSingleton<ProductService>();
            services.AddSingleton<PriceSchedulerService>();
            services.AddSingleton<ExceptionService>();
            services.AddSingleton<ExceptionDialogService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<SharedDataService>();
            services.AddSingleton<LocalizationService>();
            services.AddSingleton<ProgressService>();
            services.AddSingleton<DialogsService>();
            services.AddSingleton<TemplateService>();
            services.AddSingleton<NotificationService>();

            // ViewModels (Windows)
            services.AddSingleton<MainWindowViewModel>();

            // Ventana de configuración (staging): transient para releer AppSettings en cada apertura.
            services.AddTransient<SettingsDialogViewModel>();

            // ViewModels (Controls)
            services.AddSingleton<ProductListViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<ProductListViewModel>());
            services.AddSingleton<PriceChartViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<PriceChartViewModel>());
            services.AddSingleton<ProductsOverviewViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<ProductsOverviewViewModel>());
            services.AddSingleton<FavoritesViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<FavoritesViewModel>());
            services.AddSingleton<ConsoleViewModel>();
            services.AddSingleton<LayoutSelectorViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<LayoutSelectorViewModel>());
            services.AddSingleton<WebViewViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<WebViewViewModel>());

            // Windows
            services.AddSingleton<MainWindow>();

            // Configuration
            services.Configure<AppSettings>(context.Configuration.GetSection(nameof(_appSettings)));
        }).
        Build();

        // Global exception logging to diagnose silent crashes. Background-thread and unobserved-task
        // exceptions are NOT surfaced by the WinUI UnhandledException event, so all three sinks are hooked
        // and routed to the exception log file (%LocalAppData%\Tracker\Tracker.log).
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // WinUI native crashes surface as STATUS_STOWED_EXCEPTION (0xc000027b) inside Microsoft.UI.Xaml.dll
        // and fail-fast in native code WITHOUT raising any of the managed handlers above. FirstChanceException
        // fires the instant a managed exception is thrown (before XAML stows it and tears the process down),
        // so it captures the real exception + stack behind those otherwise-untraceable silent closes. It is
        // noisy (fires for handled exceptions too): the last entries before a close are the relevant ones.
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <summary>
    /// Logs exceptions raised on the UI thread that were not handled by the application.
    /// </summary>
    private void App_UnhandledException(object? sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        ExceptionService.LogToFile(e.Exception, $"UI UnhandledException: {e.Message}");
    }

    /// <summary>
    /// Logs exceptions raised on any thread (including background threads) that reach the AppDomain. These
    /// commonly terminate the process silently, which is exactly what we want to capture here.
    /// </summary>
    private void OnDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        ExceptionService.LogToFile(e.ExceptionObject as Exception, $"AppDomain UnhandledException (IsTerminating={e.IsTerminating})");
    }

    /// <summary>
    /// Logs exceptions from Tasks that were never awaited/observed and marks them observed so they do not
    /// escalate and tear down the process.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ExceptionService.LogToFile(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }

    /// <summary>
    /// Reentrancy guard so logging a first-chance exception (which itself may throw, e.g. a file IO error)
    /// does not recurse infinitely through this handler.
    /// </summary>
    [ThreadStatic]
    private static bool _loggingFirstChance;

    /// <summary>
    /// Throttle del logging first-chance: solo las primeras <see cref="MaxFirstChanceLogsPerSignature"/>
    /// ocurrencias de cada firma (<c>tipo|mensaje</c>) llegan a disco. El handler se dispara en CADA excepción
    /// managed (incluidas las usadas para control de flujo), y sin esto cada una paga <c>ToString()</c> del
    /// stack completo + I/O síncrona. La primera ocurrencia de cada firma SIEMPRE se loguea, así que se conserva
    /// la captura de excepciones novedosas (el motivo del handler: diagnosticar fail-fasts nativos). El write se
    /// mantiene síncrono a propósito: es lo que garantiza que el log sobreviva a un fail-fast que termina el
    /// proceso al instante. La firma es <c>tipo|mensaje</c> (no <c>tipo|stack</c>) para no formatear el stack en
    /// cada first-chance, justo el coste que se quiere evitar.
    /// </summary>
    private static readonly object _firstChanceThrottleLock = new();
    private static readonly Dictionary<string, int> _firstChanceCounts = new();
    private const int MaxFirstChanceLogsPerSignature = 3;
    private const int MaxFirstChanceSignatures = 500;

    /// <summary>
    /// Logs every managed exception the moment it is thrown, before it is caught or stowed. This is what
    /// captures the cause of native WinUI fail-fasts (0xc000027b) that bypass the other handlers. Silencia la
    /// first-chance BENIGNA de Microsoft.Data.Sqlite sondeando ApplicationData.Current (ver el cuerpo).
    /// </summary>
    private void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        if (_loggingFirstChance) { return; }

        _loggingFirstChance = true;
        try
        {
            Exception ex = e.Exception;
            if (ex is null) { return; }

            // Silenciar la first-chance BENIGNA de Microsoft.Data.Sqlite: su constructor estático sondea por
            // reflexión Windows.Storage.ApplicationData.Current, que en una app WinUI 3 SIN empaquetar lanza
            // siempre; Sqlite lo captura y sigue. No es un error, solo ensuciaba el log en cada arranque. Se
            // lanza DOS veces: la InvalidOperationException interna (que en el primer throw aún no trae
            // "ApplicationData" en su propio stack, pero sí en el stack VIVO) y su envoltura
            // TargetInvocationException (que sí lo trae en su ToString). Se acota a esos dos tipos para no
            // capturar el stack vivo en balde y para no silenciar por error otras excepciones.
            if ((ex is InvalidOperationException or System.Reflection.TargetInvocationException)
                && (ex.ToString().Contains("ApplicationData") || Environment.StackTrace.Contains("ApplicationData")))
            {
                return;
            }

            // Throttle antes de formatear/escribir: las excepciones repetidas (típicas del control de flujo) no
            // vuelven a pagar ToString() + I/O una vez alcanzado el tope por firma.
            if (!ShouldLogFirstChance(ex)) { return; }

            ExceptionService.LogToFile(ex, "FirstChanceException");
        }
        finally
        {
            _loggingFirstChance = false;
        }
    }

    /// <summary>
    /// Decide si esta excepción first-chance debe loguearse: cierto para las primeras
    /// <see cref="MaxFirstChanceLogsPerSignature"/> ocurrencias de cada firma (<c>tipo|mensaje</c>), falso
    /// después. El conteo se reinicia al superar <see cref="MaxFirstChanceSignatures"/> firmas distintas, para
    /// no crecer sin límite en sesiones largas. No lanza (dict + concatenación de strings).
    /// </summary>
    private static bool ShouldLogFirstChance(Exception ex)
    {
        string signature = ex.GetType().FullName + "|" + ex.Message;
        lock (_firstChanceThrottleLock)
        {
            if (_firstChanceCounts.Count >= MaxFirstChanceSignatures) { _firstChanceCounts.Clear(); }

            int count = _firstChanceCounts.TryGetValue(signature, out int c) ? c : 0;
            _firstChanceCounts[signature] = count + 1;
            return count < MaxFirstChanceLogsPerSignature;
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        // Initialise the global error dialog service, so it's ready to be used by any component that needs to show an error dialog. This is needed to ensure that the service is initialized before any unhandled exception occurs and needs to show a dialog.
        var exceptionService = Host.Services.GetRequiredService<ExceptionService>();
        var exceptionDialogService = Host.Services.GetRequiredService<ExceptionDialogService>();
        var themeService = Host.Services.GetRequiredService<ThemeService>();

        // Await host start before creating or showing any UI.
        try
        {
            await Host.StartAsync();
        }
        catch (Exception ex)
        {
            exceptionService.Handle(ex, "There was an error starting the application.");
        }

        // Prepare and add the color converters to resources, so they can be used in XAML and can use the Theme colors.
        var logEntrySeverityToBrushConverter = Host.Services.GetRequiredService<LogEntrySeverityToBrushConverter>();
        logEntrySeverityToBrushConverter.ThemeService = themeService;
        Application.Current.Resources["LogEntrySeverityConverter"] = logEntrySeverityToBrushConverter;

        var purchasedToTextBrushConverter = Host.Services.GetRequiredService<PurchasedToTextBrushConverter>();
        purchasedToTextBrushConverter.ThemeService = themeService;
        Application.Current.Resources["PurchasedTextBrushConverter"] = purchasedToTextBrushConverter;

        // Resolve application settings configured in DI and store them in a property
        _appSettings = Host.Services.GetRequiredService<IOptions<AppSettings>>().Value;

        // Notificaciones de Windows (AppNotificationManager): registra y, al pulsar una notificación, trae la app al frente.
        NotificationService notifications = Host.Services.GetRequiredService<NotificationService>();
        notifications.Activated += () => ActivationRequested?.Invoke();
        notifications.Register();

        // Show the main window directly (no loading/splash window). Al cerrarla, se detiene y libera el host
        // (persistiendo la configuración) y se termina el proceso.
        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        mainWindow.Closed += async (_, _) =>
        {
            notifications.Unregister();
            try { await Host.StopAsync(); } catch (Exception ex) { ExceptionService.LogToFile(ex, "Error stopping the host on shutdown (settings may not have been saved)."); }
            try { Host.Dispose(); } catch (Exception ex) { ExceptionService.LogToFile(ex, "Error disposing the host on shutdown."); }
            Environment.Exit(0);
        };
        _window = mainWindow;
        mainWindow.Activate();
    }

    /// <summary>
    /// Instancia principal: crea el evento de activación y lanza un hilo en segundo plano que espera señales de futuras
    /// segundas instancias; al recibir una, invoca <see cref="ActivationRequested"/> (que la ventana restaura en pantalla).
    /// </summary>
    private static void StartActivationListener()
    {
        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        }
        catch
        {
            return;   // sin evento, simplemente no habrá activación remota (no es crítico)
        }

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (!_activateEvent.WaitOne())
                        break;
                }
                catch
                {
                    break;
                }
                ActivationRequested?.Invoke();
            }
        })
        {
            IsBackground = true,
            Name = "TrackerActivationListener",
        };
        thread.Start();
    }

    /// <summary>
    /// Segunda instancia: concede permiso de primer plano y señala a la instancia principal para que se traiga a
    /// pantalla; luego termina. Si no puede señalar (no existe el evento), cae al aviso clásico.
    /// </summary>
    private static void SignalExistingInstanceAndExit()
    {
        try
        {
            // Permite que la instancia principal pueda llamar a SetForegroundWindow aunque no esté en primer plano.
            AllowSetForegroundWindow(ASFW_ANY);
            using EventWaitHandle activate = EventWaitHandle.OpenExisting(ActivateEventName);
            activate.Set();
        }
        catch
        {
            // MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND. La localización aún no está disponible aquí (host no construido).
            MessageBox(IntPtr.Zero, "Tracker is already running.", "Tracker", 0x00000040 | 0x00010000);
        }

        Environment.Exit(0);
    }

    /// <summary>MessageBox nativo (user32) para avisar de la instancia duplicada antes de que exista UI de la app.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Concede a otros procesos permiso para llevar su ventana al primer plano (ASFW_ANY = -1).</summary>
    private const int ASFW_ANY = -1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>Identidad de taskbar/notificaciones de la app (estable y única entre versiones).</summary>
    private const string AppUserModelId = "Tracker.Tracker";

    /// <summary>Fija el AppUserModelID del proceso (identidad de taskbar). No crítico: si falla, solo se pierde el agrupado/atribución consistentes.</summary>
    private static void TrySetAppUserModelId()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch
        {
            // Best-effort: no debe impedir el arranque.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);
}