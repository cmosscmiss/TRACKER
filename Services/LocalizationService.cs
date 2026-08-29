using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Tracker.Services;

/// <summary>
/// Servicio central de localización (i18n). Fuente única del idioma activo y de la resolución de textos de UI desde
/// los recursos <c>Strings/Resources.resx</c> (neutro = inglés) + <c>Resources.&lt;código&gt;.resx</c> por idioma.
///
/// Cambio de idioma EN CALIENTE: al llamar a <see cref="SetLanguage"/> se dispara <c>PropertyChanged("Item[]")</c>,
/// de modo que TODOS los bindings a su indexador (<c>{Binding [Clave], Source={StaticResource Loc}}</c> o la markup
/// extension <c>{loc:Str Key=Clave}</c>) se refrescan solos. El texto fijado en código que persiste debe suscribirse a
/// <see cref="LanguageChanged"/> para re-aplicarse; el transitorio (progreso, títulos de diálogos creados al abrir) se
/// resuelve al vuelo con <see cref="Get"/>/<see cref="Format"/>.
///
/// Es singleton (DI) y expone <see cref="Instance"/> para la markup extension; además se registra como recurso de
/// aplicación con clave "Loc" en el arranque para poder enlazar con <c>{Binding Source={StaticResource Loc}}</c>.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    #region Attributes
    private readonly ResourceManager _resourceManager;
    private CultureInfo _current = CultureInfo.GetCultureInfo("en");
    #endregion

    #region Constructor
    public LocalizationService()
    {
        // El nombre base debe coincidir con el manifest resource del .resx (RootNamespace + carpeta + fichero).
        _resourceManager = new ResourceManager("Tracker.Strings.Resources", typeof(LocalizationService).Assembly);
        Instance = this;
    }
    #endregion

    #region Events
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara al cambiar de idioma; para re-aplicar textos fijados en código que persisten.</summary>
    public event EventHandler? LanguageChanged;
    #endregion

    #region Properties
    /// <summary>Instancia singleton, para la markup extension <c>{loc:Str}</c> (que no puede resolver por DI).</summary>
    public static LocalizationService? Instance { get; private set; }

    /// <summary>Cultura activa.</summary>
    public CultureInfo Current => _current;

    /// <summary>Código ISO de dos letras del idioma activo (p. ej. "en", "es").</summary>
    public string CurrentLanguage => _current.TwoLetterISOLanguageName;

    /// <summary>Idiomas disponibles para el selector de Settings.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("es", "Español"),
    };

    /// <summary>Indexador para binding: <c>{Binding [Clave], Source={StaticResource Loc}}</c>. Devuelve la clave si falta.</summary>
    public string this[string key] => _resourceManager.GetString(key, _current) ?? key;
    #endregion

    #region Methods (public)
    /// <summary>Resuelve un texto por su clave (o la propia clave si no existe).</summary>
    public string Get(string key) => this[key];

    /// <summary>Resuelve un texto con placeholders (<c>string.Format</c> con la cultura activa).</summary>
    public string Format(string key, params object[] args) => string.Format(_current, this[key], args);

    /// <summary>Fija el idioma activo (código ISO) y notifica para refrescar los bindings en caliente. No-op si no cambia.</summary>
    public void SetLanguage(string code)
    {
        CultureInfo culture = SafeCulture(string.IsNullOrWhiteSpace(code) ? "en" : code);
        if (culture.TwoLetterISOLanguageName == _current.TwoLetterISOLanguageName)
            return;

        _current = culture;
        CultureInfo.CurrentUICulture = culture;

        // "Item[]" refresca todos los bindings al indexador; LanguageChanged es para el texto fijado en código.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    #region Methods (private)
    private static CultureInfo SafeCulture(string code)
    {
        try { return CultureInfo.GetCultureInfo(code); }
        catch { return CultureInfo.GetCultureInfo("en"); }
    }
    #endregion

    #region Nested types
    /// <summary>Una opción de idioma para el combo de Settings (código ISO + nombre para mostrar).</summary>
    public sealed record LanguageOption(string Code, string DisplayName);
    #endregion
}
