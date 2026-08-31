using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Tracker.Services;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;

namespace Tracker.Controls.Dialogs;

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
    #region Constants
    /// <summary>
    /// Glifos (fuente de iconos del sistema) de los botones del pie, por ROL: el primario confirma (check), el de
    /// cerrar cancela (aspa) y "Apply" guarda los cambios sin cerrar (disquete). El secundario no lleva icono porque
    /// su acción cambia según el diálogo.
    /// </summary>
    private const string ConfirmGlyph = "\uE73E";   // CheckMark
    private const string CancelGlyph = "\uE711";    // Cancel: el mismo aspa que ya usan los botones de cancelar del log
    private const string ApplyGlyph = "\uE74E";     // Save (disquete)
    #endregion

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
    ///
    /// <paramref name="animate"/> gobierna la animación de entrada (fundido del overlay + escala de la tarjeta). Se
    /// desactiva cuando el diálogo NO está apareciendo de verdad, sino reconstruyéndose sobre sí mismo (la ventana de
    /// configuración al aplicar un tema nuevo): ahí la animación se lee como un parpadeo, porque el usuario ya tenía
    /// el diálogo delante.
    /// </summary>
    public Task<AppDialogResult> ShowAsync(XamlRoot xamlRoot, string title, object content, string? primaryText, string? secondaryText, string closeText, string? applyText = null, System.Action? onApply = null, bool dimOverlay = true, bool draggable = false, bool animate = true)
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

        SetButton(PrimaryButton, primaryText, ConfirmGlyph);
        SetButton(SecondaryButton, secondaryText);
        SetButton(CloseButton, closeText, CancelGlyph);
        // Botón "Apply" (opcional): aplica los cambios SIN cerrar el diálogo.
        SetButton(ApplyButton, applyText, ApplyGlyph);

        // Content can gate the primary button (e.g. require a selection before allowing the action).
        if (content is IAppDialogPrimaryGate gate)
        {
            PrimaryButton.IsEnabled = gate.IsPrimaryEnabled;
            gate.PrimaryEnabledChanged += (_, _) => PrimaryButton.IsEnabled = gate.IsPrimaryEnabled;
        }

        // Content can also gate the "Apply" button (e.g. only while there are pending changes).
        if (content is IAppDialogApplyGate applyGate)
        {
            ApplyButton.IsEnabled = applyGate.IsApplyEnabled;
            applyGate.ApplyEnabledChanged += (_, _) => ApplyButton.IsEnabled = applyGate.IsApplyEnabled;
        }

        // El contenido del diálogo resuelve sus {ThemeResource} contra la copia local de Theme.xaml (ver Resources): se
        // registra en el ThemeService para que los cambios de color del tema (u overrides en caliente) se reflejen aquí.
        // Además se escucha ThemeChanged mientras el diálogo está abierto, para lo que ese registro NO cubre: el fondo
        // del overlay toma su color de un recurso de tipo Color, que no se propaga solo (ver OnThemeChanged).
        ThemeService themeService = App.GetService<ThemeService>();
        themeService.RegisterExternalResources(Resources);
        themeService.ThemeChanged += OnThemeChanged;

        _popup = new Popup { XamlRoot = xamlRoot, Child = this };

        SizeToWindow();
        xamlRoot.Changed += OnXamlRootChanged;

        _popup.IsOpen = true;

        // Sin animación, el diálogo aparece ya montado: el overlay a plena opacidad y la tarjeta a su escala final
        // (los valores base del XAML son 1, así que basta con no arrancar el storyboard).
        if (animate && Resources.TryGetValue("ShowStoryboard", out object sb) && sb is Storyboard storyboard)
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

    /// <summary>
    /// Cierra el diálogo por CÓDIGO con el resultado indicado, igual que si se hubiera pulsado ese botón (completa la
    /// Task de <see cref="ShowAsync"/>). Lo usa la ventana de configuración para cerrarse y volver a abrirse tras
    /// aplicar un tema nuevo.
    /// </summary>
    /// <param name="keepVisible">
    /// Si es cierto, el diálogo se da por terminado (su Task se completa) pero su popup SIGUE EN PANTALLA hasta que
    /// se le llame a <see cref="Dismiss"/>. Lo usa la ventana de configuración al reconstruirse por un cambio de
    /// tema: el diálogo viejo hace de telón mientras se monta el nuevo, así no queda ni un fotograma con la ventana
    /// sin el overlay (el parpadeo que se veía al aplicar el tema).
    /// </param>
    public void Close(AppDialogResult result, bool keepVisible = false) => Complete(result, keepVisible);

    /// <summary>Retira de pantalla un diálogo ya completado con <c>keepVisible</c>. No hace nada en los demás casos.</summary>
    public void Dismiss()
    {
        if (_popup is null)
            return;

        _popup.IsOpen = false;
        _popup.Child = null;
        _popup = null;
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
    /// <summary>
    /// Configura un botón del pie: lo oculta si no tiene texto y, si se le da un glifo, compone el contenido como
    /// icono + texto. El icono ENLAZA su <c>Foreground</c> al del botón para que sea exactamente el mismo color que
    /// el texto (los estados visuales de los botones cambian el fondo, no el color de primer plano).
    /// </summary>
    private static void SetButton(Button button, string? text, string? glyph = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            button.Visibility = Visibility.Collapsed;
            return;
        }

        button.Content = string.IsNullOrEmpty(glyph) ? text : BuildIconContent(button, glyph!, text);
        button.Visibility = Visibility.Visible;
    }

    /// <summary>Contenido "icono + texto" de un botón del pie, centrado y con el icono al mismo color que el texto.</summary>
    private static StackPanel BuildIconContent(Button button, string glyph, string text)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };

        // El FontIcon no siempre hereda el Foreground del ContentPresenter (WinUI le aplica el suyo por defecto):
        // se enlaza al del propio botón, que es el mismo que pinta el texto.
        icon.SetBinding(FontIcon.ForegroundProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Source = button,
            Path = new PropertyPath(nameof(Button.Foreground))
        });

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        return panel;
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

    /// <summary>
    /// Repinta el fondo atenuado del overlay al cambiar de tema con el diálogo abierto: su brush toma el color de un
    /// recurso de tipo <c>Color</c>, que se resuelve una sola vez al cargar y NO se propaga en caliente (el
    /// <see cref="ThemeService"/> solo muta in situ los recursos de tipo <c>Brush</c>).
    /// </summary>
    private void OnThemeChanged(object? sender, System.EventArgs e)
    {
        if (OverlayBrush is not null)
            OverlayBrush.Color = App.GetService<ThemeService>().BackgroundColor;
    }

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

    private void Complete(AppDialogResult result, bool keepVisible = false)
    {
        if (_completed)
        {
            return;
        }
        _completed = true;

        ThemeService themeService = App.GetService<ThemeService>();
        themeService.UnregisterExternalResources(Resources);
        themeService.ThemeChanged -= OnThemeChanged;

        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnXamlRootChanged;
        }

        // Con keepVisible el popup se queda en pantalla (hace de telón) hasta que el llamador invoque Dismiss.
        if (!keepVisible)
        {
            Dismiss();
        }

        _tcs?.TrySetResult(result);
    }
    #endregion
}
