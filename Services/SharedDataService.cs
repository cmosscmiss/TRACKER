using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>Qué widget de dashboard está visible/activo (mutuamente excluyentes en el panel).</summary>
public enum DashboardMode
{
    /// <summary>Ningún dashboard colocado en un slot.</summary>
    None,

    /// <summary>El GameImagesDashboard estándar (todas las imágenes juntas).</summary>
    Standard,

    /// <summary>El GameImagesRegionDashboard (imágenes por región).</summary>
    Region,
}

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

    public class ImageSetChangedEventArgs : EventArgs
    {
        public PlatformImageSet? OldImageSet { get; }
        public PlatformImageSet? NewImageSet { get; }

        public ImageSetChangedEventArgs(PlatformImageSet? oldImageSet, PlatformImageSet? newImageSet)
        {
            OldImageSet = oldImageSet;
            NewImageSet = newImageSet;
        }
    }

    public class GameImageChangedEventArgs : EventArgs
    {
        public GameImage? OldImage { get; }
        public GameImage? NewImage { get; }

        public GameImageChangedEventArgs(GameImage? oldImage, GameImage? newImage)
        {
            OldImage = oldImage;
            NewImage = newImage;
        }
    }

    public class GameImagesChangedEventArgs : EventArgs
    {
        public Game? Game { get; }
        public PlatformImageSet? ImageSet { get; }

        public GameImagesChangedEventArgs(Game? game, PlatformImageSet? imageSet)
        {
            Game = game;
            ImageSet = imageSet;
        }
    }
    #endregion

    #region Attributes
    private readonly AppSettings _appSettings;
    private bool _isUiEnabled;
    private Game? _selectedGame;
    private GameImage? _selectedImage;
    private Platform? _selectedPlatform;
    private PlatformImageSet? _selectedImageSet;
    private ImageRegion? _selectedRegion;
    #endregion

    #region Properties
    public ObservableCollection<Game> GamesFiltered { get; protected set; } = new();
    public ObservableCollection<GameImage> GameImages { get; protected set; } = new();
    public ObservableCollection<PlatformImageSet> ImageTypesFiltered { get; protected set; } = new();

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
    /// Selected image of the selected game.
    /// </summary>
    public GameImage? SelectedImage
    {
        get => _selectedImage;
        set
        {
            if (!ReferenceEquals(_selectedImage, value))
            {
                var oldValue = _selectedImage;
                var newValue = value;
                SetProperty(ref _selectedImage, newValue);
                SelectedImageChanged?.Invoke(this, new GameImageChangedEventArgs(oldValue, newValue));
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

    public PlatformImageSet? SelectedImageSet
    {
        get => _selectedImageSet;
        set
        {
            if (!ReferenceEquals(_selectedImageSet, value))
            {
                var oldValue = _selectedImageSet;
                var newValue = value;
                SetProperty(ref _selectedImageSet, newValue);
                SelectedPlatform?.SetSelectedImageSet(newValue?.Type.Value);
                SelectedImageSetChanged?.Invoke(this, new ImageSetChangedEventArgs(oldValue, newValue));
            }
        }
    }

    /// <summary>
    /// Región activa de destino para los nuevos medios (import/descarga), fijada por el GameImagesRegionDashboard
    /// según su bucket seleccionado: una <see cref="ImageRegion"/> favorita, o <c>null</c> para la raíz del set
    /// ("sin región"; también cuando el bucket activo es "otras regiones"). La consultan las rutas de alta de media
    /// (drag&amp;drop del dashboard de regiones y descarga del WebView) para colocar el fichero en la subcarpeta
    /// correcta. <c>null</c> por defecto = comportamiento clásico (raíz del set).
    /// </summary>
    public ImageRegion? SelectedRegion
    {
        get => _selectedRegion;
        set => SetProperty(ref _selectedRegion, value);
    }

    private DashboardMode _activeDashboardMode = DashboardMode.None;
    /// <summary>
    /// Dashboard actualmente visible (los dos dashboards son mutuamente excluyentes en el panel). Lo fija el
    /// coordinador de <c>MainWindowViewModel</c> al cambiar los slots. Lo consulta el ImportCollection para
    /// habilitar el import y decidir si pide región de destino.
    /// </summary>
    public DashboardMode ActiveDashboardMode
    {
        get => _activeDashboardMode;
        set => SetProperty(ref _activeDashboardMode, value);
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
        SelectedImageSetChanged?.Invoke(this, new ImageSetChangedEventArgs(null, SelectedImageSet));
        SelectedGameChanged?.Invoke(this, new GameChangedEventArgs(null, SelectedGame));
    }
    #endregion

    #region Events
    public event EventHandler<PlatformChangedEventArgs>? SelectedPlatformChanged;
    public event EventHandler<GameChangedEventArgs>? SelectedGameChanged;
    public event EventHandler<GameImageChangedEventArgs>? SelectedImageChanged;
    public event EventHandler<ImageSetChangedEventArgs>? SelectedImageSetChanged;
    public event EventHandler<GameImagesChangedEventArgs>? SelectedGameImagesChanged;

    /// <summary>
    /// Se dispara cuando cambia el ajuste global de visibilidad de la cabecera de los widgets
    /// (<see cref="Models.AppSettings.GeneralSettings.ShowWidgetHeader"/>), para que los widgets lo apliquen en
    /// caliente. Lo emite la ventana de configuración al aceptar; lo escuchan los <c>WidgetBaseControl</c>.
    /// </summary>
    public event EventHandler? WidgetHeaderVisibilityChanged;

    /// <summary>
    /// Se dispara cuando cambian las regiones favoritas (<see cref="Models.AppSettings"/>), para que el
    /// GameImagesRegionDashboard reconstruya su selector de buckets en caliente. Lo emite la ventana de configuración
    /// al aceptar.
    /// </summary>
    public event EventHandler? FavouriteRegionsChanged;

    /// <summary>
    /// Se dispara cuando cambian los tipos de media favoritos
    /// (<see cref="Models.AppSettings.ImageTypeControlSettings.FavouriteImageTypes"/>), para que la banda de tipos
    /// (<c>ImageTypeControl</c>) reconstruya sus botones favoritos en caliente. Lo emite la ventana de configuración.
    /// </summary>
    public event EventHandler? FavouriteMediaTypesChanged;

    /// <summary>
    /// Se dispara cuando cambia el modo de grupos de las toolbars
    /// (<see cref="Models.AppSettings.GeneralSettings.ToolbarGroupsDisplayMode"/>), para que los
    /// <c>ExclusiveOptionsControl</c> se reconstruyan en caliente. Lo emite la ventana de configuración al aceptar.
    /// </summary>
    public event EventHandler? ToolbarGroupsDisplayModeChanged;

    /// <summary>
    /// Se dispara cuando se recarga por completo la configuración (al cargar un TEMPLATE), para que los suscriptores
    /// que cachean estado de AppSettings se realineen: <c>MainWindowViewModel</c> re-aplica el layout de widgets y
    /// <c>ConsoleViewModel</c> re-evalúa el visor del pie, etc. Lo emite el <c>TemplateService</c>.
    /// </summary>
    public event EventHandler? SettingsReloaded;

    /// <summary>
    /// Se dispara ANTES de serializar la configuración para GRABAR un template, para que los ViewModels vuelquen su
    /// estado en vivo (layout, slots de widgets, toggles) a <see cref="AppSettings"/> —que normalmente solo se hace al
    /// cerrar la app—. Sin esto, el template capturaría el estado de arranque, no el actual. Lo emite el TemplateService.
    /// </summary>
    public event EventHandler? SaveConfigRequested;
    #endregion

    #region Methods (public)
    /// <summary>
    /// Notifies listeners that the image collection of the selected game may have changed.
    /// This is typically called after matching the selected image set with the games.
    /// </summary>
    public void NotifySelectedGameImagesChanged()
    {
        SelectedGameImagesChanged?.Invoke(this, new GameImagesChangedEventArgs(SelectedGame, SelectedImageSet));
    }

    /// <summary>Notifica que la visibilidad de la cabecera de los widgets cambió, para aplicarla en caliente.</summary>
    public void NotifyWidgetHeaderVisibilityChanged()
    {
        WidgetHeaderVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifica que las regiones favoritas cambiaron, para que el dashboard de regiones se realinee.</summary>
    public void NotifyFavouriteRegionsChanged()
    {
        FavouriteRegionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifica que los tipos de media favoritos cambiaron, para que la banda de tipos se realinee.</summary>
    public void NotifyFavouriteMediaTypesChanged()
    {
        FavouriteMediaTypesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifica que el modo de grupos de las toolbars cambió, para reconstruir los ExclusiveOptionsControl.</summary>
    public void NotifyToolbarGroupsDisplayModeChanged()
    {
        ToolbarGroupsDisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifica que se recargó toda la configuración (carga de template), para realinear a los suscriptores.</summary>
    public void NotifySettingsReloaded()
    {
        SettingsReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pide a los ViewModels que vuelquen su estado en vivo a AppSettings (antes de grabar un template).</summary>
    public void NotifySaveConfigRequested()
    {
        SaveConfigRequested?.Invoke(this, EventArgs.Empty);
    }
    #endregion
}
