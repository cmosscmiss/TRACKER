using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Helpers;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.ViewModels;

/// <summary>
/// ViewModel del contenido de configuración de la app (mostrado en un <see cref="Controls.Dialogs.AppDialog"/> sobre
/// el overlay de la aplicación). Gestiona las categorías (izquierda) y sus opciones (derecha).
///
/// Modelo de edición: los controles editan copias EN STAGING (cargadas de <see cref="AppSettings"/> al abrir).
/// <see cref="Apply"/> (botón OK del diálogo) vuelca el staging en <see cref="AppSettings"/> aplicándolo en caliente
/// (cabecera de widgets, regiones favoritas del dashboard) y persiste al ini; cancelar descarta sin tocar nada.
/// </summary>
public partial class SettingsDialogViewModel : ObservableObject
{
    #region Nested types
    /// <summary>Categorías de configuración (una página de opciones cada una).</summary>
    public enum SettingsSectionKind { General, Regions, MediaTypes, Theme, About }

    /// <summary>Una categoría de la lista de la izquierda.</summary>
    public sealed class SettingsSection
    {
        public SettingsSectionKind Kind { get; }
        public string Title { get; }

        public SettingsSection(SettingsSectionKind kind, string title)
        {
            Kind = kind;
            Title = title;
        }
    }

    /// <summary>Una región en la lista de favoritas (check para seleccionarla; deshabilitada si ya hay 3).</summary>
    public sealed partial class RegionOption : ObservableObject
    {
        public ImageRegion Region { get; }
        public string Name => Region.Value;

        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool canToggle = true;

        public RegionOption(ImageRegion region, bool isSelected)
        {
            Region = region;
            this.isSelected = isSelected;
        }
    }

    /// <summary>Un tipo de media en la lista de favoritos (check para seleccionarlo; deshabilitado si ya hay 10).</summary>
    public sealed partial class MediaTypeOption : ObservableObject
    {
        public MediaType Type { get; }
        public string Name => Type.Value;

        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool canToggle = true;

        public MediaTypeOption(MediaType type, bool isSelected)
        {
            Type = type;
            this.isSelected = isSelected;
        }
    }
    #endregion

    #region Constants
    /// <summary>Máximo de regiones favoritas seleccionables a la vez (las 3 del selector del dashboard de regiones).</summary>
    public const int MaxFavouriteRegions = 3;

    /// <summary>Máximo de tipos de media favoritos seleccionables a la vez (los botones de la banda de tipos).</summary>
    public const int MaxFavouriteMediaTypes = 10;
    #endregion

    #region Attributes
    private readonly AppSettings _appSettings;
    private readonly PersistAndRestoreService _persistAndRestoreService;
    private readonly SharedDataService _sharedDataService;
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localizationService;
    private readonly Controls.ViewModels.ConsoleViewModel _consoleViewModel;
    #endregion

    #region Properties
    /// <summary>Categorías mostradas en la columna izquierda.</summary>
    public IReadOnlyList<SettingsSection> Sections { get; }

    /// <summary>Categoría seleccionada; determina las opciones mostradas a la derecha.</summary>
    [ObservableProperty] private SettingsSection? selectedSection;

    /// <summary>True cuando la categoría seleccionada es General.</summary>
    public bool IsGeneral => SelectedSection?.Kind == SettingsSectionKind.General;

    /// <summary>True cuando la categoría seleccionada es Regions.</summary>
    public bool IsRegions => SelectedSection?.Kind == SettingsSectionKind.Regions;

    /// <summary>True cuando la categoría seleccionada es Media types.</summary>
    public bool IsMediaTypes => SelectedSection?.Kind == SettingsSectionKind.MediaTypes;

    /// <summary>True cuando la categoría seleccionada es Theme.</summary>
    public bool IsTheme => SelectedSection?.Kind == SettingsSectionKind.Theme;

    /// <summary>True cuando la categoría seleccionada es About.</summary>
    public bool IsAbout => SelectedSection?.Kind == SettingsSectionKind.About;

    /// <summary>True cuando la categoría seleccionada aún no tiene página (placeholder).</summary>
    public bool IsPlaceholder => !IsGeneral && !IsRegions && !IsMediaTypes && !IsTheme && !IsAbout;
    #endregion

