using System;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Tracker.Services;

namespace Tracker.Controls.Templates;

/// <summary>
/// Carpeta del tema activo de la que se resuelve un icono indicado por nombre.
/// </summary>
public enum ToolbarIconFolder
{
    /// <summary>Carpeta <c>Icons/</c> del tema (iconos genéricos de interfaz).</summary>
    Icons,

    /// <summary>Carpeta <c>Widgets/</c> del tema (iconos de widgets).</summary>
    Widgets
}

/// <summary>
/// Control reutilizable para representar un botón de toolbar basado en iconos.
/// 
/// Soporta dos modos de uso:
/// - Botón simple: usa únicamente <see cref="Icon"/>.
/// - Botón con estado: usa <see cref="CheckedIcon"/> y <see cref="UncheckedIcon"/>
///   junto con <see cref="IsChecked"/>.
/// 
/// El control no decide internamente qué imagen se muestra modificando la propiedad
/// <see cref="Icon"/>. Esa lógica queda delegada al template visual mediante
/// VisualStates, lo que permite mantener compatibilidad con botones simples y
/// botones con estado checked/unchecked.
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
    private readonly ThemeService _themeService = App.GetService<ThemeService>();

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
    /// <summary>
    /// Evento emitido cuando el usuario completa una interacción de click
    /// sobre el botón.
    /// </summary>
    public event EventHandler? Clicked;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el control y asocia el estilo por defecto definido para
    /// <see cref="ToolbarButtonIcon"/>.
    /// </summary>
    public ToolbarButtonIcon()
    {
        DefaultStyleKey = typeof(ToolbarButtonIcon);

        Loaded += OnThemeLoaded;
        Unloaded += OnThemeUnloaded;
    }
    #endregion

    #region Theme-aware icons
    /// <summary>
    /// Nombre (sin extensión) del icono simple a resolver desde el tema activo,
    /// dentro de la carpeta indicada por <see cref="IconFolder"/>.
    /// Por ejemplo <c>Icon-layout</c> resuelve <c>Icons/Icon-layout.png</c> del tema actual.
    /// </summary>
    public string? IconName
    {
        get => (string?)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(nameof(IconName), typeof(string), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconNameChanged));

    /// <summary>Nombre del icono mostrado en estado checked (resuelto desde el tema activo).</summary>
    public string? CheckedIconName
    {
        get => (string?)GetValue(CheckedIconNameProperty);
        set => SetValue(CheckedIconNameProperty, value);
    }

    public static readonly DependencyProperty CheckedIconNameProperty = DependencyProperty.Register(nameof(CheckedIconName), typeof(string), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconNameChanged));

    /// <summary>Nombre del icono mostrado en estado unchecked (resuelto desde el tema activo).</summary>
    public string? UncheckedIconName
    {
        get => (string?)GetValue(UncheckedIconNameProperty);
        set => SetValue(UncheckedIconNameProperty, value);
    }

    public static readonly DependencyProperty UncheckedIconNameProperty = DependencyProperty.Register(nameof(UncheckedIconName), typeof(string), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconNameChanged));

    /// <summary>
    /// Carpeta del tema de la que se resuelven los nombres de icono. Por defecto <see cref="ToolbarIconFolder.Icons"/>.
    /// </summary>
    public ToolbarIconFolder IconFolder
    {
        get => (ToolbarIconFolder)GetValue(IconFolderProperty);
        set => SetValue(IconFolderProperty, value);
    }

    public static readonly DependencyProperty IconFolderProperty = DependencyProperty.Register(nameof(IconFolder), typeof(ToolbarIconFolder), typeof(ToolbarButtonIcon), new PropertyMetadata(ToolbarIconFolder.Icons, OnIconNameChanged));

    /// <summary>Recalcula los iconos cuando cambia cualquiera de los nombres o la carpeta.</summary>
    private static void OnIconNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ToolbarButtonIcon)d).UpdateThemeIcons();
    }

    /// <summary>Al cargarse: se suscribe a los cambios de tema y resuelve los iconos del tema activo.</summary>
    private void OnThemeLoaded(object sender, RoutedEventArgs e)
    {
        _themeService.ThemeChanged += OnThemeChanged;
        UpdateThemeIcons();
    }

    /// <summary>Al descargarse: libera la suscripción al cambio de tema.</summary>
    private void OnThemeUnloaded(object sender, RoutedEventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
    }

    /// <summary>Recalcula los iconos cuando cambia el tema activo de la aplicación.</summary>
    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeIcons();

    /// <summary>
    /// Resuelve y asigna <see cref="Icon"/>/<see cref="CheckedIcon"/>/<see cref="UncheckedIcon"/> a partir
    /// de los nombres declarados, usando la carpeta del tema activo. Los nombres vacíos se ignoran, de modo
    /// que un control puede seguir recibiendo sus <see cref="ImageSource"/> directamente desde XAML.
    /// </summary>
    private void UpdateThemeIcons()
    {
        if (!string.IsNullOrWhiteSpace(IconName))
            Icon = new BitmapImage(ResolveIconUri(IconName));

        if (!string.IsNullOrWhiteSpace(CheckedIconName))
            CheckedIcon = new BitmapImage(ResolveIconUri(CheckedIconName));

        if (!string.IsNullOrWhiteSpace(UncheckedIconName))
            UncheckedIcon = new BitmapImage(ResolveIconUri(UncheckedIconName));
    }

    /// <summary>Construye la URI de un icono del tema activo según <see cref="IconFolder"/>.</summary>
    private Uri ResolveIconUri(string name)
        => IconFolder == ToolbarIconFolder.Widgets
            ? _themeService.GetWidgetIconUri(name)
            : _themeService.GetIconUri(name);
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Indica si el botón se encuentra en estado seleccionado.
    /// 
    /// Esta propiedad se usa principalmente en botones de tipo toggle,
    /// donde el template alterna visualmente entre <see cref="CheckedIcon"/>
    /// y <see cref="UncheckedIcon"/>.
    /// </summary>
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="IsChecked"/>.
    /// 
    /// Cuando cambia, se actualiza el VisualState del control para reflejar
    /// el estado <c>Checked</c> o <c>Unchecked</c>.
    /// </summary>
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(nameof(IsChecked),
            typeof(bool), typeof(ToolbarButtonIcon), new PropertyMetadata(false, OnIsCheckedChanged));

    /// <summary>
    /// Actualiza el estado visual del botón cuando cambia <see cref="IsChecked"/>.
    /// </summary>
    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ToolbarButtonIcon)d).UpdateCheckState();
    }

    /// <summary>
    /// Icono principal del botón.
    /// 
    /// Se usa para botones simples que no tienen estado checked/unchecked.
    /// En el template actual se renderiza como <c>SingleIcon</c>.
    /// </summary>
    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="Icon"/>.
    /// 
    /// Permite asignar el icono desde XAML, binding o code-behind.
    /// Cuando cambia, se actualiza el estado visual para asegurar que el template
    /// refleja el icono disponible.
    /// </summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>
    /// Icono mostrado cuando el botón está en estado checked.
    /// 
    /// Se usa para botones con estado. El template controla su visibilidad
    /// mediante VisualStates.
    /// </summary>
    public ImageSource? CheckedIcon
    {
        get => (ImageSource?)GetValue(CheckedIconProperty);
        set => SetValue(CheckedIconProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="CheckedIcon"/>.
    /// 
    /// Permite asignar el icono activo desde XAML, binding o code-behind.
    /// Cuando cambia, se sincroniza el estado visual del botón.
    /// </summary>
    public static readonly DependencyProperty CheckedIconProperty = DependencyProperty.Register(nameof(CheckedIcon), typeof(ImageSource), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>
    /// Icono mostrado cuando el botón está en estado unchecked.
    /// 
    /// Se usa para botones con estado. El template controla su visibilidad
    /// mediante VisualStates.
    /// </summary>
    public ImageSource? UncheckedIcon
    {
        get => (ImageSource?)GetValue(UncheckedIconProperty);
        set => SetValue(UncheckedIconProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="UncheckedIcon"/>.
    /// 
    /// Permite asignar el icono inactivo desde XAML, binding o code-behind.
    /// Cuando cambia, se sincroniza el estado visual del botón.
    /// </summary>
    public static readonly DependencyProperty UncheckedIconProperty = DependencyProperty.Register(nameof(UncheckedIcon), typeof(ImageSource), typeof(ToolbarButtonIcon), new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>
    /// Refresca el estado visual cuando cambia cualquiera de los iconos
    /// disponibles en el control.
    /// </summary>
    private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ToolbarButtonIcon)d).UpdateCheckState();
    }
    #endregion

    #region Dependency Properties - Command
    /// <summary>
    /// Comando ejecutado cuando el usuario completa una interacción de click
    /// sobre el botón.
    /// 
    /// El comando se ejecuta en <c>PointerReleased</c>, siempre que la pulsación
    /// haya empezado y terminado dentro del botón.
    /// </summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="Command"/>.
    /// 
    /// Permite enlazar comandos desde ViewModels o asignarlos desde code-behind.
    /// </summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command),
            typeof(ICommand), typeof(ToolbarButtonIcon), new PropertyMetadata(null));

    /// <summary>
    /// Parámetro enviado al <see cref="Command"/> cuando el botón ejecuta
    /// la acción asociada.
    /// </summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="CommandParameter"/>.
    /// 
    /// Permite pasar información contextual al comando ejecutado por el botón.
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(ToolbarButtonIcon), new PropertyMetadata(null));
    #endregion

    #region Template
    /// <summary>
    /// Se ejecuta cuando se aplica el template visual del control.
    /// Inicializa las transformaciones visuales, registra los eventos de puntero
    /// y sincroniza el estado checked/unchecked con el template.
    /// </summary>
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

        // Root mantiene estable la zona real de hit testing.
        // IconHost es el único elemento que escala visualmente.
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

    /// <summary>
    /// Libera los eventos asociados al control cuando sale del árbol visual.
    /// </summary>
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
    /// <summary>
    /// Genera un nuevo token lógico para invalidar animaciones anteriores.
    /// </summary>
    private int NewAnimationToken() => ++_animationToken;

    /// <summary>
    /// Comprueba si una animación sigue siendo la animación activa más reciente.
    /// </summary>
    private bool IsTokenValid(int token) => token == _animationToken;

    /// <summary>
    /// Limpia el estado interno de puntero.
    /// </summary>
    private void ResetPointerState()
    {
        _isPointerOver = false;
        _isPressed = false;

        _targetScale = NormalScale;
        _targetOpacity = NormalOpacity;
    }

    /// <summary>
    /// Sincroniza el estado visual checked/unchecked del control con su template.
    /// </summary>
    private void UpdateCheckState()
    {
        if (_root is null)
            return;

        VisualStateManager.GoToState(this, IsChecked ? "Checked" : "Unchecked", true);
    }

    /// <summary>
    /// Fuerza el estado final esperado tras completar una animación.
    /// </summary>
    private void ForceFinalState()
    {
        if (_scale is null)
            return;

        _scale.ScaleX = _targetScale;
        _scale.ScaleY = _targetScale;
        Opacity = _targetOpacity;
    }

    /// <summary>
    /// Determina si el puntero se encuentra actualmente dentro de los límites
    /// reales del control.
    /// </summary>
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
    /// <summary>
    /// Se ejecuta cuando el puntero entra en el área del botón.
    /// Aplica el estado visual de hover.
    /// </summary>
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

    /// <summary>
    /// Se ejecuta cuando el puntero sale del área del botón.
    /// Restaura el estado visual normal si no hay una pulsación activa.
    /// </summary>
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;

        if (_isPressed)
            return;

        AnimateToState(NormalScale, NormalOpacity, PointerExitAnimationDuration);
    }

    /// <summary>
    /// Se ejecuta cuando el usuario presiona el botón.
    /// Captura el puntero y aplica el estado visual de pulsación.
    /// </summary>
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        _isPressed = true;
        _isPointerOver = true;

        CapturePointer(e.Pointer);

        AnimateToState(PressedScale, PressedOpacity, PointerPressedAnimationDuration);
    }

    /// <summary>
    /// Se ejecuta cuando el usuario libera el puntero.
    /// Si la liberación ocurre sobre el botón, emite el evento de click
    /// y ejecuta el comando asociado.
    /// </summary>
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

    /// <summary>
    /// Se ejecuta cuando el sistema cancela la interacción de puntero.
    /// Restaura el estado visual normal y libera la captura del puntero.
    /// </summary>
    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        _isPressed = false;
        _isPointerOver = false;

        ReleasePointerCapture(e.Pointer);

        AnimateToState(NormalScale, NormalOpacity, PointerReleasedAnimationDuration);
    }

    /// <summary>
    /// Se ejecuta cuando se pierde la captura del puntero.
    /// 
    /// Esto puede ocurrir por cambios de foco, cambios de layout, drag,
    /// navegación visual o pérdida inesperada de captura.
    /// </summary>
    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        bool isPointerInside = IsPointerInside(e);

        _isPressed = false;
        _isPointerOver = isPointerInside;

        AnimateToState(isPointerInside ? PointerOverScale : NormalScale, isPointerInside ? PointerOverOpacity : NormalOpacity, PointerExitAnimationDuration);
    }

    #endregion

    #region Animation

    /// <summary>
    /// Detiene cualquier animación activa y conserva el valor visual actual
    /// como punto de partida para la siguiente animación.
    /// </summary>
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

    /// <summary>
    /// Ejecuta la animación hacia el estado visual solicitado.
    /// 
    /// La animación anterior se cancela explícitamente mediante AnimationService
    /// antes de iniciar la nueva, evitando que estados antiguos sigan modificando
    /// ScaleX, ScaleY u Opacity.
    /// </summary>
    /// <param name="targetScale">Escala final esperada.</param>
    /// <param name="targetOpacity">Opacidad final esperada.</param>
    /// <param name="duration">Duración de la animación en milisegundos.</param>
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