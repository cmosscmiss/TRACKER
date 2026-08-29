using System;
using Microsoft.UI.Xaml.Data;


namespace Tracker.Helpers;

public class NullToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value == null ? 0 : 1;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
