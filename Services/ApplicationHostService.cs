using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using MM4LB.Contracts.Services;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;

namespace MM4LB.Services;

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
    private readonly ThemeService _themeService;
    private readonly BackupService _backupService;
    private readonly LocalizationService _localizationService;
    private readonly AppSettings _appSettings;

    private bool _isInitialized;
    #endregion

    #region Constructor
    public ApplicationHostService(ConsoleViewModel consoleViewModel, PersistAndRestoreService persistAndRestoreService, ThemeService themeService, BackupService backupService, LocalizationService localizationService, IOptions<AppSettings> settings)
    {
        _persistAndRestoreService = persistAndRestoreService;
        _themeService = themeService;
        _backupService = backupService;
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

        // Aplica la preferencia de logging de excepciones ya cargada (por defecto activado). Antes de este punto
        // el logging está activo para capturar también fallos tempranos del arranque.
        ExceptionService.LoggingEnabled = _appSettings.General.ExceptionLoggingEnabled;

        // Localización (i18n): aplica el idioma guardado y publica el servicio como recurso de app ("Loc") para poder
        // enlazar textos con {Binding Source={StaticResource Loc}} (además de la markup extension {loc:Str}). Debe
        // ocurrir antes de mostrar cualquier ventana. El validador de claves solo actúa en DEBUG.
        _localizationService.SetLanguage(_appSettings.General.Language);
        if (Application.Current is Application app)
            app.Resources["Loc"] = _localizationService;
        LocalizationValidator.Validate();

        await _themeService.InitializeAsync();

        // Lee el estado inicial de la carpeta de backup (nº de imágenes + tamaño) antes de que la UI se monte,
        // para que la pastilla del ACTIVITY LOG arranque con los valores correctos.
        await _backupService.RefreshAsync();

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