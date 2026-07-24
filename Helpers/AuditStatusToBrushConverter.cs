using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using MM4LB.Models;
using Windows.UI;

namespace MM4LB.Helpers;

/// <summary>
/// Devuelve el pincel del indicador de estado de una celda de auditoría: verde (Match), rojo (Missing =
/// faltan por emparejar en MM4LB) o ámbar (Extra = MM4LB empareja de más).
/// </summary>
public class AuditStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        AuditStatus status = value is AuditStatus s ? s : AuditStatus.Match;
        Color color = status switch
        {
            AuditStatus.Missing => Color.FromArgb(0xFF, 0xE5, 0x39, 0x35),  // rojo
            AuditStatus.Extra   => Color.FromArgb(0xFF, 0xFB, 0x8C, 0x00),  // ámbar
            _                   => Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50),  // verde
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
