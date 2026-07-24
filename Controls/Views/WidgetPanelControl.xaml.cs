using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.Options;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control principal que gestiona un panel de widgets con soporte para:
/// - Layout dinámico basado en <see cref="LayoutInfo"/>.
/// - Drag & drop entre slots con ghost visual.
/// - Hitboxes dinámicos para detectar slots durante el drag.
/// - Reposicionamiento determinista de widgets según <see cref="WidgetViewModelBase.SlotIndex"/>.
/// - Reacción a cambios externos de SlotIndex.
/// - Integración con <see cref="AnimationService"/> para transiciones visuales.
/// 
/// Este control es la fuente visual principal del sistema de widgets. El estado lógico
/// de cada widget viene dado por su ViewModel; el control solo interpreta ese estado
/// para posicionar, mostrar u ocultar los widgets.
/// </summary>
[ContentProperty(Name = nameof(Children))]
public sealed partial class WidgetPanelControl : UserControl
{
    #region Nested classes
    /// <summary>
    /// Representa un widget registrado dentro del panel.
    /// Asocia el ViewModel lógico del widget con su control visual real y conserva
    /// un estado temporal de posicionamiento durante la reconstrucción del layout.
    /// </summary>
    public sealed class WidgetEntry
    {
        /// <summary>
        /// Crea una nueva entrada de widget.
        /// </summary>
        /// <param name="vm">ViewModel asociado al widget.</param>
        /// <param name="control">Control visual asociado al widget.</param>
        public WidgetEntry(WidgetViewModelBase vm, WidgetBaseControl control)
        {
            ViewModel = vm;
            Control = control;
            IsPositioned = false;
        }

        public WidgetViewModelBase ViewModel { get; init; }

        public WidgetBaseControl Control { get; init; }

        public bool IsPositioned { get; set; }
    }

    /// <summary>
    /// Mantiene el estado temporal de una operación de drag & drop dentro del panel.
    /// </summary>
    private sealed class DragState
    {
        public WidgetEntry? DraggedEntry { get; set; }
        public bool IsDragging { get; set; }
        public int OriginSlot { get; set; } = -1;
        public int CurrentHoverSlot { get; set; } = -1;
        public Image? Ghost { get; set; }
        public Point Offset { get; set; }

        /// <summary>
        /// Restaura el estado de drag a sus valores iniciales.
        /// </summary>
        public void Reset()
        {
            DraggedEntry = null;
            IsDragging = false;
            OriginSlot = -1;
            CurrentHoverSlot = -1;
            Ghost = null;
            Offset = default;
        }
    }

