using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using MM4LB.Models;

namespace MM4LB.Helpers;

/// <summary>
/// Convierte una <see cref="PriceTrend"/> en un color de fondo para el recuadro del precio en la lista de productos:
/// verde si el precio bajó, rojo si subió, gris si se mantiene o no hay datos suficientes.
/// </summary>
public sealed class PriceTrendToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Down = new(Color.FromArgb(0xFF, 0x2E, 0x7D, 0x32));   // verde
    private static readonly SolidColorBrush Up = new(Color.FromArgb(0xFF, 0xC6, 0x28, 0x28));     // rojo
    private static readonly SolidColorBrush Neutral = new(Color.FromArgb(0xFF, 0x75, 0x75, 0x75)); // gris

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is PriceTrend trend
            ? trend switch
            {
                PriceTrend.Down => Down,
                PriceTrend.Up => Up,
                _ => Neutral,
            }
            : Neutral;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
