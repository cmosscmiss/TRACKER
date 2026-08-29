using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Controls.Templates;
using MM4LB.Controls.ViewModels;
using MM4LB.Converters;
using MM4LB.Helpers;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.Foundation;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control visual que permite seleccionar qué widgets están activos y asignarlos
/// a los slots del layout actualmente seleccionado.
/// 
/// Responsabilidades principales:
/// - Renderizar dinámicamente los botones de widgets disponibles.
/// - Mostrar una previsualización del layout seleccionado.
/// - Mostrar el icono de cada widget dentro del slot que ocupa.
/// - Permitir drag & drop desde la lista de widgets hacia los slots del layout.
/// - Actualizar el <see cref="WidgetViewModelBase.SlotIndex"/> de cada widget.
/// - Refrescar el estado visual cuando cambian los slots, el layout o el tema.
/// </summary>
public sealed partial class WidgetSelectorControl : UserControl
{
    #region Nested subclasses
    /// <summary>
    /// Mantiene el estado temporal de una operación de drag iniciada desde
    /// un botón de widget del selector.
    /// </summary>
    private sealed class WidgetButtonDragState
    {
        public WidgetInfo? Widget { get; set; }
        public ToolbarButtonIcon? SourceButton { get; set; }
        public Image? Ghost { get; set; }
        public Point Offset { get; set; }
        public uint PointerId { get; set; }
        public int HoverSlot { get; set; } = -1;

        /// <summary>
        /// Slot que ocupaba el widget arrastrado al iniciar el drag (-1 si no estaba colocado).
        /// Permanece iluminado durante toda la operación como referencia de su posición actual.
        /// </summary>
        public int OriginSlot { get; set; } = -1;

        public bool IsDragging => Widget is not null;

        /// <summary>
        /// Restablece el estado interno de drag a sus valores iniciales.
        /// </summary>
        public void Reset()
        {
            Widget = null;
            SourceButton = null;
            Ghost = null;
            Offset = default;
            PointerId = 0;
            HoverSlot = -1;
            OriginSlot = -1;
        }
    }
    #endregion

    #region Constants
    private const double SlotIconSize = 72;
    private const double DragGhostSize = 64;
    #endregion

    #region Attributes
    private readonly ThemeService _themeService;

    private readonly SlotIndexToIsCheckedConverter _slotIndexToIsCheckedConverter = new();
    private readonly WidgetButtonDragState _widgetButtonDragState = new();
    private bool _suppressSelectedLayoutRender;
    private bool _themeSubscribed;
    private bool _widgetSlotChangesSubscribed;
    #endregion

    #region Dependency Properties
    public LayoutInfo? SelectedLayout
    {
        get => (LayoutInfo?)GetValue(SelectedLayoutProperty);
        set => SetValue(SelectedLayoutProperty, value);
    }

    public static readonly DependencyProperty SelectedLayoutProperty = DependencyProperty.Register(nameof(SelectedLayout), typeof(LayoutInfo), typeof(WidgetSelectorControl), new PropertyMetadata(null, OnSelectedLayoutChanged));

    public IReadOnlyList<WidgetInfo>? Widgets
    {
        get => (IReadOnlyList<WidgetInfo>?)GetValue(WidgetsProperty);
        set => SetValue(WidgetsProperty, value);
    }

    public static readonly DependencyProperty WidgetsProperty = DependencyProperty.Register(nameof(Widgets), typeof(IReadOnlyList<WidgetInfo>), typeof(WidgetSelectorControl), new PropertyMetadata(null, OnWidgetsChanged));
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el control, resuelve los servicios necesarios y registra
    /// los eventos de ciclo de vida del control.
    /// </summary>
    public WidgetSelectorControl()
    {
        InitializeComponent();

        _themeService = App.GetService<ThemeService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Dependency Property callbacks
    /// <summary>
    /// Se ejecuta cuando cambia la lista de widgets disponible para el selector.
    /// Actualiza las suscripciones si el control está activo y vuelve a renderizar
    /// tanto la lista de botones como la previsualización del layout.
    /// </summary>
    private static void OnWidgetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WidgetSelectorControl control)
            return;

        if (control._widgetSlotChangesSubscribed)
        {
            control.UnsubscribeFromWidgetSlotChanges(e.OldValue as IReadOnlyList<WidgetInfo>);
            control.SubscribeToWidgetSlotChanges(e.NewValue as IReadOnlyList<WidgetInfo>);
        }

