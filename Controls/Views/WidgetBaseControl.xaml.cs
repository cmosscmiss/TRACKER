using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Tracker.Controls.ViewModels;
using Tracker.Models;
using Tracker.Services;
using System;
using System.ComponentModel;

namespace Tracker.Controls.Views;

/// <summary>
/// Modo de presentación de un <see cref="WidgetBaseControl"/>.
/// </summary>
public enum WidgetDisplayMode
{
    /// <summary>
    /// Widget normal dentro del flujo del panel: chrome completo (brillo y sombra), y barra de cabecera con asa de
    /// arrastre cuya visibilidad la gobierna el setting global <see cref="GeneralSettings.ShowWidgetHeader"/>.
    /// </summary>
    Default,

    /// <summary>
    /// Widget fijo fuera del flujo del panel (p. ej. la banda superior con el selector de tipo de medio): sin barra de
    /// cabecera ni asa, y con chrome compacto (sin las capas decorativas de altura fija). Ignora el setting de cabecera.
    /// </summary>
    Fixed
}

/// <summary>
/// Base visual reutilizable para los widgets de la aplicación.
/// 
/// Responsabilidades principales:
/// - Proporcionar una estructura visual común para todos los widgets.
/// - Mostrar un título, un icono temático y un botón de cierre.
/// - Alojar contenido variable mediante la propiedad <see cref="Content"/>.
/// - Exponer eventos de drag para que el contenedor externo pueda gestionar
///   el reordenamiento o movimiento de widgets.
/// - Resolver automáticamente los iconos del widget y del botón de cierre
///   a partir del tema activo gestionado por <see cref="ThemeService"/>.
/// </summary>
public sealed partial class WidgetBaseControl : UserControl, INotifyPropertyChanged
{
    #region Constants
    private const double DefaultWidgetCornerRadius = 18;
    private const double WidgetInnerCornerRadiusOffset = 1;

    /// <summary>Alto de la barra de cabecera completa (con título).</summary>
    private const double FullHeaderHeight = 40;
    #endregion

    #region Attributes
    private readonly ThemeService _themeService;
    private readonly SharedDataService _sharedDataService;

    /// <summary>Configuración general (instancia compartida): se re-lee para aplicar la cabecera en caliente.</summary>
    private readonly AppSettings.GeneralSettings _general;

    /// <summary>Cache local de <see cref="AppSettings.GeneralSettings.ShowWidgetHeader"/>; se actualiza en caliente.</summary>
    private bool _showWidgetHeader;

    /// <summary>
    /// Evento emitido cuando el usuario inicia una operación de arrastre
    /// sobre la barra superior del widget.
    /// </summary>
    public event PointerEventHandler? DragStart;

    /// <summary>
    /// Evento emitido mientras el usuario mueve el puntero durante
    /// una operación de arrastre sobre la barra superior del widget.
    /// </summary>
    public event PointerEventHandler? DragMove;

    /// <summary>
    /// Evento emitido cuando el usuario finaliza una operación de arrastre
    /// sobre la barra superior del widget.
    /// </summary>
    public event PointerEventHandler? DragEnd;

    public event PropertyChangedEventHandler? PropertyChanged;
    #endregion

    #region Properties
    /// <summary>
    /// CornerRadius aplicado a la superficie exterior del widget.
    /// </summary>
    public CornerRadius WidgetOuterCornerRadius
    {
        get
        {
            double radius = NormalizeWidgetCornerRadius(WidgetCornerRadius);
            return new CornerRadius(radius);
        }
    }

    /// <summary>
    /// CornerRadius aplicado a las capas internas.
    /// Se reduce ligeramente para respetar el borde exterior.
    /// </summary>
    public CornerRadius WidgetInnerCornerRadius
    {
        get
        {
            double radius = GetInnerCornerRadius();
            return new CornerRadius(radius);
        }
    }

    /// <summary>
    /// CornerRadius solo para la parte superior del widget.
    /// Se usa en gloss superior y cabecera.
    /// </summary>
    public CornerRadius WidgetTopOnlyInnerCornerRadius
    {
        get
        {
            double radius = GetInnerCornerRadius();
            return new CornerRadius(radius, radius, 0, 0);
        }
    }

