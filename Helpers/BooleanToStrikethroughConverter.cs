using Microsoft.UI.Xaml.Data;
using System;

namespace Tracker.Helpers;

/// <summary>
/// Converter from boolean to <see cref="Windows.UI.Text.TextDecorations"/>: true means strikethrough,
/// false means none. Used to tachar el texto de una entry del log cuya operación se ha deshecho.
/// </summary>
public sealed class BooleanToStrikethroughConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