        control.RenderWidgets();
        control.RenderSelectedLayout();
    }

    /// <summary>
    /// Se ejecuta cuando cambia el layout seleccionado.
    /// Re-renderiza la previsualización y normaliza los slots de widgets
    /// que ya no sean válidos para el nuevo layout.
    /// </summary>
    private static void OnSelectedLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WidgetSelectorControl control)
            return;

        control.RenderSelectedLayout();
    }
    #endregion

    #region Lifecycle events
    /// <summary>
    /// Se ejecuta cuando el control entra en el árbol visual.
    /// Renderiza el estado actual y activa las suscripciones necesarias.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RenderWidgets();
        RenderSelectedLayout();

        SubscribeToThemeChanges();
        SubscribeToWidgetSlotChanges();
    }

    /// <summary>
    /// Se ejecuta cuando el control sale del árbol visual.
    /// Cancela cualquier drag activo y libera las suscripciones externas.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        EndWidgetButtonDrag();

        UnsubscribeFromThemeChanges();
        UnsubscribeFromWidgetSlotChanges();
    }
    #endregion

    #region Theme events
    /// <summary>
    /// Se ejecuta cuando cambia el tema activo.
    /// Vuelve a renderizar los botones y la previsualización para resolver
    /// los iconos y brushes del tema actualizado.
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RenderWidgets();
        RenderSelectedLayout();
    }
    #endregion

    #region Rendering - widgets
    /// <summary>
    /// Renderiza la lista de botones de widgets disponibles.
    /// Cada botón queda enlazado al <see cref="WidgetViewModelBase.SlotIndex"/>
    /// de su widget para reflejar automáticamente si está activo o no.
    /// </summary>
    private void RenderWidgets()
    {
        if (RootPanel is null)
            return;

        RootPanel.Children.Clear();

        if (Widgets is null)
            return;

        foreach (var widget in Widgets)
        {
            var button = new ToolbarButtonIcon
            {
                CheckedIcon = CreateWidgetIcon(widget.IconName),
                UncheckedIcon = CreateWidgetIcon(widget.IconName, isChecked: false),
                Style = (Style)Application.Current.Resources["ToolbarButtonIconLargeStyle"],
                DataContext = widget,
                Tag = widget
            };

            // Tooltip descriptivo del widget, gobernado por el toggle global de tooltips (pie de página).
            Help.SetKey(button, GetWidgetTooltipKey(widget.IconName));

            button.SetBinding(ToolbarButtonIcon.IsCheckedProperty, new Binding
                {
                    Source = widget.ViewModel,
                    Path = new PropertyPath(nameof(WidgetViewModelBase.SlotIndex)),
                    Mode = BindingMode.OneWay,
                    Converter = _slotIndexToIsCheckedConverter
                });

            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnWidgetButtonPointerPressed), true);
            button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnWidgetButtonPointerMoved), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnWidgetButtonPointerReleased), true);
            button.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnWidgetButtonPointerCanceled), true);

            RootPanel.Children.Add(button);
        }
    }

    /// <summary>
    /// Crea un icono de widget usando el tema activo.
    /// </summary>
    /// <param name="iconName">Nombre base del icono del widget.</param>
    /// <param name="isChecked">Indica si se debe resolver el icono activo o inactivo.</param>
    /// <returns>Imagen lista para ser usada como fuente visual.</returns>
    private BitmapImage CreateWidgetIcon(string iconName, bool isChecked = true)
    {
        string resolvedIconName = isChecked ? iconName : $"{iconName}-off";

        return new BitmapImage(_themeService.GetWidgetIconUri(resolvedIconName));
    }

    /// <summary>
    /// Resuelve la clave de recurso del tooltip de un widget a partir del nombre de su icono (que es el nombre del
    /// tipo del control alojado). Los widgets sin tooltip propio caen en la clave genérica.
    /// </summary>
    /// <param name="iconName">Nombre base del icono del widget.</param>
    /// <returns>Clave de recurso localizada, para <see cref="Help"/>.</returns>
    private static string GetWidgetTooltipKey(string iconName) => iconName switch
    {
        nameof(WebViewControl) => LocKeys.WidgetSelector_WebView_Tooltip,
        nameof(FavoritesControl) => LocKeys.WidgetSelector_Favorites_Tooltip,
        nameof(ProductsOverviewControl) => LocKeys.WidgetSelector_ProductsOverview_Tooltip,
        nameof(ConsoleControl) => LocKeys.WidgetSelector_Console_Tooltip,
        _ => LocKeys.WidgetSelector_Default_Tooltip
    };
    #endregion

    #region Rendering - selected layout
    /// <summary>
    /// Renderiza la previsualización del layout seleccionado y coloca dentro
    /// de cada slot ocupado el icono del widget correspondiente.
    /// </summary>
    private void RenderSelectedLayout()
    {
        if (SelectedLayoutHost is null)
            return;

        if (SelectedLayout is null)
        {
            SelectedLayoutHost.Children.Clear();
            SelectedLayoutHost.RowDefinitions.Clear();
            SelectedLayoutHost.ColumnDefinitions.Clear();
            return;
        }

        NormalizeWidgetSlotIndexesForSelectedLayout();
        LayoutBuilder.Build(SelectedLayoutHost, SelectedLayout.Index);
        RenderWidgetIconsInSelectedLayout();
    }

    /// <summary>
    /// Añade los iconos de los widgets visibles sobre los slots correspondientes
    /// dentro de la previsualización del layout seleccionado.
    /// </summary>
    private void RenderWidgetIconsInSelectedLayout()
    {
        if (SelectedLayout?.Slots is null)
            return;

        if (Widgets is null)
            return;

        for (int slotIndex = 0; slotIndex < SelectedLayout.Slots.Count; slotIndex++)
        {
            var widget = Widgets.FirstOrDefault(w => w.ViewModel.SlotIndex == slotIndex);

            if (widget is null)
                continue;

            var slot = SelectedLayout.Slots[slotIndex];

            var slotBorder = FindSlotBorder(slotIndex);

            if (slotBorder is not null)
                ApplyOccupiedSlotStyle(slotBorder);

            var icon = new Image
            {
                Source = CreateWidgetIcon(widget.IconName),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = SlotIconSize,
                MaxHeight = SlotIconSize,
                IsHitTestVisible = false
            };

            var iconHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
                Margin = slotBorder?.Margin ?? new Thickness(0)
            };

            iconHost.Children.Add(icon);

            Grid.SetRow(iconHost, slot.Row);
            Grid.SetColumn(iconHost, slot.Column);
            Grid.SetRowSpan(iconHost, Math.Max(1, slot.RowSpan));
            Grid.SetColumnSpan(iconHost, Math.Max(1, slot.ColSpan));

            SelectedLayoutHost.Children.Add(iconHost);
        }
    }
    #endregion

    #region Drag & Drop
    /// <summary>
    /// Inicia una operación de drag desde un botón de widget.
    /// Crea el ghost visual y captura el puntero sobre el botón de origen.
    /// </summary>
    private void OnWidgetButtonPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ToolbarButtonIcon button)
            return;

        if (button.Tag is not WidgetInfo widget)
            return;

        _widgetButtonDragState.Widget = widget;
        _widgetButtonDragState.SourceButton = button;
        _widgetButtonDragState.PointerId = e.Pointer.PointerId;
        _widgetButtonDragState.Offset = new Point(DragGhostSize / 2, DragGhostSize / 2);

        // Si el widget ya está colocado, ilumina su slot actual igual que el resaltado de drop,
        // para indicar dónde está posicionado mientras se arrastra.
        _widgetButtonDragState.OriginSlot = widget.ViewModel.SlotIndex;
        HighlightOriginSlot();

        button.CapturePointer(e.Pointer);

        var rootPosition = e.GetCurrentPoint(RootGrid).Position;

        var ghost = new Image
        {
            Source = CreateWidgetIcon(widget.IconName),
            Width = DragGhostSize,
            Height = DragGhostSize,
            Stretch = Stretch.Uniform,
            Opacity = 0.85,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(ghost, rootPosition.X - _widgetButtonDragState.Offset.X);
        Canvas.SetTop(ghost, rootPosition.Y - _widgetButtonDragState.Offset.Y);

        _widgetButtonDragState.Ghost = ghost;
        DragLayer.Children.Add(ghost);

        e.Handled = true;
    }

    /// <summary>
    /// Actualiza la posición del ghost durante el drag y resalta el slot
    /// que se encuentra bajo el puntero.
    /// </summary>
    private void OnWidgetButtonPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_widgetButtonDragState.IsDragging)
            return;

        if (e.Pointer.PointerId != _widgetButtonDragState.PointerId)
            return;

        var rootPosition = e.GetCurrentPoint(RootGrid).Position;

        if (_widgetButtonDragState.Ghost is not null)
        {
            Canvas.SetLeft(
                _widgetButtonDragState.Ghost,
                rootPosition.X - _widgetButtonDragState.Offset.X);

            Canvas.SetTop(
                _widgetButtonDragState.Ghost,
                rootPosition.Y - _widgetButtonDragState.Offset.Y);
        }

        var layoutPosition = e.GetCurrentPoint(SelectedLayoutHost).Position;
        int hoverSlot = GetSlotIndexUnderPointer(layoutPosition);

        UpdateDropTargetHighlight(hoverSlot);

        e.Handled = true;
    }

    /// <summary>
    /// Finaliza una operación de drag y aplica el drop si el puntero
    /// se encuentra sobre un slot válido.
    /// </summary>
    private void OnWidgetButtonPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_widgetButtonDragState.IsDragging)
            return;

        if (e.Pointer.PointerId != _widgetButtonDragState.PointerId)
            return;

        var layoutPosition = e.GetCurrentPoint(SelectedLayoutHost).Position;
        int targetSlot = GetSlotIndexUnderPointer(layoutPosition);

        ApplyWidgetDrop(targetSlot);

        EndWidgetButtonDrag(e);

        e.Handled = true;
    }

    /// <summary>
    /// Cancela una operación de drag cuando el sistema cancela el puntero.
    /// </summary>
    private void OnWidgetButtonPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_widgetButtonDragState.IsDragging)
            return;

        EndWidgetButtonDrag(e);

        e.Handled = true;
    }

    /// <summary>
    /// Aplica la lógica de drop sobre el slot destino.
    /// Si el slot está vacío, asigna el widget soltado a ese slot.
    /// Si el slot está ocupado, oculta el widget anterior y asigna el nuevo.
    /// </summary>
    private void ApplyWidgetDrop(int targetSlot)
    {
        if (_widgetButtonDragState.Widget is null)
            return;

        if (Widgets is null)
            return;

        if (SelectedLayout?.Slots is null)
            return;

        if (targetSlot < 0 || targetSlot >= SelectedLayout.Slots.Count)
            return;

        var droppedWidget = _widgetButtonDragState.Widget;

        if (droppedWidget.ViewModel.SlotIndex == targetSlot)
            return;

        var occupyingWidget = Widgets.FirstOrDefault(widget => !ReferenceEquals(widget, droppedWidget) && widget.ViewModel.SlotIndex == targetSlot);

        _suppressSelectedLayoutRender = true;

        try
        {
            if (occupyingWidget is not null)
                occupyingWidget.ViewModel.SlotIndex = -1;

            droppedWidget.ViewModel.SlotIndex = targetSlot;
        }
        finally
        {
            _suppressSelectedLayoutRender = false;
        }

        RenderSelectedLayout();
    }

    /// <summary>
    /// Finaliza una operación de drag iniciada por un evento de puntero
    /// y libera la captura del puntero.
    /// </summary>
    private void EndWidgetButtonDrag(PointerRoutedEventArgs e)
    {
        _widgetButtonDragState.SourceButton?.ReleasePointerCapture(e.Pointer);
        EndWidgetButtonDrag();
    }

    /// <summary>
    /// Finaliza cualquier operación de drag activa y limpia el ghost visual,
    /// incluso cuando no hay un evento de puntero disponible.
    /// </summary>
    private void EndWidgetButtonDrag()
    {
        // El slot de origen deja de estar iluminado: se limpia primero para que la restauración
        // del hover no lo mantenga vivo, y luego se devuelve a su estilo real.
        int originSlot = _widgetButtonDragState.OriginSlot;
        _widgetButtonDragState.OriginSlot = -1;

        UpdateDropTargetHighlight(-1);

        if (originSlot >= 0)
            ApplySlotStyleForCurrentState(originSlot);

        if (_widgetButtonDragState.Ghost is not null)
            DragLayer.Children.Remove(_widgetButtonDragState.Ghost);

        _widgetButtonDragState.Reset();
    }
    #endregion

    #region Slot detection and styling
    /// <summary>
    /// Devuelve el índice del slot que se encuentra bajo el puntero dentro
    /// de la previsualización del layout.
    /// </summary>
    /// <param name="pointerPosition">Posición del puntero relativa a <c>SelectedLayoutHost</c>.</param>
    /// <returns>Índice del slot bajo el puntero, o -1 si no hay ninguno.</returns>
    private int GetSlotIndexUnderPointer(Point pointerPosition)
    {
        if (SelectedLayout?.Slots is null)
            return -1;

        for (int slotIndex = 0; slotIndex < SelectedLayout.Slots.Count; slotIndex++)
        {
            var slotBorder = FindSlotBorder(slotIndex);
            if (slotBorder is null)
                continue;

            var topLeft = slotBorder.TransformToVisual(SelectedLayoutHost).TransformPoint(new Point(0, 0));
            var rect = new Rect(topLeft.X, topLeft.Y, slotBorder.ActualWidth, slotBorder.ActualHeight);

            if (rect.Contains(pointerPosition))
                return slotIndex;
        }

        return -1;
    }

    /// <summary>
    /// Actualiza el resaltado visual del slot bajo el puntero durante una
    /// operación de drag.
    /// </summary>
    private void UpdateDropTargetHighlight(int hoverSlot)
    {
        if (_widgetButtonDragState.HoverSlot == hoverSlot)
            return;

        if (_widgetButtonDragState.HoverSlot >= 0)
            RestoreSlotStyleDuringDrag(_widgetButtonDragState.HoverSlot);

        _widgetButtonDragState.HoverSlot = hoverSlot;

        if (hoverSlot < 0)
            return;

        var slotBorder = FindSlotBorder(hoverSlot);

        if (slotBorder is null)
            return;

        ApplyDropHoverSlotStyle(slotBorder);
    }

    /// <summary>
    /// Ilumina el slot de origen del widget arrastrado (si está colocado) con el mismo
    /// resaltado que el de drop, para señalar su posición actual durante el drag.
    /// </summary>
    private void HighlightOriginSlot()
    {
        if (_widgetButtonDragState.OriginSlot < 0)
            return;

        var originBorder = FindSlotBorder(_widgetButtonDragState.OriginSlot);

        if (originBorder is not null)
            ApplyDropHoverSlotStyle(originBorder);
    }

    /// <summary>
    /// Restaura el estilo de un slot al dejar de estar bajo el puntero durante un drag.
    /// El slot de origen del widget arrastrado se mantiene iluminado en lugar de volver
    /// a su estilo de slot ocupado.
    /// </summary>
    private void RestoreSlotStyleDuringDrag(int slotIndex)
    {
        if (slotIndex == _widgetButtonDragState.OriginSlot)
        {
            var originBorder = FindSlotBorder(slotIndex);

            if (originBorder is not null)
                ApplyDropHoverSlotStyle(originBorder);

            return;
        }

        ApplySlotStyleForCurrentState(slotIndex);
    }

    /// <summary>
    /// Aplica al slot indicado el estilo que corresponde a su estado real:
    /// ocupado o vacío.
    /// </summary>
    private void ApplySlotStyleForCurrentState(int slotIndex)
    {
        var slotBorder = FindSlotBorder(slotIndex);

        if (slotBorder is null)
            return;

        bool isOccupied = Widgets?.Any(widget =>
            widget.ViewModel.SlotIndex == slotIndex) == true;

        if (isOccupied)
            ApplyOccupiedSlotStyle(slotBorder);
        else
            ApplyEmptySlotStyle(slotBorder);
    }

    /// <summary>
    /// Localiza el borde visual que representa un slot dentro del layout renderizado.
    /// </summary>
    /// <param name="slotIndex">Índice lógico del slot.</param>
    /// <returns>El borde asociado al slot, o null si no existe.</returns>
    private Border? FindSlotBorder(int slotIndex)
    {
        return SelectedLayoutHost.Children.OfType<Border>().FirstOrDefault(border => border.Tag is int index && index == slotIndex);
    }

    /// <summary>
    /// Aplica el estilo visual de hover/drop target a un slot.
    /// </summary>
    private void ApplyDropHoverSlotStyle(Border slotBorder)
    {
        slotBorder.Background = (Brush)Application.Current.Resources["AccentBrush"];
        slotBorder.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
        slotBorder.BorderThickness = new Thickness(2);
    }

    /// <summary>
    /// Aplica el estilo visual de slot ocupado.
    /// </summary>
    private void ApplyOccupiedSlotStyle(Border slotBorder)
    {
        slotBorder.Background = (Brush)Application.Current.Resources["BackgroundBrush"];
        slotBorder.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
        slotBorder.BorderThickness = new Thickness(1);
    }

    /// <summary>
    /// Aplica el estilo visual de slot vacío.
    /// </summary>
    private void ApplyEmptySlotStyle(Border slotBorder)
    {
        slotBorder.Background = (Brush)Application.Current.Resources["CardBackgroundLightBrush"];
        slotBorder.BorderBrush = (Brush)Application.Current.Resources["TextSecondaryBrush"];
        slotBorder.BorderThickness = new Thickness(1);
    }
    #endregion

    #region Widget slot state
    /// <summary>
    /// Activa la escucha de cambios de <see cref="WidgetViewModelBase.SlotIndex"/>
    /// para la lista actual de widgets.
    /// </summary>
    private void SubscribeToWidgetSlotChanges()
    {
        if (_widgetSlotChangesSubscribed)
            return;

        SubscribeToWidgetSlotChanges(Widgets);
        _widgetSlotChangesSubscribed = true;
    }

    /// <summary>
    /// Activa la escucha de cambios de <see cref="WidgetViewModelBase.SlotIndex"/>
    /// para una lista concreta de widgets.
    /// </summary>
    private void SubscribeToWidgetSlotChanges(IReadOnlyList<WidgetInfo>? widgets)
    {
        if (widgets is null)
            return;

        foreach (var widget in widgets)
            widget.ViewModel.PropertyChanged += OnWidgetViewModelPropertyChanged;
    }

    /// <summary>
    /// Desactiva la escucha de cambios de <see cref="WidgetViewModelBase.SlotIndex"/>
    /// para la lista actual de widgets.
    /// </summary>
    private void UnsubscribeFromWidgetSlotChanges()
    {
        if (!_widgetSlotChangesSubscribed)
            return;

        UnsubscribeFromWidgetSlotChanges(Widgets);
        _widgetSlotChangesSubscribed = false;
    }

    /// <summary>
    /// Desactiva la escucha de cambios de <see cref="WidgetViewModelBase.SlotIndex"/>
    /// para una lista concreta de widgets.
    /// </summary>
    private void UnsubscribeFromWidgetSlotChanges(IReadOnlyList<WidgetInfo>? widgets)
    {
        if (widgets is null)
            return;

        foreach (var widget in widgets)
            widget.ViewModel.PropertyChanged -= OnWidgetViewModelPropertyChanged;
    }

    /// <summary>
    /// Se ejecuta cuando cambia una propiedad de un ViewModel de widget.
    /// Si cambia el slot del widget, actualiza la previsualización del layout.
    /// </summary>
    private void OnWidgetViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WidgetViewModelBase.SlotIndex))
            return;

        if (_suppressSelectedLayoutRender)
            return;

        RenderSelectedLayout();
    }

    /// <summary>
    /// Normaliza los slots de los widgets para que todos sean válidos
    /// dentro del layout seleccionado.
    /// </summary>
    private void NormalizeWidgetSlotIndexesForSelectedLayout()
    {
        if (SelectedLayout?.Slots is null)
            return;

        if (Widgets is null)
            return;

        int validSlotCount = SelectedLayout.Slots.Count;
        var usedSlots = new HashSet<int>();

        _suppressSelectedLayoutRender = true;

        try
        {
            foreach (var widget in Widgets)
            {
                int slotIndex = widget.ViewModel.SlotIndex;

                if (slotIndex == -1)
                    continue;

                bool slotIsInvalid = slotIndex < 0 || slotIndex >= validSlotCount;
                bool slotAlreadyUsed = !slotIsInvalid && !usedSlots.Add(slotIndex);

                if (slotIsInvalid || slotAlreadyUsed)
                    widget.ViewModel.SlotIndex = -1;
            }
        }
        finally
        {
            _suppressSelectedLayoutRender = false;
        }
    }
    #endregion

    #region Theme subscriptions
    /// <summary>
    /// Activa la suscripción a cambios de tema si todavía no está activa.
    /// </summary>
    private void SubscribeToThemeChanges()
    {
        if (_themeSubscribed)
            return;

        _themeService.ThemeChanged += OnThemeChanged;
        _themeSubscribed = true;
    }

    /// <summary>
    /// Desactiva la suscripción a cambios de tema si está activa.
    /// </summary>
    private void UnsubscribeFromThemeChanges()
    {
        if (!_themeSubscribed)
            return;

        _themeService.ThemeChanged -= OnThemeChanged;
        _themeSubscribed = false;
    }
    #endregion
}
