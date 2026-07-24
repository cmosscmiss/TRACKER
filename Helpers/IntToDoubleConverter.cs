using System;
using Microsoft.UI.Xaml.Data;

namespace MM4LB.Helpers;

/// <summary>
/// Converter bidireccional int &lt;-&gt; double para enlazar un <see cref="Microsoft.UI.Xaml.Controls.Slider"/>
/// (Value/Minimum/Maximum, de tipo double) con una propiedad de tamaño entera del view model vía x:Bind, que —a
/// diferencia de {Binding}— no convierte tipos por su cuenta. Al volver (ConvertBack) redondea el double del slider
/// al entero más cercano.
/// </summary>
public sealed class IntToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int i ? (double)i : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is double d ? (int)Math.Round(d) : 0;
}
