using System;
using Tracker.Models;
using Tracker.Services;

namespace Tracker.Helpers;

/// <summary>
/// Pincel del TEXTO del recuadro de precio de la lista, en pareja con <see cref="PriceHighlightToBrushConverter"/>,
/// que da el fondo: por cada <see cref="PriceHighlight"/> devuelve el color que mejor contrasta con ese fondo
/// (verde, rojo, azul o neutro) según <see cref="ThemeService.TextColorOn"/>.
///
/// Antes el texto era "White" fijo, que en un tema con el verde o el azul claros se leía mal. Con el ajuste de texto
/// por contraste desactivado devuelve el color de texto del tema, que en los cuatro temas actuales es blanco: mismo
/// aspecto que antes.
/// </summary>
public class PriceHighlightToTextBrushConverter : ThemeBrushConverter
{
    public override object Convert(object value, Type targetType, object parameter, string language)
    {
        if (ThemeService is null)
            return Transparent;

        PriceHighlight highlight = value is PriceHighlight h ? h : PriceHighlight.None;

        return highlight switch
        {
            PriceHighlight.Down => GetBrush(PriceHighlight.Down, static ts => ts.TextColorOn(ts.SuccessColor)),
            PriceHighlight.Up => GetBrush(PriceHighlight.Up, static ts => ts.TextColorOn(ts.DangerColor)),
            PriceHighlight.Low => GetBrush(PriceHighlight.Low, static ts => ts.TextColorOn(ts.ExtraColor2)),
            _ => GetBrush(PriceHighlight.None, static ts => ts.TextColorOn(ts.CardBackgroundLightColor)),
        };
    }
}
