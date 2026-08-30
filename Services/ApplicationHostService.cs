using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Tracker.Contracts.Services;
using Tracker.Controls.ViewModels;
using Tracker.Models;

namespace Tracker.Services;

/// <summary>
/// Hosted service executed automatically by the .NET Generic Host
/// during application startup and shutdown.
///
/// This service is responsible for:
/// - Initializing infrastructure services before the UI becomes active
/// - Restoring persisted data
/// - Initializing LaunchBox data (platforms, games, folders)
/// - Wiring ProgressService to the ConsoleViewModel
///
/// It runs BEFORE MainWindow is activated, making it the correct place
/// to perform initialization that must happen once and early.
/// </summary>
public sealed class ApplicationHostService : IHostedService
{
    #region Attributes
    private readonly PersistAndRestoreService _persistAndRestoreService;
    private readonly ProductDatabaseService _productDatabaseService;
    private readonly SharedDataService _sharedDataService;
    private readonly ProgressService _progressService;
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localizationService;
    private readonly AppSettings _appSettings;

    private bool _isInitialized;
    #endregion

    #region Constructor
    public ApplicationHostService(ConsoleViewModel consoleViewModel, PersistAndRestoreService persistAndRestoreService, ProductDatabaseService productDatabaseService, SharedDataService sharedDataService, ProgressService progressService, ThemeService themeService, LocalizationService localizationService, IOptions<AppSettings> settings)
    {
        _persistAndRestoreService = persistAndRestoreService;
        _productDatabaseService = productDatabaseService;
        _sharedDataService = sharedDataService;
        _progressService = progressService;
        _themeService = themeService;
        _localizationService = localizationService;
        _appSettings = settings.Value;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Called automatically by the Generic Host BEFORE the main window is shown.
    ///
    /// Responsibilities:
    /// - Ensure initialization runs only once
    /// - Connect ProgressService to the ConsoleViewModel (UI output)
    /// - Restore persisted application data
    /// - Initialize LaunchBox data (platforms, games, folder definitions)
    ///
    /// This method is the correct place for initialization because:
    /// - All services have already been constructed by DI
    /// - The UI has not yet been activated
    /// - It avoids mixing initialization logic inside App.xaml.cs or LaunchBoxService
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized) return;

        _persistAndRestoreService.RestoreData();

        // Base de datos de productos: se crea en el primer arranque (%LocalAppData%\Tracker\tracker.db) y se cargan
        // los productos persistidos (con sus tiendas e histórico) en el ProductSet compartido antes de mostrar la UI.
        _productDatabaseService.Initialize();
        _productDatabaseService.LoadInto(_sharedDataService.ProductSet);
        _sharedDataService.ProductSet.SortByName();   // lista en orden alfabético desde el arranque

        // Re-selecciona el producto que estaba seleccionado al cerrar (por Id); si no se encuentra (o no había),
        // cae al primero de la lista.
        var products = _sharedDataService.ProductSet.Products;
        if (products.Count > 0)
        {
            long savedId = _appSettings.ProductListControl.SelectedProductId;
            Product? toSelect = savedId != 0 ? products.FirstOrDefault(product => product.Id == savedId) : null;
            _sharedDataService.SelectedProduct = toSelect ?? products[0];
        }

        // Aplica la preferencia de logging de excepciones ya cargada (por defecto activado). Antes de este punto
        // el logging está activo para capturar también fallos tempranos del arranque.
        ExceptionService.LoggingEnabled = _appSettings.General.ExceptionLoggingEnabled;

        // Arranque automático con Windows: sincroniza la clave Run del usuario con el ajuste (por defecto activado),
        // de modo que también se corrige sola si el ejecutable ha cambiado de carpeta.
        StartupService.Apply(_appSettings.General.StartWithWindows);

        // Localización (i18n): aplica el idioma guardado y publica el servicio como recurso de app ("Loc") para poder
        // enlazar textos con {Binding Source={StaticResource Loc}} (además de la markup extension {loc:Str}). Debe
        // ocurrir antes de mostrar cualquier ventana. El validador de claves solo actúa en DEBUG.
        _localizationService.SetLanguage(_appSettings.General.Language);
        if (Application.Current is Application app)
            app.Resources["Loc"] = _localizationService;
        LocalizationValidator.Validate();

        await _themeService.InitializeAsync();

        // Primer evento del ACTIVITY LOG: la app se ha cargado.
        _progressService.LogEvent(_localizationService[Tracker.Helpers.LocKeys.AppLog_Loaded_Progress]);

        _isInitialized = true;
    }

    /// <summary>
    /// Called automatically by the Generic Host BEFORE the application closes.
    ///
    /// Responsibilities:
    /// - Persist application state (window size, last selections, etc.)
    ///
    /// This ensures the next session can restore the previous state.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        var _configurableViewModels = App.GetService<IEnumerable<IWidgetViewModelBase>>();
        foreach (var viewModel in _configurableViewModels)
            viewModel.SaveConfig();
        _persistAndRestoreService.PersistData();
        return Task.CompletedTask;
    }
    #endregion
}