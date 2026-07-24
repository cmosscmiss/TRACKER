using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace MM4LB.Helpers;

/// <summary>
/// Converter to the Visibility enum type for any object (not null means visible).
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool bvalue = value is string ? (value as string) == string.Empty : value == null;
        return bvalue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}