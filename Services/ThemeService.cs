using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tracker.Models;
using Windows.UI;
using static Tracker.Models.AppSettings;

namespace Tracker.Services;

/// <summary>
/// Servicio responsable de gestionar el tema visual de la aplicación.
/// 
/// Funciones principales:
/// - Cargar el tema definido en AppSettings.
/// - Exponer los colores del tema como propiedades reactivas.
/// - Generar dinámicamente un ResourceDictionary con brushes basados en el tema.
/// - Aplicar el diccionario a Application.Resources.
/// - Permitir cambiar de tema en tiempo de ejecución.
/// 
/// Este servicio actúa como ThemeService + ThemeManager unificados.
/// </summary>
public class ThemeService : ObservableObject
{
    #region Attributes
    private readonly AppSettings _appSettings;
    private ResourceDictionary? _currentDictionary;
    private ThemeDefinition _currentTheme = null!;   // lo fija InitializeAsync al arranque, antes de cualquier acceso
    private string _currentThemeName = string.Empty; // nombre del tema activo (puede diferir de Theme.Name si RandomTheme)

    /// <summary>Color ORIGINAL de cada nombre base sobrescrito en caliente (capturado en el primer override), para revertir.</summary>
    private readonly Dictionary<string, Color> _originalColors = new();

    /// <summary>Overrides ACTIVOS (nombre base -> hex #RRGGBB) aplicados en caliente. Es lo que persiste en settings.</summary>
    private readonly Dictionary<string, string> _overrides = new();

    /// <summary>
    /// Diccionarios "satélite" que deben mantenerse sincronizados con los colores del tema. Los usan los diálogos
    /// (<see cref="Controls.Dialogs.AppDialog"/>): al mostrarse en un Popup, sus <c>{ThemeResource}</c> NO alcanzan los
    /// diccionarios mergeados de la app, así que cada diálogo mergea su propia copia de Theme.xaml; se registra aquí para
    /// que los cambios de color (tema u overrides) se reflejen también en esa copia y el diálogo se recolorea en vivo.
    /// </summary>
    private readonly List<ResourceDictionary> _externalDictionaries = new();

    public event EventHandler? ThemeChanged;
    #endregion

    #region Properties
    public Color AccentColor => Parse(_currentTheme.AccentColor);
    public Color AccentLightColor => Parse(_currentTheme.AccentLightColor);
    public Color AccentDarkColor => Parse(_currentTheme.AccentDarkColor);
    public Color BackgroundColor => Parse(_currentTheme.BackgroundColor);
    public Color BackgroundLightColor => Parse(_currentTheme.BackgroundLightColor);
    public Color CardBackgroundColor => Parse(_currentTheme.CardBackgroundColor);
    public Color CardBackgroundLightColor => Parse(_currentTheme.CardBackgroundLightColor);
    public Color TextColor => Parse(_currentTheme.TextColor);
    public Color TextSecondaryColor => Parse(_currentTheme.TextSecondaryColor);
    public Color DangerColor => Parse(_currentTheme.DangerColor);
    public Color SuccessColor => Parse(_currentTheme.SuccessColor);
    public Color WarningColor => Parse(_currentTheme.WarningColor);
    public Color ExtraColor1 => Parse(_currentTheme.ExtraColor1);
    public Color ExtraColor2 => Parse(_currentTheme.ExtraColor2);
    public Color ExtraColor3 => Parse(_currentTheme.ExtraColor3);
    public Color ExtraColor4 => Parse(_currentTheme.ExtraColor4);
    public Uri? LogoImageUri => string.IsNullOrWhiteSpace(_currentTheme.LogoImage) ? null : new Uri($"ms-appx:///{_currentTheme.AssetsPath}{_currentTheme.LogoImage}");
    public Uri? OverlayImageUri => string.IsNullOrWhiteSpace(_currentTheme.OverlayImage) ? null : new Uri($"ms-appx:///{_currentTheme.AssetsPath}{_currentTheme.OverlayImage}");
    public double TintOpacity => _appSettings.Theme.TintOpacity;
    public double TintSaturation => _appSettings.Theme.TintSaturation;
    public double TintBrightness => _appSettings.Theme.TintBrightness;
    public bool RandomTheme => _appSettings.Theme.RandomTheme;

    /// <summary>
    /// Si el texto sobre fondos de color se elige por contraste (ver <see cref="TextColorOn"/> y los recursos
    /// <c>TextOn&lt;Name&gt;Brush</c>). Con false, todo eso vale <see cref="TextColor"/>, como antes de la función.
    ///
    /// Quién manda: con el TEMA PURO decide el propio tema
    /// (<see cref="AppSettings.ThemeDefinition.UseContrastText"/>: solo LoL lo trae activado, por su acento claro). En
    /// cuanto se usan colores personalizados tiene precedencia el ajuste GENERAL
    /// (<see cref="AppSettings.ThemeSettings.UseContrastText"/>), que es el del pie del editor de colores: si el
    /// usuario ha cambiado los colores, el tema ya no sabe si su texto contrasta, y además el editor solo se abre con
    /// los colores personalizados activos.
    /// </summary>
    public bool UseContrastText => _appSettings.General.UseCustomColors
        ? _appSettings.Theme.UseContrastText
        : _currentTheme.UseContrastText;

