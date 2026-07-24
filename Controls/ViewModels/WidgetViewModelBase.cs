using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Contracts.Services;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Base class for ViewModels that load and persist user‑configurable settings.
///
/// This class establishes a consistent pattern for configuration-driven ViewModels:
/// - Provides access to the shared <see cref="AppSettings"/> instance injected via DI.
/// - Defines a clear contract for loading and saving configuration sections.
/// - Ensures ViewModels remain focused on UI state, while persistence is delegated to host services.
///
/// Responsibilities of derived ViewModels:
/// - Read their configuration values from <see cref="_appSettings"/> inside <see cref="LoadConfig"/>.
/// - Write updated values back into <see cref="_appSettings"/> inside <see cref="SaveConfig"/>.
/// - Never perform file I/O or serialization; persistence is handled externally by <see cref="PersistAndRestoreService"/>.
/// </summary>
public abstract class WidgetViewModelBase : ObservableObject, IWidgetViewModelBase
{
    #region Attributes
    protected readonly SharedDataService _sharedDataService;
    protected readonly AppSettings _appSettings;

    protected int _slotIndex = -1;
    #endregion

    #region Properties
    public SharedDataService SharedDataService => _sharedDataService;

    public int SlotIndex
    {
        get => _slotIndex;
        set => SetProperty(ref _slotIndex, value);
    }
    #endregion

    #region Constructor
    /// <summary>
    /// Creates a new configurable ViewModel.
    ///
    /// The <see cref="AppSettings"/> instance is resolved through dependency injection
    /// and shared across the entire application. Each ViewModel interacts only with
    /// its own configuration section, keeping concerns well isolated.
    /// </summary>
    /// <param name="appSettings">
    /// The options wrapper providing the application's settings.
    /// Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="appSettings"/> is null.
    /// </exception>
    protected WidgetViewModelBase(SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
    {
        _sharedDataService = sharedDataService;
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Releases resources held by the ViewModel, primarily by unsubscribing from
    /// events, detaching handlers, and disposing of transient objects.
    ///
    /// This method is invoked automatically during application shutdown by the
    /// <see cref="ApplicationHostService"/>. Its purpose is to prevent memory leaks
    /// and avoid ViewModels continuing to receive events after the UI has been
    /// torn down.
    ///
    /// Derived ViewModels should:
    /// - Unsubscribe from all events they previously subscribed to
    ///   (e.g., <see cref="SharedDataService"/> events, model events, timers).
    /// - Dispose any <see cref="IDisposable"/> fields they own.
    /// - Cancel any outstanding asynchronous operations or tokens.
    /// - Avoid performing persistence or I/O; configuration saving is handled
    ///   separately via <see cref="SaveConfig"/>.
    ///
    /// Implementations must be idempotent: calling <c>Dispose()</c> multiple times
    /// should not throw exceptions or attempt to unsubscribe twice.
    /// </summary>
    public abstract void Dispose();

    /// <summary>
    /// Loads the ViewModel's configuration from <see cref="_appSettings"/>.
    ///
    /// Called automatically by <see cref="ApplicationHostService"/> during application startup.
    /// Implementations should:
    /// - Read only the configuration values relevant to this ViewModel.
    /// - Apply those values to ViewModel properties.
    /// - Avoid any persistence or I/O operations.
    /// </summary>
    public abstract void LoadConfig();

    /// <summary>
    /// Saves the ViewModel's configuration back into <see cref="_appSettings"/>.
    ///
    /// Called automatically by <see cref="ApplicationHostService"/> before application shutdown.
    /// Implementations should:
    /// - Write only the settings owned by this ViewModel.
    /// - Avoid performing persistence; saving to disk is handled by the host service.
    /// </summary>
    public abstract void SaveConfig();
    #endregion
}
