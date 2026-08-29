using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Tracker.Services;

namespace Tracker.Markup;

/// <summary>
/// Markup extension de localización: <c>{loc:Str Key=Area_Element_Role}</c>. Devuelve un <see cref="Binding"/> al
/// indexador del <see cref="LocalizationService"/> (singleton), de modo que el texto se traduce y, al cambiar de
/// idioma, se refresca EN CALIENTE (el servicio notifica <c>Item[]</c>).
///
/// Uso: <c>Text="{loc:Str Key=General_Language_Label}"</c> (declarar <c>xmlns:loc="using:Tracker.Markup"</c>).
/// </summary>
public sealed class StrExtension : MarkupExtension
{
    /// <summary>Clave del recurso (debe existir en <see cref="Tracker.Helpers.LocKeys"/> y en el .resx).</summary>
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue()
    {
        return new Binding
        {
            Source = LocalizationService.Instance,
            Path = new PropertyPath($"[{Key}]"),
            Mode = BindingMode.OneWay,
        };
    }
}
