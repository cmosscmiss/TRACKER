using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Service to keep a snapshot of the data structures and status being used by the different components.
/// </summary>
public class SharedDataService : ObservableObject
{
    #region Subclasses
    public class PlatformChangedEventArgs : EventArgs
    {
        public Platform? OldPlatform { get; }
        public Platform? NewPlatform { get; }
        public PlatformChangedEventArgs(Platform? oldPlatform, Platform? newPlatform)
        {
            OldPlatform = oldPlatform;
            NewPlatform = newPlatform;
        }
    }

    public class GameChangedEventArgs : EventArgs
    {
        public Game? OldGame { get; }
        public Game? NewGame { get; }
        public GameChangedEventArgs(Game? oldGame, Game? newGame)
        {
            OldGame = oldGame;
            NewGame = newGame;
        }
    }
    #endregion

    #region Attributes
    private readonly AppSettings _appSettings;
    private bool _isUiEnabled;
    private Game? _selectedGame;
    private Platform? _selectedPlatform;
    #endregion

    #region Properties
    public ObservableCollection<Game> GamesFiltered { get; protected set; } = new();

    public bool IsUIEnabled
    {
        get => _isUiEnabled;
        set => SetProperty(ref _isUiEnabled, value);
    }

    /// <summary>
    /// Ayuda (tooltips + paneles de ayuda) activada. Espeja <see cref="AppSettings.GeneralSettings.HelpTooltipsEnabled"/>
    /// (la fuente persistida): el valor vivo ES el del setting, así que se guarda con el resto de la config al cerrar.
    /// Lo alterna en caliente el botón de ayuda del footer; lo consumen la attached property <c>Help.Key</c> (tooltips)
    /// y <c>Help.AffordanceVisible</c> (visibilidad de los iconos de ayuda), que se suscriben a este cambio.
    /// </summary>
    public bool HelpTooltipsEnabled
    {
        get => _appSettings.General.HelpTooltipsEnabled;
        set
        {
            if (_appSettings.General.HelpTooltipsEnabled != value)
            {
                _appSettings.General.HelpTooltipsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public PlatformSet? PlatformSet { get; set; }

    /// <summary>
    /// Selected game of the selected platform.
    /// </summary>
    public Game? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!ReferenceEquals(_selectedGame, value))
            {
                var oldValue = _selectedGame;
                var newValue = value;
                SetProperty(ref _selectedGame, newValue);
                SelectedGameChanged?.Invoke(this, new GameChangedEventArgs(oldValue, newValue));
            }
        }
    }

    /// <summary>
    /// Selected platform.
    /// </summary>
    public Platform? SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            if (!ReferenceEquals(_selectedPlatform, value))
            {
                var oldValue = _selectedPlatform;
                var newValue = value;
                SetProperty(ref _selectedPlatform, value);
                SelectedPlatformChanged?.Invoke(this, new PlatformChangedEventArgs(oldValue, newValue));
            }
        }
    }
    #endregion

    #region Constructors
    public SharedDataService(IOptions<AppSettings> appSettings)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
    }
    #endregion

    #region
    public void NotifyInitialState()
    {
        SelectedPlatformChanged?.Invoke(this, new PlatformChangedEventArgs(null, SelectedPlatform));
        SelectedGameChanged?.Invoke(this, new GameChangedEventArgs(null, SelectedGame));
    }
    #endregion

    #region Events
    public event EventHandler<PlatformChangedEventArgs>? SelectedPlatformChanged;
    public event EventHandler<GameChangedEventArgs>? SelectedGameChanged;

    /// <summary>
    /// Se dispara cuando cambia el ajuste global de visibilidad de la cabecera de los widgets
    /// (<see cref="Models.AppSettings.GeneralSettings.ShowWidgetHeader"/>), para que los widgets lo apliquen en
    /// caliente. Lo emite la ventana de configuración al aceptar; lo escuchan los <c>WidgetBaseControl</c>.
    /// </summary>
    public event EventHandler? WidgetHeaderVisibilityChanged;

    /// <summary>
    /// Se dispara cuando cambia el modo de grupos de las toolbars
    /// (<see cref="Models.AppSettings.GeneralSettings.ToolbarGroupsDisplayMode"/>), para que las toolbars con grupos
    /// excluyentes se reconstruyan en caliente. Lo emite la ventana de configuración al aceptar.
    /// </summary>
    public event EventHandler? ToolbarGroupsDisplayModeChanged;
    #endregion

    #region Methods (public)
    /// <summary>Notifica que la visibilidad de la cabecera de los widgets cambió, para aplicarla en caliente.</summary>
    public void NotifyWidgetHeaderVisibilityChanged()
    {
        WidgetHeaderVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifica que el modo de grupos de las toolbars cambió, para reconstruir las toolbars con grupos excluyentes.</summary>
    public void NotifyToolbarGroupsDisplayModeChanged()
    {
        ToolbarGroupsDisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }
    #endregion
}
