using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tracker.Services;

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
/// Separador visual configurable para WinUI 3.
///
/// Este control es *templatable* (usa ControlTemplate) y expone varias
/// DependencyProperties que permiten modificar su comportamiento:
/// - Orientation: Horizontal o Vertical
/// - Thickness: Grosor del separador
/// - LineBrush: Color base cuando no se usa degradado
/// - UseGradient: Activa o desactiva el degradado
///
/// Debido a las limitaciones de WinUI 3 (no soporta triggers ni VisualStateGroups
/// en plantillas personalizadas), toda la lógica visual se aplica en C#
/// dentro de OnApplyTemplate() y ApplyVisuals().
/// </summary>
public sealed class SeparatorEx : Control
{
    #region Attributes
    /// <summary>
    /// Referencia al Rectangle definido en el ControlTemplate.
    /// Este es el elemento visual real que se dibuja en pantalla.
    /// </summary>
    private Rectangle? _lineElement;

    /// <summary>Servicio de tema; se usa para reaplicar el degradado (que hornea el color de LineBrush) al cambiar de tema.</summary>
    private readonly ThemeService? _themeService;
    #endregion

    #region Constructor
    /// <summary>
    /// Constructor del control.
    /// Asigna el DefaultStyleKey para que WinUI cargue el template desde Resources/Controls.xaml.
    /// </summary>
    public SeparatorEx()
    {
        DefaultStyleKey = typeof(SeparatorEx);

        // El degradado se construye horneando el color de LineBrush (un recurso de tipo Color): no se propaga solo al
        // cambiar de tema en caliente, así que se reconstruye al recibir ThemeChanged. Suscripción atada a Loaded/Unloaded
        // para no retener el control (hay muchos separadores) más allá de su vida en el árbol.
        _themeService = App.GetService<ThemeService>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Brush usado cuando UseGradient = false.
    /// Si UseGradient = true y LineBrush es SolidColorBrush,
    /// su color se usa como base del degradado.
    /// </summary>
    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(SeparatorEx), new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>
    /// Orientación del separador: Horizontal o Vertical.
    /// Afecta al tamaño y dirección del degradado.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(SeparatorEx), new PropertyMetadata(Orientation.Horizontal, OnPropertyChanged));

    /// <summary>
    /// Grosor del separador.
    /// Si Horizontal → Height = Thickness
    /// Si Vertical → Width = Thickness
    /// </summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(SeparatorEx), new PropertyMetadata(1d, OnPropertyChanged));

    /// <summary>
    /// Indica si el separador debe usar un degradado.
    /// Si es false, se usa LineBrush como color plano.
    /// </summary>
    public bool UseGradient
    {
        get => (bool)GetValue(UseGradientProperty);
        set => SetValue(UseGradientProperty, value);
    }

    public static readonly DependencyProperty UseGradientProperty = DependencyProperty.Register(nameof(UseGradient), typeof(bool), typeof(SeparatorEx), new PropertyMetadata(true, OnPropertyChanged));

    /// <summary>
    /// Forma del separador: <see cref="SeparatorExShape.Line"/> (por defecto) o <see cref="SeparatorExShape.Circle"/>.
    /// En modo círculo se ignoran Orientation/Thickness/UseGradient y se dibuja un punto de <see cref="CircleDiameter"/>
    /// relleno con <see cref="LineBrush"/>.
    /// </summary>
    public SeparatorExShape Shape
    {
        get => (SeparatorExShape)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(SeparatorExShape), typeof(SeparatorEx), new PropertyMetadata(SeparatorExShape.Line, OnPropertyChanged));

    /// <summary>
    /// Diámetro del círculo cuando <see cref="Shape"/> es <see cref="SeparatorExShape.Circle"/>. Por defecto 9,
    /// el mismo tamaño que el badge de estado de los eventos del ACTIVITY LOG.
    /// </summary>
    public double CircleDiameter
    {
        get => (double)GetValue(CircleDiameterProperty);
        set => SetValue(CircleDiameterProperty, value);
    }

    public static readonly DependencyProperty CircleDiameterProperty = DependencyProperty.Register(nameof(CircleDiameter), typeof(double), typeof(SeparatorEx), new PropertyMetadata(9d, OnPropertyChanged));
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Se ejecuta cuando el template se aplica.
    /// Aquí obtenemos la referencia al Rectangle del template
    /// y aplicamos la lógica visual inicial.
    /// </summary>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _lineElement = GetTemplateChild("LineElement") as Rectangle;
        ApplyVisuals();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_themeService != null)
            _themeService.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_themeService != null)
            _themeService.ThemeChanged -= OnThemeChanged;
    }

    /// <summary>Al cambiar el tema, LineBrush ya está mutado: reconstruye el degradado con el nuevo color.</summary>
    private void OnThemeChanged(object? sender, System.EventArgs e) => ApplyVisuals();

    /// <summary>
    /// Se ejecuta cuando cambia cualquier DependencyProperty registrada con este callback.
    /// Si el template ya está cargado, actualiza la apariencia del separador.
    /// </summary>
    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SeparatorEx control && control._lineElement != null)
            control.ApplyVisuals();
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Aplica todos los cambios visuales al Rectangle del template.
    /// Este método se ejecuta:
    /// - Al aplicar el template
    /// - Cada vez que cambia una DependencyProperty relevante
    /// </summary>
    private void ApplyVisuals()
    {
        if (_lineElement == null)
            return;

        if (Shape == SeparatorExShape.Circle)
        {
            // Punto circular: tamaño fijo centrado, esquinas redondeadas al máximo y relleno sólido (sin degradado).
            _lineElement.Width = CircleDiameter;
            _lineElement.Height = CircleDiameter;
            _lineElement.RadiusX = CircleDiameter / 2;
            _lineElement.RadiusY = CircleDiameter / 2;
            _lineElement.HorizontalAlignment = HorizontalAlignment.Center;
            _lineElement.VerticalAlignment = VerticalAlignment.Center;
            _lineElement.Fill = LineBrush ?? new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["TextSecondaryColor"]);
            return;
        }

        // Línea: restaura el estado por si venía de modo círculo y aplica grosor/orientación.
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

        if (UseGradient)
        {
            _lineElement.Fill = CreateGradientBrush();
        }
        else
        {
            _lineElement.Fill = LineBrush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
    }

    /// <summary>
    /// Crea un LinearGradientBrush dinámico.
    ///
    /// Si LineBrush es SolidColorBrush:
    ///     - Usa su color como centro del degradado.
    ///     - Extremos transparentes del mismo color.
    ///
    /// Si LineBrush es null:
    ///     - Usa los ThemeResources TextSecondaryColor y TextSecondaryColorTransparent.
    ///
    /// La dirección del degradado depende de Orientation.
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

        // Determinar color central del degradado
        Windows.UI.Color centerColor;
        if (LineBrush is SolidColorBrush solid)
        {
            centerColor = solid.Color;
        }
        else
        {
            centerColor = (Windows.UI.Color)Application.Current.Resources["TextSecondaryColor"];
        }

        // Crear color transparente del mismo tono
        var transparentColor = Windows.UI.Color.FromArgb(0, centerColor.R, centerColor.G, centerColor.B);

        // Añadir stops del degradado
        brush.GradientStops.Add(new GradientStop
        {
            Color = transparentColor,
            Offset = 0
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = centerColor,
            Offset = 0.5
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = transparentColor,
            Offset = 1
        });

        return brush;
    }
    #endregion
}
