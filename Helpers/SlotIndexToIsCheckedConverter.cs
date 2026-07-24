using System;
using Microsoft.UI.Xaml.Data;

namespace MM4LB.Converters;

public sealed class SlotIndexToIsCheckedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is int slotIndex && slotIndex != -1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
