using System;
using Windows.UI;

namespace Tracker.Helpers;

/// <summary>
/// Cálculo de contraste entre colores según WCAG 2.x. Lo usa <see cref="Tracker.Services.ThemeService"/> para elegir,
/// por cada color del tema, el color de texto que mejor se lee encima (los recursos <c>TextOn&lt;Name&gt;Brush</c>).
///
/// Umbrales de referencia: 4.5:1 para texto normal (AA) y 3:1 para texto grande.
/// </summary>
public static class ContrastHelper
{
    /// <summary>
    /// Candidato OSCURO del cálculo (el claro es el <c>TextColor</c> del tema). Negro puro resulta demasiado duro
    /// sobre los acentos saturados de los temas, así que se usa un gris muy oscuro.
    /// </summary>
    public static readonly Color DarkText = Color.FromArgb(255, 0x10, 0x10, 0x10);

    /// <summary>
    /// Luminancia relativa WCAG de un color (0 = negro, 1 = blanco). Ignora el alfa: el color debe ser el fondo YA
    /// compuesto (ver el gotcha de las variantes con opacidad en docs/Plan-Contraste-Texto.md).
    /// </summary>
    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>Ratio de contraste WCAG entre dos colores: de 1:1 (idénticos) a 21:1 (negro sobre blanco).</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double luminanceA = RelativeLuminance(a);
        double luminanceB = RelativeLuminance(b);
        double lighter = Math.Max(luminanceA, luminanceB);
        double darker = Math.Min(luminanceA, luminanceB);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// De los dos candidatos, el que más contrasta sobre <paramref name="background"/>. En caso de empate gana
    /// <paramref name="light"/> (el color de texto del tema), para no cambiar nada sin motivo.
    /// </summary>
    public static Color BestForeground(Color background, Color light, Color dark)
        => ContrastRatio(background, dark) > ContrastRatio(background, light) ? dark : light;
}
