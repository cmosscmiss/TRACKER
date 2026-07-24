using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Tracker.Controls.Templates;

/// <summary>
/// Forma del <see cref="SeparatorEx"/>: línea (por defecto) o círculo.
/// </summary>
public enum SeparatorExShape
{
    /// <summary>Línea (recta) que respeta Orientation, Thickness y UseGradient.</summary>
    Line,

    /// <summary>Punto circular de diámetro <see cref="SeparatorEx.CircleDiameter"/>, relleno con LineBrush.</summary>
    Circle
}

/// <summary>
/// Separador visual configurable para WinUI 3 (línea o punto, con degradado opcional).
///
/// Portado de MM4LB y DESACOPLADO del ThemeService: aquí no reacciona a un cambio de tema en caliente
/// (la app nueva usa una paleta estática). Si más adelante añades temas en caliente, vuelve a suscribir la
/// reconstrucción del degradado como en el original.
/// </summary>
public sealed class SeparatorEx : Control
{
    /// <summary>Rectangle del ControlTemplate: el elemento visual real que se dibuja.</summary>
    private Rectangle? _lineElement;

    public SeparatorEx()
    {
        DefaultStyleKey = typeof(SeparatorEx);
    }

    #region Dependency Properties
    /// <summary>Brush usado cuando UseGradient = false; si UseGradient = true y es SolidColorBrush, su color es la base del degradado.</summary>
    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(SeparatorEx), new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>Orientación del separador: Horizontal o Vertical.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(SeparatorEx), new PropertyMetadata(Orientation.Horizontal, OnPropertyChanged));

    /// <summary>Grosor del separador (Height si Horizontal, Width si Vertical).</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(SeparatorEx), new PropertyMetadata(1d, OnPropertyChanged));

    /// <summary>Si el separador usa degradado; si es false, usa LineBrush como color plano.</summary>
    public bool UseGradient
    {
        get => (bool)GetValue(UseGradientProperty);
        set => SetValue(UseGradientProperty, value);
    }

    public static readonly DependencyProperty UseGradientProperty = DependencyProperty.Register(nameof(UseGradient), typeof(bool), typeof(SeparatorEx), new PropertyMetadata(true, OnPropertyChanged));

    /// <summary>Forma: <see cref="SeparatorExShape.Line"/> (por defecto) o <see cref="SeparatorExShape.Circle"/>.</summary>
    public SeparatorExShape Shape
    {
        get => (SeparatorExShape)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(SeparatorExShape), typeof(SeparatorEx), new PropertyMetadata(SeparatorExShape.Line, OnPropertyChanged));

    /// <summary>Diámetro del círculo cuando <see cref="Shape"/> es Circle. Por defecto 9.</summary>
    public double CircleDiameter
    {
        get => (double)GetValue(CircleDiameterProperty);
        set => SetValue(CircleDiameterProperty, value);
    }

    public static readonly DependencyProperty CircleDiameterProperty = DependencyProperty.Register(nameof(CircleDiameter), typeof(double), typeof(SeparatorEx), new PropertyMetadata(9d, OnPropertyChanged));
    #endregion

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _lineElement = GetTemplateChild("LineElement") as Rectangle;
        ApplyVisuals();
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SeparatorEx control && control._lineElement != null)
            control.ApplyVisuals();
    }

    /// <summary>Aplica los cambios visuales al Rectangle del template (al aplicar template y al cambiar DPs).</summary>
    private void ApplyVisuals()
    {
        if (_lineElement == null)
            return;

        if (Shape == SeparatorExShape.Circle)
        {
            _lineElement.Width = CircleDiameter;
            _lineElement.Height = CircleDiameter;
            _lineElement.RadiusX = CircleDiameter / 2;
            _lineElement.RadiusY = CircleDiameter / 2;
            _lineElement.HorizontalAlignment = HorizontalAlignment.Center;
            _lineElement.VerticalAlignment = VerticalAlignment.Center;
            _lineElement.Fill = LineBrush ?? new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["TextSecondaryColor"]);
            return;
        }

        _lineElement.RadiusX = 0;
        _lineElement.RadiusY = 0;
        _lineElement.HorizontalAlignment = HorizontalAlignment.Stretch;
        _lineElement.VerticalAlignment = VerticalAlignment.Stretch;

        if (Orientation == Orientation.Horizontal)
        {
            _lineElement.Height = Thickness;
            _lineElement.Width = double.NaN; // Stretch horizontal
        }
        else
        {
            _lineElement.Width = Thickness;
            _lineElement.Height = double.NaN; // Stretch vertical
        }

        _lineElement.Fill = UseGradient ? CreateGradientBrush() : (LineBrush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray));
    }

    /// <summary>
    /// Crea un LinearGradientBrush: color central = LineBrush (si es SolidColorBrush) o TextSecondaryColor,
    /// con extremos transparentes del mismo tono. La dirección depende de Orientation.
    /// </summary>
    private Brush CreateGradientBrush()
    {
        var brush = new LinearGradientBrush();

        if (Orientation == Orientation.Horizontal)
        {
            brush.StartPoint = new Windows.Foundation.Point(0, 0);
            brush.EndPoint = new Windows.Foundation.Point(1, 0);
        }
        else
        {
            brush.StartPoint = new Windows.Foundation.Point(0, 0);
            brush.EndPoint = new Windows.Foundation.Point(0, 1);
        }

        Windows.UI.Color centerColor = LineBrush is SolidColorBrush solid
            ? solid.Color
            : (Windows.UI.Color)Application.Current.Resources["TextSecondaryColor"];

        var transparentColor = Windows.UI.Color.FromArgb(0, centerColor.R, centerColor.G, centerColor.B);

        brush.GradientStops.Add(new GradientStop { Color = transparentColor, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = centerColor, Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = transparentColor, Offset = 1 });

        return brush;
    }
}
