using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MM4LB.Services;

namespace MM4LB.Views;

/// <summary>
/// Contains the behavior that shows and hides the floating toolbar in the main window.
/// 
/// The toolbar appears after the pointer remains over the hover zone for a short delay,
/// and disappears after the pointer leaves the toolbar for a configured delay.
/// </summary>
public sealed partial class MainWindow
{
    #region Constants
    private const double ToolbarHiddenY = -140;
    private const double ToolbarVisibleY = 8;
    private const int ToolbarAnimationDuration = 200;
    #endregion

    #region Attributes
    private static readonly TimeSpan ToolbarShowDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ToolbarHideDelay = TimeSpan.FromSeconds(3);
    private readonly DispatcherTimer _toolbarTimer = new();
    private bool _isToolbarVisible;
    private bool _isPointerOverToolbar;
    private DelayedAction _toolbarPendingAction;

    /// <summary>
    /// Handler de <see cref="UIElement.PointerMovedEvent"/> del toolbar, guardado para poder retirarlo. Se suscribe
    /// con <c>handledEventsToo</c> para recibir el movimiento aunque los botones hijos marquen el evento como tratado.
    /// </summary>
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _toolbarPointerMovedHandler;

    /// <summary>
    /// Represents the delayed toolbar action currently waiting to be executed.
    /// </summary>
    private enum DelayedAction { None, Show, Hide }
    #endregion

    #region Initialization / Disposal
    /// <summary>
    /// Subscribes the pointer and timer events required to control toolbar visibility.
    /// </summary>
    private void InitializeToolbarBehavior()
    {
        ucToolbarHoverZone.PointerEntered += OnToolbarHoverZonePointerEntered;
        ucToolbarHoverZone.PointerExited += OnToolbarHoverZonePointerExited;

        ucToolbar.PointerEntered += OnToolbarPointerEntered;
        ucToolbar.PointerExited += OnToolbarPointerExited;

        // El movimiento sobre el toolbar es el keep-alive principal del auto-ocultado. Se engancha con
        // handledEventsToo para que llegue aunque algún control hijo trate el PointerMoved.
        _toolbarPointerMovedHandler = OnToolbarPointerMoved;
        ucToolbar.AddHandler(UIElement.PointerMovedEvent, _toolbarPointerMovedHandler, handledEventsToo: true);

        _toolbarTimer.Tick += OnToolbarTimerTick;
    }