    /// <summary>
    /// CornerRadius solo para la parte inferior del widget.
    /// Se usa en la sombra interna inferior.
    /// </summary>
    public CornerRadius WidgetBottomOnlyInnerCornerRadius
    {
        get
        {
            double radius = GetInnerCornerRadius();
            return new CornerRadius(0, 0, radius, radius);
        }
    }
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Radio de esquina base del widget.
    /// Este valor controla la superficie principal, la sombra y las capas visuales internas.
    /// </summary>
    public double WidgetCornerRadius
    {
        get => (double)GetValue(WidgetCornerRadiusProperty);
        set => SetValue(WidgetCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty WidgetCornerRadiusProperty = DependencyProperty.Register(nameof(WidgetCornerRadius), typeof(double), typeof(WidgetBaseControl), new PropertyMetadata(DefaultWidgetCornerRadius, OnWidgetCornerRadiusChanged));

    /// <summary>
    /// Título mostrado en la barra superior del widget.
    /// </summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Dependency Property asociada a <see cref="Title"/>.
    /// </summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(WidgetBaseControl), new PropertyMetadata(string.Empty, OnTitleComponentChanged));

    /// <summary>
    /// Prefijo opcional que se antepone al <see cref="Title"/> en la barra superior del widget.
    /// El prefijo debe incluir su propio separador si lo necesita (por ejemplo <c>"GAME STATISTICS | "</c>).
    /// Cuando está vacío, se muestra solo el <see cref="Title"/>.
    /// </summary>
    public string TitlePrefix
    {
        get => (string)GetValue(TitlePrefixProperty);
        set => SetValue(TitlePrefixProperty, value);
    }

    /// <summary>
    /// Dependency Property asociada a <see cref="TitlePrefix"/>.
    /// </summary>
    public static readonly DependencyProperty TitlePrefixProperty = DependencyProperty.Register(nameof(TitlePrefix), typeof(string), typeof(WidgetBaseControl), new PropertyMetadata(string.Empty, OnTitleComponentChanged));

    /// <summary>
    /// Título compuesto que se muestra realmente: <see cref="TitlePrefix"/> (si existe) concatenado
    /// con <see cref="Title"/>. Es la propiedad a la que se enlaza la barra superior del widget.
    /// </summary>
    public string ComposedTitle => string.IsNullOrEmpty(TitlePrefix) ? Title : $"{TitlePrefix}  |  {Title}";

    /// <summary>
    /// Callback ejecutado cuando cambia <see cref="Title"/> o <see cref="TitlePrefix"/>: notifica el cambio
    /// de <see cref="ComposedTitle"/> para que el enlace <c>x:Bind</c> de la cabecera se actualice.
    /// </summary>
    private static void OnTitleComponentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (WidgetBaseControl)d;
        control.PropertyChanged?.Invoke(control, new PropertyChangedEventArgs(nameof(ComposedTitle)));
    }

    /// <summary>
    /// Fuente de imagen del icono principal del widget.
    /// Esta propiedad se calcula internamente a partir del control contenido
    /// en <see cref="Content"/> y del tema activo.
    /// </summary>
    public ImageSource? WidgetIconSource
    {
        get => (ImageSource?)GetValue(WidgetIconSourceProperty);
        private set => SetValue(WidgetIconSourceProperty, value);
    }

    /// <summary>
    /// Dependency Property asociada a <see cref="WidgetIconSource"/>.
    /// </summary>
    public static readonly DependencyProperty WidgetIconSourceProperty = DependencyProperty.Register(nameof(WidgetIconSource), typeof(ImageSource), typeof(WidgetBaseControl), new PropertyMetadata(null));

    /// <summary>
    /// Contenido visual alojado dentro del widget.
    /// 
    /// Normalmente será un UserControl concreto, por ejemplo:
    /// <c>GameListControl</c>, <c>PlatformDetailsControl</c>,
    /// <c>ImageTypeControl</c>, etc.
    /// 
    /// El tipo de este control se usa para calcular automáticamente
    /// el icono temático del widget.
    /// </summary>
    public new object Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Dependency Property asociada a <see cref="Content"/>.
    /// 
    /// Incluye un callback para recalcular el icono del widget cuando
    /// cambia el contenido alojado.
    /// </summary>
    public static new readonly DependencyProperty ContentProperty = DependencyProperty.Register(nameof(Content), typeof(object), typeof(WidgetBaseControl), new PropertyMetadata(null, OnContentChanged));

    /// <summary>
    /// Callback ejecutado cuando cambia el contenido del widget.
    /// 
    /// Se utiliza para recalcular el icono principal en función del nuevo
    /// control contenido.
    /// </summary>
    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (WidgetBaseControl)d;
        control.UpdateWidgetIconFromContent(e.NewValue);
    }

