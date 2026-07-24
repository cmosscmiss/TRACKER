using Microsoft.UI.Xaml.Data;
using System;

namespace MM4LB.Helpers;

/// <summary>
/// Converter from boolean to an opacity value: true means dimmed (0.4), false means fully opaque (1.0).
/// Used to atenuar a log entry once its operation has been undone.
/// </summary>
public sealed class BooleanToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? 0.4 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
