using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Tracker.Controls.Views;

/// <summary>
/// Un único dígito de un panel split-flap (tipo Solari de aeropuerto): una tarjeta negra partida por el centro cuya
/// mitad superior vuelca hacia delante para dejar paso a la siguiente cifra. Solo se anima cuando el dígito cambia.
///
/// Estructura (de atrás hacia delante): mitad inferior estática (muestra el dígito ANTERIOR hasta que aterriza la
/// solapa), mitad superior estática (muestra el dígito NUEVO, que aparece al plegarse la solapa de arriba), solapa
/// inferior (nuevo, gira 90→0) y solapa superior (anterior, gira 0→-90). Cada mitad recorta un glifo de altura
/// completa centrado, de modo que arriba se ve su mitad de arriba y abajo su mitad de abajo.
/// </summary>
public sealed class SplitFlapDigit : UserControl
{
    #region Layout constants (tamaño footer)
    private const double W = 22;    // ancho de la tarjeta
    private const double H = 34;    // alto de la tarjeta
    private const double FS = 25;   // tamaño de fuente del glifo
    #endregion

    #region Colors
    private static readonly Color Ivory = Color.FromArgb(0xFF, 0xF4, 0xEF, 0xE3);
    private static readonly Color CardBg = Color.FromArgb(0xFF, 0x0F, 0x0F, 0x11);
    private static readonly Color Seam = Color.FromArgb(0x99, 0x00, 0x00, 0x00);
    #endregion

    #region Fields
    private readonly TextBlock _topText, _bottomText, _foldTopText, _foldBottomText;
    private readonly PlaneProjection _foldTopProj, _foldBottomProj;
    private char _current = '\0';
    private Storyboard? _storyboard;
    #endregion

    public SplitFlapDigit()
    {
        var card = new Grid { Width = W, Height = H, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(CardBg) };

        (Grid bottomStatic, _bottomText) = MakeHalf(top: false);
        (Grid topStatic, _topText) = MakeHalf(top: true);

        (Grid foldBottom, _foldBottomText) = MakeHalf(top: false);
        _foldBottomProj = new PlaneProjection { CenterOfRotationY = 0, RotationX = 90 };
        foldBottom.Projection = _foldBottomProj;

        (Grid foldTop, _foldTopText) = MakeHalf(top: true);
        _foldTopProj = new PlaneProjection { CenterOfRotationY = 1, RotationX = 0 };
        foldTop.Projection = _foldTopProj;

        var seam = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(Seam) };

        card.Children.Add(bottomStatic);
        card.Children.Add(topStatic);
        card.Children.Add(foldBottom);
        card.Children.Add(foldTop);
        card.Children.Add(seam);

        Content = card;
    }

    /// <summary>Crea una mitad (superior o inferior) que recorta un glifo de altura completa centrado.</summary>
    private static (Grid Half, TextBlock Text) MakeHalf(bool top)
    {
        var text = new TextBlock
        {
            Text = "0",
            FontFamily = new FontFamily("Arial"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = FS,
            Foreground = new SolidColorBrush(Ivory),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        // Rejilla interior de ALTURA COMPLETA con el glifo centrado; alineada arriba o abajo dentro de la mitad.
        var inner = new Grid { Height = H, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom };
        inner.Children.Add(text);

        var half = new Grid
        {
            Width = W,
            Height = H / 2,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Background = new SolidColorBrush(top ? Color.FromArgb(0xFF, 0x24, 0x24, 0x27) : Color.FromArgb(0xFF, 0x14, 0x14, 0x16)),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, W, H / 2) },
        };
        half.Children.Add(inner);
        return (half, text);
    }

    /// <summary>Fija el dígito mostrado. Si cambia respecto al actual, dispara la animación de vuelco.</summary>
    public void SetDigit(char digit)
    {
        if (digit == _current)
            return;

        char previous = _current;
        _current = digit;

        string next = digit.ToString();

        // Primera asignación (sin animación): todo al valor.
        if (previous == '\0')
        {
            _topText.Text = _bottomText.Text = _foldTopText.Text = _foldBottomText.Text = next;
            return;
        }

        string old = previous.ToString();
        _topText.Text = next;         // se revela cuando la solapa de arriba se pliega
        _bottomText.Text = old;       // sigue visible hasta que aterriza la solapa de abajo
        _foldTopText.Text = old;
        _foldBottomText.Text = next;
        _foldTopProj.RotationX = 0;   // visible, tapando la mitad superior estática
        _foldBottomProj.RotationX = 90; // oculta (de canto)

        _storyboard?.Stop();

        var foldDown = new DoubleAnimation
        {
            To = -90,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(foldDown, _foldTopProj);
        Storyboard.SetTargetProperty(foldDown, "RotationX");

        var foldUp = new DoubleAnimation
        {
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(130),
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(foldUp, _foldBottomProj);
        Storyboard.SetTargetProperty(foldUp, "RotationX");

        var storyboard = new Storyboard();
        storyboard.Children.Add(foldDown);
        storyboard.Children.Add(foldUp);
        storyboard.Completed += (_, _) =>
        {
            // Estado de reposo con el nuevo dígito: estáticas al nuevo, solapas ocultas/al nuevo.
            _bottomText.Text = next;
            _foldTopText.Text = next;
            _foldTopProj.RotationX = 0;
            _foldBottomProj.RotationX = 90;
        };
        _storyboard = storyboard;
        storyboard.Begin();
    }
}