    /// <summary>
    /// Border usado como handle de fila. Subclasea solo para poder fijar el cursor de redimensionado
    /// vertical (<see cref="UIElement.ProtectedCursor"/> es protegido y no se puede asignar a un Border normal).
    /// </summary>
    private sealed class RowResizeHandle : Grid
    {
        public RowResizeHandle()
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth);
        }
    }
    #endregion

    #region Attributes
    private readonly ThemeService _themeService;
    private readonly AppSettings _appSettings;

    private readonly List<WidgetEntry> _widgets = new();
    private readonly List<Border> _slotHitBoxes = new();
    private readonly List<GridSplitter> _columnSplitters = new();
    // Handles de fila PROPIOS (uno por columna partida): a diferencia de un GridSplitter sobre filas
    // compartidas, cada uno ajusta SOLO el ratio de su columna, así el redimensionado es independiente de
    // verdad. Los widgets nunca se reparentan (siempre hijos directos de WidgetGrid) → las gráficas SkiaSharp
    // no se rompen; solo cambian su Grid.Row/RowSpan sobre una rejilla de filas de grano fino.
    private readonly List<Grid> _rowHandles = new();
    // Ratio de partición (fracción de altura de la fila superior) por columna partida; estado vivo.
    private readonly Dictionary<int, double> _columnRatios = new();
    // Fronteras de fila normalizadas (0..1) del layout actual, ordenadas; definen las RowDefinitions.
    private List<double> _rowBoundaries = new() { 0.0, 1.0 };
    private int _draggingRowColumn = -1;
    private readonly DragState _dragState = new();
    private readonly SolidColorBrush _slotBaseBrush;
    private readonly SolidColorBrush _slotHoverBrush;
    private readonly SolidColorBrush _slotTransparentBrush = new(Colors.Transparent);
    private bool _suppressNextSizeCapture;
    private bool _themeSubscribed;
    private bool _suppressSlotIndexLayoutRefresh;
    private bool _slotIndexLayoutRefreshQueued;

    // Banda fija superior: alto natural medido (para animar entre 0 y ese alto), animación en curso y banderas de
    // ciclo de vida. _bandReady evita animar el estado inicial (que se aplica sin animación en Loaded).
    private double _bandNaturalHeight;
    private bool _bandReady;
    private bool _bandAnimating;
    private AnimationService.IAnimationHandle? _bandAnimation;
    #endregion

    #region Top band constants
    private const double TopBandFadeMs = 180;
    private const double TopBandResizeMs = 240;
    private const double TopBandFallbackHeight = 64;
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Layout actual aplicado al panel.
    /// Define la estructura de slots, sus posiciones, spans, márgenes y anchos de columna.
    /// </summary>
    public LayoutInfo? Layout
    {
        get => (LayoutInfo?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="Layout"/>.
    /// Cuando cambia, el panel reconstruye la disposición visual si ya está cargado.
    /// </summary>
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(nameof(Layout), typeof(LayoutInfo), typeof(WidgetPanelControl), new PropertyMetadata(null, OnLayoutChanged));

    /// <summary>
    /// Indica si los grid splitters (entre filas y columnas usadas por el layout) están visibles y
    /// activos. Es estado de UI transitorio: no se persiste y siempre arranca en <c>false</c>.
    /// </summary>
    public bool SplittersVisible
    {
        get => (bool)GetValue(SplittersVisibleProperty);
        set => SetValue(SplittersVisibleProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="SplittersVisible"/>.
    /// Al cambiar, actualiza la visibilidad de los splitters sin reconstruir el layout y, al mostrarlos,
    /// lanza el efecto de atención.
    /// </summary>
    public static readonly DependencyProperty SplittersVisibleProperty = DependencyProperty.Register(nameof(SplittersVisible), typeof(bool), typeof(WidgetPanelControl), new PropertyMetadata(false, OnSplittersVisibleChanged));

    /// <summary>
    /// Espejo bidireccional del toggle maestro de splitters (<c>SplittersEnabled</c> del VM). El panel no
    /// reacciona a los cambios entrantes (su estado visual lo gobierna <see cref="SplittersVisible"/>); solo
    /// <em>escribe</em> en esta propiedad para autoocultar los splitters tras un periodo de inactividad,
    /// propagando la desactivación al VM (que cierra el gap, oculta y persiste los tamaños).
    /// </summary>
    public bool SplittersEnabled
    {
        get => (bool)GetValue(SplittersEnabledProperty);
        set => SetValue(SplittersEnabledProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="SplittersEnabled"/>.
    /// </summary>
    public static readonly DependencyProperty SplittersEnabledProperty = DependencyProperty.Register(nameof(SplittersEnabled), typeof(bool), typeof(WidgetPanelControl), new PropertyMetadata(false));

    /// <summary>
    /// Contenido de la banda fija superior del panel, a ancho completo, en una fila propia por encima del grid de
    /// widgets. NO participa en el sistema de slots: no se arrastra, no se redimensiona y no se oculta. Pensada para
    /// alojar un control persistente (p. ej. el selector de tipo de medio). Es independiente de <see cref="Children"/>.
    /// </summary>
    public object TopBandContent
    {
        get => GetValue(TopBandContentProperty);
        set => SetValue(TopBandContentProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="TopBandContent"/>.
    /// </summary>
    public static readonly DependencyProperty TopBandContentProperty = DependencyProperty.Register(nameof(TopBandContent), typeof(object), typeof(WidgetPanelControl), new PropertyMetadata(null));

    /// <summary>
    /// Visibilidad de la banda fija superior (<see cref="TopBandContent"/>). Al cambiar en caliente, anima la
    /// transición: al ocultar hace fade out del contenido y luego colapsa su fila (el grid de widgets crece hacia
    /// arriba); al mostrar expande la fila (el grid decrece para hacer hueco) y luego hace fade in. Por defecto true.
    /// </summary>
    public bool TopBandVisible
    {
        get => (bool)GetValue(TopBandVisibleProperty);
        set => SetValue(TopBandVisibleProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="TopBandVisible"/>.
    /// </summary>
    public static readonly DependencyProperty TopBandVisibleProperty = DependencyProperty.Register(nameof(TopBandVisible), typeof(bool), typeof(WidgetPanelControl), new PropertyMetadata(true, OnTopBandVisibleChanged));

    /// <summary>
    /// Anima la banda al cambiar <see cref="TopBandVisible"/>, salvo en el arranque (antes del primer Loaded), donde
    /// el estado se aplica sin animación desde <see cref="OnLoaded"/>.
    /// </summary>
    private static void OnTopBandVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (WidgetPanelControl)d;

        if (panel._bandReady)
            panel.AnimateTopBand((bool)e.NewValue);
    }
    #endregion

    #region Properties
    /// <summary>
    /// Colección visual de hijos del panel.
    /// Permite declarar widgets como contenido del control.
    /// </summary>
    public UIElementCollection Children => WidgetGrid.Children;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el panel de widgets, resuelve servicios, prepara brushes visuales
    /// y registra los eventos de ciclo de vida.
    /// </summary>
    public WidgetPanelControl()
    {
        _themeService = App.GetService<ThemeService>();
        _appSettings = App.GetService<IOptions<AppSettings>>().Value;

        _slotBaseBrush = new SolidColorBrush(_themeService.AccentLightColor);
        _slotHoverBrush = new SolidColorBrush(_themeService.AccentColor);

        InitializeComponent();

        WidgetGrid.Opacity = 0;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Dependency Property callbacks
    /// <summary>
    /// Se ejecuta cuando cambia el layout asignado al panel.
    /// Si el control ya está cargado, aplica inmediatamente la nueva estructura.
    /// </summary>
    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var widgetPanel = (WidgetPanelControl)d;
        if (!widgetPanel.IsLoaded || e.NewValue is not LayoutInfo newLayout)
            return;

        var oldLayout = e.OldValue as LayoutInfo;
        var sameLayoutType = oldLayout != null && oldLayout.Index == newLayout.Index;

        // Al abandonar un layout por otro de distinto tipo, persistir los tamaños vivos del que se deja
        // (con su índice antiguo, ya que Layout apunta al nuevo) para que al volver se restauren.
        if (!sameLayoutType && oldLayout != null)
            widgetPanel.CaptureLayoutSizes(oldLayout);

        // Mismo tipo de layout (p. ej. cada frame de la animación del gap) ⇒ conservar los tamaños vivos;
        // cambio de tipo o primera asignación ⇒ resetear y aplicar los tamaños persistidos.
        _ = widgetPanel.ApplyGridLayout(newLayout, animateLayoutTransition: !sameLayoutType, preserveCurrentSizes: sameLayoutType);
    }

    /// <summary>
    /// Se ejecuta cuando cambia <see cref="SplittersVisible"/>.
    /// Muestra u oculta los splitters existentes sin reconstruir el layout y, al mostrarlos, lanza el
    /// efecto de atención.
    /// </summary>
    private static void OnSplittersVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var widgetPanel = (WidgetPanelControl)d;

        // Al ocultar los splitters (estaban visibles y dejan de estarlo), persistir los tamaños vivos: en
        // ese instante las columnas/filas aún reflejan el redimensionado del usuario, antes de que la
        // animación de cierre del gap empiece. Es el punto de captura fiable, ya que el GridSplitter del
        // toolkit gestiona el puntero internamente y no propaga PointerCaptureLost.
        // La acción "Cancelar"/"Default" de la barra flotante suprime esta captura (ya ha restaurado los
        // tamaños que toca y no quiere guardar el redimensionado descartado).
        if (e.OldValue is true && e.NewValue is false)
        {
            if (!widgetPanel._suppressNextSizeCapture)
                widgetPanel.CaptureLayoutSizes(widgetPanel.Layout);

            widgetPanel._suppressNextSizeCapture = false;
        }

        widgetPanel.UpdateSplittersVisibility();
        widgetPanel.UpdateSplittersToolbarVisibility();

        if (e.NewValue is true)
            widgetPanel.HighlightSplitters();
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Se ejecuta cuando el control entra en el árbol visual.
    /// Aplica el layout inicial, muestra los widgets visibles y ejecuta la animación de entrada.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeToThemeChanges();

        if (Layout != null && _widgets.Count > 0)
            await ApplyGridLayout(Layout, animateLayoutTransition: false);

        foreach (var widget in _widgets)
        {
            if (widget.ViewModel.SlotIndex != -1)
                widget.Control.Visibility = Visibility.Visible;
        }

        // Estado inicial de la banda según la propiedad (sin animación); a partir de aquí, los cambios animan.
        ApplyTopBandStateImmediate(TopBandVisible);
        _bandReady = true;

        await AnimationService.CreateOpacityAnimation(WidgetGrid, 0, 1, 500).StartAsync();
    }

    /// <summary>
    /// Se ejecuta cuando el control sale del árbol visual.
    /// Libera suscripciones externas dependientes del ciclo de vida visual.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeFromThemeChanges();
    }

    #region Top band visibility
    /// <summary>
    /// Mientras la banda está desplegada y no hay animación en curso, recuerda su alto natural, que es el destino de
    /// la animación de expansión/colapso. Se ignora durante la animación (alturas intermedias) y cuando está oculta.
    /// </summary>
    private void OnTopBandHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_bandAnimating && TopBandVisible && e.NewSize.Height > 0)
            _bandNaturalHeight = e.NewSize.Height;
    }

    /// <summary>
    /// Aplica el estado de la banda sin animación (arranque): fila a Auto/0 y contenido visible/colapsado.
    /// </summary>
    private void ApplyTopBandStateImmediate(bool visible)
    {
        _bandAnimation?.Cancel();
        _bandAnimation = null;
        _bandAnimating = false;

        TopBandRow.Height = visible ? GridLength.Auto : new GridLength(0);
        TopBandHost.Opacity = visible ? 1 : 0;
        TopBandHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Anima mostrar/ocultar la banda de forma secuencial para que no se vea aplastamiento del contenido:
    /// - Ocultar: fade out del contenido (a tamaño completo) y, al terminar, colapso de su fila de su alto a 0; el
    ///   grid de widgets crece hacia arriba ocupando el hueco.
    /// - Mostrar: expansión de la fila de 0 a su alto natural (el grid de widgets decrece para hacer hueco) y, al
    ///   terminar, fade in del contenido.
    /// El colapso/expansión ocurren con el contenido invisible, así que el recorte de la fila no se percibe.
    /// </summary>
    private void AnimateTopBand(bool visible)
    {
        _bandAnimation?.Cancel();
        _bandAnimation = null;

        if (!visible)
        {
            double from = TopBandHost.ActualHeight > 0 ? TopBandHost.ActualHeight : _bandNaturalHeight;
            if (from > 0)
                _bandNaturalHeight = from;

            _bandAnimating = true;

            var fade = AnimationService.CreateOpacityAnimation(TopBandHost, TopBandHost.Opacity, 0, TopBandFadeMs);
            fade.Completed += () =>
            {
                double collapseFrom = _bandNaturalHeight > 0 ? _bandNaturalHeight : TopBandHost.ActualHeight;
                TopBandRow.Height = new GridLength(collapseFrom);

                var collapse = AnimationService.CreateDoubleAnimation(v => TopBandRow.Height = new GridLength(v), collapseFrom, 0, TopBandResizeMs);
                collapse.Completed += () =>
                {
                    TopBandHost.Visibility = Visibility.Collapsed;
                    _bandAnimating = false;
                    _bandAnimation = null;
                };
                _bandAnimation = collapse;
                collapse.Start();
            };
            _bandAnimation = fade;
            fade.Start();
        }
        else
        {
            double target = _bandNaturalHeight > 0 ? _bandNaturalHeight : MeasureTopBandHeight();
            if (target <= 0)
                target = TopBandFallbackHeight;

            _bandAnimating = true;

            TopBandHost.Visibility = Visibility.Visible;
            TopBandHost.Opacity = 0;
            TopBandRow.Height = new GridLength(0);

            var expand = AnimationService.CreateDoubleAnimation(v => TopBandRow.Height = new GridLength(v), 0, target, TopBandResizeMs);
            expand.Completed += () =>
            {
                // Tras expandir, deja la fila en Auto para que se readapte si el contenido cambia de alto.
                TopBandRow.Height = GridLength.Auto;

                var fade = AnimationService.CreateOpacityAnimation(TopBandHost, 0, 1, TopBandFadeMs);
                fade.Completed += () =>
                {
                    _bandAnimating = false;
                    _bandAnimation = null;
                };
                _bandAnimation = fade;
                fade.Start();
            };
            _bandAnimation = expand;
            expand.Start();
        }
    }

    /// <summary>
    /// Mide el alto natural de la banda cuando aún no se conoce (p. ej. arrancó oculta), forzando un Measure del
    /// contenido con el ancho disponible del panel.
    /// </summary>
    private double MeasureTopBandHeight()
    {
        TopBandHost.Visibility = Visibility.Visible;

        double width = WidgetGrid.ActualWidth > 0 ? WidgetGrid.ActualWidth : ActualWidth;
        TopBandHost.Measure(new Windows.Foundation.Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));

        return TopBandHost.DesiredSize.Height;
    }
    #endregion

    /// <summary>
    /// Se ejecuta cuando cambia el tema activo.
    /// Actualiza los brushes usados por los hitboxes durante el drag & drop.
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _slotBaseBrush.Color = _themeService.AccentLightColor;
        _slotHoverBrush.Color = _themeService.AccentColor;
    }

    /// <summary>
    /// Se ejecuta cuando cambia una propiedad de un ViewModel de widget.
    /// Si cambia <see cref="WidgetViewModelBase.SlotIndex"/>, programa una reconstrucción del layout.
    /// </summary>
    private void OnWidgetViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WidgetViewModelBase.SlotIndex))
            return;

        if (_suppressSlotIndexLayoutRefresh)
            return;

        QueueLayoutRefreshFromSlotIndexChange();
    }
    #endregion

    #region Drag & Drop - hit boxes
    /// <summary>
    /// Se ejecuta cuando el puntero entra en un hitbox durante el drag.
    /// Actualiza el slot en hover y refresca la visualización de los hitboxes.
    /// </summary>
    private void OnHitBoxPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragState.IsDragging || _dragState.DraggedEntry == null)
            return;

        int slotIndex = (int)((Border)sender).Tag;

        _dragState.CurrentHoverSlot = slotIndex;

        UpdateHitBoxVisuals(_dragState.OriginSlot, slotIndex);
    }

    /// <summary>
    /// Se ejecuta cuando el puntero se mueve dentro de un hitbox.
    /// Evita repintados si el hover sigue estando sobre el mismo slot.
    /// </summary>
    private void OnHitBoxPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragState.IsDragging || _dragState.DraggedEntry == null)
            return;

        int slotIndex = (int)((Border)sender).Tag;
        if (_dragState.CurrentHoverSlot == slotIndex)
            return;

        _dragState.CurrentHoverSlot = slotIndex;

        UpdateHitBoxVisuals(_dragState.OriginSlot, slotIndex);
    }

    /// <summary>
    /// Se ejecuta cuando el puntero sale de un hitbox.
    /// Limpia el slot en hover y restaura los estilos visuales.
    /// </summary>
    private void OnHitBoxPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragState.IsDragging || _dragState.DraggedEntry == null)
            return;

        UpdateHitBoxVisuals(_dragState.OriginSlot, -1);

        _dragState.CurrentHoverSlot = -1;
    }
    #endregion

    #region Drag & Drop - widgets
    /// <summary>
    /// Inicia el drag de un widget visible dentro del panel.
    /// Guarda el slot de origen, crea el ghost visual, desactiva temporalmente
    /// el hit testing de widgets y trae los hitboxes al frente.
    /// </summary>
    private async void OnWidgetDragStart(object sender, PointerRoutedEventArgs e)
    {
        var control = (WidgetBaseControl)sender;

        _dragState.DraggedEntry = _widgets.FirstOrDefault(w => w.Control == control);

        if (_dragState.DraggedEntry == null)
            return;

        _dragState.IsDragging = true;
        _dragState.OriginSlot = _dragState.DraggedEntry.ViewModel.SlotIndex;
        _dragState.CurrentHoverSlot = -1;

        foreach (var widget in _widgets)
            widget.Control.IsHitTestVisible = false;

        UpdateHitBoxVisuals(_dragState.OriginSlot,-1);
        BringHitBoxesToFront();
        CollapseSplitters();

        var pointerPosition = e.GetCurrentPoint(WidgetGrid).Position;
        var widgetPosition = _dragState.DraggedEntry.Control.TransformToVisual(WidgetGrid).TransformPoint(new Point(0, 0));

        _dragState.Offset = new Point(pointerPosition.X - widgetPosition.X, pointerPosition.Y - widgetPosition.Y);

        var renderTargetBitmap = new RenderTargetBitmap();
        _dragState.Ghost = new Image
        {
            Source = renderTargetBitmap,
            Width = _dragState.DraggedEntry.Control.ActualWidth,
            Height = _dragState.DraggedEntry.Control.ActualHeight,
            Opacity = 0.85,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(_dragState.Ghost, pointerPosition.X - _dragState.Offset.X);

        Canvas.SetTop(_dragState.Ghost, pointerPosition.Y - _dragState.Offset.Y);

        WidgetGridDragLayer.Children.Add(_dragState.Ghost);

        await renderTargetBitmap.RenderAsync(_dragState.DraggedEntry.Control);

        // El await de arriba cede el hilo de UI: si el usuario suelta el puntero mientras se renderiza,
        // OnWidgetDragEnd (async void) resetea _dragState antes de que volvamos aquí. Sin esta guarda,
        // DraggedEntry sería null → NRE no controlado (cierre silencioso); y si el drag ya terminó no debemos
        // colapsar el widget (quedaría invisible tras haberle restaurado la visibilidad el drag-end).
        if (_dragState.IsDragging && _dragState.DraggedEntry != null)
            _dragState.DraggedEntry.Control.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Mueve el ghost durante el drag y actualiza el slot actualmente bajo el puntero.
    /// </summary>
    private void OnWidgetDragMove(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragState.IsDragging)
            return;

        var pointerPosition = e.GetCurrentPoint(WidgetGrid).Position;

        if (_dragState.Ghost != null)
        {
            Canvas.SetLeft(_dragState.Ghost, pointerPosition.X - _dragState.Offset.X);

            Canvas.SetTop(_dragState.Ghost, pointerPosition.Y - _dragState.Offset.Y);
        }

        int slotUnderPointer = GetSlotUnderPointer(pointerPosition);

        if (slotUnderPointer == _dragState.CurrentHoverSlot)
            return;

        _dragState.CurrentHoverSlot = slotUnderPointer;

        UpdateHitBoxVisuals(_dragState.OriginSlot, slotUnderPointer);

        if (slotUnderPointer >= 0 && slotUnderPointer != _dragState.OriginSlot)
        {
            var hitBox = _slotHitBoxes[slotUnderPointer];
            hitBox.Background = _slotHoverBrush;
            hitBox.Opacity = 0.5;
        }
    }

    /// <summary>
    /// Finaliza el drag de un widget.
    /// Si el destino es válido, actualiza los SlotIndex mediante una foto del estado actual
    /// y reconstruye el layout completo desde la fuente de verdad lógica.
    /// </summary>
    private async void OnWidgetDragEnd(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragState.IsDragging || _dragState.DraggedEntry == null || Layout?.Slots == null)
            return;

        var layout = Layout;
        var dragged = _dragState.DraggedEntry;
        int originSlot = _dragState.OriginSlot;
        var releasePosition = e.GetCurrentPoint(WidgetGrid).Position;
        int targetSlot = GetSlotUnderPointer(releasePosition);

        _dragState.IsDragging = false;

        ClearDragVisualState(dragged);

        if (!IsValidSlot(originSlot) || !IsValidSlot(targetSlot) || targetSlot == originSlot)
        {
            await ApplyGridLayout(layout, animateLayoutTransition: false, preserveCurrentSizes: true);
            _dragState.Reset();
            return;
        }

        var slotSnapshot = _widgets.ToDictionary(widget => widget, widget => widget.ViewModel.SlotIndex);
        var targetWidget = slotSnapshot.Where(pair => !ReferenceEquals(pair.Key, dragged)).FirstOrDefault(pair => pair.Value == targetSlot).Key;
        slotSnapshot[dragged] = targetSlot;

        if (targetWidget != null)
            slotSnapshot[targetWidget] = originSlot;

        _suppressSlotIndexLayoutRefresh = true;

        try
        {
            foreach (var pair in slotSnapshot)
                pair.Key.ViewModel.SlotIndex = pair.Value;
        }
        finally
        {
            _suppressSlotIndexLayoutRefresh = false;
        }

        await ApplyGridLayout(layout, animateLayoutTransition: false, preserveCurrentSizes: true);

        _dragState.Reset();
    }
    #endregion

    #region Methods - layout
    /// <summary>
    /// Aplica el layout completo al panel.
    /// Ajusta columnas, posiciona widgets según SlotIndex, actualiza visibilidad
    /// y posiciona los hitboxes usados durante drag & drop.
    /// </summary>
    private async Task ApplyGridLayout(LayoutInfo layout, bool animateLayoutTransition = true, bool preserveCurrentSizes = false)
    {
        if (layout is null)
            return;

        if (animateLayoutTransition)
        {
            await AnimationService.CreateOpacityAnimation(WidgetGrid, 1, 0, 120).StartAsync();
        }

        // En refrescos del mismo layout (animación del gap, mover widgets, mostrar/ocultar widgets) solo
        // cambian márgenes y posiciones: se conservan los anchos de columna y alturas de fila que el usuario
        // haya ajustado con los splitters. El reseteo de columnas/filas y la aplicación de los tamaños
        // persistidos solo se hace al cargar o al cambiar de tipo de layout, evitando que la transición del
        // gap pierda los tamaños definidos por el usuario.
        if (!preserveCurrentSizes)
        {
            ApplyColumnWidths(layout);
            ApplyPersistedColumnWidths(layout);
            ResetRatiosToDefault(layout);
            ApplyPersistedRatios(layout);
        }

        // Las filas se reconstruyen SIEMPRE desde los ratios vivos (en refrescos de mismo layout los ratios
        // no cambian, así que el resultado es idéntico; en un arrastre de handle de fila sí cambian).
        RebuildRows(layout);

        foreach (var entry in _widgets)
        {
            entry.IsPositioned = false;
            entry.Control.Visibility = entry.ViewModel.SlotIndex == -1 ? Visibility.Collapsed : Visibility.Visible;
        }

        if (layout.Slots != null)
        {
            for (int slotIndex = 0; slotIndex < layout.Slots.Count; slotIndex++)
            {
                WidgetEntry? found = _widgets.FirstOrDefault(entry => entry.ViewModel.SlotIndex == slotIndex);

                if (found == null)
                    continue;

                MoveWidgetToSlot(found, slotIndex);

                found.IsPositioned = true;
            }
        }

        foreach (var entry in _widgets)
        {
            entry.Control.Visibility = entry.ViewModel.SlotIndex == -1 || !entry.IsPositioned ? Visibility.Collapsed : Visibility.Visible;
        }

        PositionHitBoxes(layout);
        RefreshSplitters(layout);

        if (animateLayoutTransition)
        {
            await AnimationService.CreateOpacityAnimation(WidgetGrid, 0, 1, 120).StartAsync();
        }
    }

    /// <summary>
    /// Aplica al grid los anchos de columna definidos por el layout.
    /// </summary>
    private void ApplyColumnWidths(LayoutInfo layout)
    {
        if (layout.ColumnWidths == null)
            return;

        int limit = Math.Min(layout.ColumnWidths.Count, WidgetGrid.ColumnDefinitions.Count);

        for (int i = 0; i < limit; i++)
        {
            var raw = layout.ColumnWidths[i];
            bool isUnused = string.IsNullOrWhiteSpace(raw) || raw.Trim() == "0";

            // Las columnas no usadas por el layout deben quedar a 0px reales. Sin esto, el MinWidth="150" fijo
            // de las ColumnDefinitions del XAML las mantiene a 150px y deja un hueco vacío a la derecha (una
            // columna en layouts de 2, dos en el de 1), tanto más visible cuanto menor es la resolución. Las
            // columnas usadas conservan el mínimo que evita el layout patológico al estrechar o arrastrar.
            WidgetGrid.ColumnDefinitions[i].MinWidth = isUnused ? 0 : MinColumnWidth;
            WidgetGrid.ColumnDefinitions[i].Width = ParseGridLength(raw);
        }
    }

    /// <summary>
    /// Posiciona un widget en el slot indicado aplicando fila, columna,
    /// RowSpan, ColumnSpan y márgenes.
    /// </summary>
    private void MoveWidgetToSlot(WidgetEntry entry, int slotIndex)
    {
        if (Layout?.Slots is null)
            return;

        if (slotIndex < 0 || slotIndex >= Layout.Slots.Count)
            return;

        var slot = Layout.Slots[slotIndex];
        var margin = Layout.Margins != null && Layout.Margins.Count > slotIndex ? Layout.Margins[slotIndex] : new SlotMargin(0, 0, 0, 0);
        var control = entry.Control;
        control.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom);

        ApplySlotGridPlacement(control, slot);
    }

    #region Methods - filas independientes por columna
    /// <summary>
    /// Coloca un elemento (widget o hitbox) en el rectángulo de su slot sobre la rejilla de filas de grano
    /// fino: columna + ColSpan directos; y para la parte de fila, si pertenece a una columna partida abarca
    /// las filas de su mitad (según el ratio de esa columna) con <c>RowSpan</c>; si no, abarca todas las
    /// filas. Nunca reparenta: el elemento sigue siendo hijo directo de <see cref="WidgetGrid"/>.
    /// </summary>
    private void ApplySlotGridPlacement(FrameworkElement element, SlotInfo slot)
    {
        int rowCount = Math.Max(1, WidgetGrid.RowDefinitions.Count);

        Grid.SetColumn(element, slot.Column);
        Grid.SetColumnSpan(element, Math.Max(1, slot.ColSpan));

        if (Math.Max(1, slot.ColSpan) == 1 && IsSplitColumn(Layout, slot.Column))
        {
            int boundary = BoundaryIndexForColumn(slot.Column);

            if (slot.Row == 0)
            {
                Grid.SetRow(element, 0);
                Grid.SetRowSpan(element, Math.Max(1, boundary));
            }
            else
            {
                Grid.SetRow(element, boundary);
                Grid.SetRowSpan(element, Math.Max(1, rowCount - boundary));
            }
        }
        else
        {
            Grid.SetRow(element, 0);
            Grid.SetRowSpan(element, rowCount);
        }
    }

    /// <summary>Columnas que el layout parte en dos filas (tienen un slot Row==1 de una sola columna), ordenadas.</summary>
    private static List<int> GetSplitColumns(LayoutInfo layout)
    {
        var columns = new List<int>();

        if (layout?.Slots == null)
            return columns;

        foreach (var slot in layout.Slots)
            if (slot.Row == 1 && Math.Max(1, slot.ColSpan) == 1 && !columns.Contains(slot.Column))
                columns.Add(slot.Column);

        columns.Sort();
        return columns;
    }

    private bool IsSplitColumn(LayoutInfo? layout, int column) => layout != null && GetSplitColumns(layout).Contains(column);

    /// <summary>Ratio de partición vivo de una columna (fracción de la fila superior); 0.5 por defecto.</summary>
    private double GetColumnRatio(int column) => _columnRatios.TryGetValue(column, out var r) ? r : 0.5;

    /// <summary>Índice de la frontera (= fila donde empieza la mitad inferior) de una columna partida.</summary>
    private int BoundaryIndexForColumn(int column)
    {
        double ratio = GetColumnRatio(column);

        for (int i = 0; i < _rowBoundaries.Count; i++)
            if (Math.Abs(_rowBoundaries[i] - ratio) < 1e-6)
                return i;

        return 1;
    }

    /// <summary>
    /// Reconstruye las <c>RowDefinitions</c> del grid a partir de las fronteras (0, los ratios de cada
    /// columna partida, y 1). Cada columna parte por SU frontera; las columnas que cruzan otra frontera la
    /// abarcan con RowSpan, así su tamaño total no cambia al mover el split de otra columna.
    /// </summary>
    private void RebuildRows(LayoutInfo layout)
    {
        var boundaries = new SortedSet<double> { 0.0, 1.0 };

        foreach (int column in GetSplitColumns(layout))
            boundaries.Add(GetColumnRatio(column));

        _rowBoundaries = boundaries.ToList();

        WidgetGrid.RowDefinitions.Clear();

        for (int i = 0; i < _rowBoundaries.Count - 1; i++)
        {
            double height = _rowBoundaries[i + 1] - _rowBoundaries[i];
            WidgetGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height, GridUnitType.Star) });
        }
    }

    /// <summary>Fija el ratio de cada columna partida del layout a su valor por defecto (0.5).</summary>
    private void ResetRatiosToDefault(LayoutInfo layout)
    {
        _columnRatios.Clear();

        foreach (int column in GetSplitColumns(layout))
            _columnRatios[column] = 0.5;
    }

    /// <summary>Aplica los ratios persistidos del layout (si los hay) sobre las columnas partidas.</summary>
    private void ApplyPersistedRatios(LayoutInfo layout)
    {
        if (layout is null || !_appSettings.LayoutSelectorControl.LayoutSizes.TryGetValue(layout.Index, out var sizes) || sizes?.RowRatiosByColumn is null)
            return;

        foreach (int column in GetSplitColumns(layout))
            if (sizes.RowRatiosByColumn.TryGetValue(column, out var ratio) && ratio > 0.01 && ratio < 0.99)
                _columnRatios[column] = ratio;
    }
    #endregion

    /// <summary>
    /// Convierte una cadena de texto en un <see cref="GridLength"/>.
    /// Soporta valores tipo "*", "2*", píxeles y fallback a Auto.
    /// </summary>
    private GridLength ParseGridLength(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GridLength(1, GridUnitType.Star);

        text = text.Trim();

        if (text.EndsWith("*"))
        {
            var prefix = text[..^1];

            if (string.IsNullOrWhiteSpace(prefix))
                return new GridLength(1, GridUnitType.Star);

            if (double.TryParse(prefix, out var factor))
                return new GridLength(factor, GridUnitType.Star);

            return new GridLength(1, GridUnitType.Star);
        }

        if (double.TryParse(text, out var pixels))
            return new GridLength(pixels, GridUnitType.Pixel);

        return new GridLength(1, GridUnitType.Auto);
    }

    /// <summary>
    /// Programa una reconstrucción del layout provocada por cambios externos de SlotIndex.
    /// Agrupa múltiples cambios consecutivos en una única actualización visual.
    /// </summary>
    private void QueueLayoutRefreshFromSlotIndexChange()
    {
        if (_slotIndexLayoutRefreshQueued)
            return;

        _slotIndexLayoutRefreshQueued = true;

        DispatcherQueue.TryEnqueue(async () =>
        {
            _slotIndexLayoutRefreshQueued = false;

            if (Layout is null || !IsLoaded)
                return;

            if (_dragState.IsDragging)
                return;

            await ApplyGridLayout(Layout, animateLayoutTransition: false, preserveCurrentSizes: true);
        });
    }
    #endregion

    #region Methods - splitters
    /// <summary>
    /// Grosor (px) de la barra interactiva del splitter; debe coincidir con el de los estilos
    /// GridSplitterVerticalStyle/GridSplitterHorizontalStyle. Se usa para centrar la barra en la línea
    /// de separación mediante un margen negativo.
    /// </summary>
    private const double SplitterThickness = 12;

    /// <summary>
    /// Anchura mínima (px) de una columna del panel. La hace cumplir tanto el MinWidth de las ColumnDefinitions
    /// (al estrechar el panel) como <see cref="MinWidthColumnSplitter"/> (al arrastrar el splitter). Evita que
    /// una columna se reduzca tanto que el contenido entre en un layout patológico y congele la app.
    /// </summary>
    private const double MinColumnWidth = 150;

    /// <summary>
    /// Alto (px) del área interactiva del handle de fila propio. Algo mayor que <see cref="SplitterThickness"/>
    /// (2px por arriba y 2 por abajo) para que la pista sea más fácil de agarrar; el grip visible no cambia.
    /// </summary>
    private const double RowHandleThickness = 16;

    /// <summary>
    /// Recrea y posiciona los splitters según las filas y columnas que realmente usa el layout actual:
    /// un splitter vertical por cada frontera entre columnas usadas y uno horizontal si el layout divide
    /// el panel en dos filas. Su visibilidad depende de <see cref="SplittersEnabled"/>.
    /// </summary>
    private void RefreshSplitters(LayoutInfo layout)
    {
        foreach (var splitter in _columnSplitters)
            WidgetGrid.Children.Remove(splitter);
        _columnSplitters.Clear();

        foreach (var handle in _rowHandles)
            WidgetGrid.Children.Remove(handle);
        _rowHandles.Clear();

        if (layout is null)
            return;

        int rowCount = Math.Max(1, WidgetGrid.RowDefinitions.Count);

        // Splitters verticales (GridSplitter sobre las columnas, que SÍ son compartidas): uno por frontera
        // entre columnas usadas, abarcando todas las filas.
        for (int boundary = 1; boundary < layout.UsedColumnCount; boundary++)
        {
            var splitter = CreateColumnSplitter(boundary, rowCount);
            _columnSplitters.Add(splitter);
            WidgetGrid.Children.Add(splitter);
        }

        // Handles de fila PROPIOS: uno por columna partida, en su frontera. Cada uno ajusta solo su ratio,
        // independiente del resto (a diferencia de un GridSplitter sobre filas compartidas).
        foreach (int column in GetSplitColumns(layout))
        {
            var handle = CreateRowHandle(column);
            _rowHandles.Add(handle);
            WidgetGrid.Children.Add(handle);
        }

        UpdateSplittersVisibility();
    }

    /// <summary>
    /// Crea un splitter vertical en la frontera izquierda de la columna indicada, que redimensiona la
    /// columna previa y la actual. Abarca todas las filas de grano fino.
    /// </summary>
    private GridSplitter CreateColumnSplitter(int columnIndex, int rowSpan)
    {
        var splitter = new MinWidthColumnSplitter
        {
            IsTabStop = false,
            Margin = new Thickness(-SplitterThickness / 2, 0, 0, 0),
            MinColumnWidth = MinColumnWidth
        };

        // Estilo con TargetType=MinWidthColumnSplitter (definido en GenericControls.xaml, hereda del vertical):
        // su mera referencia en XAML registra el tipo para WinUI; sin eso el control creado aquí se trataría como
        // su base Control y la asignación del Style fallaría.
        if (Application.Current.Resources.TryGetValue("MinWidthColumnSplitterStyle", out var style) && style is Style splitterStyle)
            splitter.Style = splitterStyle;

        Grid.SetRow(splitter, 0);
        Grid.SetRowSpan(splitter, Math.Max(1, rowSpan));
        Grid.SetColumn(splitter, columnIndex);

        return splitter;
    }

    /// <summary>
    /// Crea el handle de fila de una columna partida: un Border arrastrable en la frontera de esa columna.
    /// Al arrastrarlo ajusta SOLO el ratio de su columna (Tag) y rehace las filas en vivo.
    /// </summary>
    private Grid CreateRowHandle(int column)
    {
        // Replica del template del GridSplitter horizontal: pista transparente de 12px con un grip central
        // (56×3) y un resplandor (HighlightGlow) para el efecto breathe. Así es visualmente idéntico a los
        // splitters de columna, pero con drag propio para ajustar solo el ratio de esta columna.
        var glow = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(2),
            Opacity = 0,
            IsHitTestVisible = false,
            Background = new SolidColorBrush(GetThemeColor("AccentColor"))
        };

        var grip = new Border
        {
            Width = 56,
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(2),
            Background = GetThemeBrush("AccentDarkBrushOpacity80")
        };

        var handle = new RowResizeHandle
        {
            Tag = column,
            Height = RowHandleThickness,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, -RowHandleThickness / 2, 0, 0),
            Background = _slotTransparentBrush
        };
        handle.Children.Add(glow);
        handle.Children.Add(grip);

        handle.PointerPressed += OnRowHandlePointerPressed;
        handle.PointerMoved += OnRowHandlePointerMoved;
        handle.PointerReleased += OnRowHandlePointerReleased;
        handle.PointerCanceled += OnRowHandlePointerReleased;
        handle.PointerCaptureLost += OnRowHandlePointerReleased;
        handle.PointerEntered += OnRowHandlePointerEntered;
        handle.PointerExited += OnRowHandlePointerExited;

        Grid.SetColumn(handle, column);
        Grid.SetRow(handle, BoundaryIndexForColumn(column));

        return handle;
    }

    private static Border? RowHandleGlow(Grid handle) => handle.Children.ElementAtOrDefault(0) as Border;
    private static Border? RowHandleGrip(Grid handle) => handle.Children.ElementAtOrDefault(1) as Border;
    private static Brush? GetThemeBrush(string key) => Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : null;
    private static Windows.UI.Color GetThemeColor(string key) => Application.Current.Resources.TryGetValue(key, out var v) && v is Windows.UI.Color c ? c : Colors.Transparent;

    private void OnRowHandlePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingRowColumn < 0)
            SetRowHandleVisualState((Grid)sender, "AccentBrushOpacity20", "AccentLightBrush");
    }

    private void OnRowHandlePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingRowColumn < 0)
            SetRowHandleVisualState((Grid)sender, null, "AccentDarkBrushOpacity80");
    }

    /// <summary>Aplica al handle de fila el aspecto de un estado (pista + grip), replicando los estados
    /// Normal/PointerOver/Pressed del GridSplitter. <paramref name="trackBrushKey"/> nulo = pista transparente.</summary>
    private void SetRowHandleVisualState(Grid handle, string? trackBrushKey, string gripBrushKey)
    {
        handle.Background = trackBrushKey is null ? _slotTransparentBrush : (GetThemeBrush(trackBrushKey) ?? _slotTransparentBrush);

        if (RowHandleGrip(handle) is Border grip)
            grip.Background = GetThemeBrush(gripBrushKey);
    }

    /// <summary>Lanza el efecto "breathe" (resplandor que pulsa entre acentos, 2,4 s) en el handle de fila,
    /// idéntico al del estado "Highlight" de los GridSplitter de columna.</summary>
    private void PlayRowHandleBreathe(Grid handle)
    {
        if (RowHandleGlow(handle) is not Border glow)
            return;

        var storyboard = new Storyboard();

        var opacity = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(opacity, glow);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0 });
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0.2), Value = 0.85 });
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(2.0), Value = 0.85 });
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(2.4), Value = 0 });
        storyboard.Children.Add(opacity);

        var color = new ColorAnimationUsingKeyFrames { EnableDependentAnimation = true };
        Storyboard.SetTarget(color, glow);
        Storyboard.SetTargetProperty(color, "(Border.Background).(SolidColorBrush.Color)");
        color.KeyFrames.Add(new LinearColorKeyFrame { KeyTime = TimeSpan.Zero, Value = GetThemeColor("AccentColor") });
        color.KeyFrames.Add(new LinearColorKeyFrame { KeyTime = TimeSpan.FromSeconds(0.6), Value = GetThemeColor("AccentLightColor") });
        color.KeyFrames.Add(new LinearColorKeyFrame { KeyTime = TimeSpan.FromSeconds(1.2), Value = GetThemeColor("AccentDarkColor") });
        color.KeyFrames.Add(new LinearColorKeyFrame { KeyTime = TimeSpan.FromSeconds(1.8), Value = GetThemeColor("AccentLightColor") });
        color.KeyFrames.Add(new LinearColorKeyFrame { KeyTime = TimeSpan.FromSeconds(2.4), Value = GetThemeColor("AccentColor") });
        storyboard.Children.Add(color);

        storyboard.Begin();
    }

    private void OnRowHandlePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var handle = (Grid)sender;
        _draggingRowColumn = (int)handle.Tag;
        handle.CapturePointer(e.Pointer);

        SetRowHandleVisualState(handle, "AccentBrushOpacity40", "AccentBrush");

        e.Handled = true;
    }

    private void OnRowHandlePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingRowColumn < 0)
            return;

        double height = WidgetGrid.ActualHeight;
        if (height <= 0)
            return;

        double y = e.GetCurrentPoint(WidgetGrid).Position.Y;
        double ratio = Math.Clamp(y / height, 0.12, 0.88);

        if (Math.Abs(ratio - GetColumnRatio(_draggingRowColumn)) < 0.002)
            return;

        _columnRatios[_draggingRowColumn] = ratio;
        RelayoutRowsLive();
        e.Handled = true;
    }

    private void OnRowHandlePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingRowColumn < 0)
            return;

        var handle = (Grid)sender;
        handle.ReleasePointerCapture(e.Pointer);
        _draggingRowColumn = -1;

        // Tras soltar, el puntero suele seguir encima → estado hover (como el GridSplitter); al salir,
        // PointerExited lo devolverá a Normal.
        SetRowHandleVisualState(handle, "AccentBrushOpacity20", "AccentLightBrush");

        // Persistir los ratios nuevos del layout actual.
        CaptureLayoutSizes(Layout);
    }

    /// <summary>
    /// Reconstruye filas + recoloca widgets/hitboxes/handles en vivo durante el arrastre de un handle de
    /// fila, SIN recrear los handles (eso perdería la captura del puntero) ni animar.
    /// </summary>
    private void RelayoutRowsLive()
    {
        if (Layout?.Slots is null)
            return;

        RebuildRows(Layout);

        foreach (var entry in _widgets)
            if (entry.ViewModel.SlotIndex >= 0 && entry.ViewModel.SlotIndex < Layout.Slots.Count)
                ApplySlotGridPlacement(entry.Control, Layout.Slots[entry.ViewModel.SlotIndex]);

        for (int i = 0; i < _slotHitBoxes.Count && i < Layout.Slots.Count; i++)
            ApplySlotGridPlacement(_slotHitBoxes[i], Layout.Slots[i]);

        int rowCount = Math.Max(1, WidgetGrid.RowDefinitions.Count);
        foreach (var splitter in _columnSplitters)
            Grid.SetRowSpan(splitter, rowCount);

        foreach (var handle in _rowHandles)
            Grid.SetRow(handle, BoundaryIndexForColumn((int)handle.Tag));
    }

    /// <summary>
    /// Captura el estado de tamaños del layout: anchos de columna (factores estrella, compartidos) y el
    /// ratio de partición de cada columna partida. Se guarda en la configuración por índice de layout.
    /// </summary>
    private void CaptureLayoutSizes(LayoutInfo? layout)
    {
        if (layout is null || !IsLoaded || WidgetGrid.ColumnDefinitions.Count == 0)
            return;

        var sizes = new AppSettings.LayoutSizes
        {
            Columns = new double[WidgetGrid.ColumnDefinitions.Count],
            RowRatiosByColumn = new Dictionary<int, double>()
        };

        for (int i = 0; i < WidgetGrid.ColumnDefinitions.Count; i++)
            sizes.Columns[i] = WidgetGrid.ColumnDefinitions[i].ActualWidth;

        foreach (int column in GetSplitColumns(layout))
            sizes.RowRatiosByColumn[column] = GetColumnRatio(column);

        _appSettings.LayoutSelectorControl.LayoutSizes[layout.Index] = sizes;
    }

    /// <summary>
    /// Aplica los anchos de columna persistidos (si existen) del layout. Los ratios de fila se aplican
    /// aparte en <see cref="ApplyPersistedRatios"/>. Las columnas no usadas ("0") se respetan.
    /// </summary>
    private void ApplyPersistedColumnWidths(LayoutInfo layout)
    {
        if (layout is null || !_appSettings.LayoutSelectorControl.LayoutSizes.TryGetValue(layout.Index, out var sizes) || sizes?.Columns is null)
            return;

        int limit = Math.Min(sizes.Columns.Length, WidgetGrid.ColumnDefinitions.Count);
        for (int i = 0; i < limit; i++)
        {
            bool columnUsed = i < layout.ColumnWidths.Count && layout.ColumnWidths[i].Trim() != "0";
            if (columnUsed && sizes.Columns[i] > 0)
                WidgetGrid.ColumnDefinitions[i].Width = new GridLength(sizes.Columns[i], GridUnitType.Star);
        }
    }

    /// <summary>
    /// Muestra u oculta los splitters según <see cref="SplittersVisible"/>.
    /// </summary>
    private void UpdateSplittersVisibility()
    {
        var visibility = SplittersVisible ? Visibility.Visible : Visibility.Collapsed;

        foreach (var splitter in _columnSplitters)
            splitter.Visibility = visibility;

        foreach (var handle in _rowHandles)
            handle.Visibility = visibility;

        // Señalar a cada widget que el panel entra/sale de modo edición, para que solapen su capa de
        // redimensionado (acento oscuro) y bloqueen la interacción con su contenido mientras dure.
        foreach (var entry in _widgets)
            entry.Control.PanelInEditMode = SplittersVisible;
    }

    /// <summary>
    /// Lanza el efecto de atención (estado visual "Highlight", un "breathe" entre los colores de acento)
    /// en todos los splitters visibles, para que el usuario advierta que hay nuevos elementos con los que
    /// interactuar. Se reinicia el estado a "Idle" antes para forzar que la animación se reproduzca de nuevo.
    /// </summary>
    private void HighlightSplitters()
    {
        foreach (var splitter in _columnSplitters)
            PlaySplitterHighlight(splitter);

        foreach (var handle in _rowHandles)
            PlayRowHandleBreathe(handle);
    }

    private static void PlaySplitterHighlight(Control splitter)
    {
        // Los splitters se recrean al reconstruir el layout (p. ej. en cada frame de la animación del gap),
        // así que al lanzar el efecto pueden no tener aún el template aplicado y GoToState no encontraría el
        // estado "Highlight". Forzar la aplicación del template garantiza que los estados visuales existan.
        splitter.ApplyTemplate();

        VisualStateManager.GoToState(splitter, "Idle", false);
        VisualStateManager.GoToState(splitter, "Highlight", true);
    }

    /// <summary>
    /// Oculta los splitters temporalmente (p. ej. durante el drag de widgets). Se restauran al
    /// reconstruir el layout.
    /// </summary>
    private void CollapseSplitters()
    {
        foreach (var splitter in _columnSplitters)
            splitter.Visibility = Visibility.Collapsed;

        foreach (var handle in _rowHandles)
            handle.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Muestra u oculta la barra flotante de acciones de los splitters, en sincronía con
    /// <see cref="SplittersVisible"/>, con un fundido coherente con la aparición de los splitters.
    /// </summary>
    private void UpdateSplittersToolbarVisibility()
    {
        if (SplittersToolbar is null)
            return;

        if (SplittersVisible)
        {
            SplittersToolbar.Visibility = Visibility.Visible;
            AnimationService.CreateOpacityAnimation(SplittersToolbar, SplittersToolbar.Opacity, 1, 200).Start();
            PlaySplittersToolbarBreathe();
        }
        else
        {
            AnimationService.CreateOpacityAnimation(SplittersToolbar, SplittersToolbar.Opacity, 0, 150)
                .StartAsync()
                .ContinueWith(_ => DispatcherQueue.TryEnqueue(() => SplittersToolbar.Visibility = Visibility.Collapsed));
        }
    }

    /// <summary>
    /// Lanza el efecto "breathe" de la barra flotante (resplandor que pulsa entre los colores de acento),
    /// análogo al de los splitters, para que el usuario advierta que la barra acaba de aparecer. Se reinicia
    /// (Stop antes de Begin) para forzar que la animación se reproduzca de nuevo en cada aparición.
    /// </summary>
    private void PlaySplittersToolbarBreathe()
    {
        if (SplittersToolbar.Resources.TryGetValue("SplittersToolbarBreatheStoryboard", out var resource) && resource is Storyboard breathe)
        {
            breathe.Stop();
            breathe.Begin();
        }
    }

    /// <summary>
    /// Acción "Confirmar": cierra los splitters conservando la captura automática de tamaños vivos (el flujo
    /// normal de ocultado persiste los tamaños redimensionados).
    /// </summary>
    private void OnSplittersConfirm(object sender, RoutedEventArgs e)
    {
        if (Layout is null)
            return;

        SplittersEnabled = false;
    }

    /// <summary>
    /// Acción "Cancelar": descarta el redimensionado restaurando los tamaños persistidos del layout (o los
    /// originales si no había ninguno), suprime la captura automática y cierra los splitters.
    /// </summary>
    private async void OnSplittersCancel(object sender, RoutedEventArgs e)
    {
        if (Layout is null)
            return;

        _suppressNextSizeCapture = true;

        await ApplyGridLayout(Layout, animateLayoutTransition: false, preserveCurrentSizes: false);

        SplittersEnabled = false;
    }

    /// <summary>
    /// Acción "Default": elimina las dimensiones custom de este layout, restaura las originales, suprime la
    /// captura automática y cierra los splitters.
    /// </summary>
    private async void OnSplittersDefault(object sender, RoutedEventArgs e)
    {
        if (Layout is null)
            return;

        _suppressNextSizeCapture = true;

        _appSettings.LayoutSelectorControl.LayoutSizes.Remove(Layout.Index);

        await ApplyGridLayout(Layout, animateLayoutTransition: false, preserveCurrentSizes: false);

        SplittersEnabled = false;
    }
    #endregion

    #region Helpers - hit boxes
    /// <summary>
    /// Garantiza que existen suficientes hitboxes para cubrir todos los slots
    /// del layout actual.
    /// </summary>
    private void EnsureHitBoxes(int requiredCount)
    {
        while (_slotHitBoxes.Count < requiredCount)
        {
            int index = _slotHitBoxes.Count;
            var hitBox = CreateHitBox(index);
            _slotHitBoxes.Add(hitBox);
            WidgetGrid.Children.Insert(0, hitBox);
        }
    }

    /// <summary>
    /// Crea un hitbox asociado a un índice de slot.
    /// </summary>
    private Border CreateHitBox(int slotIndex)
    {
        var hitBox = new Border
        {
            Background = _slotTransparentBrush,
            Tag = slotIndex,
            IsHitTestVisible = true,
            Visibility = Visibility.Collapsed
        };

        hitBox.PointerEntered += OnHitBoxPointerEntered;
        hitBox.PointerMoved += OnHitBoxPointerMoved;
        hitBox.PointerExited += OnHitBoxPointerExited;

        return hitBox;
    }

    /// <summary>
    /// Determina qué slot está bajo el puntero comprobando la posición
    /// contra los rectángulos de los hitboxes visibles.
    /// </summary>
    private int GetSlotUnderPointer(Point pointerPosition)
    {
        for (int i = 0; i < _slotHitBoxes.Count; i++)
        {
            var hitBox = _slotHitBoxes[i];

            if (hitBox.Visibility != Visibility.Visible)
                continue;

            var topLeft = hitBox.TransformToVisual(WidgetGrid).TransformPoint(new Point(0, 0));

            var rect = new Rect(topLeft.X, topLeft.Y, hitBox.ActualWidth, hitBox.ActualHeight);

            if (rect.Contains(pointerPosition))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Posiciona los hitboxes según la definición de slots del layout actual.
    /// Los hitboxes sobrantes quedan ocultos.
    /// </summary>
    private void PositionHitBoxes(LayoutInfo layout)
    {
        if (layout.Slots == null)
        {
            foreach (var hitBox in _slotHitBoxes)
                hitBox.Visibility = Visibility.Collapsed;

            return;
        }

        EnsureHitBoxes(layout.Slots.Count);

        for (int i = 0; i < _slotHitBoxes.Count; i++)
        {
            var hitBox = _slotHitBoxes[i];

            if (i >= layout.Slots.Count)
            {
                hitBox.Visibility = Visibility.Collapsed;
                continue;
            }

            var slot = layout.Slots[i];

            var margin = layout.Margins != null && layout.Margins.Count > i ? layout.Margins[i] : new SlotMargin(0, 0, 0, 0);

            hitBox.Visibility = Visibility.Visible;
            hitBox.Tag = i;

            hitBox.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom);

            ApplySlotGridPlacement(hitBox, slot);
        }
    }

    /// <summary>
    /// Actualiza el aspecto visual de los hitboxes durante una operación de drag.
    /// El slot de origen queda transparente, el slot en hover se resalta y el resto
    /// se muestra como zona disponible.
    /// </summary>
    private void UpdateHitBoxVisuals(int originSlot, int hoverSlot)
    {
        foreach (var hitBox in _slotHitBoxes)
        {
            if (hitBox.Visibility != Visibility.Visible)
                continue;

            int slot = (int)hitBox.Tag;

            if (slot == originSlot)
            {
                hitBox.Background = _slotTransparentBrush;
                hitBox.Opacity = 1;
            }
            else if (slot == hoverSlot)
            {
                hitBox.Background = _slotHoverBrush;
                hitBox.Opacity = 0.5;
            }
            else
            {
                hitBox.Background = _slotBaseBrush;
                hitBox.Opacity = 0.25;
            }
        }
    }

    /// <summary>
    /// Trae los hitboxes al frente para que puedan recibir eventos de puntero
    /// durante una operación de drag.
    /// </summary>
    private void BringHitBoxesToFront()
    {
        foreach (var hitBox in _slotHitBoxes)
        {
            WidgetGrid.Children.Remove(hitBox);
            WidgetGrid.Children.Add(hitBox);
        }
    }

    /// <summary>
    /// Devuelve los hitboxes al fondo para que los widgets queden por encima visualmente.
    /// </summary>
    private void SendHitBoxesToBack()
    {
        foreach (var hitBox in _slotHitBoxes)
        {
            WidgetGrid.Children.Remove(hitBox);
            WidgetGrid.Children.Insert(0, hitBox);
        }
    }

    /// <summary>
    /// Indica si un índice corresponde a un slot válido del layout actual.
    /// </summary>
    private bool IsValidSlot(int slotIndex)
    {
        return Layout?.Slots != null && slotIndex >= 0 && slotIndex < Layout.Slots.Count;
    }
    #endregion

    #region Helpers - drag state
    /// <summary>
    /// Limpia todo el estado visual temporal generado durante el drag.
    /// </summary>
    private void ClearDragVisualState(WidgetEntry dragged)
    {
        if (_dragState.Ghost != null)
        {
            WidgetGridDragLayer.Children.Remove(_dragState.Ghost);
            _dragState.Ghost = null;
        }

        dragged.Control.Opacity = 1;

        foreach (var hitBox in _slotHitBoxes)
        {
            hitBox.Background = _slotTransparentBrush;
            hitBox.Opacity = 1;
        }

        foreach (var widget in _widgets)
            widget.Control.IsHitTestVisible = true;

        SendHitBoxesToBack();
    }
    #endregion

    #region Helpers - theme
    /// <summary>
    /// Activa la suscripción a cambios de tema.
    /// </summary>
    private void SubscribeToThemeChanges()
    {
        if (_themeSubscribed)
            return;

        _themeService.ThemeChanged += OnThemeChanged;
        _themeSubscribed = true;
    }

    /// <summary>
    /// Desactiva la suscripción a cambios de tema.
    /// </summary>
    private void UnsubscribeFromThemeChanges()
    {
        if (!_themeSubscribed)
            return;

        _themeService.ThemeChanged -= OnThemeChanged;
        _themeSubscribed = false;
    }
    #endregion

    #region Methods - public
    /// <summary>
    /// Registra los widgets gestionados por el panel.
    /// Reemplaza la colección interna, renueva suscripciones y aplica el layout
    /// si el control ya está cargado.
    /// </summary>
    public void SetWidgets(IEnumerable<WidgetEntry> widgets)
    {
        foreach (var widget in _widgets)
        {
            widget.Control.DragStart -= OnWidgetDragStart;
            widget.Control.DragMove -= OnWidgetDragMove;
            widget.Control.DragEnd -= OnWidgetDragEnd;

            widget.ViewModel.PropertyChanged -= OnWidgetViewModelPropertyChanged;
        }

        _widgets.Clear();
        _widgets.AddRange(widgets);

        foreach (var widget in _widgets)
            widget.Control.Visibility = Visibility.Collapsed;

        foreach (var widget in _widgets)
        {
            widget.Control.DragStart += OnWidgetDragStart;
            widget.Control.DragMove += OnWidgetDragMove;
            widget.Control.DragEnd += OnWidgetDragEnd;

            widget.ViewModel.PropertyChanged += OnWidgetViewModelPropertyChanged;
        }

        if (Layout != null && IsLoaded)
            _ = ApplyGridLayout(Layout, animateLayoutTransition: false);
    }
    #endregion
}