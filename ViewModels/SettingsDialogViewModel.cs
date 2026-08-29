using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Tracker.Enums;
using Tracker.Helpers;
using Tracker.Models;
using Tracker.Services;

namespace Tracker.ViewModels;

/// <summary>
/// ViewModel del contenido de configuración de la app (mostrado en un <see cref="Controls.Dialogs.AppDialog"/> sobre
/// el overlay de la aplicación). Gestiona las categorías (izquierda) y sus opciones (derecha): General, Theme y About.
///
/// Modelo de edición: los controles editan copias EN STAGING (cargadas de <see cref="AppSettings"/> al abrir).
/// <see cref="Apply"/> (botón OK/Apply del diálogo) vuelca el staging en <see cref="AppSettings"/> aplicándolo en
/// caliente (idioma, cabecera de widgets, modo de grupos de toolbar, tema) y persiste al ini; cancelar descarta.
/// </summary>
public partial class SettingsDialogViewModel : ObservableObject
{
    #region Nested types
    /// <summary>Categorías de configuración (una página de opciones cada una).</summary>
    public enum SettingsSectionKind { General, Theme, About }

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
    #endregion

    #region Attributes
    private readonly AppSettings _appSettings;
    private readonly PersistAndRestoreService _persistAndRestoreService;
    private readonly SharedDataService _sharedDataService;
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localizationService;
    private readonly Controls.ViewModels.ConsoleViewModel _consoleViewModel;

    /// <summary>True mientras se carga el staging desde settings, para inhibir los efectos en caliente de los setters.</summary>
    private bool _loadingSettings;
    #endregion

    #region Properties
    /// <summary>Categorías mostradas en la columna izquierda.</summary>
    public IReadOnlyList<SettingsSection> Sections { get; }

    /// <summary>Categoría seleccionada; determina las opciones mostradas a la derecha.</summary>
    [ObservableProperty] private SettingsSection? selectedSection;

    /// <summary>True cuando la categoría seleccionada es General.</summary>
    public bool IsGeneral => SelectedSection?.Kind == SettingsSectionKind.General;

    /// <summary>True cuando la categoría seleccionada es Theme.</summary>
    public bool IsTheme => SelectedSection?.Kind == SettingsSectionKind.Theme;

    /// <summary>True cuando la categoría seleccionada es About.</summary>
    public bool IsAbout => SelectedSection?.Kind == SettingsSectionKind.About;

    /// <summary>True cuando la categoría seleccionada aún no tiene página (placeholder).</summary>
    public bool IsPlaceholder => !IsGeneral && !IsTheme && !IsAbout;
    #endregion

    #region General settings (staging)
    [ObservableProperty] private bool showWidgetHeader;
    [ObservableProperty] private bool footerEventViewerAlwaysVisible;
    [ObservableProperty] private bool exceptionLoggingEnabled;

    /// <summary>Horas entre actualizaciones automáticas de precios (mínimo 1). Se aplica al planificador al aceptar.</summary>
    [ObservableProperty] private double autoRefreshHours;

