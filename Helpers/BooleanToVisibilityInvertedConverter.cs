using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace MM4LB.Helpers;

/// <summary>
/// Converter from boolean to the Visibility enum type (true means hidden).
/// </summary>
public sealed class BooleanToVisibilityInvertedConverter : IValueConverter
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
        return bValue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility visibility ? visibility == Visibility.Collapsed : (object)false;
    }
}