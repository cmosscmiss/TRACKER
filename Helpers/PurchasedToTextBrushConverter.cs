using System;
using MM4LB.Services;

namespace MM4LB.Helpers;

/// <summary>
/// Devuelve el pincel del título de un producto en la lista según si está comprado: texto SECUNDARIO cuando lo está
/// (para que el tachado se distinga del texto normal) y texto NORMAL cuando no. Reutiliza los brushes por rama y los
/// refresca al cambiar de tema (ver <see cref="ThemeBrushConverter"/>).
/// </summary>
public class PurchasedToTextBrushConverter : ThemeBrushConverter
{
    public override object Convert(object value, Type targetType, object parameter, string language)
    {
        if (ThemeService is null)
            return Transparent;

        bool purchased = value is bool b && b;
        return purchased
            ? GetBrush(true, ts => ts.TextSecondaryColor)
            : GetBrush(false, ts => ts.TextColor);
    }
}
