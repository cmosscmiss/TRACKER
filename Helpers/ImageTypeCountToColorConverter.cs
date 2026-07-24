using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MM4LB.Helpers;

/// <summary>
/// Converter to set a colour depending on the whether the value is 0, or >1. Reutiliza los brushes por rama
/// (ver <see cref="ThemeBrushConverter"/>) en vez de crear uno nuevo en cada evaluación.
/// </summary>
public class ImageTypeCountToColorConverter : ThemeBrushConverter
{
    public override object Convert(object value, Type targetType, object parameter, string language)
    {
        if (ThemeService is null) { return Transparent; }

        return (int)value switch
        {
            0 => GetBrush(0, ts => ts.BadgeNoImageColor),
            _ => GetBrush(1, ts => ts.BadgeMoreThanOneImageColor)
        };
    }
}
