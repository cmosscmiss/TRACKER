using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using MM4LB.Contracts.Services;
using MM4LB.Controls.ViewModels;
using MM4LB.Controls.Views;
using MM4LB.Helpers;
using MM4LB.Models;
using MM4LB.Services;
using MM4LB.ViewModels;
using MM4LB.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MM4LB;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private AppSettings? _appSettings { get; set; }
    
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

            // Services
            services.AddSingleton<WindowService>();
            services.AddSingleton<PersistAndRestoreService>();
            services.AddSingleton<ProductDatabaseService>();
            services.AddSingleton<ProductParsingService>();
            services.AddSingleton<ProductService>();
            services.AddSingleton<PriceSchedulerService>();
            services.AddSingleton<ExceptionService>();
            services.AddSingleton<ExceptionDialogService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<SharedDataService>();
            services.AddSingleton<LocalizationService>();
            services.AddSingleton<ProgressService>();
            services.AddSingleton<DialogsService>();

            // ViewModels (Windows)
            services.AddSingleton<MainWindowViewModel>();

            // ViewModels (Controls)
            services.AddSingleton<ProductListViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<ProductListViewModel>());
            services.AddSingleton<PriceChartViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<PriceChartViewModel>());
            services.AddSingleton<ProductsOverviewViewModel>();
            services.AddSingleton<IWidgetViewModelBase>(sp => sp.GetRequiredService<ProductsOverviewViewModel>());
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
        // and routed to the exception log file (%LocalAppData%\MM4LB\MM4LB.log).
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

        // Await host start before creating or showing any UI. The loading window will be
        // created only after the host has finished starting.
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

        // Resolve application settings configured in DI and store them in a property
        _appSettings = Host.Services.GetRequiredService<IOptions<AppSettings>>().Value;

        // Show the main window directly (no loading/splash window). Al cerrarla, se detiene y libera el host
        // (persistiendo la configuración) y se termina el proceso.
        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        mainWindow.Closed += async (_, _) =>
        {
            try { await Host.StopAsync(); } catch (Exception ex) { ExceptionService.LogToFile(ex, "Error stopping the host on shutdown (settings may not have been saved)."); }
            try { Host.Dispose(); } catch (Exception ex) { ExceptionService.LogToFile(ex, "Error disposing the host on shutdown."); }
            Environment.Exit(0);
        };
        _window = mainWindow;
        mainWindow.Activate();
    }
}