    #region General settings (staging)
    /// <summary>Una opción del combo del modo de grupos de la toolbar (valor + etiqueta mostrada).</summary>
    public sealed record ToolbarGroupsModeOption(ToolbarGroupsDisplayMode Value, string Label);

    /// <summary>
    /// Opciones del combo del modo de grupos de la toolbar. Se enlaza con <c>DisplayMemberPath="Label"</c> y
    /// <c>SelectedValuePath="Value"</c>: así el combo muestra texto y la (pre)selección casa por valor de enum
    /// (con <c>SelectedItem</c> sobre valores enum "boxed" WinUI no preselecciona de forma fiable y salía vacío).
    /// </summary>
    public IReadOnlyList<ToolbarGroupsModeOption> ToolbarGroupsModeOptions { get; }

    /// <summary>Opción seleccionada del combo. Se enlaza con <c>SelectedItem</c> a la INSTANCIA del record (igualdad por
    /// referencia): con <c>SelectedValue</c>/enum boxed WinUI no re-preseleccionaba al reabrir el diálogo.</summary>
    [ObservableProperty] private ToolbarGroupsModeOption? selectedToolbarGroupsOption;
    [ObservableProperty] private bool showWidgetHeader;
    [ObservableProperty] private bool footerEventViewerAlwaysVisible;
    [ObservableProperty] private bool promptBeforeDeleteImage;
    [ObservableProperty] private double cacheSize;
    [ObservableProperty] private bool exceptionLoggingEnabled;

    /// <summary>Idiomas disponibles para el combo (código + nombre). Vienen del <see cref="LocalizationService"/>.</summary>
    public IReadOnlyList<LocalizationService.LanguageOption> LanguageOptions { get; }

    /// <summary>Idioma seleccionado en el combo. Se aplica en caliente al aceptar (OK).</summary>
    [ObservableProperty] private LocalizationService.LanguageOption? selectedLanguageOption;
    #endregion

    #region Regions settings (staging)
    /// <summary>Todas las regiones seleccionables como favoritas (excluida "sin región"), con su estado de check.</summary>
    public IReadOnlyList<RegionOption> RegionOptions { get; }
    #endregion

    #region Media types settings (staging)
    /// <summary>Todos los tipos de media de juego seleccionables como favoritos, con su estado de check.</summary>
    public IReadOnlyList<MediaTypeOption> MediaTypeOptions { get; }
    #endregion

    #region Theme settings (staging)
    /// <summary>Nombres de los temas disponibles (claves de <see cref="AppSettings.ThemeSettings.Themes"/>).</summary>
    public IReadOnlyList<string> ThemeNames { get; }

    /// <summary>Tema seleccionado en el combo; se aplica en caliente al aceptar.</summary>
    [ObservableProperty] private string? selectedThemeName;

    // Resto de parámetros del tema (AppSettings.ThemeSettings). Sliders en double; OverlayImageBlur se castea a int.
    [ObservableProperty] private bool randomTheme;
    [ObservableProperty] private bool backgroundImageTinted;
    [ObservableProperty] private bool backgroundImageFramed;
    [ObservableProperty] private double tintOpacity;
    [ObservableProperty] private double tintSaturation;
    [ObservableProperty] private double tintBrightness;
    [ObservableProperty] private double overlayImageBlur;
    [ObservableProperty] private double overlayImageOpacity;
    #endregion

    #region Theme preview (staging, no aplicado)
    /// <summary>
    /// URI (ms-appx) del fondo de la VENTANA PRINCIPAL del tema SELECCIONADO en el combo (no el aplicado), para el
    /// preview en vivo. Es el <c>OverlayImage</c> del tema (el que tinta el overlay de la ventana principal), no el
    /// wallpaper de la ventana de carga. Se recalcula al cambiar <see cref="SelectedThemeName"/>.
    /// </summary>
    public string ThemePreviewSource
    {
        get
        {
            if (string.IsNullOrEmpty(SelectedThemeName) || !_appSettings.Theme.Themes.TryGetValue(SelectedThemeName!, out AppSettings.ThemeDefinition? def))
                return string.Empty;
            return string.IsNullOrWhiteSpace(def.OverlayImage) ? string.Empty : $"ms-appx:///{def.AssetsPath}{def.OverlayImage}";
        }
    }