    public int OverlayImageBlur => _appSettings.Theme.OverlayImageBlur;
    public double OverlayImageOpacity => _appSettings.Theme.OverlayImageOpacity;

    /// <summary>Nombre del tema realmente activo. Si <see cref="RandomTheme"/> está activo puede diferir de
    /// <c>AppSettings.Theme.Name</c> (que guarda la selección del usuario, no la elegida al azar al arrancar).</summary>
    public string CurrentThemeName => _currentThemeName;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el servicio cargando el tema definido en AppSettings.
    /// Si el nombre del tema no existe, se carga el tema por defecto.
    /// </summary>
    public ThemeService(IOptions<AppSettings> appSettings)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Inicializa el sistema de temas generando y aplicando
    /// el ResourceDictionary correspondiente al tema actual.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_appSettings.Theme.RandomTheme)
        {
            var keys = _appSettings.Theme.Themes.Keys.ToList();
            var random = new Random();
            _currentThemeName = keys[random.Next(keys.Count)];
            _currentTheme = _appSettings.Theme.Themes[_currentThemeName];
        }
        else if (_appSettings.Theme.Themes.TryGetValue(_appSettings.Theme.Name, out ThemeDefinition? theme))
        {
            _currentTheme = theme;
            _currentThemeName = _appSettings.Theme.Name;
        }
        else
        {
            // El nombre de tema configurado ya no existe (tema renombrado/eliminado, settings editados a mano o
            // corruptos): caemos al primer tema disponible en vez de lanzar KeyNotFoundException, tal y como el
            // resumen del constructor ya promete. ApplyTheme (más abajo) ya usaba este patrón seguro.
            KeyValuePair<string, ThemeDefinition> first = _appSettings.Theme.Themes.FirstOrDefault();
            _currentTheme = first.Value!;
            _currentThemeName = first.Key ?? string.Empty;
        }
        ApplyThemeResources();
        // Reaplica los colores personalizados guardados (si el ajuste está activo) sobre el tema recién cargado.
        ApplyStoredOverrides();
    }

    /// <summary>
    /// Cambia el tema activo en tiempo de ejecución.
    /// Actualiza las propiedades reactivas y regenera el diccionario de recursos.
    /// </summary>
    public void ApplyTheme(string themeName)
    {
        if (_appSettings.Theme.Themes.TryGetValue(themeName, out var theme))
        {
            _currentTheme = theme;
            _currentThemeName = themeName;

            OnPropertyChanged(nameof(AccentColor));
            OnPropertyChanged(nameof(AccentLightColor));
            OnPropertyChanged(nameof(AccentDarkColor));
            OnPropertyChanged(nameof(BackgroundColor));
            OnPropertyChanged(nameof(BackgroundLightColor));
            OnPropertyChanged(nameof(CardBackgroundColor));
            OnPropertyChanged(nameof(CardBackgroundLightColor));
            OnPropertyChanged(nameof(DangerColor));
            OnPropertyChanged(nameof(SuccessColor));
            OnPropertyChanged(nameof(TintOpacity));
            OnPropertyChanged(nameof(TintSaturation));
            OnPropertyChanged(nameof(TintBrightness));

            OnPropertyChanged(nameof(LogoImageUri));
            OnPropertyChanged(nameof(OverlayImageUri));

            OnPropertyChanged(nameof(TintOpacity));
            OnPropertyChanged(nameof(TintSaturation));
            OnPropertyChanged(nameof(TintBrightness));
            OnPropertyChanged(nameof(OverlayImageBlur));
            OnPropertyChanged(nameof(OverlayImageOpacity));

            ApplyThemeResources();

            // Un cambio de tema regenera todos los brushes desde el tema puro: los overrides previos (y sus originales
            // capturados) ya no aplican. Se descarta el registro y se reaplican los colores personalizados guardados
            // sobre el nuevo tema (recapturando sus originales), de modo que revertir siga funcionando.
            _overrides.Clear();
            _originalColors.Clear();
            ApplyStoredOverrides();

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Construye una URI absoluta de tipo <c>ms-appx</c> para un asset perteneciente
    /// al tema visual actualmente activo.
    /// </summary>
    /// <param name="relativeAssetPath">
    /// Ruta relativa del asset dentro de la carpeta base del tema.
    /// Por ejemplo: <c>Widgets/GameListControl.png</c> o <c>Icons/icon-close.png</c>.
    /// </param>
    /// <returns>
    /// URI absoluta lista para ser usada por controles WinUI como <see cref="Image"/>
    /// o <see cref="Microsoft.UI.Xaml.Media.Imaging.BitmapImage"/>.
    /// </returns>
    public Uri GetThemeAssetUri(string relativeAssetPath)
    {
        string assetsPath = _currentTheme.AssetsPath
            .Replace("\\", "/")
            .Trim('/');

        relativeAssetPath = relativeAssetPath
            .Replace("\\", "/")
            .TrimStart('/');

        return new Uri($"ms-appx:///{assetsPath}/{relativeAssetPath}");
    }

    /// <summary>
    /// Obtiene la URI del icono asociado a un widget concreto dentro del tema activo.
    /// </summary>
    /// <param name="widgetControlName">
    /// Nombre del control del widget. Normalmente coincide con el nombre del tipo,
    /// por ejemplo: <c>GameListControl</c>.
    /// </param>
    /// <returns>
    /// URI del icono del widget dentro de la carpeta <c>Widgets</c> del tema activo, o <c>null</c> si el nombre viene
    /// vacío: no hay icono genérico de reserva, así que el llamante simplemente se queda sin icono.
    /// </returns>
    public Uri? GetWidgetIconUri(string? widgetControlName)
        => string.IsNullOrWhiteSpace(widgetControlName)
            ? null
            : GetThemeAssetUri($"Widgets/{widgetControlName}.png");

    /// <summary>
    /// Obtiene la URI de un icono genérico de interfaz dentro del tema activo.
    /// </summary>
    /// <param name="iconName">
    /// Nombre del icono sin extensión. Por ejemplo: <c>icon-close</c>.
    /// </param>
    /// <returns>
    /// URI del icono dentro de la carpeta <c>Icons</c> del tema activo, o <c>null</c> si el nombre viene vacío: no hay
    /// icono genérico de reserva, así que el llamante simplemente se queda sin icono.
    /// </returns>
    public Uri? GetIconUri(string? iconName)
        => string.IsNullOrWhiteSpace(iconName)
            ? null
            : GetThemeAssetUri($"Icons/{iconName}.png");

    /// <summary>
    /// Sobrescribe EN CALIENTE un color del tema por su nombre base (p. ej. "Accent", "CardBackgroundLight",
    /// "Danger"): regenera su brush base y las variantes de opacidad (mutados in situ), refresca los brushes con
    /// nombre de los controles y avisa vía <see cref="ThemeChanged"/>, de modo que todo lo que use ese color se
    /// recolorea EN VIVO sin reconstruir el árbol. El cambio se refleja también en la definición del tema en memoria
    /// (NO se persiste en disco). Pensado para probar/afinar colores del tema al vuelo.
    /// </summary>
    public void OverrideColor(string baseName, Color color)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return;

        // Guarda el color ORIGINAL la primera vez que se sobrescribe este nombre base (antes de tocarlo), para revertir.
        if (!_originalColors.ContainsKey(baseName))
            _originalColors[baseName] = GetThemeColor(baseName);

        _overrides[baseName] = ToHex(color);
        ApplyColorInternal(baseName, color);
    }

    /// <summary>
    /// Revierte un color del tema a su valor ORIGINAL (el que tenía antes del primer override en caliente), si se había
    /// sobrescrito. No hace nada si el color no se había tocado.
    /// </summary>
    public void RevertColor(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return;

        if (_originalColors.TryGetValue(baseName, out Color original))
        {
            ApplyColorInternal(baseName, original);
            _originalColors.Remove(baseName);
            _overrides.Remove(baseName);
        }
    }

    /// <summary>Overrides de color activos (nombre base -> hex #RRGGBB). Es la instantánea que se persiste en settings al aceptar.</summary>
    public IReadOnlyDictionary<string, string> CurrentOverrides => _overrides;

    /// <summary>
    /// Aplica los overrides de color guardados en settings (<see cref="AppSettings.GeneralSettings.CustomColors"/>) sobre
    /// el tema actual, EN CALIENTE. No hace nada si <see cref="AppSettings.GeneralSettings.UseCustomColors"/> está desactivado
    /// o no hay overrides. Se llama al arrancar (tras cargar el tema) y al reactivar los colores personalizados.
    /// </summary>
    public void ApplyStoredOverrides()
    {
        if (!_appSettings.General.UseCustomColors)
            return;

        foreach (KeyValuePair<string, string> entry in _appSettings.General.CustomColors)
        {
            if (!string.IsNullOrWhiteSpace(entry.Value) && entry.Value.Length >= 7)
                OverrideColor(entry.Key, Parse(entry.Value));
        }
    }

    /// <summary>
    /// Revierte TODOS los overrides activos a su color original (tema puro) y limpia el registro. Lo usa el ajuste
    /// "Usar colores personalizados" al desactivarse.
    /// </summary>
    public void ClearOverrides()
    {
        // Copia de claves porque RevertColor muta _overrides/_originalColors.
        foreach (string baseName in _overrides.Keys.ToList())
            RevertColor(baseName);
    }

    /// <summary>
    /// Restaura los overrides a una instantánea previa de <see cref="CurrentOverrides"/>: revierte todo al tema puro y
    /// reaplica la instantánea. Lo usa "Cancelar" del editor de colores para deshacer los cambios de la sesión de edición.
    /// </summary>
    public void RestoreOverrides(IReadOnlyDictionary<string, string> snapshot)
    {
        ClearOverrides();
        if (snapshot is null)
            return;

        foreach (KeyValuePair<string, string> entry in snapshot)
        {
            if (!string.IsNullOrWhiteSpace(entry.Value) && entry.Value.Length >= 7)
                OverrideColor(entry.Key, Parse(entry.Value));
        }
    }

    /// <summary>Aplica EN CALIENTE un color a un nombre base (regenera brush + opacidades in situ y avisa), sin tocar el registro de originales.</summary>
    private void ApplyColorInternal(string baseName, Color color)
    {
        if (_currentDictionary is null)
            ApplyThemeResources();   // asegura el diccionario persistente en Application.Resources

        // Refleja el color en la definición del tema activo (coherencia con la propiedad reactiva y un re-apply).
        _currentTheme.GetType().GetProperty(baseName + "Color")?.SetValue(_currentTheme, ToHex(color));

        // Color/Brush base + opacidades + TextOn* (brushes mutados in situ). Si lo que cambia es el propio color de
        // texto, hay que rehacer TODOS los nombres base: el TextOn* de cada uno se calcula contra él.
        if (string.Equals(baseName, "Text", StringComparison.Ordinal))
            AddAllThemeColors(_currentDictionary!);
        else
            AddThemeColorResources(_currentDictionary!, baseName, color);
        RefreshNamedControlBrushes(Application.Current.Resources);
        SyncExternalDictionaries();

        OnPropertyChanged(baseName + "Color");
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Registra un diccionario de recursos externo (p. ej. el de un <see cref="Controls.Dialogs.AppDialog"/> mostrado en
    /// un Popup) para mantener sus brushes de color sincronizados con el tema. Al registrarlo se sincroniza de inmediato
    /// con el estado actual (tema + overrides), de modo que el diálogo abre ya con los colores correctos.
    /// </summary>
    public void RegisterExternalResources(ResourceDictionary dictionary)
    {
        if (dictionary is null || _externalDictionaries.Contains(dictionary))
            return;

        _externalDictionaries.Add(dictionary);
        ApplyColorsToDictionary(dictionary);
    }

    /// <summary>Deja de sincronizar un diccionario externo (al cerrarse el diálogo que lo registró).</summary>
    public void UnregisterExternalResources(ResourceDictionary dictionary)
    {
        if (dictionary is not null)
        {
            _externalDictionaries.Remove(dictionary);
        }
    }

    /// <summary>

    /// <summary>
    /// Color de texto con el que pintar SOBRE <paramref name="background"/> (que debe ser el fondo real al 100%, no
    /// una variante con opacidad): el que mejor contraste da entre <see cref="TextColor"/> y
    /// <see cref="Helpers.ContrastHelper.DarkText"/>. Punto ÚNICO de decisión: si
    /// <see cref="UseContrastText"/> está desactivado devuelve siempre <see cref="TextColor"/>.
    ///
    /// Lo usan tanto los recursos <c>TextOn&lt;Name&gt;Brush</c> como los sitios que resuelven el foreground por
    /// código (converters, gráficas, brushes de slot), que no ven los <c>{ThemeResource}</c>.
    /// </summary>
    public Color TextColorOn(Color background)
        => UseContrastText
            ? Helpers.ContrastHelper.BestForeground(background, TextColor, Helpers.ContrastHelper.DarkText)
            : TextColor;

    /// <summary>
    /// Repuebla los diccionarios (el de la app y las copias de los diálogos) con el tema actual y avisa vía
    /// <see cref="ThemeChanged"/>, sin cambiar de tema. Se usa cuando cambia un ajuste que altera los recursos
    /// derivados pero no los colores base, como <see cref="UseContrastText"/>: al ir todo por <c>UpsertBrush</c>, los
    /// brushes se mutan in situ y el cambio se ve EN VIVO.
    /// </summary>
    public void RefreshThemeResources()
    {
        ApplyThemeResources();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Color actual del tema para un nombre base (p. ej. "Accent"); negro si el nombre no corresponde a ninguno.</summary>
    public Color GetThemeColor(string baseName)
        => GetType().GetProperty(baseName + "Color")?.GetValue(this) is Color c ? c : Color.FromArgb(255, 0, 0, 0);

    /// <summary>Formatea un color como <c>#RRGGBB</c> (el formato que espera la definición del tema).</summary>
    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    #endregion

    #region Methods (private)
    /// Example:
    /// AddThemeColorResources(dict, "Accent", AccentColor);
    /// 
    /// Generates:
    /// - AccentColor
    /// - AccentBrush
    /// - TextOnAccentColor
    /// - TextOnAccentBrush
    /// - AccentColorTransparent20
    /// - AccentColorTransparent40
    /// - AccentColorTransparent60
    /// - AccentColorTransparent80
    /// - AccentBrushTransparent20
    /// - AccentBrushTransparent40
    /// - AccentBrushTransparent60
    /// - AccentBrushTransparent80
    /// </summary>
    /// <param name="dict">Target ResourceDictionary.</param>
    /// <param name="resourceName">Base resource name, for example "Accent", "Background", "Text".</param>
    /// <param name="color">Base color.</param>
    /// <remarks>
    /// UPSERT en vez de crear: si el brush ya existe en el diccionario (cambio de tema en caliente), se muta su
    /// <see cref="SolidColorBrush.Color"/> en lugar de crear una instancia nueva. Así todos los elementos que ya
    /// resolvieron ese brush (vía <c>{ThemeResource}</c> o <c>{StaticResource}</c>) se actualizan en vivo sin depender
    /// de re-evaluar los bindings. Los recursos de tipo <see cref="Color"/> (usados p. ej. en gradientes) sí se
    /// reemplazan, pero al ser tipos por valor no se propagan a elementos ya cargados: esos controles deben
    /// reconstruirse al recibir <see cref="ThemeChanged"/>.
    /// </remarks>
    private void AddThemeColorResources(ResourceDictionary dict, string resourceName, Color color)
    {
        if (dict is null)
        {
            throw new ArgumentNullException(nameof(dict));
        }

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("The resource name cannot be null or empty.", nameof(resourceName));
        }

        // Base color and base brush.
        dict[$"{resourceName}Color"] = color;
        UpsertBrush(dict, $"{resourceName}Brush", color);

        // Color de texto para pintar ENCIMA de este color al 100%: el que mejor contraste da entre el del tema y el
        // oscuro de ContrastHelper. Con el ajuste desactivado es el del tema, o sea lo mismo que TextBrush, de forma
        // que usar TextOn<Name>Brush en el XAML no cambia nada hasta que se activa.
        Color textOn = TextColorOn(color);
        dict[$"TextOn{resourceName}Color"] = textOn;
        UpsertBrush(dict, $"TextOn{resourceName}Brush", textOn);

        double[] opacityLevels = [0.2, 0.4, 0.6, 0.8];

        foreach (double opacity in opacityLevels)
        {
            byte alpha = (byte)Math.Round(255 * opacity);
            int suffix = (int)(opacity * 100);
            Color transparentColor = Color.FromArgb(alpha, color.R, color.G, color.B);

            dict[$"{resourceName}ColorOpacity{suffix}"] = transparentColor;
            UpsertBrush(dict, $"{resourceName}BrushOpacity{suffix}", transparentColor);
        }
    }

    /// <summary>
    /// Inserta o actualiza un <see cref="SolidColorBrush"/> en el diccionario: si ya existe con esa clave, muta su
    /// color in situ (actualización en vivo de todo lo que ya lo referencia); si no, crea la instancia.
    ///
    /// La búsqueda entra en los diccionarios MERGEADOS (<see cref="TryFindResource"/>): en la copia de
    /// <c>Theme.xaml</c> que registra cada diálogo, los brushes viven en el merge, no en el diccionario raíz. Mirando
    /// solo el raíz se creaba ahí un brush NUEVO y los controles del diálogo seguían pintando con la instancia
    /// anterior, que es justo lo que hacía que la ventana de configuración no se recoloreara en caliente.
    /// </summary>
    private static void UpsertBrush(ResourceDictionary dict, string key, Color color)
    {
        if (TryFindResource(dict, key, out object? existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            dict[key] = new SolidColorBrush(color);
        }
    }

    /// <summary>
    /// Convierte un color en formato hexadecimal (#RRGGBB)
    /// a un objeto Color de WinUI.
    /// </summary>
    private static Color Parse(string hex)
    {
        return ColorHelper.FromArgb(255, byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber)
        );
    }

    /// <summary>
    /// Aplica los recursos del tema actual sobre un ÚNICO diccionario persistente registrado en Application.Resources.
    /// En el primer uso lo crea y lo añade; en los sucesivos (cambio de tema en caliente) reutiliza el mismo diccionario
    /// y muta los brushes existentes in situ, de modo que todo lo que ya los referencia se actualiza en vivo sin
    /// reconstruir el árbol ni re-evaluar bindings. NO se reemplaza el diccionario (eso rompía la actualización en
    /// caliente: los elementos ya cargados seguían apuntando a los brushes del diccionario anterior).
    /// </summary>
    private void ApplyThemeResources()
    {
        if (_currentDictionary == null)
        {
            _currentDictionary = new ResourceDictionary();
            Application.Current.Resources.MergedDictionaries.Add(_currentDictionary);
        }

        PopulateResourceDictionary(_currentDictionary);
    }

    /// <summary>
    /// Vuelca (upsert) los brushes y valores derivados del tema actual en <paramref name="dict"/>.
    /// </summary>
    private void PopulateResourceDictionary(ResourceDictionary dict)
    {
        AddAllThemeColors(dict);

        dict["LogoImage"] = LogoImageUri;
        dict["OverlayImageUri"] = OverlayImageUri;

        dict["TintOpacity"] = TintOpacity;
        dict["TintSaturation"] = TintSaturation;
        dict["TintBrightness"] = TintBrightness;
        dict["OverlayImageBlur"] = OverlayImageBlur;
        dict["OverlayImageOpacity"] = OverlayImageOpacity;

        RefreshNamedControlBrushes(Application.Current.Resources);
        SyncExternalDictionaries();
    }

    /// <summary>Vuelca todos los colores del tema (brush base + variantes de opacidad) en <paramref name="dict"/>.</summary>
    private void AddAllThemeColors(ResourceDictionary dict)
    {
        AddThemeColorResources(dict, "Accent", AccentColor);
        AddThemeColorResources(dict, "AccentLight", AccentLightColor);
        AddThemeColorResources(dict, "AccentDark", AccentDarkColor);

        AddThemeColorResources(dict, "Background", BackgroundColor);
        AddThemeColorResources(dict, "BackgroundLight", BackgroundLightColor);

        AddThemeColorResources(dict, "CardBackground", CardBackgroundColor);
        AddThemeColorResources(dict, "CardBackgroundLight", CardBackgroundLightColor);

        AddThemeColorResources(dict, "Text", TextColor);
        AddThemeColorResources(dict, "TextSecondary", TextSecondaryColor);

        AddThemeColorResources(dict, "Danger", DangerColor);
        AddThemeColorResources(dict, "Success", SuccessColor);
        AddThemeColorResources(dict, "Warning", WarningColor);

        // Colores genéricos extra (pills que antes eran fijos): mismo tratamiento (brush + opacidades).
        AddThemeColorResources(dict, "ExtraColor1", ExtraColor1);
        AddThemeColorResources(dict, "ExtraColor2", ExtraColor2);
        AddThemeColorResources(dict, "ExtraColor3", ExtraColor3);
        AddThemeColorResources(dict, "ExtraColor4", ExtraColor4);
    }

    /// <summary>
    /// Sincroniza un diccionario externo (copia de Theme.xaml de un diálogo) con los colores actuales del tema: vuelca
    /// los brushes de color (mutando in situ los existentes) y refresca los brushes con nombre de los controles. Así el
    /// contenido del diálogo, que resuelve contra su propia copia, se recolorea en vivo igual que la ventana principal.
    /// </summary>
    private void ApplyColorsToDictionary(ResourceDictionary dict)
    {
        AddAllThemeColors(dict);
        RefreshNamedControlBrushes(dict);
    }

    /// <summary>Reaplica los colores actuales a todos los diccionarios externos registrados (diálogos abiertos).</summary>
    private void SyncExternalDictionaries()
    {
        foreach (ResourceDictionary dict in _externalDictionaries)
            ApplyColorsToDictionary(dict);
    }

    /// <summary>
    /// Muchos controles (botones, RadioButton, CheckBox, ComboBox y sus ítems, TextBox y sliders) usan brushes con
    /// NOMBRE definidos en <c>Resources/Buttons.xaml</c> y <c>Resources/GenericControls.xaml</c> (p. ej.
    /// <c>ButtonBrushBorder</c>, <c>ComboBoxItemBackgroundSelected</c>), cuyo color se fijó UNA vez desde un recurso de
    /// tipo <see cref="Color"/>. Al ser instancias distintas de las que genera este servicio, no se actualizan solas al
    /// cambiar de tema en caliente. Aquí se localizan por clave y se muta su color in situ para que todos esos
    /// controles se recoloreen en vivo.
    ///
    /// El mapeo clave -> color reproduce el de esos XAML, así que hay que mantenerlo si se tocan. Se puede REGENERAR
    /// con:
    /// <code>
    /// grep -rh 'SolidColorBrush x:Key="[^"]*" Color="{ThemeResource [^}]*}"' Resources/*.xaml \
    ///   | sed 's/.*x:Key="\([^"]*\)" Color="{ThemeResource \([^}]*\)}".*/\1|\2/'
    /// </code>
    /// (las variantes <c>...ColorOpacity60</c> se traducen a <c>WithAlpha(&lt;base&gt;Color, 0.6)</c>).
    ///
    /// Los estados deshabilitados llevan su propia <see cref="Brush.Opacity"/> en el XAML: aquí solo se toca el color
    /// (la opacidad del brush se conserva).
    /// </summary>
    private void RefreshNamedControlBrushes(ResourceDictionary target)
    {
        // (clave del brush en Buttons.xaml / GenericControls.xaml, color del tema con el que debe pintarse)
        (string Key, Color Color)[] map =
        {
            // Botones (Buttons.xaml)
            ("ButtonBrushBackgroundSubtle", WithAlpha(CardBackgroundLightColor, 0.6)),
            ("ButtonBrushBorder", WithAlpha(CardBackgroundLightColor, 0.6)),
            ("ButtonBrushForeground", TextSecondaryColor),
            ("ButtonBrushBackgroundSubtlePointerOver", CardBackgroundLightColor),
            ("ButtonBrushBorderPointerOver", CardBackgroundLightColor),
            ("ButtonBrushForegroundPointerOver", AccentLightColor),
            ("ButtonBrushBackgroundSubtlePressed", CardBackgroundLightColor),
            ("ButtonBrushBorderPressed", CardBackgroundLightColor),
            ("ButtonBrushForegroundPressed", AccentColor),
            // Un botón "checked" se pinta con el acento, así que su contenido pasa por TextColorOn. El PointerOver es
            // la excepción: su fondo lleva opacidad (mezcla con lo de debajo), y ahí el contraste no aplica.
            ("ButtonBrushBackgroundChecked", AccentDarkColor),
            ("ButtonBrushBorderChecked", AccentDarkColor),
            ("ButtonBrushForegroundChecked", TextColorOn(AccentDarkColor)),
            ("ButtonBrushBackgroundCheckedPointerOver", WithAlpha(AccentLightColor, 0.6)),
            ("ButtonBrushBorderCheckedPointerOver", AccentLightColor),
            ("ButtonBrushForegroundCheckedPointerOver", TextColor),
            ("ButtonBrushBackgroundCheckedPressed", AccentLightColor),
            ("ButtonBrushBorderCheckedPressed", AccentLightColor),
            ("ButtonBrushForegroundCheckedPressed", TextColorOn(AccentLightColor)),
            ("ButtonBrushBackgroundSubtleDisabled", CardBackgroundLightColor),
            ("ButtonBrushBorderDisabled", CardBackgroundLightColor),
            ("ButtonBrushForegroundDisabled", TextSecondaryColor),
            ("ButtonBrushBackgroundCheckedDisabled", AccentDarkColor),
            ("ButtonBrushBorderCheckedDisabled", AccentDarkColor),
            ("ButtonBrushForegroundCheckedDisabled", TextColorOn(AccentDarkColor)),

            // RadioButton (Buttons.xaml)
            ("RadioButtonForeground", TextColor),
            ("RadioButtonForegroundPointerOver", TextColor),
            ("RadioButtonForegroundPressed", TextColor),
            ("RadioButtonForegroundDisabled", TextColor),
            ("RadioButtonOuterEllipseStroke", TextSecondaryColor),
            ("RadioButtonOuterEllipseStrokePointerOver", AccentLightColor),
            ("RadioButtonOuterEllipseStrokePressed", AccentColor),
            ("RadioButtonOuterEllipseStrokeDisabled", TextSecondaryColor),
            ("RadioButtonOuterEllipseCheckedStroke", AccentColor),
            ("RadioButtonOuterEllipseCheckedStrokePointerOver", AccentLightColor),
            ("RadioButtonOuterEllipseCheckedStrokePressed", AccentLightColor),
            ("RadioButtonOuterEllipseCheckedStrokeDisabled", AccentColor),
            ("RadioButtonCheckGlyphFill", AccentColor),
            ("RadioButtonCheckGlyphFillPointerOver", AccentLightColor),
            ("RadioButtonCheckGlyphFillPressed", AccentLightColor),
            ("RadioButtonCheckGlyphFillDisabled", AccentColor),
            ("RadioButtonCheckGlyphStroke", AccentColor),
            ("RadioButtonCheckGlyphStrokeChecked", AccentColor),
            ("RadioButtonCheckGlyphStrokeCheckedPointerOver", AccentLightColor),
            ("RadioButtonCheckGlyphStrokeCheckedPressed", AccentLightColor),
            ("RadioButtonCheckGlyphStrokeCheckedDisabled", AccentColor),
            ("RadioButtonCheckGlyphStrokePointerOver", AccentLightColor),
            ("RadioButtonCheckGlyphStrokePressed", AccentLightColor),
            ("RadioButtonCheckGlyphStrokeDisabled", AccentColor),

            // ComboBox (GenericControls.xaml)
            ("ComboBoxDropDownBackground", BackgroundLightColor),
            ("ComboBoxDropDownBorderBrush", AccentColor),

            // Items del desplegable de un ComboBox (GenericControls.xaml). El item resaltado o seleccionado se pinta
            // con el acento, así que su texto pasa por TextColorOn (ver docs/Plan-Contraste-Texto.md): en reposo el
            // fondo es transparente y ahí se queda el color de texto normal.
            ("ComboBoxItemForeground", TextColor),
            ("ComboBoxItemBackgroundPointerOver", AccentLightColor),
            ("ComboBoxItemBorderBrushPointerOver", AccentLightColor),
            ("ComboBoxItemForegroundPointerOver", TextColorOn(AccentLightColor)),
            ("ComboBoxItemBackgroundPressed", AccentLightColor),
            ("ComboBoxItemBorderBrushPressed", AccentLightColor),
            ("ComboBoxItemForegroundPressed", TextColorOn(AccentLightColor)),
            ("ComboBoxItemBackgroundSelected", AccentColor),
            ("ComboBoxItemBorderBrushSelected", AccentColor),
            ("ComboBoxItemForegroundSelected", TextColorOn(AccentColor)),
            ("ComboBoxItemBackgroundSelectedPointerOver", AccentLightColor),
            ("ComboBoxItemBorderBrushSelectedPointerOver", AccentLightColor),
            ("ComboBoxItemForegroundSelectedPointerOver", TextColorOn(AccentLightColor)),
            ("ComboBoxItemBackgroundSelectedPressed", AccentLightColor),
            ("ComboBoxItemBorderBrushSelectedPressed", AccentLightColor),
            ("ComboBoxItemForegroundSelectedPressed", TextColorOn(AccentLightColor)),

            // ComboBox (GenericControls.xaml)
            ("ComboBoxForeground", TextColor),
            ("ComboBoxForegroundPointerOver", TextColor),
            ("ComboBoxForegroundPressed", TextColor),
            ("ComboBoxForegroundDisabled", TextSecondaryColor),
            ("ComboBoxForegroundFocused", TextColor),
            ("ComboBoxForegroundFocusedPressed", TextColor),
            ("ComboBoxBackground", WithAlpha(CardBackgroundLightColor, 0.6)),
            ("ComboBoxBackgroundPointerOver", CardBackgroundLightColor),
            ("ComboBoxBackgroundPressed", CardBackgroundLightColor),
            ("ComboBoxBackgroundDisabled", CardBackgroundLightColor),
            ("ComboBoxBackgroundFocused", CardBackgroundLightColor),
            ("ComboBoxBorderBrush", WithAlpha(CardBackgroundLightColor, 0.6)),
            ("ComboBoxBorderBrushPointerOver", AccentLightColor),
            ("ComboBoxBorderBrushPressed", AccentColor),
            ("ComboBoxBorderBrushDisabled", CardBackgroundLightColor),
            ("ComboBoxBorderBrushFocused", AccentColor),
            ("ComboBoxDropDownGlyphForeground", AccentColor),

            // TextBox (GenericControls.xaml)
            ("TextControlBorderBrush", TextSecondaryColor),
            ("TextControlBorderBrushPointerOver", AccentLightColor),
            ("TextControlBorderBrushFocused", AccentColor),
            ("TextControlBorderBrushDisabled", TextSecondaryColor),

            // CheckBox (GenericControls.xaml)
            ("CheckBoxCheckBackgroundStrokeUnchecked", TextSecondaryColor),
            ("CheckBoxCheckBackgroundStrokeUncheckedPointerOver", TextColor),
            ("CheckBoxCheckBackgroundStrokeUncheckedPressed", TextColor),
            ("CheckBoxCheckBackgroundFillChecked", AccentColor),
            ("CheckBoxCheckBackgroundFillCheckedPointerOver", AccentLightColor),
            ("CheckBoxCheckBackgroundFillCheckedPressed", AccentDarkColor),
            ("CheckBoxCheckBackgroundStrokeChecked", AccentColor),
            ("CheckBoxCheckBackgroundStrokeCheckedPointerOver", AccentLightColor),
            ("CheckBoxCheckBackgroundStrokeCheckedPressed", AccentDarkColor),
            ("CheckBoxCheckGlyphForegroundChecked", BackgroundColor),
            ("CheckBoxCheckGlyphForegroundCheckedPointerOver", BackgroundColor),
            ("CheckBoxCheckGlyphForegroundCheckedPressed", BackgroundColor),

            // Sliders (GenericControls.xaml). Los *Disabled llevan su Opacity en el XAML: aqui solo el color.
            ("SliderThumbBackground", AccentColor),
            ("SliderThumbBackgroundPointerOver", AccentLightColor),
            ("SliderThumbBackgroundPressed", AccentLightColor),
            ("SliderThumbBackgroundDisabled", TextColor),
            ("SliderTrackFill", AccentDarkColor),
            ("SliderTrackFillPointerOver", AccentDarkColor),
            ("SliderTrackFillPressed", AccentDarkColor),
            ("SliderTrackFillDisabled", AccentDarkColor),
            ("SliderTrackValueFill", AccentColor),
            ("SliderTrackValueFillPointerOver", AccentLightColor),
            ("SliderTrackValueFillPressed", AccentLightColor),
            ("SliderTrackValueFillDisabled", TextColor),
            ("SliderHeaderForeground", TextColor),
            ("SliderHeaderForegroundDisabled", TextColor),
            ("SliderTickBarFill", TextColor),
            ("SliderTickBarFillDisabled", TextColor),
            ("SliderInlineTickBarFill", TextColor),
        };

        int applied = 0;
        foreach ((string key, Color color) in map)
        {
            if (TryFindResource(target, key, out object? value) && value is SolidColorBrush brush)
            {
                brush.Color = color;
                applied++;
            }
        }


        // Red de seguridad: si NO se encontró ninguna clave, el tema no se está aplicando a estos controles (es lo que
        // pasaba cuando la búsqueda no miraba los diccionarios mergeados). Solo se registra ese caso, para no ensuciar.
        if (applied == 0)
            ExceptionService.LogToFile(null, $"Theme: no se encontró ninguno de los {map.Length} brushes con nombre; los controles no se recolorearán.");
    }

    /// <summary>
    /// Busca una clave en un diccionario y, si no está, en sus <see cref="ResourceDictionary.MergedDictionaries"/> en
    /// profundidad. Hace falta porque el lookup DIRECTO de <see cref="ResourceDictionary"/> (indexador / TryGetValue)
    /// NO mira los diccionarios mergeados: eso solo lo hace la resolución de <c>{StaticResource}</c>/<c>{ThemeResource}</c>
    /// del XAML. Los brushes con nombre de esta app cuelgan dos niveles por debajo
    /// (<c>App.xaml</c> -> <c>Theme.xaml</c> -> <c>Buttons.xaml</c> / <c>GenericControls.xaml</c>), así que sin esto
    /// <see cref="RefreshNamedControlBrushes"/> no encontraba NINGUNA clave y no repintaba nada.
    ///
    /// Los merges se recorren en orden INVERSO, que es el de prioridad real en XAML: el último diccionario mergeado
    /// gana. Importa porque muchas de estas claves (p. ej. <c>SliderTrackFill</c>) existen también en
    /// <c>XamlControlsResources</c>, mergeado ANTES que el tema de la app: hay que quedarse con el de la app.
    /// </summary>
    private static bool TryFindResource(ResourceDictionary dict, string key, out object? value)
    {
        if (dict.TryGetValue(key, out value))
            return true;

        for (int i = dict.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (TryFindResource(dict.MergedDictionaries[i], key, out value))
                return true;
        }

        value = null;
        return false;
    }

    /// <summary>Devuelve el color con la componente alfa ajustada a <paramref name="opacity"/> (0..1).</summary>
    private static Color WithAlpha(Color color, double opacity)
        => Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B);
    #endregion
}
