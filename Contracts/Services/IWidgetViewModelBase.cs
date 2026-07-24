using MM4LB.Models;

namespace MM4LB.Contracts.Services;

/// <summary>
/// Defines the contract for ViewModels that support loading and saving
/// user-specific configuration.
///
/// This interface is used by <see cref="ApplicationHostService"/> to:
/// - Load configuration during application startup.
/// - Save configuration before shutdown.
///
/// Implementations should:
/// - Read their configuration from the injected <see cref="AppSettings"/> instance.
/// - Write their configuration back into <see cref="AppSettings"/>.
/// - Avoid performing any persistence or I/O directly; that is handled externally
///   by <see cref="PersistAndRestoreService"/>.
/// </summary>
public interface IWidgetViewModelBase
{
    /// <summary>
    /// Loads the ViewModel's configuration from the application's settings.
    ///
    /// Called once during application startup.
    /// Implementations should read only their own configuration section and
    /// apply values to ViewModel properties.
    /// </summary>
    void LoadConfig();

    /// <summary>
    /// Saves the ViewModel's configuration back into the application's settings.
    ///
    /// Called before the application shuts down.
    /// Implementations should write only their own configuration section.
    /// Persistence to disk is handled externally.
    /// </summary>
    void SaveConfig();
}
