using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using MM4LB.Controls.ViewModels;
using MM4LB.Services;
using System;
using System.ComponentModel;
using Windows.System;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control visual encargado de mostrar un WebView2 dentro del sistema de widgets de MM4LB.
/// 
/// La navegación no depende de IsEnabled, sino del estado funcional del widget:
/// si el ViewModel tiene SlotIndex == -1, el widget se considera inactivo y no debe navegar.
/// Cuando SlotIndex pasa a un valor distinto de -1, el widget se considera activo y puede navegar
/// a la URL preparada por el ViewModel.
/// 
/// Responsabilidades principales:
/// - Inicializar WebView2.
/// - Escuchar las peticiones de navegación emitidas por el ViewModel.
/// - Evitar navegación cuando el widget no está asignado a ningún slot.
/// - Navegar cuando el widget vuelve a estar activo.
/// - Gestionar navegación atrás/adelante.
/// - Gestionar URLs introducidas manualmente en la barra de direcciones.
/// - Redirigir ventanas nuevas dentro del mismo WebView.
/// </summary>
public sealed partial class WebViewControl : UserControl
{
    #region Attributes
    private bool _isWidgetActive;

    /// <summary>
    /// Prefijo de los mensajes postMessage emitidos por el script inyectado para identificar
    /// las peticiones de "añadir imagen". Debe coincidir con el prefijo usado en <see cref="ImagePickerScript"/>.
    /// </summary>
    private const string ImagePickMessagePrefix = "mm4lb-add-image:";

    /// <summary>
    /// Script inyectado en cada documento que permite elegir una imagen con doble clic o Ctrl+clic
    /// y enviar su URL al host mediante postMessage.
    ///
    /// Se usan clics (no drag) porque el arrastre desde WebView2 hacia XAML está roto en WinUI 3;
    /// los eventos de clic, en cambio, funcionan con normalidad.
    /// </summary>
    private const string ImagePickerScript = @"
(function () {
    if (window.__mm4lbImagePickerInstalled) { return; }
    window.__mm4lbImagePickerInstalled = true;

    function sendImage(target) {
        var img = target && target.closest ? target.closest('img') : null;
        if (!img) { return false; }
        var url = img.currentSrc || img.src;
        if (!url) { return false; }
        window.chrome.webview.postMessage('mm4lb-add-image:' + url);
        return true;
    }

    document.addEventListener('dblclick', function (e) {
        if (sendImage(e.target)) { e.preventDefault(); }
    }, true);

    document.addEventListener('click', function (e) {
        if (!e.ctrlKey) { return; }
        if (sendImage(e.target)) { e.preventDefault(); e.stopPropagation(); }
    }, true);
})();
";

    /// <summary>
    /// Prefijo de los mensajes postMessage para las peticiones de "añadir vídeo" (debe coincidir con
    /// <see cref="VideoPickerScript"/>).
    /// </summary>
    private const string VideoPickMessagePrefix = "mm4lb-add-video:";

    /// <summary>
    /// Script inyectado en las páginas de YouTube (modo vídeo) que permite elegir un vídeo con doble clic o
    /// Ctrl+clic y enviar su URL de visionado (watch?v=...) al host. Busca el enlace de vídeo más cercano al
    /// clic (resultados de búsqueda, recomendados) y, en su defecto, usa la URL de la propia página si es la de
    /// un vídeo. Mismo enfoque de clics que el de imágenes (el drag desde WebView2 está roto en WinUI 3).
    /// </summary>
    private const string VideoPickerScript = @"
(function () {
    if (window.__mm4lbVideoPickerInstalled) { return; }
    window.__mm4lbVideoPickerInstalled = true;

    function videoUrlFrom(target) {
        var a = target && target.closest ? target.closest('a[href*=""/watch?v=""]') : null;
        if (a && a.href) { return a.href; }
        if (location.href.indexOf('/watch?v=') !== -1) { return location.href; }
        return null;
    }

    function sendVideo(target) {
        var url = videoUrlFrom(target);
        if (!url) { return false; }
        window.chrome.webview.postMessage('mm4lb-add-video:' + url);
        return true;
    }

    document.addEventListener('dblclick', function (e) {
        if (sendVideo(e.target)) { e.preventDefault(); e.stopPropagation(); }
    }, true);

    document.addEventListener('click', function (e) {
        if (!e.ctrlKey) { return; }
        if (sendVideo(e.target)) { e.preventDefault(); e.stopPropagation(); }
    }, true);
})();
";
    #endregion

