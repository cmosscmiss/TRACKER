namespace MM4LB.Controls.Views;

/// <summary>
/// Una opción de un grupo excluyente de toolbar (ver <see cref="ExclusiveOptionsControl"/>). Se declaran como hijos
/// del control en XAML. <see cref="Value"/> es la clave que se compara con <c>SelectedValue</c> del control.
/// </summary>
public sealed class ExclusiveOption
{
    /// <summary>Texto mostrado (en el botón expandido y en la opción del desplegable). Se ignora si hay <see cref="LabelKey"/>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Clave de recurso de localización para el texto (i18n). Si está fijada, <see cref="ExclusiveOptionsControl"/>
    /// resuelve la etiqueta contra el <c>LocalizationService</c> y la re-resuelve al cambiar de idioma (en caliente);
    /// tiene prioridad sobre <see cref="Label"/>. <see cref="ExclusiveOption"/> es un POCO (no acepta binding), por eso
    /// se localiza por clave en vez de con <c>{loc:Str}</c>.
    /// </summary>
    public string LabelKey { get; set; } = string.Empty;

    /// <summary>Glyph opcional del icono (FontIcon) para el modo expandido.</summary>
    public string Glyph { get; set; } = string.Empty;

    /// <summary>Clave de la opción; se compara con <c>ExclusiveOptionsControl.SelectedValue</c>.</summary>
    public string Value { get; set; } = string.Empty;
}
