using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace MM4LB.Helpers;

/// <summary>
/// Converter from boolean to a <see cref="GridLength"/> (true means a star row/column, false means zero).
///
/// Used to collapse a grid row/column to no height/width when its content is hidden, while letting the
/// visible ones share the available space evenly: a single visible element gets the whole star space,
/// two visible elements split it.
/// </summary>
public sealed class BooleanToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool bValue = false;
        if (value is bool boolean)
        {
            bValue = boolean;
        }
        else if (value is bool?)
        {
            bool? tmp = (bool?)value;
            bValue = tmp ?? false;
        }
        return bValue ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is GridLength length && length.Value > 0;
    }
}