    /// <summary>
    /// Modo de presentación del widget (<see cref="WidgetDisplayMode"/>). Por defecto
    /// <see cref="WidgetDisplayMode.Default"/> (widget del panel). <see cref="WidgetDisplayMode.Fixed"/> lo deja sin
    /// cabecera ni asa y con chrome compacto (para la banda fija del panel). Sustituye a las antiguas propiedades
    /// ShowHeader/ShowDragHandle/CompactChrome.
    /// </summary>
    public WidgetDisplayMode DisplayMode
    {
        get => (WidgetDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>
    /// Dependency Property asociada a <see cref="DisplayMode"/>.
    /// </summary>
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(nameof(DisplayMode), typeof(WidgetDisplayMode), typeof(WidgetBaseControl), new PropertyMetadata(WidgetDisplayMode.Default, OnDisplayModeChanged));

    /// <summary>Callback ejecutado cuando cambia <see cref="DisplayMode"/>: reaplica cabecera y chrome.</summary>
    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WidgetBaseControl)d).ApplyDisplayMode();
    }

    /// <summary>
    /// Indica si el panel que contiene este widget está en "modo edición" (los splitters están visibles y
    /// el usuario puede redimensionar el layout). Cuando es <c>true</c> se solapa una capa de acento oscuro
    /// sobre todo el contenido del widget, señalando ese modo y bloqueando la interacción con el contenido
    /// hasta que el panel salga del modo edición.
    /// </summary>
    public bool PanelInEditMode
    {
        get => (bool)GetValue(PanelInEditModeProperty);
        set => SetValue(PanelInEditModeProperty, value);
    }

    /// <summary>
    /// Dependency Property asociada a <see cref="PanelInEditMode"/>.
    /// </summary>
    public static readonly DependencyProperty PanelInEditModeProperty = DependencyProperty.Register(nameof(PanelInEditMode), typeof(bool), typeof(WidgetBaseControl), new PropertyMetadata(false, OnPanelInEditModeChanged));

    /// <summary>
    /// Callback ejecutado cuando cambia <see cref="PanelInEditMode"/>: muestra u oculta la capa de
    /// redimensionado del widget.
    /// </summary>
    private static void OnPanelInEditModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WidgetBaseControl)d).UpdateResizeOverlay();
    }

    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el control, resuelve el <see cref="ThemeService"/>,
    /// registra los eventos de puntero para drag y configura el comando
    /// de cierre por defecto.
    /// </summary>
    public WidgetBaseControl()
    {
        InitializeComponent();

        _themeService = App.GetService<ThemeService>();
        _sharedDataService = App.GetService<SharedDataService>();

        // La cabecera de los widgets del panel se controla globalmente desde la configuración de la app. Se lee al
        // construir y también se aplica EN CALIENTE al aceptar la ventana de ajustes (evento WidgetHeaderVisibilityChanged).
        _general = App.GetService<IOptions<AppSettings>>().Value.General;
        _showWidgetHeader = _general.ShowWidgetHeader;

        // El arrastre se puede iniciar desde la barra de cabecera completa o desde el asa superpuesta fina (cuando la
        // cabecera está oculta). Ambos disparan los mismos eventos; el handler captura sobre el elemento que lo recibe.
        WidgetDragHandle.PointerPressed += OnDragHandlePointerPressed;
        WidgetDragHandle.PointerMoved += OnDragHandlePointerMoved;
        WidgetDragHandle.PointerReleased += OnDragHandlePointerReleased;

        WidgetDragOverlay.PointerPressed += OnDragHandlePointerPressed;
        WidgetDragOverlay.PointerMoved += OnDragHandlePointerMoved;
        WidgetDragOverlay.PointerReleased += OnDragHandlePointerReleased;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handler ejecutado cuando el control se carga en el árbol visual.
    /// 
    /// Se suscribe a los cambios de tema y calcula inicialmente los iconos
    /// correspondientes al tema activo.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _themeService.ThemeChanged += OnThemeChanged;
        _sharedDataService.WidgetHeaderVisibilityChanged += OnWidgetHeaderVisibilityChanged;

        UpdateThemeIcons();
        ApplyDisplayMode();
    }

    /// <summary>
    /// Handler ejecutado cuando el control se descarga del árbol visual.
    /// 
    /// Libera las suscripciones a eventos para evitar referencias vivas
    /// innecesarias y posibles fugas de memoria.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        _sharedDataService.WidgetHeaderVisibilityChanged -= OnWidgetHeaderVisibilityChanged;

        WidgetDragHandle.PointerPressed -= OnDragHandlePointerPressed;
        WidgetDragHandle.PointerMoved -= OnDragHandlePointerMoved;
        WidgetDragHandle.PointerReleased -= OnDragHandlePointerReleased;

        WidgetDragOverlay.PointerPressed -= OnDragHandlePointerPressed;
        WidgetDragOverlay.PointerMoved -= OnDragHandlePointerMoved;
        WidgetDragOverlay.PointerReleased -= OnDragHandlePointerReleased;

        Unloaded -= OnUnloaded;
    }

    /// <summary>
    /// Handler ejecutado cuando cambia el tema activo de la aplicación.
    /// 
    /// Recalcula todos los iconos dependientes del tema.
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeIcons();
        RefreshWidgetGradients();
    }

    /// <summary>
    /// Refresca los <see cref="LinearGradientBrush"/> del chrome del widget (sombra, borde, superficie, brillo, cabecera)
    /// con los colores del tema actual. Sus <see cref="GradientStop"/> usan recursos de tipo <see cref="Windows.UI.Color"/>
    /// (tipo por valor), que no se propagan solos al cambiar de tema en caliente como sí lo hacen los brushes; por eso se
    /// reasignan aquí. Cada instancia de widget tiene su propia copia de estos brushes (definidos en sus Resources).
    /// </summary>
    private void RefreshWidgetGradients()
    {
        Windows.UI.Color background = _themeService.BackgroundColor;
        Windows.UI.Color backgroundLight = _themeService.BackgroundLightColor;
        Windows.UI.Color cardLight = _themeService.CardBackgroundLightColor;
        Windows.UI.Color accent = _themeService.AccentColor;
        Windows.UI.Color accentLight = _themeService.AccentLightColor;
        Windows.UI.Color text = _themeService.TextColor;

        SetGradientStops("WidgetShadowBrush", A(background, 0.8), A(background, 0.6), A(background, 0.2));
        SetGradientStops("WidgetOuterBorderBrush", A(text, 0.6), A(accentLight, 0.8), A(accent, 0.8), A(background, 0.8));
        SetGradientStops("WidgetSurfaceBackgroundBrush", A(cardLight, 0.8), A(backgroundLight, 0.8), A(background, 0.8));
        SetGradientStops("WidgetBottomInnerShadowBrush", A(background, 0.2), A(background, 0.6), A(background, 0.8));
        SetGradientStops("WidgetInnerBorderBrush", A(text, 0.4), A(text, 0.2), A(background, 0.2), A(background, 0.6));
        // El último stop de WidgetTopGlossBrush es Transparent fijo: solo se refrescan los dos primeros.
        SetGradientStops("WidgetTopGlossBrush", A(text, 0.6), A(text, 0.2));
        SetGradientStops("WidgetHeaderBackgroundBrush", A(text, 0.4), A(text, 0.2), A(background, 0.2));
    }

    /// <summary>Asigna, en orden, los colores dados a los primeros <see cref="GradientStop"/> del brush con esa clave.</summary>
    private void SetGradientStops(string brushKey, params Windows.UI.Color[] colors)
    {
        if (Resources.TryGetValue(brushKey, out object? value) && value is LinearGradientBrush brush)
        {
            for (int i = 0; i < colors.Length && i < brush.GradientStops.Count; i++)
                brush.GradientStops[i].Color = colors[i];
        }
    }

    /// <summary>Devuelve el color con la componente alfa ajustada a <paramref name="opacity"/> (0..1).</summary>
    private static Windows.UI.Color A(Windows.UI.Color color, double opacity)
        => Windows.UI.Color.FromArgb((byte)System.Math.Round(255 * opacity), color.R, color.G, color.B);

    /// <summary>
    /// Aplica en caliente el ajuste global de visibilidad de la cabecera (al aceptar la ventana de configuración):
    /// re-lee el valor de la configuración compartida y reconstruye la presentación del widget.
    /// </summary>
    private void OnWidgetHeaderVisibilityChanged(object? sender, EventArgs e)
    {
        _showWidgetHeader = _general.ShowWidgetHeader;
        ApplyDisplayMode();
    }

    /// <summary>
    /// Handler ejecutado cuando el usuario presiona el puntero sobre
    /// la barra superior del widget.
    /// 
    /// Captura el puntero para mantener el seguimiento durante el drag
    /// y emite el evento público <see cref="DragStart"/>.
    /// </summary>
    private void OnDragHandlePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ((UIElement)sender).CapturePointer(e.Pointer);
        DragStart?.Invoke(this, e);
    }

    /// <summary>
    /// Handler ejecutado mientras el puntero se mueve sobre la barra superior
    /// del widget.
    /// 
    /// Emite el evento público <see cref="DragMove"/> para que el contenedor
    /// externo pueda actualizar la posición o el slot objetivo.
    /// </summary>
    private void OnDragHandlePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        DragMove?.Invoke(this, e);
    }

    /// <summary>
    /// Handler ejecutado cuando el usuario libera el puntero tras una posible
    /// operación de arrastre.
    /// 
    /// Libera la captura del puntero y emite el evento público
    /// <see cref="DragEnd"/>.
    /// </summary>
    private void OnDragHandlePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        DragEnd?.Invoke(this, e);
    }

    /// <summary>
    /// Callback ejecutado automáticamente cuando cambia la propiedad
    /// <see cref="WidgetCornerRadius"/>.
    /// </summary>
    private static void OnWidgetCornerRadiusChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WidgetBaseControl control)
        {
            control.ApplyWidgetCornerRadius();
        }
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Actualiza todos los iconos temáticos usados por el widget:
    /// - Icono principal del widget.
    /// - Icono del botón de cierre.
    /// </summary>
    private void UpdateThemeIcons()
    {
        UpdateWidgetIconFromContent(Content);
    }

    /// <summary>
    /// Calcula y asigna el icono principal del widget a partir del control
    /// contenido en <see cref="Content"/>.
    /// 
    /// El nombre del fichero se deriva del nombre del tipo del control.
    /// Por ejemplo, <c>GameListControl</c> buscará:
    /// <c>Widgets/GameListControl.png</c> dentro del tema activo.
    /// </summary>
    /// <param name="content">
    /// Control alojado dentro del widget. Si es null (o su tema no trae ese icono), el widget se queda SIN icono: no
    /// hay imagen genérica de reserva.
    /// </param>
    private void UpdateWidgetIconFromContent(object? content)
    {
        Uri? iconUri = _themeService.GetWidgetIconUri(content?.GetType().Name);

        WidgetIconSource = iconUri is null ? null : new BitmapImage(iconUri);
    }

    /// <summary>
    /// Aplica el valor actual de <see cref="WidgetCornerRadius"/> a todas las capas visuales
    /// que forman el widget, manteniendo la coherencia entre la superficie exterior,
    /// las capas internas, la cabecera, el brillo superior y la sombra inferior.
    /// </summary>
    private void ApplyWidgetCornerRadius()
    {
        if (WidgetSurface is null)
            return;

        double outerRadius = NormalizeWidgetCornerRadius(WidgetCornerRadius);
        double innerRadius = Math.Max(0, outerRadius - 1);

        var outer = new CornerRadius(outerRadius);
        var inner = new CornerRadius(innerRadius);
        var topOnly = new CornerRadius(innerRadius, innerRadius, 0, 0);
        var bottomOnly = new CornerRadius(0, 0, innerRadius, innerRadius);

        WidgetShadowLayer.CornerRadius = outer;
        WidgetSurface.CornerRadius = outer;

        WidgetResizeOverlay.CornerRadius = outer;

        WidgetThemeTintLayer.CornerRadius = inner;
        WidgetInnerBorderLayer.CornerRadius = inner;

        WidgetTopGlossLayer.CornerRadius = topOnly;
        WidgetHeaderBackgroundLayer.CornerRadius = topOnly;

        WidgetBottomInnerShadowLayer.CornerRadius = bottomOnly;
    }

    /// <summary>
    /// Aplica <see cref="DisplayMode"/> a la UI. Tres presentaciones:
    /// - <b>Default</b> con <see cref="GeneralSettings.ShowWidgetHeader"/> = true: barra de cabecera completa (título,
    ///   asa, cerrar) y chrome completo (brillo superior y sombra inferior).
    /// - <b>Default</b> con el setting = false: SIN barra de cabecera, su fondo ni el chrome (queda limpio, igual que
    ///   Fixed); el contenido ocupa todo el alto y el arrastre se hace con el asa superpuesta fina
    ///   (<c>WidgetDragOverlay</c>), que no se solapa apenas con el contenido y no bloquea el drag&amp;drop.
    /// - <b>Fixed</b> (banda): igual que el anterior pero SIN asa de arrastre (no participa en el drag).
    /// </summary>
    private void ApplyDisplayMode()
    {
        if (WidgetHeaderRow is null)
            return;

        bool isFixed = DisplayMode == WidgetDisplayMode.Fixed;
        bool headerBar = !isFixed && _showWidgetHeader;       // barra de cabecera completa
        bool dragOverlay = !isFixed && !_showWidgetHeader;    // asa fina superpuesta (cabecera oculta, modo Default)

        WidgetHeaderRow.Height = headerBar ? new GridLength(FullHeaderHeight) : new GridLength(0);
        WidgetDragHandle.Visibility = headerBar ? Visibility.Visible : Visibility.Collapsed;
        WidgetHeaderBackgroundLayer.Visibility = headerBar ? Visibility.Visible : Visibility.Collapsed;

        // Chrome (brillo superior y sombra interna inferior): solo con la cabecera completa. Sin cabecera (oculta o
        // Fixed) se ocultan para que el widget quede limpio y sin imponer altura mínima.
        var chromeVisibility = headerBar ? Visibility.Visible : Visibility.Collapsed;
        WidgetTopGlossLayer.Visibility = chromeVisibility;
        WidgetBottomInnerShadowLayer.Visibility = chromeVisibility;

        WidgetDragOverlay.Visibility = dragOverlay ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Muestra u oculta la capa de redimensionado del widget según <see cref="PanelInEditMode"/>, con un
    /// fundido coherente con la aparición de los splitters y la barra flotante del panel.
    /// </summary>
    private void UpdateResizeOverlay()
    {
        if (WidgetResizeOverlay is null)
            return;

        if (PanelInEditMode)
        {
            WidgetResizeOverlay.Visibility = Visibility.Visible;
            AnimationService.CreateOpacityAnimation(WidgetResizeOverlay, WidgetResizeOverlay.Opacity, 1, 200).Start();
        }
        else
        {
            AnimationService.CreateOpacityAnimation(WidgetResizeOverlay, WidgetResizeOverlay.Opacity, 0, 150)
                .StartAsync()
                .ContinueWith(_ => DispatcherQueue.TryEnqueue(() => WidgetResizeOverlay.Visibility = Visibility.Collapsed));
        }
    }

    /// <summary>
    /// Normaliza un valor de radio de esquina para garantizar que sea válido antes
    /// de aplicarlo visualmente al widget.
    /// </summary>
    /// <param name="value">Valor de radio recibido desde <see cref="WidgetCornerRadius"/>.</param>
    /// <returns>Un radio válido, nunca negativo, o el valor por defecto si el valor recibido no es válido.</returns>
    private static double NormalizeWidgetCornerRadius(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return DefaultWidgetCornerRadius;

        return Math.Max(0, value);
    }

    /// <summary>
    /// Calcula el radio de esquina que deben usar las capas internas del widget,
    /// aplicando un pequeño offset respecto al radio exterior.
    /// </summary>
    /// <returns>Radio interno normalizado, nunca negativo.</returns>
    private double GetInnerCornerRadius()
    {
        double radius = NormalizeWidgetCornerRadius(WidgetCornerRadius);
        return Math.Max(0, radius - WidgetInnerCornerRadiusOffset);
    }
    #endregion
}