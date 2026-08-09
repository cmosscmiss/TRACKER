using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using MM4LB.Services;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;

namespace MM4LB.Controls.Dialogs;

/// <summary>Resultado de un <see cref="AppDialog"/>.</summary>
public enum AppDialogResult
{
    Primary,
    Secondary,
    Close
}

/// <summary>
/// Diálogo modal propio de la aplicación: se muestra en un <see cref="Popup"/> ligado al XamlRoot (overlay a
/// pantalla completa, sin ContentDialog), con el look de la app (cabecera con logo + título accent, contenido
/// sobre tarjeta y botones a la derecha). <see cref="ShowAsync"/> devuelve el resultado por
/// <see cref="TaskCompletionSource{TResult}"/>. Se cierra con un botón, con clic en el fondo o con Esc.
/// </summary>
public sealed partial class AppDialog : UserControl
{
    #region Attributes
    private Popup? _popup;
    private XamlRoot? _xamlRoot;
    private TaskCompletionSource<AppDialogResult>? _tcs;
    private bool _completed;
    private System.Action? _onApply;

    /// <summary>Si el diálogo se puede mover arrastrando desde la cabecera.</summary>
    private bool _draggable;

    /// <summary>Arrastre en curso + posición del puntero y traslación de la tarjeta al empezar.</summary>
    private bool _dragging;
    private Point _dragStartPointer;
    private double _dragStartX;
    private double _dragStartY;
    #endregion

    #region Constructor
    public AppDialog()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Muestra el diálogo y devuelve el resultado. Los botones con texto vacío/null se ocultan.
    /// </summary>
    public Task<AppDialogResult> ShowAsync(XamlRoot xamlRoot, string title, object content, string? primaryText, string? secondaryText, string closeText, string? applyText = null, System.Action? onApply = null, bool dimOverlay = true, bool draggable = false)
    {
        _xamlRoot = xamlRoot;
        _completed = false;
        _onApply = onApply;
        _draggable = draggable;
        _tcs = new TaskCompletionSource<AppDialogResult>();

        // El overlay a pantalla completa SIEMPRE está y capta el puntero (el diálogo es modal: no se puede interactuar
        // con la app detrás). 'dimOverlay' solo decide si atenúa (fondo semitransparente) o es totalmente transparente
        // —pero igualmente hit-testable— para ver la ventana sin atenuar (p. ej. el editor de colores).
        if (!dimOverlay)
            Overlay.Background = new SolidColorBrush(Colors.Transparent);

        TitleText.Text = title ?? string.Empty;
        ContentHost.Content = content;

        SetButton(PrimaryButton, primaryText);
        SetButton(SecondaryButton, secondaryText);
        SetButton(CloseButton, closeText);
        // Botón "Apply" (opcional): aplica los cambios SIN cerrar el diálogo.
        SetButton(ApplyButton, applyText);

        // Content can gate the primary button (e.g. require a selection before allowing the action).
        if (content is IAppDialogPrimaryGate gate)
        {
            PrimaryButton.IsEnabled = gate.IsPrimaryEnabled;
            gate.PrimaryEnabledChanged += (_, _) => PrimaryButton.IsEnabled = gate.IsPrimaryEnabled;
        }

        // El contenido del diálogo resuelve sus {ThemeResource} contra la copia local de Theme.xaml (ver Resources): se
        // registra en el ThemeService para que los cambios de color del tema (u overrides en caliente) se reflejen aquí.
        App.GetService<ThemeService>().RegisterExternalResources(Resources);

        _popup = new Popup { XamlRoot = xamlRoot, Child = this };

        SizeToWindow();
        xamlRoot.Changed += OnXamlRootChanged;

        _popup.IsOpen = true;

        if (Resources.TryGetValue("ShowStoryboard", out object sb) && sb is Storyboard storyboard)
        {
            storyboard.Begin();
        }
        else
        {
            Overlay.Opacity = 1;
        }

        // Foco al botón por defecto (para Esc/teclado y resaltado).
        Button defaultButton = PrimaryButton.Visibility == Visibility.Visible ? PrimaryButton : CloseButton;
        defaultButton.Focus(FocusState.Programmatic);

        return _tcs.Task;
    }

    /// <summary>Oculta temporalmente el diálogo SIN completarlo (la Task de <see cref="ShowAsync"/> sigue pendiente). Reversible con <see cref="Reopen"/>.</summary>
    public void Hide()
    {
        if (_popup is not null)
            _popup.IsOpen = false;
    }

    /// <summary>Vuelve a mostrar un diálogo previamente ocultado con <see cref="Hide"/>, reponiendo el tamaño y el foco.</summary>
    public void Reopen()
    {
        if (_popup is null || _completed)
            return;

        SizeToWindow();
        _popup.IsOpen = true;
        Overlay.Opacity = 1;

        Button defaultButton = PrimaryButton.Visibility == Visibility.Visible ? PrimaryButton : CloseButton;
        defaultButton.Focus(FocusState.Programmatic);
    }
    #endregion

    #region Methods (private)
    private static void SetButton(Button button, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            button.Visibility = Visibility.Collapsed;
            return;
        }

        button.Content = text;
        button.Visibility = Visibility.Visible;
    }

    private void SizeToWindow()
    {
        if (_xamlRoot is null)
        {
            return;
        }

        Width = _xamlRoot.Size.Width;
        Height = _xamlRoot.Size.Height;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => SizeToWindow();

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(AppDialogResult.Close);
        }
    }

    // Clic en el fondo atenuado = Cancel.
    private void OnOverlayTapped(object sender, TappedRoutedEventArgs e) => Complete(AppDialogResult.Close);

    // El clic dentro de la tarjeta no debe cerrar el diálogo.
    private void OnCardTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    // Arrastre del diálogo desde la cabecera (solo si draggable): traslada la tarjeta con el puntero.
    private void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggable)
            return;

        _dragging = true;
        _dragStartPointer = e.GetCurrentPoint(this).Position;
        _dragStartX = CardTranslate.X;
        _dragStartY = CardTranslate.Y;
        ((UIElement)sender).CapturePointer(e.Pointer);
    }

    private void OnHeaderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;

        Point p = e.GetCurrentPoint(this).Position;
        CardTranslate.X = _dragStartX + (p.X - _dragStartPointer.X);
        CardTranslate.Y = _dragStartY + (p.Y - _dragStartPointer.Y);
    }

    private void OnHeaderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => Complete(AppDialogResult.Primary);

    // "Apply": aplica los cambios sin cerrar el diálogo (el diálogo sigue abierto para seguir editando).
    private void OnApplyClick(object sender, RoutedEventArgs e) => _onApply?.Invoke();

    private void OnSecondaryClick(object sender, RoutedEventArgs e) => Complete(AppDialogResult.Secondary);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Complete(AppDialogResult.Close);

    private void Complete(AppDialogResult result)
    {
        if (_completed)
        {
            return;
        }
        _completed = true;

        App.GetService<ThemeService>().UnregisterExternalResources(Resources);

        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnXamlRootChanged;
        }

        if (_popup is not null)
        {
            _popup.IsOpen = false;
            _popup.Child = null;
        }

        _tcs?.TrySetResult(result);
    }
    #endregion
}