    /// <summary>
    /// Horas como TEXTO, para poder usar un <c>TextBox</c> normal (mismo estilo que el resto de campos de texto) en vez de
    /// un NumberBox. El getter formatea el valor actual; el setter parsea (ignora entradas no numéricas). El mínimo (1) lo
    /// acota <see cref="Apply"/> al aceptar, así que aquí no se fuerza durante la edición (no molesta al teclear).
    /// </summary>
    public string AutoRefreshHoursText
    {
        get => ((int)Math.Round(AutoRefreshHours)).ToString(CultureInfo.CurrentCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int hours) ||
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
                AutoRefreshHours = hours;
        }
    }

    /// <summary>Idiomas disponibles para el combo (código + nombre). Vienen del <see cref="LocalizationService"/>.</summary>
    public IReadOnlyList<LocalizationService.LanguageOption> LanguageOptions { get; }

    /// <summary>Idioma seleccionado en el combo. Se aplica en caliente al aceptar (OK/Apply).</summary>
    [ObservableProperty] private LocalizationService.LanguageOption? selectedLanguageOption;
    #endregion

    #region Theme settings (staging)
    /// <summary>Nombres de los temas disponibles (claves de <see cref="AppSettings.ThemeSettings.Themes"/>).</summary>
    public IReadOnlyList<string> ThemeNames { get; }

    /// <summary>Tema seleccionado en el combo; se aplica en caliente al aceptar.</summary>
    [ObservableProperty] private string? selectedThemeName;

    [ObservableProperty] private bool randomTheme;
    [ObservableProperty] private double tintOpacity;
    [ObservableProperty] private double tintSaturation;
    [ObservableProperty] private double tintBrightness;
    [ObservableProperty] private double overlayImageBlur;
    [ObservableProperty] private double overlayImageOpacity;

    /// <summary>Si es true, se aplican los colores personalizados sobre el tema. Habilita el botón de editar colores.</summary>
    [ObservableProperty] private bool useCustomColors;

    /// <summary>Lo dispara el botón "Editar colores…": la vista oculta el diálogo de ajustes y abre el editor de colores.</summary>
    public event Action? EditColorsRequested;

    private RelayCommand? _editColorsCommand;

    /// <summary>Comando del botón "Editar colores…": pide abrir el editor de colores (ver <see cref="EditColorsRequested"/>).</summary>
    public RelayCommand EditColorsCommand => _editColorsCommand ??= new RelayCommand(() => EditColorsRequested?.Invoke());
    #endregion

    #region Theme preview (staging, no aplicado)
    /// <summary>
    /// URI (ms-appx) del fondo (overlay) del tema SELECCIONADO en el combo (no el aplicado), para el preview en vivo.
    /// Se recalcula al cambiar <see cref="SelectedThemeName"/>.
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

    /// <summary>Licencias de los componentes de terceros usados por la app (mostradas en About).</summary>
    public IReadOnlyList<LicenseInfo> ThirdPartyLicenses { get; } = new[]
    {
        new LicenseInfo("Windows App SDK / WinUI 3", "MIT"),
        new LicenseInfo("CommunityToolkit (MVVM · WinUI)", "MIT"),
        new LicenseInfo("LiveCharts + SkiaSharp", "MIT"),
        new LicenseInfo("Win2D", "MIT"),
        new LicenseInfo("Microsoft.Data.Sqlite", "MIT"),
        new LicenseInfo("Newtonsoft.Json", "MIT"),
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

        // Inhibe los efectos en caliente de los setters mientras se inicializa el staging (sección, tema, flags).
        _loadingSettings = true;

        LanguageOptions = _localizationService.AvailableLanguages;

        Sections = new List<SettingsSection>
        {
            new(SettingsSectionKind.General, _localizationService.Get(LocKeys.SettingsDialog_General_Title)),
            new(SettingsSectionKind.Theme, _localizationService.Get(LocKeys.SettingsDialog_Theme_Title)),
            new(SettingsSectionKind.About, _localizationService.Get(LocKeys.SettingsDialog_About_Title)),
        };
        // Restaura la última categoría abierta (persistida al aceptar). Si el valor guardado ya no existe, cae a la 1ª.
        SelectedSection = Sections.FirstOrDefault(s => s.Kind.ToString() == _appSettings.General.SettingsLastSection) ?? Sections[0];

        // Temas disponibles: las claves del diccionario de temas. El seleccionado arranca del tema REALMENTE activo
        // (ThemeService.CurrentThemeName): así, con RandomTheme activo, el combo muestra el tema elegido al azar.
        ThemeNames = _appSettings.Theme.Themes.Keys.ToList();
        SelectedThemeName = ThemeNames.Contains(_themeService.CurrentThemeName) ? _themeService.CurrentThemeName : ThemeNames.FirstOrDefault();

        LoadGeneralFromSettings();
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Descarta el staging (lo llama Cancelar del diálogo). Como "Usar colores personalizados" se previsualiza en
    /// caliente, aquí se deshace esa vista previa reaplicando el estado CONFIRMADO (el de settings, que solo cambia en
    /// <see cref="Apply"/>) y se resincroniza el staging del flag con ese valor.
    /// </summary>
    public void Cancel()
    {
        if (_appSettings.General.UseCustomColors)
            _themeService.ApplyStoredOverrides();
        else
            _themeService.ClearOverrides();

        _loadingSettings = true;
        UseCustomColors = _appSettings.General.UseCustomColors;
        _loadingSettings = false;
    }

    /// <summary>Vuelca el staging en AppSettings (aplicándolo en caliente donde aplica) y persiste al ini. Lo llama OK/Apply.</summary>
    public void Apply()
    {
        AppSettings.GeneralSettings g = _appSettings.General;
        bool headerChanged = g.ShowWidgetHeader != ShowWidgetHeader;
        bool footerViewerChanged = g.FooterEventViewerAlwaysVisible != FooterEventViewerAlwaysVisible;

        int refreshHours = Math.Max(1, (int)Math.Round(AutoRefreshHours));
        bool refreshHoursChanged = g.AutoRefreshHours != refreshHours;
        g.AutoRefreshHours = refreshHours;

        g.ShowWidgetHeader = ShowWidgetHeader;
        // Recuerda la categoría abierta para restaurarla al reabrir el diálogo.
        if (SelectedSection != null)
            g.SettingsLastSection = SelectedSection.Kind.ToString();
        g.FooterEventViewerAlwaysVisible = FooterEventViewerAlwaysVisible;
        g.ExceptionLoggingEnabled = ExceptionLoggingEnabled;

        // Idioma: se aplica EN CALIENTE (los textos localizados se refrescan solos por notificación del servicio).
        string newLanguage = SelectedLanguageOption?.Code ?? "en";
        g.Language = newLanguage;
        _localizationService.SetLanguage(newLanguage);

        ExceptionService.LoggingEnabled = ExceptionLoggingEnabled;
        if (refreshHoursChanged)
            App.GetService<PriceSchedulerService>().ApplyIntervalChange();
        if (footerViewerChanged)
            _consoleViewModel.NotifyFooterViewerVisibilityChanged();
        if (headerChanged)
            _sharedDataService.NotifyWidgetHeaderVisibilityChanged();

        // Tema: nombre (comparado con el tema REALMENTE activo, no con Theme.Name, que con RandomTheme puede diferir)
        // y el resto de parámetros (tinte del overlay, aleatorio).
        AppSettings.ThemeSettings t = _appSettings.Theme;
        bool themeNameChanged = !string.IsNullOrEmpty(SelectedThemeName) && SelectedThemeName != _themeService.CurrentThemeName;
        bool themeParamsChanged =
            t.RandomTheme != RandomTheme ||
            t.TintOpacity != TintOpacity ||
            t.TintSaturation != TintSaturation ||
            t.TintBrightness != TintBrightness ||
            t.OverlayImageBlur != (int)OverlayImageBlur ||
            t.OverlayImageOpacity != OverlayImageOpacity;

        if (themeNameChanged)
            t.Name = SelectedThemeName!;
        t.RandomTheme = RandomTheme;
        t.TintOpacity = TintOpacity;
        t.TintSaturation = TintSaturation;
        t.TintBrightness = TintBrightness;
        t.OverlayImageBlur = (int)OverlayImageBlur;
        t.OverlayImageOpacity = OverlayImageOpacity;

        // Re-aplica el tema si cambió el nombre o cualquier parámetro: regenera los recursos (colores + tinte del
        // overlay) y dispara ThemeChanged (la ventana principal refresca su TintedImage). Nota: ApplyTheme ya reaplica
        // por su cuenta los colores personalizados guardados sobre el nuevo tema.
        if (themeNameChanged || themeParamsChanged)
            _themeService.ApplyTheme(themeNameChanged ? SelectedThemeName! : _themeService.CurrentThemeName);

        // Colores personalizados: al activarse se reaplican los guardados; al desactivarse se revierte al tema puro.
        // (Si además cambió el tema, ApplyTheme ya reaplicó los overrides; solo hace falta revertir al desactivar.)
        bool useCustomColorsChanged = g.UseCustomColors != UseCustomColors;
        g.UseCustomColors = UseCustomColors;
        if (useCustomColorsChanged)
        {
            if (UseCustomColors)
                _themeService.ApplyStoredOverrides();
            else
                _themeService.ClearOverrides();
        }

        _persistAndRestoreService.PersistData();
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// "Usar colores personalizados" se previsualiza EN CALIENTE mientras el diálogo está abierto (al desactivarse se ve
    /// al instante el tema puro; al activarse se reaplican los colores guardados), pero NO se confirma aquí: el cambio se
    /// vuelca a settings en <see cref="Apply"/> (OK/Apply) y se revierte al valor confirmado en <see cref="Cancel"/>.
    /// </summary>
    partial void OnUseCustomColorsChanged(bool value)
    {
        if (_loadingSettings)
            return;

        if (value)
            _themeService.ApplyStoredOverrides();
        else
            _themeService.ClearOverrides();
    }

    private void LoadGeneralFromSettings()
    {
        _loadingSettings = true;
        AppSettings.GeneralSettings g = _appSettings.General;
        ShowWidgetHeader = g.ShowWidgetHeader;
        FooterEventViewerAlwaysVisible = g.FooterEventViewerAlwaysVisible;
        ExceptionLoggingEnabled = g.ExceptionLoggingEnabled;
        AutoRefreshHours = g.AutoRefreshHours;
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(o => o.Code == g.Language) ?? LanguageOptions[0];

        AppSettings.ThemeSettings t = _appSettings.Theme;
        RandomTheme = t.RandomTheme;
        TintOpacity = t.TintOpacity;
        TintSaturation = t.TintSaturation;
        TintBrightness = t.TintBrightness;
        OverlayImageBlur = t.OverlayImageBlur;
        OverlayImageOpacity = t.OverlayImageOpacity;

        UseCustomColors = g.UseCustomColors;
        _loadingSettings = false;
    }

    partial void OnSelectedSectionChanged(SettingsSection? value)
    {
        OnPropertyChanged(nameof(IsGeneral));
        OnPropertyChanged(nameof(IsTheme));
        OnPropertyChanged(nameof(IsAbout));
        OnPropertyChanged(nameof(IsPlaceholder));

        // La categoría abierta se recuerda EN CALIENTE (no espera a OK/Apply): así el diálogo reabre siempre en la última
        // sección vista, aunque se cerrara con Cancelar, y se conserva entre reinicios.
        if (_loadingSettings || value is null)
            return;

        _appSettings.General.SettingsLastSection = value.Kind.ToString();
        _persistAndRestoreService.PersistData();
    }
    #endregion
}
