using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MM4LB.Models;
using Windows.UI;
using static MM4LB.Models.AppSettings;

namespace MM4LB.Services;

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
    public Color BadgeNoImageColor => Parse(_currentTheme.BadgeNoImageColor);
    public Color BadgeOneImageColor => Parse(_currentTheme.BadgeOneImageColor);
    public Color BadgeMoreThanOneImageColor => Parse(_currentTheme.BadgeMoreThanOneImageColor);
    public Color ExtraColor1 => Parse(_currentTheme.ExtraColor1);
    public Color ExtraColor2 => Parse(_currentTheme.ExtraColor2);
    public Color ExtraColor3 => Parse(_currentTheme.ExtraColor3);
    public Color ExtraColor4 => Parse(_currentTheme.ExtraColor4);
    public Color ExtraColor5 => Parse(_currentTheme.ExtraColor5);
    public Uri? BackgroundImageUri => string.IsNullOrWhiteSpace(_currentTheme.BackgroundImage) ? null : new Uri($"ms-appx:///{_currentTheme.AssetsPath}{_currentTheme.BackgroundImage}");
    public Uri? LogoImageUri => string.IsNullOrWhiteSpace(_currentTheme.LogoImage) ? null : new Uri($"ms-appx:///{_currentTheme.AssetsPath}{_currentTheme.LogoImage}");
    public Uri? OverlayImageUri => string.IsNullOrWhiteSpace(_currentTheme.OverlayImage) ? null : new Uri($"ms-appx:///{_currentTheme.AssetsPath}{_currentTheme.OverlayImage}");
    public double TintOpacity => _appSettings.Theme.TintOpacity;
    public double TintSaturation => _appSettings.Theme.TintSaturation;
    public double TintBrightness => _appSettings.Theme.TintBrightness;
    public bool RandomTheme => _appSettings.Theme.RandomTheme;
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

            OnPropertyChanged(nameof(BackgroundImageUri));
            OnPropertyChanged(nameof(LogoImageUri));
            OnPropertyChanged(nameof(OverlayImageUri));

            OnPropertyChanged(nameof(TintOpacity));
            OnPropertyChanged(nameof(TintSaturation));
            OnPropertyChanged(nameof(TintBrightness));
            OnPropertyChanged(nameof(OverlayImageBlur));
            OnPropertyChanged(nameof(OverlayImageOpacity));

            ApplyThemeResources();

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
    /// URI del icono del widget dentro de la carpeta <c>Widgets</c> del tema activo.
    /// Si el nombre recibido está vacío, se utiliza <c>DefaultWidget.png</c>.
    /// </returns>
    public Uri GetWidgetIconUri(string widgetControlName)
    {
        if (string.IsNullOrWhiteSpace(widgetControlName))
        {
            widgetControlName = "DefaultWidget";
        }

        return GetThemeAssetUri($"Widgets/{widgetControlName}.png");
    }

    /// <summary>
    /// Obtiene la URI de un icono genérico de interfaz dentro del tema activo.
    /// </summary>
    /// <param name="iconName">
    /// Nombre del icono sin extensión. Por ejemplo: <c>icon-close</c>.
    /// </param>
    /// <returns>
    /// URI del icono dentro de la carpeta <c>Icons</c> del tema activo.
    /// Si el nombre recibido está vacío, se utiliza <c>icon-default.png</c>.
    /// </returns>
    public Uri GetIconUri(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            iconName = "icon-default";
        }

        return GetThemeAssetUri($"Icons/{iconName}.png");
    }

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
        }
    }

    /// <summary>Aplica EN CALIENTE un color a un nombre base (regenera brush + opacidades in situ y avisa), sin tocar el registro de originales.</summary>
    private void ApplyColorInternal(string baseName, Color color)
    {
        if (_currentDictionary is null)
            ApplyThemeResources();   // asegura el diccionario persistente en Application.Resources

        // Refleja el color en la definición del tema activo (coherencia con la propiedad reactiva y un re-apply).
        _currentTheme.GetType().GetProperty(baseName + "Color")?.SetValue(_currentTheme, ToHex(color));

        AddThemeColorResources(_currentDictionary!, baseName, color);   // Color/Brush base + opacidades (brushes in situ)
        RefreshNamedControlBrushes();

        OnPropertyChanged(baseName + "Color");
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
    private static void AddThemeColorResources(ResourceDictionary dict, string resourceName, Color color)
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
    /// </summary>
    private static void UpsertBrush(ResourceDictionary dict, string key, Color color)
    {
        if (dict.TryGetValue(key, out object? existing) && existing is SolidColorBrush brush)
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

        AddThemeColorResources(dict, "BadgeNoImage", BadgeNoImageColor);
        AddThemeColorResources(dict, "BadgeOneImage", BadgeOneImageColor);
        AddThemeColorResources(dict, "BadgeMoreThanOneImage", BadgeMoreThanOneImageColor);

        // Colores genéricos extra (pills que antes eran fijos): mismo tratamiento (brush + opacidades).
        AddThemeColorResources(dict, "ExtraColor1", ExtraColor1);
        AddThemeColorResources(dict, "ExtraColor2", ExtraColor2);
        AddThemeColorResources(dict, "ExtraColor3", ExtraColor3);
        AddThemeColorResources(dict, "ExtraColor4", ExtraColor4);
        AddThemeColorResources(dict, "ExtraColor5", ExtraColor5);

        dict["BackgroundImage"] = BackgroundImageUri;
        dict["LogoImage"] = LogoImageUri;
        dict["OverlayImageUri"] = OverlayImageUri;

        dict["TintOpacity"] = TintOpacity;
        dict["TintSaturation"] = TintSaturation;
        dict["TintBrightness"] = TintBrightness;
        dict["OverlayImageBlur"] = OverlayImageBlur;
        dict["OverlayImageOpacity"] = OverlayImageOpacity;

        RefreshNamedControlBrushes();
    }

    /// <summary>
    /// Muchos controles (botones AppBar/Toggle/Split/Icon y sliders) usan brushes con NOMBRE definidos en
    /// <c>Resources/Buttons.xaml</c> y <c>Resources/GenericControls.xaml</c> (p. ej. <c>ButtonBrushBorder</c>,
    /// <c>SliderTrackFill</c>), cuyo color se fijó una vez desde un recurso de tipo <see cref="Color"/>. Al ser instancias
    /// distintas de las que genera este servicio, no se actualizaban al cambiar de tema en caliente. Aquí se localizan por
    /// clave y se muta su color in situ para que todos esos controles se actualicen en vivo.
    ///
    /// El mapeo clave -> color reproduce el de esos XAML. Los estados deshabilitados llevan su propia
    /// <see cref="Brush.Opacity"/> en el XAML: aquí solo se toca el color (la opacidad del brush se conserva).
    /// </summary>
    private void RefreshNamedControlBrushes()
    {
        // (clave del brush en Buttons.xaml / GenericControls.xaml, color del tema con el que debe pintarse)
        (string Key, Color Color)[] map =
        {
            // Normal
            ("ButtonBrushBackgroundSubtle", WithAlpha(CardBackgroundLightColor, 0.6)),
            ("ButtonBrushBorder", WithAlpha(CardBackgroundLightColor, 0.6)),
            ("ButtonBrushForeground", TextSecondaryColor),
            // PointerOver
            ("ButtonBrushBackgroundSubtlePointerOver", CardBackgroundLightColor),
            ("ButtonBrushBorderPointerOver", CardBackgroundLightColor),
            ("ButtonBrushForegroundPointerOver", AccentLightColor),
            // Pressed
            ("ButtonBrushBackgroundSubtlePressed", CardBackgroundLightColor),
            ("ButtonBrushBorderPressed", CardBackgroundLightColor),
            ("ButtonBrushForegroundPressed", AccentColor),
            // Checked
            ("ButtonBrushBackgroundChecked", AccentDarkColor),
            ("ButtonBrushBorderChecked", AccentDarkColor),
            ("ButtonBrushForegroundChecked", TextColor),
            // Checked + PointerOver
            ("ButtonBrushBackgroundCheckedPointerOver", WithAlpha(AccentLightColor, 0.6)),
            ("ButtonBrushBorderCheckedPointerOver", AccentLightColor),
            ("ButtonBrushForegroundCheckedPointerOver", TextColor),
            // Checked + Pressed
            ("ButtonBrushBackgroundCheckedPressed", AccentLightColor),
            ("ButtonBrushBorderCheckedPressed", AccentLightColor),
            ("ButtonBrushForegroundCheckedPressed", TextColor),
            // Disabled (la opacidad del brush la fija el XAML; aquí solo el color)
            ("ButtonBrushBackgroundSubtleDisabled", CardBackgroundLightColor),
            ("ButtonBrushBorderDisabled", CardBackgroundLightColor),
            ("ButtonBrushForegroundDisabled", TextSecondaryColor),
            // Checked + Disabled
            ("ButtonBrushBackgroundCheckedDisabled", AccentDarkColor),
            ("ButtonBrushBorderCheckedDisabled", AccentDarkColor),
            ("ButtonBrushForegroundCheckedDisabled", TextColor),

            // Sliders (GenericControls.xaml). Los *Disabled llevan su Opacity en el XAML: aquí solo el color.
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

        ResourceDictionary appResources = Application.Current.Resources;
        foreach ((string key, Color color) in map)
        {
            if (appResources.TryGetValue(key, out object? value) && value is SolidColorBrush brush)
            {
                brush.Color = color;
            }
        }
    }

    /// <summary>Devuelve el color con la componente alfa ajustada a <paramref name="opacity"/> (0..1).</summary>
    private static Color WithAlpha(Color color, double opacity)
        => Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B);
    #endregion
}