    /// <summary>Color de acento del tema SELECCIONADO en el combo, usado como tinte del preview.</summary>
    public Windows.UI.Color ThemePreviewTintColor
    {
        get
        {
            if (!string.IsNullOrEmpty(SelectedThemeName) && _appSettings.Theme.Themes.TryGetValue(SelectedThemeName!, out AppSettings.ThemeDefinition? def))
                return ParseHex(def.AccentColor);
            return Microsoft.UI.Colors.Transparent;
        }
    }

    /// <summary>Convierte un color hex <c>#RRGGBB</c> en <see cref="Windows.UI.Color"/> (alfa opaco).</summary>
    private static Windows.UI.Color ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length < 7)
            return Microsoft.UI.Colors.Transparent;
        return Windows.UI.Color.FromArgb(255,
            byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber));
    }

    partial void OnSelectedThemeNameChanged(string? value)
    {
        OnPropertyChanged(nameof(ThemePreviewSource));
        OnPropertyChanged(nameof(ThemePreviewTintColor));
    }
    #endregion

    #region About (solo lectura)
    /// <summary>Un componente de terceros y su licencia, para la lista de la sección About.</summary>
    public sealed record LicenseInfo(string Component, string License);

    /// <summary>Marca de compilación (fecha/hora de última escritura del ensamblado, que se reescribe al compilar).</summary>
    public string BuildText => ResolveBuildText();

    /// <summary>Descripción del runtime (p. ej. ".NET 8.0.x") más WinUI 3.</summary>
    public string RuntimeText => $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} · WinUI 3";

    /// <summary>Arquitectura del proceso (p. ej. X64) y SO.</summary>
    public string ArchitectureText => $"Windows · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";

    /// <summary>Carpeta de LaunchBox configurada (fuente de datos). Vacía si aún no se ha configurado.</summary>
    public string DataSourceText => string.IsNullOrWhiteSpace(_appSettings.LaunchBox?.LaunchBoxFolder) ? "—" : _appSettings.LaunchBox!.LaunchBoxFolder;

    /// <summary>Licencias de los componentes de terceros usados por la app (mostradas en About).</summary>
    public IReadOnlyList<LicenseInfo> ThirdPartyLicenses { get; } = new[]
    {
        new LicenseInfo("Windows App SDK / WinUI 3", "MIT"),
        new LicenseInfo("CommunityToolkit (MVVM · WinUI)", "MIT"),
        new LicenseInfo("LiveCharts + SkiaSharp", "MIT"),
        new LicenseInfo("Win2D", "MIT"),
        new LicenseInfo("Microsoft.Data.Sqlite", "MIT"),
        new LicenseInfo("ClosedXML", "MIT"),
        new LicenseInfo("Newtonsoft.Json", "MIT"),
        new LicenseInfo("YoutubeExplode", "LGPL-3.0"),
    };

    /// <summary>Fecha/hora de compilación derivada de la última escritura del .exe/.dll (se reescribe en cada build).</summary>
    private static string ResolveBuildText()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                path = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return "build ?";
            return System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "build ?";
        }
    }
    #endregion

    #region Constructor
    public SettingsDialogViewModel(IOptions<AppSettings> appSettings, PersistAndRestoreService persistAndRestoreService, SharedDataService sharedDataService, ThemeService themeService, LocalizationService localizationService, Controls.ViewModels.ConsoleViewModel consoleViewModel)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
        _persistAndRestoreService = persistAndRestoreService;
        _sharedDataService = sharedDataService;
        _themeService = themeService;
        _localizationService = localizationService;
        _consoleViewModel = consoleViewModel;

        LanguageOptions = _localizationService.AvailableLanguages;

        // Opciones del combo de grupos de toolbar, localizadas (el VM es transient: se recrea al abrir el diálogo,
        // así que toma el idioma actual en cada apertura).
        ToolbarGroupsModeOptions = new[]
        {
            new ToolbarGroupsModeOption(ToolbarGroupsDisplayMode.Expanded, _localizationService.Get(LocKeys.SettingsDialog_ToolbarGroupsSeparate_Label)),
            new ToolbarGroupsModeOption(ToolbarGroupsDisplayMode.Collapsed, _localizationService.Get(LocKeys.SettingsDialog_ToolbarGroupsGrouped_Label)),
            new ToolbarGroupsModeOption(ToolbarGroupsDisplayMode.Auto, _localizationService.Get(LocKeys.SettingsDialog_ToolbarGroupsAuto_Label)),
        };

        Sections = new List<SettingsSection>
        {
            new(SettingsSectionKind.General, _localizationService.Get(LocKeys.SettingsDialog_General_Title)),
            new(SettingsSectionKind.Regions, _localizationService.Get(LocKeys.SettingsDialog_Regions_Title)),
            new(SettingsSectionKind.MediaTypes, _localizationService.Get(LocKeys.SettingsDialog_MediaTypes_Title)),
            new(SettingsSectionKind.Theme, _localizationService.Get(LocKeys.SettingsDialog_Theme_Title)),
            new(SettingsSectionKind.About, _localizationService.Get(LocKeys.SettingsDialog_About_Title)),
        };
        // Restaura la última categoría abierta (persistida al aceptar). Si el valor guardado ya no existe, cae a la 1ª.
        SelectedSection = Sections.FirstOrDefault(s => s.Kind.ToString() == _appSettings.General?.SettingsLastSection) ?? Sections[0];

        // Opciones de regiones: todas menos "sin región", en orden alfabético. Marcadas según las favoritas actuales.
        ImageRegion[] favourites = _appSettings.GameImagesRegionDashboardControl?.FavouriteRegions ?? Array.Empty<ImageRegion>();
        RegionOptions = Enumeration.GetAll<ImageRegion>()
            .Where(r => !string.IsNullOrEmpty(r.Value))
            .OrderBy(r => r.Value)
            .Select(r => new RegionOption(r, favourites.Any(f => f.Key == r.Key)))
            .ToList();

        foreach (RegionOption option in RegionOptions)
            option.PropertyChanged += OnRegionOptionChanged;
        UpdateRegionToggles();

        // Opciones de tipos de media: los tipos de imagen de JUEGO (IsImage) más los tipos de vídeo que la app
        // gestiona (Video y Theme Video; Recordings/Trailer no se escanean), en orden alfabético. Marcados según
        // los favoritos actuales.
        MediaType[] favouriteTypes = _appSettings.ImageTypeControl?.FavouriteImageTypes ?? Array.Empty<MediaType>();
        MediaTypeOptions = Enumeration.GetAll<MediaType>()
            .Where(t => MediaType.IsImage(t.Key) || t.Key == MediaType.Video.Key || t.Key == MediaType.ThemeVideo.Key)
            .OrderBy(t => t.Value)
            .Select(t => new MediaTypeOption(t, favouriteTypes.Any(f => f.Key == t.Key)))
            .ToList();

        foreach (MediaTypeOption option in MediaTypeOptions)
            option.PropertyChanged += OnMediaTypeOptionChanged;
        UpdateMediaTypeToggles();

        // Temas disponibles: las claves del diccionario de temas. El seleccionado arranca del tema REALMENTE activo
        // (ThemeService.CurrentThemeName), no de Theme.Name: así, con RandomTheme activo, el combo muestra el tema
        // elegido al azar en este arranque y no el guardado. Si no existe, caemos al primero disponible.
        ThemeNames = _appSettings.Theme.Themes.Keys.ToList();
        SelectedThemeName = ThemeNames.Contains(_themeService.CurrentThemeName) ? _themeService.CurrentThemeName : ThemeNames.FirstOrDefault();

        LoadGeneralFromSettings();
    }
    #endregion

    #region Methods (public)
    /// <summary>Vuelca el staging en AppSettings (aplicándolo en caliente donde aplica) y persiste al ini. Lo llama OK.</summary>
    public void Apply()
    {
        AppSettings.GeneralSettings g = _appSettings.General;
        ToolbarGroupsDisplayMode toolbarGroupsMode = SelectedToolbarGroupsOption?.Value ?? ToolbarGroupsDisplayMode.Auto;
        bool headerChanged = g.ShowWidgetHeader != ShowWidgetHeader;
        bool footerViewerChanged = g.FooterEventViewerAlwaysVisible != FooterEventViewerAlwaysVisible;
        bool toolbarGroupsChanged = g.ToolbarGroupsDisplayMode != toolbarGroupsMode;

        g.ToolbarGroupsDisplayMode = toolbarGroupsMode;
        g.ShowWidgetHeader = ShowWidgetHeader;
        // Recuerda la categoría abierta para restaurarla al reabrir el diálogo.
        if (SelectedSection != null)
            g.SettingsLastSection = SelectedSection.Kind.ToString();
        g.FooterEventViewerAlwaysVisible = FooterEventViewerAlwaysVisible;
        g.PromptBeforeDeleteImage = PromptBeforeDeleteImage;
        g.CacheSize = CacheSize;
        g.ExceptionLoggingEnabled = ExceptionLoggingEnabled;

        // Idioma: se aplica EN CALIENTE (los textos localizados se refrescan solos por notificación del servicio).
        string newLanguage = SelectedLanguageOption?.Code ?? "en";
        g.Language = newLanguage;
        _localizationService.SetLanguage(newLanguage);

        // Aplicación en caliente de los flags de General:
        // - ShowWidgetHeader: vía NotifyWidgetHeaderVisibilityChanged (más abajo).
        // - FooterEventViewerAlwaysVisible: se re-evalúa la visibilidad del visor del pie.
        // - PromptBeforeDeleteImage: se lee bajo demanda de AppSettings (ya persistido arriba), nada que notificar.
        // - ExceptionLoggingEnabled: el logger cachea el flag (estático), así que se refleja aquí.
        ExceptionService.LoggingEnabled = ExceptionLoggingEnabled;
        if (footerViewerChanged)
            _consoleViewModel.NotifyFooterViewerVisibilityChanged();
        if (toolbarGroupsChanged)
            _sharedDataService.NotifyToolbarGroupsDisplayModeChanged();

        // Regiones favoritas (máx. 3), en orden de la lista.
        ImageRegion[] newFavourites = RegionOptions.Where(o => o.IsSelected).Select(o => o.Region).ToArray();
        AppSettings.GameImagesRegionDashboardControlSettings region = _appSettings.GameImagesRegionDashboardControl;
        bool favouritesChanged = region != null && !newFavourites.Select(r => r.Key).SequenceEqual(region.FavouriteRegions?.Select(r => r.Key) ?? Enumerable.Empty<int>());
        if (region != null)
            region.FavouriteRegions = newFavourites;

        // Tipos de media favoritos (máx. 10), en orden de la lista.
        MediaType[] newFavouriteTypes = MediaTypeOptions.Where(o => o.IsSelected).Select(o => o.Type).ToArray();
        AppSettings.ImageTypeControlSettings imageType = _appSettings.ImageTypeControl;
        bool favouriteTypesChanged = imageType != null && !newFavouriteTypes.Select(t => t.Key).SequenceEqual(imageType.FavouriteImageTypes?.Select(t => t.Key) ?? Enumerable.Empty<int>());
        if (imageType != null)
            imageType.FavouriteImageTypes = newFavouriteTypes;

        // Tema: nombre (comparado con el tema REALMENTE activo, no con Theme.Name, que con RandomTheme puede diferir)
        // y el resto de parámetros (tinte del overlay, aleatorio, fondo de la ventana de carga).
        AppSettings.ThemeSettings t = _appSettings.Theme;
        bool themeNameChanged = !string.IsNullOrEmpty(SelectedThemeName) && SelectedThemeName != _themeService.CurrentThemeName;
        bool themeParamsChanged =
            t.RandomTheme != RandomTheme ||
            t.BackgroundImageTinted != BackgroundImageTinted ||
            t.BackgroundImageFramed != BackgroundImageFramed ||
            t.TintOpacity != TintOpacity ||
            t.TintSaturation != TintSaturation ||
            t.TintBrightness != TintBrightness ||
            t.OverlayImageBlur != (int)OverlayImageBlur ||
            t.OverlayImageOpacity != OverlayImageOpacity;

        if (themeNameChanged)
            t.Name = SelectedThemeName!;
        t.RandomTheme = RandomTheme;
        t.BackgroundImageTinted = BackgroundImageTinted;
        t.BackgroundImageFramed = BackgroundImageFramed;
        t.TintOpacity = TintOpacity;
        t.TintSaturation = TintSaturation;
        t.TintBrightness = TintBrightness;
        t.OverlayImageBlur = (int)OverlayImageBlur;
        t.OverlayImageOpacity = OverlayImageOpacity;

        // Aplicación en caliente.
        if (headerChanged)
            _sharedDataService.NotifyWidgetHeaderVisibilityChanged();
        if (favouritesChanged)
            _sharedDataService.NotifyFavouriteRegionsChanged();
        if (favouriteTypesChanged)
            _sharedDataService.NotifyFavouriteMediaTypesChanged();
        // Re-aplica el tema si cambió el nombre o cualquier parámetro: regenera los recursos (colores + tinte del
        // overlay) y dispara ThemeChanged (la ventana principal refresca su TintedImage). El nombre destino es el
        // seleccionado o, si no cambió, el que ya está activo.
        if (themeNameChanged || themeParamsChanged)
            _themeService.ApplyTheme(themeNameChanged ? SelectedThemeName! : _themeService.CurrentThemeName);

        _persistAndRestoreService.PersistData();
    }
    #endregion

    #region Methods (private)
    private void LoadGeneralFromSettings()
    {
        AppSettings.GeneralSettings g = _appSettings.General;
        SelectedToolbarGroupsOption = ToolbarGroupsModeOptions.FirstOrDefault(o => o.Value == g.ToolbarGroupsDisplayMode) ?? ToolbarGroupsModeOptions[0];
        ShowWidgetHeader = g.ShowWidgetHeader;
        FooterEventViewerAlwaysVisible = g.FooterEventViewerAlwaysVisible;
        PromptBeforeDeleteImage = g.PromptBeforeDeleteImage;
        CacheSize = g.CacheSize;
        ExceptionLoggingEnabled = g.ExceptionLoggingEnabled;
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(o => o.Code == g.Language) ?? LanguageOptions[0];

        // Parámetros del tema (además del nombre, que se fija en el constructor desde el tema realmente activo).
        AppSettings.ThemeSettings t = _appSettings.Theme;
        RandomTheme = t.RandomTheme;
        BackgroundImageTinted = t.BackgroundImageTinted;
        BackgroundImageFramed = t.BackgroundImageFramed;
        TintOpacity = t.TintOpacity;
        TintSaturation = t.TintSaturation;
        TintBrightness = t.TintBrightness;
        OverlayImageBlur = t.OverlayImageBlur;
        OverlayImageOpacity = t.OverlayImageOpacity;
    }

    private void OnRegionOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegionOption.IsSelected))
            UpdateRegionToggles();
    }

    /// <summary>Deshabilita las regiones sin marcar cuando ya hay <see cref="MaxFavouriteRegions"/> seleccionadas.</summary>
    private void UpdateRegionToggles()
    {
        int selected = RegionOptions.Count(o => o.IsSelected);
        foreach (RegionOption option in RegionOptions)
            option.CanToggle = option.IsSelected || selected < MaxFavouriteRegions;
    }

    private void OnMediaTypeOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaTypeOption.IsSelected))
            UpdateMediaTypeToggles();
    }

    /// <summary>Deshabilita los tipos sin marcar cuando ya hay <see cref="MaxFavouriteMediaTypes"/> seleccionados.</summary>
    private void UpdateMediaTypeToggles()
    {
        int selected = MediaTypeOptions.Count(o => o.IsSelected);
        foreach (MediaTypeOption option in MediaTypeOptions)
            option.CanToggle = option.IsSelected || selected < MaxFavouriteMediaTypes;
    }

    partial void OnSelectedSectionChanged(SettingsSection? value)
    {
        OnPropertyChanged(nameof(IsGeneral));
        OnPropertyChanged(nameof(IsRegions));
        OnPropertyChanged(nameof(IsMediaTypes));
        OnPropertyChanged(nameof(IsTheme));
        OnPropertyChanged(nameof(IsAbout));
        OnPropertyChanged(nameof(IsPlaceholder));
    }
    #endregion
}
