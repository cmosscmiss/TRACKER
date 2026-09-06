using System;
using Tracker.Models;
using Tracker.Services;

namespace Tracker.Helpers;

/// <summary>
/// Traduce el <see cref="ListTextTone"/> de un producto al pincel con el que se pinta su texto en la lista: normal,
/// secundario (comprado, para que el tachado se distinga) o el que contrasta con el acento cuando la fila está
/// seleccionada y su fondo ES el acento.
///
/// Reutiliza los brushes por rama y los refresca al cambiar de tema (ver <see cref="ThemeBrushConverter"/>). El tono
/// "sobre acento" pasa por <see cref="ThemeService.TextColorOn"/>, así que respeta el ajuste de texto por contraste:
/// desactivado, devuelve el mismo color de texto que el tono normal.
/// </summary>
public class ListTextToneToBrushConverter : ThemeBrushConverter
{
    public override object Convert(object value, Type targetType, object parameter, string language)
    {
        if (ThemeService is null)
            return Transparent;

        ListTextTone tone = value is ListTextTone t ? t : ListTextTone.Normal;

        return tone switch
        {
            ListTextTone.Secondary => GetBrush(ListTextTone.Secondary, static ts => ts.TextSecondaryColor),
            ListTextTone.OnAccent => GetBrush(ListTextTone.OnAccent, static ts => ts.TextColorOn(ts.AccentColor)),
            _ => GetBrush(ListTextTone.Normal, static ts => ts.TextColor),
        };
    }
}
