using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System.Threading.Tasks;
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
    public Task<AppDialogResult> ShowAsync(XamlRoot xamlRoot, string title, object content, string? primaryText, string? secondaryText, string closeText, string? applyText = null, System.Action? onApply = null)
    {
        _xamlRoot = xamlRoot;
        _completed = false;
        _onApply = onApply;
        _tcs = new TaskCompletionSource<AppDialogResult>();

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
