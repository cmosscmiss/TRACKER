using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MM4LB.Helpers;

/// <summary>
/// Converter to set a colour depending on whether the value is true or false. Reutiliza los brushes por rama
/// (ver <see cref="ThemeBrushConverter"/>) en vez de crear uno nuevo en cada evaluación.
/// </summary>
public class BooleanToColorConverter : ThemeBrushConverter
{
    public override object Convert(object value, Type targetType, object parameter, string language)
    {
        if (ThemeService is null) { return Transparent; }

        bool bValue = value is bool b && b;

        return bValue
            ? GetBrush(true, ts => ts.TextColor)
            : GetBrush(false, ts => ts.TextSecondaryColor);
    }
}
