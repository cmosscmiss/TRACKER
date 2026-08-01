using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using MM4LB.Models;

namespace MM4LB.Helpers;

/// <summary>
/// Convierte un <see cref="PriceHighlight"/> en el brush de fondo del recuadro del precio en la lista de productos:
/// <see cref="PriceHighlight.Down"/> (bajó) usa el verde del tema (<c>SuccessBrush</c>), <see cref="PriceHighlight.Up"/>
/// (subió) el rojo (<c>DangerBrush</c>), <see cref="PriceHighlight.Low"/> (mínimo histórico, sin subida/bajada reciente)
/// el azul del tema (<c>BadgeMoreThanOneImageBrush</c>) y el resto un fondo neutro (<c>CardBackgroundLightBrush</c>). Se
/// resuelven las instancias VIVAS de los brushes del tema (el <see cref="Services.ThemeService"/> muta su color in situ
/// al cambiar de tema), de modo que el recuadro se recolorea en caliente. Si algún recurso no existe, cae a un color
/// fijo equivalente.
/// </summary>
public sealed class PriceHighlightToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DownFallback = new(Color.FromArgb(0xFF, 0x2E, 0x7D, 0x32));    // verde
    private static readonly SolidColorBrush UpFallback = new(Color.FromArgb(0xFF, 0xC6, 0x28, 0x28));      // rojo
    private static readonly SolidColorBrush LowFallback = new(Color.FromArgb(0xFF, 0x38, 0x70, 0xC4));     // azul
    private static readonly SolidColorBrush NeutralFallback = new(Color.FromArgb(0xFF, 0x2F, 0x2F, 0x2F)); // gris de tarjeta

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        (string key, SolidColorBrush fallback) = value is PriceHighlight highlight
            ? highlight switch
            {
                PriceHighlight.Down => ("SuccessBrush", DownFallback),
                PriceHighlight.Up => ("DangerBrush", UpFallback),
                PriceHighlight.Low => ("BadgeMoreThanOneImageBrush", LowFallback),
                _ => ("CardBackgroundLightBrush", NeutralFallback),
            }
            : ("CardBackgroundLightBrush", NeutralFallback);

        return FindThemeBrush(key) ?? fallback;
    }

    /// <summary>Busca un brush del tema por clave en Application.Resources y sus diccionarios combinados (donde vive el tema).</summary>
    private static Brush? FindThemeBrush(string key)
    {
        ResourceDictionary appResources = Application.Current.Resources;
        if (appResources.TryGetValue(key, out object? direct) && direct is Brush directBrush)
            return directBrush;

        foreach (ResourceDictionary merged in appResources.MergedDictionaries)
            if (merged.TryGetValue(key, out object? value) && value is Brush brush)
                return brush;

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
