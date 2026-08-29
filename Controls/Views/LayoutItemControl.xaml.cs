using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Tracker.Controls.Views;

/// <summary>
/// Control que representa un elemento de diseño dentro del editor de layouts.
/// Gestiona estados visuales, interacción del puntero y selección.
/// </summary>
public sealed partial class LayoutItemControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Propiedad de dependencia que indica el índice del elemento dentro de la colección de layouts.
    /// </summary>
    public static readonly DependencyProperty IndexProperty = DependencyProperty.Register(nameof(Index), typeof(int), typeof(LayoutItemControl), new PropertyMetadata(-1));
    #endregion

    #region Properties
    /// <summary>
    /// Devuelve el contenedor visual donde se aloja el layout.
    /// </summary>
    public Grid GetLayoutHost() => LayoutHost;
    #endregion

    #region Properties (Observable)
    /// <summary>
    /// Obtiene o establece el índice del elemento dentro del conjunto de layouts.
    /// </summary>
    public int Index
    {
        get => (int)GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }
    #endregion

    #region Events
    /// <summary>
    /// Evento que se dispara cuando el usuario hace clic o tap sobre el elemento.
    /// Devuelve el índice del layout seleccionado.
    /// </summary>
    public event EventHandler<int>? OnLayoutItemClicked;

    /// <summary>
    /// Evento estático que notifica cuando un elemento de layout es hovered.
    /// </summary>
    public static event Action<LayoutItemControl>? OnHoveredLayoutItemChanged;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa una nueva instancia del control y suscribe los eventos necesarios.
    /// </summary>
    public LayoutItemControl()
    {
        InitializeComponent();

        PointerEntered += OnLayoutItemPointerEntered;
        PointerExited += OnLayoutItemPointerExited;
        PointerPressed += OnLayoutItemPointerPressed;
        PointerReleased += OnLayoutItemPointerReleased;
        Tapped += OnLayoutItemTapped;
        OnHoveredLayoutItemChanged += OnOtherLayoutItemHovered;

        Unloaded += OnUnloaded;

        VisualStateManager.GoToState(this, "Normal", false);
        VisualStateManager.GoToState(this, "Unselected", false);
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Maneja el evento cuando el puntero entra en el control.
    /// Cambia el estado visual y notifica que este elemento está siendo hovered.
    /// </summary>
    private void OnLayoutItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        OnHoveredLayoutItemChanged?.Invoke(this);
        VisualStateManager.GoToState(this, "PointerOver", true);
    }

    /// <summary>
    /// Maneja el evento cuando el puntero sale del control.
    /// Restaura el estado visual a Normal.
    /// </summary>
    private void OnLayoutItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "Normal", true);
    }

    /// <summary>
    /// Se ejecuta cuando otro elemento del layout es hovered.
    /// Si no es este mismo, restaura el estado visual.
    /// </summary>
    private void OnOtherLayoutItemHovered(LayoutItemControl sender)
    {
        if (sender != this)
            VisualStateManager.GoToState(this, "Normal", true);
    }

    /// <summary>
    /// Maneja el evento de presionar el puntero sobre el control.
    /// Cambia el estado visual a Pressed.
    /// </summary>
    private void OnLayoutItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "Pressed", true);
    }

    /// <summary>
    /// Maneja el evento de liberar el puntero sobre el control.
    /// Cambia el estado visual a PointerOver.
    /// </summary>
    private void OnLayoutItemPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "PointerOver", true);
    }

    /// <summary>
    /// Maneja el evento de tap sobre el control.
    /// Dispara el evento OnLayoutItemClicked con el índice del elemento.
    /// </summary>
    private void OnLayoutItemTapped(object sender, TappedRoutedEventArgs e)
    {
        OnLayoutItemClicked?.Invoke(this, Index);
    }

    /// <summary>
    /// Desuscribe todos los eventos registrados por el control.
    /// Debe llamarse al cerrar la aplicación o al descartar el control.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PointerEntered -= OnLayoutItemPointerEntered;
        PointerExited -= OnLayoutItemPointerExited;
        PointerPressed -= OnLayoutItemPointerPressed;
        PointerReleased -= OnLayoutItemPointerReleased;
        Tapped -= OnLayoutItemTapped;
        OnHoveredLayoutItemChanged -= OnOtherLayoutItemHovered;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Cambia el estado visual del control según si está seleccionado o no.
    /// </summary>
    /// <param name="isSelected">Indica si el elemento debe mostrarse como seleccionado.</param>
    public void SetSelected(bool isSelected)
    {
        VisualStateManager.GoToState(this, isSelected ? "Selected" : "Unselected", true);
    }
    #endregion
}