    /// <summary>
    /// Unsubscribes toolbar-related events and stops any pending toolbar timer.
    /// </summary>
    private void DisposeToolbarBehavior()
    {
        ucToolbarHoverZone.PointerEntered -= OnToolbarHoverZonePointerEntered;
        ucToolbarHoverZone.PointerExited -= OnToolbarHoverZonePointerExited;

        ucToolbar.PointerEntered -= OnToolbarPointerEntered;
        ucToolbar.PointerExited -= OnToolbarPointerExited;

        if (_toolbarPointerMovedHandler is not null)
            ucToolbar.RemoveHandler(UIElement.PointerMovedEvent, _toolbarPointerMovedHandler);

        _toolbarTimer.Tick -= OnToolbarTimerTick;
        _toolbarTimer.Stop();
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Schedules the toolbar to be shown when the pointer enters the hover zone. If it is already visible, the
    /// hover counts as interaction and refreshes the auto-hide countdown.
    /// </summary>
    private void OnToolbarHoverZonePointerEntered(object? sender, PointerRoutedEventArgs e)
    {
        if (_isToolbarVisible)
        {
            KeepToolbarAlive();
            return;
        }

        ScheduleToolbarAction(DelayedAction.Show, ToolbarShowDelay);
    }

    /// <summary>
    /// Cancels a pending show action if the pointer leaves the hover zone before
    /// the show delay has elapsed.
    /// </summary>
    private void OnToolbarHoverZonePointerExited(object? sender, PointerRoutedEventArgs e)
    {
        if (_toolbarPendingAction == DelayedAction.Show)
            ClearToolbarPendingAction();
    }

    /// <summary>The pointer is over the toolbar: mark it and refresh the auto-hide countdown so it does not hide while in use.</summary>
    private void OnToolbarPointerEntered(object? sender, PointerRoutedEventArgs e)
    {
        _isPointerOverToolbar = true;
        KeepToolbarAlive();
    }

    /// <summary>
    /// Movement over the toolbar: confirm the over-state (in case an Entered was missed) and refresh the countdown.
    /// </summary>
    private void OnToolbarPointerMoved(object? sender, PointerRoutedEventArgs e)
    {
        _isPointerOverToolbar = true;
        KeepToolbarAlive();
    }

    /// <summary>
    /// The pointer left the toolbar: clear the over-state and (re)start the auto-hide countdown so it hides 3 s
    /// after the pointer is actually gone — never while it is still on screen.
    /// </summary>
    private void OnToolbarPointerExited(object? sender, PointerRoutedEventArgs e)
    {
        _isPointerOverToolbar = false;
        KeepToolbarAlive();
    }

    /// <summary>
    /// Refreshes the auto-hide countdown (3 s) while the toolbar is visible. Called on any pointer interaction over
    /// the toolbar/hover zone; when interaction stops, the last scheduled hide fires.
    /// </summary>
    private void KeepToolbarAlive()
    {
        if (_isToolbarVisible)
            ScheduleToolbarAction(DelayedAction.Hide, ToolbarHideDelay);
    }

    /// <summary>
    /// Executes the currently pending toolbar action once the configured delay has elapsed.
    /// </summary>
    private async void OnToolbarTimerTick(object? sender, object e)
    {
        _toolbarTimer.Stop();

        // Se captura y limpia el pendiente ANTES de ejecutar la acción: ShowToolbar programa a su vez el Hide
        // (cuenta atrás de auto-ocultado), y resetear el pendiente después lo borraría.
        DelayedAction action = _toolbarPendingAction;
        _toolbarPendingAction = DelayedAction.None;

        switch (action)
        {
            case DelayedAction.Show:
                ShowToolbar();
                break;
            case DelayedAction.Hide:
                // Nunca ocultar mientras el puntero siga sobre la toolbar (aunque esté quieto o se interactúe sin
                // generar PointerMoved): se reprograma la cuenta atrás y el Hide real esperará a que el ratón salga.
                if (_isPointerOverToolbar)
                    ScheduleToolbarAction(DelayedAction.Hide, ToolbarHideDelay);
                else
                    await HideToolbarAsync();
                break;
        }
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Shows the toolbar by translating it from its hidden position into the visible area.
    /// </summary>
    private void ShowToolbar()
    {
        if (_isToolbarVisible)
            return;

        _isToolbarVisible = true;

        AnimationService.CreateTranslateAnimation(ucToolbarTransform, 0, 0, ToolbarHiddenY, ToolbarVisibleY, ToolbarAnimationDuration).Start();

        // Robustez clave: en cuanto se muestra, queda SIEMPRE programado el auto-ocultado, así que si el puntero
        // nunca llega a entrar en el toolbar se ocultará tras 3 s. Si entra, el tick reprograma mientras
        // _isPointerOverToolbar siga activo y el Hide real esperará al PointerExited.
        ScheduleToolbarAction(DelayedAction.Hide, ToolbarHideDelay);
    }

    /// <summary>
    /// Hides the toolbar by first collapsing the panel, if expanded,
    /// and then translating the toolbar outside the visible area.
    /// </summary>
    private async Task HideToolbarAsync()
    {
        if (!_isToolbarVisible)
            return;

        // The toolbar owns the panel animation, so it is collapsed before
        // the window moves the toolbar out of view.
        await ucToolbar.CollapseExpandedSelectorAsync();

        _isToolbarVisible = false;

        AnimationService.CreateTranslateAnimation(ucToolbarTransform, 0, 0, ToolbarVisibleY, ToolbarHiddenY, ToolbarAnimationDuration).Start();
    }

    /// <summary>
    /// Clears any pending toolbar action and stops the toolbar timer.
    /// </summary>
    private void ClearToolbarPendingAction()
    {
        _toolbarPendingAction = DelayedAction.None;
        _toolbarTimer.Stop();
    }

    /// <summary>
    /// Schedules a toolbar action to be executed after the specified delay.
    /// Any previous pending action is replaced.
    /// </summary>
    /// <param name="action"> The toolbar action to execute. </param>
    /// <param name="delay"> The delay before the action is executed. </param>
    private void ScheduleToolbarAction(DelayedAction action, TimeSpan delay)
    {
        _toolbarPendingAction = action;
        _toolbarTimer.Interval = delay;
        _toolbarTimer.Stop();
        _toolbarTimer.Start();
    }
    #endregion
}