    #region Dependency Properties
    public WebViewViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as WebViewViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(WebViewViewModel), typeof(WebViewControl), new PropertyMetadata(null));
    #endregion

    #region Constructors
    /// <summary>
    /// Inicializa el control, registra los eventos de ciclo de vida y prepara la carga diferida de WebView2.
    /// </summary>
    public WebViewControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Subscribed Events (Lifecycle)
    /// <summary>
    /// Se ejecuta cuando el control entra en el árbol visual.
    /// 
    /// Inicializa WebView2, carga la configuración del ViewModel, conecta los eventos del ViewModel
    /// y sincroniza el estado inicial del widget para decidir si debe navegar inmediatamente.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // In an unpackaged app, WebView2's default initialization falls back to
            // ApplicationData.Current.LocalFolder, which throws (ApplicationData.Current is only valid for
            // packaged apps) and brings the process down with a native stowed exception (0xc000027b).
            // Initialize with an explicit, writable user data folder to avoid that fallback entirely.
            string userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracker", "WebView2");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);
            await MyWebView.EnsureCoreWebView2Async(environment);

            ViewModel?.LoadConfig();
            ViewModel?.NavigationRequested += OnNavigationRequested;
            ViewModel?.PropertyChanged += OnViewModelPropertyChanged;

            RefreshWidgetActivation(force: true);
        }
        catch (Exception ex)
        {
            // Fallo real y visible para el usuario final (p. ej. runtime WebView2 Evergreen no instalado): lo
            // enrutamos al ExceptionService (diálogo + log) en vez de un Debug.WriteLine invisible en Release.
            App.GetService<ExceptionService>().Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.WebView_Init_Error] ?? "The web browser (WebView2) could not be initialized.");
        }
    }

    /// <summary>
    /// Se ejecuta cuando el control sale del árbol visual.
    /// 
    /// Limpia las suscripciones al ViewModel y a los eventos internos de WebView2 para evitar
    /// referencias persistentes y comportamientos duplicados al volver a cargar el control.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel?.NavigationRequested -= OnNavigationRequested;
        ViewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        if (MyWebView.CoreWebView2 is not null)
        {
            MyWebView.CoreWebView2.NewWindowRequested -= MyWebView_NewWindowRequested;
            MyWebView.CoreWebView2.ContextMenuRequested -= MyWebView_ContextMenuRequested;
            MyWebView.CoreWebView2.WebMessageReceived -= MyWebView_WebMessageReceived;
        }
    }
    #endregion

    #region Subscribed events (ViewModel)
    /// <summary>
    /// Atiende una petición de navegación emitida por el ViewModel.
    /// 
    /// Si el widget no está activo, la petición se ignora. La URL permanece almacenada en el
    /// ViewModel y podrá usarse más adelante cuando el widget vuelva a tener un SlotIndex válido.
    /// </summary>
    private void OnNavigationRequested(string url)
    {
        if (!IsWidgetActive)
        {
            return;
        }

        NavigateTo(url);
    }

    /// <summary>
    /// Reacciona a cambios en propiedades relevantes del ViewModel.
    /// 
    /// Actualmente solo escucha SlotIndex, porque es la propiedad que determina si el widget
    /// está realmente activo dentro del layout.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetViewModelBase.SlotIndex))
        {
            RefreshWidgetActivation();
        }
    }
    #endregion

    #region Activation logic
    /// <summary>
    /// Indica si el widget está activo dentro del layout.
    /// 
    /// Un SlotIndex igual a -1 significa que el widget no está visible o no está asignado
    /// a ningún slot, por lo que no debe ejecutar navegación.
    /// </summary>
    private bool IsWidgetActive => ViewModel is not null && ViewModel?.SlotIndex != -1;

    /// <summary>
    /// Sincroniza el estado activo/inactivo del widget.
    /// 
    /// Si el widget acaba de pasar de inactivo a activo, navega a la URL actualmente preparada
    /// por el ViewModel. Esto permite que el ViewModel mantenga la URL actualizada aunque el
    /// widget esté oculto, y que la navegación se ejecute solo cuando vuelva a estar visible.
    /// </summary>
    private void RefreshWidgetActivation(bool force = false)
    {
        bool newActiveState = IsWidgetActive;

        if (!force && newActiveState == _isWidgetActive)
        {
            return;
        }

        bool becameActive = !_isWidgetActive && newActiveState;

        _isWidgetActive = newActiveState;

        if (becameActive || force && _isWidgetActive)
        {
            NavigateToCurrentViewModelUrl();
        }
    }

    /// <summary>
    /// Navega a la URL actualmente almacenada en el ViewModel, si existe.
    /// 
    /// Este método se utiliza principalmente cuando el widget se activa después de haber estado
    /// fuera del layout.
    /// </summary>
    private void NavigateToCurrentViewModelUrl()
    {
        if (!string.IsNullOrWhiteSpace(ViewModel?.SearchStringUrl))
        {
            NavigateTo(ViewModel.SearchStringUrl);
        }
    }
    #endregion

    #region WebView Events
    /// <summary>
    /// Se ejecuta cuando CoreWebView2 termina de inicializarse.
    /// 
    /// Registra el tratamiento de nuevas ventanas para evitar que enlaces externos o target="_blank"
    /// abran una ventana separada fuera del control.
    /// </summary>
    private void MyWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception is not null)
        {
            System.Diagnostics.Debug.WriteLine($"CoreWebView2 initialization failed: {args.Exception}");
            return;
        }

        if (sender.CoreWebView2 is null)
        {
            return;
        }

        sender.CoreWebView2.NewWindowRequested -= MyWebView_NewWindowRequested;
        sender.CoreWebView2.NewWindowRequested += MyWebView_NewWindowRequested;

        // Image picking from the browser: a custom context menu command plus double-click / Ctrl+click.
        // The double-click / Ctrl+click behaviour is provided by a script injected on every navigation
        // (see MyWebView_NavigationCompleted), because clicks work while WebView2 drag events do not.
        sender.CoreWebView2.ContextMenuRequested -= MyWebView_ContextMenuRequested;
        sender.CoreWebView2.ContextMenuRequested += MyWebView_ContextMenuRequested;

        sender.CoreWebView2.WebMessageReceived -= MyWebView_WebMessageReceived;
        sender.CoreWebView2.WebMessageReceived += MyWebView_WebMessageReceived;
    }

    /// <summary>
    /// Añade un comando "Add to game images" al menú contextual cuando el usuario hace clic
    /// derecho sobre una imagen del navegador. Al seleccionarlo, la imagen se añade al juego
    /// seleccionado y queda seleccionada.
    /// </summary>
    private void MyWebView_ContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        // En modo vídeo (YouTube) se ofrece "Add to game videos" sobre un enlace de vídeo o la página de un vídeo.
        if (ViewModel?.IsVideoSearch == true)
        {
            AddVideoContextMenuItem(sender, args);
            return;
        }

        CoreWebView2ContextMenuTarget target = args.ContextMenuTarget;

        if (target.Kind != CoreWebView2ContextMenuTargetKind.Image || !target.HasSourceUri)
        {
            return;
        }

        string imageUrl = target.SourceUri;

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        CoreWebView2ContextMenuItem addImageItem = sender.Environment.CreateContextMenuItem(
            MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.WebView_AddToGameImages_MenuItem] ?? "Add to game images",
            null,
            CoreWebView2ContextMenuItemKind.Command);

        addImageItem.CustomItemSelected += (menuItem, e) => _ = ViewModel?.AddImageFromBrowserAsync(imageUrl);

        args.MenuItems.Insert(0, addImageItem);
    }

    /// <summary>
    /// Añade el comando "Add to game videos" cuando, en modo vídeo, el clic derecho recae sobre un enlace de
    /// vídeo de YouTube o sobre la propia página de un vídeo (watch?v=...). Descarga el vídeo y lo añade al juego.
    /// </summary>
    private void AddVideoContextMenuItem(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        CoreWebView2ContextMenuTarget target = args.ContextMenuTarget;

        string? videoUrl = null;
        if (target.HasLinkUri && YoutubeDownloadService.IsYoutubeVideoUrl(target.LinkUri))
        {
            videoUrl = target.LinkUri;
        }
        else if (YoutubeDownloadService.IsYoutubeVideoUrl(sender.Source))
        {
            videoUrl = sender.Source;
        }

        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            return;
        }

        CoreWebView2ContextMenuItem addVideoItem = sender.Environment.CreateContextMenuItem(
            MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.WebView_AddToGameVideos_MenuItem] ?? "Add to game videos",
            null,
            CoreWebView2ContextMenuItemKind.Command);

        addVideoItem.CustomItemSelected += (menuItem, e) => _ = ViewModel?.AddVideoFromBrowserAsync(videoUrl);

        args.MenuItems.Insert(0, addVideoItem);
    }

    /// <summary>
    /// Recibe los mensajes del script inyectado (doble clic / Ctrl+clic sobre una imagen) y
    /// reenvía la URL al ViewModel para añadir la imagen al juego seleccionado.
    /// </summary>
    private void MyWebView_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string message;

        try
        {
            message = args.TryGetWebMessageAsString();
        }
        catch
        {
            // The message was not a string: it does not come from our image picker script.
            return;
        }

        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (message.StartsWith(ImagePickMessagePrefix, StringComparison.Ordinal))
        {
            _ = ViewModel?.AddImageFromBrowserAsync(message[ImagePickMessagePrefix.Length..]);
        }
        else if (message.StartsWith(VideoPickMessagePrefix, StringComparison.Ordinal))
        {
            _ = ViewModel?.AddVideoFromBrowserAsync(message[VideoPickMessagePrefix.Length..]);
        }
    }

    /// <summary>
    /// Se ejecuta al finalizar una navegación del WebView.
    /// 
    /// Actualiza la barra de direcciones con la URL real cargada y refresca el estado de los
    /// botones de navegación atrás/adelante.
    /// </summary>
    private async void MyWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (sender.Source is not null)
        {
            tbAddressBar.Text = sender.Source.ToString();
        }

        UpdateNavigationButtonsState();

        // Inject the picker script for the current mode (video on YouTube, image otherwise). It is idempotent
        // (guarded by a window flag) and installs delegated document listeners, so a single injection per
        // navigation also covers elements added to the page dynamically afterwards.
        try
        {
            string pickerScript = ViewModel?.IsVideoSearch == true ? VideoPickerScript : ImagePickerScript;
            await sender.ExecuteScriptAsync(pickerScript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to inject picker script: {ex}");
        }
    }

    /// <summary>
    /// Intercepta peticiones de nueva ventana generadas desde el contenido web.
    /// 
    /// En lugar de abrir una ventana externa, navega a la URL solicitada dentro del mismo WebView.
    /// </summary>
    private void MyWebView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        if (!string.IsNullOrWhiteSpace(args.Uri))
        {
            sender.Navigate(args.Uri);
        }
    }
    #endregion

    #region UI events
    /// <summary>
    /// Abre el TeachingTip de ayuda que explica cómo descargar imágenes desde el navegador.
    /// </summary>
    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpTip != null)
        {
            HelpTip.IsOpen = true;
        }
    }

    /// <summary>
    /// Navega hacia atrás en el historial del WebView si existe una página anterior.
    /// </summary>
    private void AbNavigateBack_Click(object sender, RoutedEventArgs e)
    {
        if (MyWebView.CanGoBack)
        {
            MyWebView.GoBack();
        }
    }

    /// <summary>
    /// Navega hacia delante en el historial del WebView si existe una página posterior.
    /// </summary>
    private void AbNavigateForward_Click(object sender, RoutedEventArgs e)
    {
        if (MyWebView.CanGoForward)
        {
            MyWebView.GoForward();
        }
    }

    /// <summary>
    /// Procesa la navegación manual desde la barra de direcciones.
    /// 
    /// Al pulsar Enter, normaliza la URL si no incluye esquema HTTP/HTTPS y delega la petición
    /// en el ViewModel. El control decidirá después si navega o no según SlotIndex.
    /// </summary>
    private void TbAddressBar_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        string url = tbAddressBar.Text.Trim();

        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        ViewModel?.RequestNavigation(url);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Ejecuta la navegación efectiva del WebView.
    /// 
    /// Antes de navegar, comprueba que el widget esté activo, que la URL no esté vacía,
    /// que sea una URL absoluta válida y que use un esquema HTTP o HTTPS.
    /// </summary>
    private void NavigateTo(string? url)
    {
        if (!IsWidgetActive)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string trimmedUrl = url.Trim();

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out Uri? targetUri))
        {
            System.Diagnostics.Debug.WriteLine($"Invalid URL: {trimmedUrl}");
            return;
        }

        if (targetUri.Scheme != Uri.UriSchemeHttp &&
            targetUri.Scheme != Uri.UriSchemeHttps)
        {
            System.Diagnostics.Debug.WriteLine($"Unsupported URL scheme: {targetUri.Scheme}");
            return;
        }

        MyWebView.Source = targetUri;
        tbAddressBar.Text = targetUri.ToString();

        UpdateNavigationButtonsState();
    }

    /// <summary>
    /// Actualiza el estado habilitado/deshabilitado de los botones atrás y adelante
    /// según el historial disponible en el WebView.
    /// </summary>
    private void UpdateNavigationButtonsState()
    {
        abNavigateBack.IsEnabled = MyWebView.CanGoBack;
        abNavigateForward.IsEnabled = MyWebView.CanGoForward;
    }
    #endregion
}