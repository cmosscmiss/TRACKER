namespace MM4LB.Enums;

/// <summary>
/// Cómo se muestran los grupos de botones EXCLUYENTES de las toolbars de los widgets (layout, calidad de vídeo,
/// aspect ratio, resolución, vista, etc.).
/// </summary>
public enum ToolbarGroupsDisplayMode
{
    /// <summary>Botones independientes: una fila de toggles excluyentes (como hasta ahora).</summary>
    Expanded,

    /// <summary>Un <c>ToggleSplitButton</c> por grupo (cara = selección actual, desplegable = opciones 1-de-N).</summary>
    Collapsed,

    /// <summary>
    /// Expandido, salvo que la toolbar que los contiene no quepa en el widget; en ese caso se colapsan todos sus
    /// grupos a <c>ToggleSplitButton</c>.
    /// </summary>
    Auto,
}
