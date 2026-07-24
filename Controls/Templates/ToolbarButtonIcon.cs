using System;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Tracker.Services;

namespace Tracker.Controls.Templates;

/// <summary>
/// Botón de toolbar basado en iconos, con animación de hover/press y estado checked/unchecked.
///
/// Portado de MM4LB y DESACOPLADO del ThemeService: aquí los iconos se asignan directamente como
/// <see cref="ImageSource"/> (<see cref="Icon"/> / <see cref="CheckedIcon"/> / <see cref="UncheckedIcon"/>),
/// sin resolución por carpeta de tema. Si más adelante añades temas con iconos por carpeta, reintroduce las
/// propiedades *Name y su resolución vía un servicio de tema, como en el original.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public class ToolbarButtonIcon : ContentControl
{
    #region Constants
    private const double NormalScale = 1.00;
    private const double PointerOverScale = 1.20;
    private const double PressedScale = 0.94;

    private const double NormalOpacity = 1.00;
    private const double PointerOverOpacity = 0.75;
    private const double PressedOpacity = 0.60;

    private const int PointerOverAnimationDuration = 100;
    private const int PointerExitAnimationDuration = 100;
    private const int PointerPressedAnimationDuration = 100;
    private const int PointerReleasedAnimationDuration = 100;
    #endregion

    #region Attributes
    private ScaleTransform? _scale;
    private AnimationService.IAnimationHandle? _activeAnimation;

    private Grid? _root;
    private Grid? _iconHost;

    private bool _isPointerOver;
    private bool _isPressed;

    private double _targetScale = NormalScale;
    private double _targetOpacity = NormalOpacity;

    private int _animationToken;
    #endregion

    #region Events
    /// <summary>Se emite cuando el usuario completa un click sobre el botón.</summary>
    public event EventHandler? Clicked;
    #endregion

    #region Constructor
    public ToolbarButtonIcon()
    {
        DefaultStyleKey = typeof(ToolbarButtonIcon);
    }
    #endregion

    #region Dependency Properties - Icons & state
    /// <summary>Indica si el botón está seleccionado (para botones tipo toggle: alterna Checked/Unchecked).</summary>
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(ToolbarButtonIcon), new PropertyMetadata(false, OnIsCheckedChanged));

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ToolbarButtonIcon)d).UpdateCheckState();
    }

    /// <summary>Icono principal (botones simples sin estado).</summary>
    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>Icono mostrado en estado checked.</summary>
    public ImageSource? CheckedIcon
    {
        get => (ImageSource?)GetValue(CheckedIconProperty);
        set => SetValue(CheckedIconProperty, value);
    }

    public static readonly DependencyProperty CheckedIconProperty = DependencyProperty.Register(nameof(CheckedIcon), typeof(ImageSource), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>Icono mostrado en estado unchecked.</summary>
    public ImageSource? UncheckedIcon
    {
        get => (ImageSource?)GetValue(UncheckedIconProperty);
        set => SetValue(UncheckedIconProperty, value);
    }

    public static readonly DependencyProperty UncheckedIconProperty = DependencyProperty.Register(nameof(UncheckedIcon), typeof(ImageSource), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconSourceChanged));

    private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ToolbarButtonIcon)d).UpdateCheckState();
    }
    #endregion

    #region Dependency Properties - Command
    /// <summary>Comando ejecutado al completar el click (en PointerReleased dentro del botón).</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ToolbarButtonIcon), new PropertyMetadata(null));

    /// <summary>Parámetro enviado al <see cref="Command"/>.</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(ToolbarButtonIcon), new PropertyMetadata(null));
    #endregion

    #region Template
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        StopActiveAnimation();

        _root = GetTemplateChild("Root") as Grid;
        _iconHost = GetTemplateChild("IconHost") as Grid;

        if (_root is null || _iconHost is null)
            return;

        _scale = new ScaleTransform
        {
            ScaleX = NormalScale,
            ScaleY = NormalScale
        };

        var group = new TransformGroup();
        group.Children.Add(_scale);

        // Root mantiene estable el hit testing; IconHost es lo único que escala visualmente.
        _iconHost.RenderTransform = group;
        _iconHost.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerExited;
        PointerPressed -= OnPointerPressed;
        PointerReleased -= OnPointerReleased;
        PointerCanceled -= OnPointerCanceled;
        PointerCaptureLost -= OnPointerCaptureLost;

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCaptureLost;

        Unloaded -= OnUnloaded;
        Unloaded += OnUnloaded;

        ResetPointerState();
        UpdateCheckState();
        ForceFinalState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerExited;
        PointerPressed -= OnPointerPressed;
        PointerReleased -= OnPointerReleased;
        PointerCanceled -= OnPointerCanceled;
        PointerCaptureLost -= OnPointerCaptureLost;

        Unloaded -= OnUnloaded;

        StopActiveAnimation();
        ResetPointerState();
        ForceFinalState();
    }
    #endregion

    #region State
    private int NewAnimationToken() => ++_animationToken;

    private bool IsTokenValid(int token) => token == _animationToken;

    private void ResetPointerState()
    {
        _isPointerOver = false;
        _isPressed = false;

        _targetScale = NormalScale;
        _targetOpacity = NormalOpacity;
    }

    private void UpdateCheckState()
    {
        if (_root is null)
            return;

        VisualStateManager.GoToState(this, IsChecked ? "Checked" : "Unchecked", true);
    }

    private void ForceFinalState()
    {
        if (_scale is null)
            return;

        _scale.ScaleX = _targetScale;
        _scale.ScaleY = _targetScale;
        Opacity = _targetOpacity;
    }

    private bool IsPointerInside(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        return position.X >= 0
            && position.Y >= 0
            && position.X <= ActualWidth
            && position.Y <= ActualHeight;
    }
    #endregion

    #region Pointer events
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;

        if (_isPressed)
        {
            AnimateToState(PressedScale, PressedOpacity, PointerOverAnimationDuration);
            return;
        }

        AnimateToState(PointerOverScale, PointerOverOpacity, PointerOverAnimationDuration);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;

        if (_isPressed)
            return;

        AnimateToState(NormalScale, NormalOpacity, PointerExitAnimationDuration);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        _isPressed = true;
        _isPointerOver = true;

        CapturePointer(e.Pointer);

        AnimateToState(PressedScale, PressedOpacity, PointerPressedAnimationDuration);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        bool isPointerInside = IsPointerInside(e);
        bool shouldClick = _isPressed && isPointerInside;

        _isPressed = false;
        _isPointerOver = isPointerInside;

        ReleasePointerCapture(e.Pointer);

        AnimateToState(isPointerInside ? PointerOverScale : NormalScale, isPointerInside ? PointerOverOpacity : NormalOpacity, PointerReleasedAnimationDuration);

        if (!shouldClick)
            return;

        Clicked?.Invoke(this, EventArgs.Empty);

        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        _isPressed = false;
        _isPointerOver = false;

        ReleasePointerCapture(e.Pointer);

        AnimateToState(NormalScale, NormalOpacity, PointerReleasedAnimationDuration);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        bool isPointerInside = IsPointerInside(e);

        _isPressed = false;
        _isPointerOver = isPointerInside;

        AnimateToState(isPointerInside ? PointerOverScale : NormalScale, isPointerInside ? PointerOverOpacity : NormalOpacity, PointerExitAnimationDuration);
    }
    #endregion

    #region Animation
    private void StopActiveAnimation()
    {
        double currentScaleX = _scale?.ScaleX ?? NormalScale;
        double currentScaleY = _scale?.ScaleY ?? NormalScale;
        double currentOpacity = Opacity;

        _activeAnimation?.Cancel();
        _activeAnimation = null;

        if (_scale is not null)
        {
            _scale.ScaleX = currentScaleX;
            _scale.ScaleY = currentScaleY;
        }

        Opacity = currentOpacity;
    }

    private void AnimateToState(double targetScale, double targetOpacity, int duration)
    {
        if (_scale is null)
            return;

        StopActiveAnimation();

        _targetScale = targetScale;
        _targetOpacity = targetOpacity;

        int token = NewAnimationToken();

        AnimationService.IAnimationHandle? animation = null;

        animation = AnimationService.CreateScaleAndOpacityAnimation(_scale, this, _targetScale, _targetOpacity, duration, onCompleted: () =>
            {
                if (!IsTokenValid(token))
                    return;

                if (!ReferenceEquals(_activeAnimation, animation))
                    return;

                _activeAnimation = null;
                ForceFinalState();
            });

        _activeAnimation = animation;
        animation.Start();
    }
    #endregion
}
