using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using MM4LB.Controls.ViewModels;
using MM4LB.Helpers;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.ComponentModel;
using System.Linq;
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

    /// <summary>Qué está eligiendo el usuario con el modo "picking" (para interpretar el mensaje que devuelve el JS).</summary>
    private enum PickMode { None, Price, Image }
    private PickMode _pickMode = PickMode.None;
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

            // Registra este navegador VISIBLE como el que usa el login de Amazon (para ver captcha/2FA). Dispara la
            // comprobación de sesión de arranque en la ventana principal.
            App.GetService<AmazonAuthService>().AttachLoginBrowser(MyWebView);

            // Modo "seleccionar precio": el JS de picking envía el selector CSS elegido por aquí.
            MyWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            InitializeCountrySelector();
            UpdatePickPriceButtonState(MyWebView.Source);
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
            MyWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
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
        Models.Product? product = ViewModel?.SharedDataService.SelectedProduct;
        string? url = product?.BestStore?.Url ?? product?.Stores.FirstOrDefault()?.Url;
        if (!string.IsNullOrWhiteSpace(url))
        {
            NavigateTo(url);
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
    }

    /// <summary>
    /// Se ejecuta al finalizar una navegación del WebView.
    ///
    /// Actualiza la barra de direcciones con la URL real cargada y refresca el estado de los
    /// botones de navegación atrás/adelante.
    /// </summary>
    private void MyWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (sender.Source is not null)
        {
            tbAddressBar.Text = sender.Source.ToString();
            ViewModel?.SetCurrentUrl(sender.Source.ToString());
            SyncCountrySelectorToUrl(sender.Source);
            UpdatePickPriceButtonState(sender.Source);
        }

        // El usuario pudo iniciar/cerrar sesión de Amazon navegando a mano: refresca el estado del botón de sesión.
        App.GetService<AmazonAuthService>().NotifyNavigated();

        UpdateNavigationButtonsState();
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

    /// <summary>
    /// Toggle de marketplace de Amazon: hace exclusiva la selección (marca este país y desmarca el resto), persiste el
    /// país y, si cambió, navega al MISMO producto en ese dominio (si la página actual es de Amazon se conserva la ruta
    /// —incluye /dp/ASIN, común entre marketplaces—; si no, va a la portada).
    /// </summary>
    private void OnCountryToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string code)
            return;

        string? host = Amazon.HostForCountry(code);
        if (host is null)
        {
            SelectCountryVisual(App.GetService<IOptions<AppSettings>>().Value.WebViewControl.Country);
            return;
        }

        AppSettings.WebViewControlSettings settings = App.GetService<IOptions<AppSettings>>().Value.WebViewControl;
        bool changed = !string.Equals(settings.Country, code, StringComparison.OrdinalIgnoreCase);

        // Exclusividad: marca este y desmarca los demás (también re-marca si se clicó el ya activo, para no dejarlo suelto).
        SelectCountryVisual(code);

        if (!changed)
            return;

        settings.Country = code;
        ViewModel?.RequestNavigation(BuildMarketplaceUrl(host));
    }

    /// <summary>
    /// Botón "Añadir producto": da de alta un producto nuevo desde la PÁGINA ACTUAL (extrae nombre/precio/imagen de
    /// lo que se está viendo, sin re-navegar) y lo selecciona.
    /// </summary>
    private async void OnAddProductClick(object sender, RoutedEventArgs e)
    {
        string? url = MyWebView.Source?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Alta multi-tienda: si es un producto de Amazon se rastrea en todos los marketplaces (mismo ASIN); luego
        // se leen los precios de cada tienda navegando en segundo plano.
        ProductService products = App.GetService<ProductService>();

        // Si ya se rastrea un producto que cubre esta URL (mismo ASIN de Amazon o misma URL), se avisa y no se importa.
        if (products.ContainsProductForUrl(url))
        {
            await App.GetService<DialogsService>().AlertAsync(
                XamlRoot,
                L(Helpers.LocKeys.AddProduct_Duplicate_Title),
                L(Helpers.LocKeys.AddProduct_Duplicate_Message),
                L(Helpers.LocKeys.Common_OK_Label));
            return;
        }

        Models.Product? product = products.AddProductFromUrl(url);
        if (product is not null)
        {
            await products.RefreshProductAsync(product, addedProduct: true);

            // Al terminar la carga de precios, navega a la tienda con el precio más bajo.
            string? bestUrl = product.BestStore?.Url;
            if (!string.IsNullOrWhiteSpace(bestUrl))
                ViewModel?.RequestNavigation(bestUrl);
        }
    }

    /// <summary>
    /// Botón "Añadir link alternativo": añade la PÁGINA ACTUAL como una tienda más del producto seleccionado (p. ej.
    /// el mismo artículo en otro Amazon), con su precio. No hace nada si no hay producto seleccionado.
    /// </summary>
    private async void OnAddAlternativeLinkClick(object sender, RoutedEventArgs e)
    {
        Models.Product? product = ViewModel?.SharedDataService.SelectedProduct;
        string? url = MyWebView.Source?.ToString();
        if (product is null || string.IsNullOrWhiteSpace(url))
            return;

        ProductParseResult? parsed = await App.GetService<ProductParsingService>().ExtractAsync(MyWebView);
        App.GetService<ProductService>().AddAlternativeLink(product, url, parsed);
    }

    /// <summary>
    /// Activa el modo "seleccionar precio": inyecta un script que resalta el elemento bajo el cursor y, al hacer clic,
    /// envía su selector CSS (por WebMessage) para fijarlo como fuente del precio de la tienda actual. Escape cancela.
    /// </summary>
    private async void OnPickPriceClick(object sender, RoutedEventArgs e)
    {
        if (MyWebView.CoreWebView2 is null)
            return;

        _pickMode = PickMode.Price;
        await MyWebView.CoreWebView2.ExecuteScriptAsync(PricePickerScript);
    }

    /// <summary>
    /// Activa el modo "seleccionar imagen": resalta el elemento bajo el cursor y, al hacer clic, extrae la URL de su
    /// imagen (o la del &lt;img&gt; que contenga, o su background-image) y la fija como imagen del producto seleccionado.
    /// Escape cancela. Pensado para páginas no-Amazon donde la imagen no se detecta sola.
    /// </summary>
    private async void OnPickImageClick(object sender, RoutedEventArgs e)
    {
        if (MyWebView.CoreWebView2 is null)
            return;

        _pickMode = PickMode.Image;
        await MyWebView.CoreWebView2.ExecuteScriptAsync(ImagePickerScript);
    }

    /// <summary>
    /// Recibe el mensaje del modo picking. Según <see cref="_pickMode"/>: si es IMAGEN, la cadena es la URL de la
    /// imagen elegida y se fija como imagen del producto seleccionado; si es PRECIO, es el selector CSS del precio, que
    /// se fija en la tienda de la página actual y se refresca el producto. Cadena vacía = cancelado (Escape).
    /// </summary>
    private async void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string message;
        try { message = args.TryGetWebMessageAsString(); }
        catch { return; }

        PickMode mode = _pickMode;
        _pickMode = PickMode.None;

        if (string.IsNullOrEmpty(message))
            return;   // cancelado (Escape) o elemento no válido

        Models.Product? product = ViewModel?.SharedDataService.SelectedProduct;
        if (product is null)
            return;

        if (mode == PickMode.Image)
        {
            App.GetService<ProductService>().SetProductImage(product, message);
            return;
        }

        // Precio: fija el selector en la tienda que corresponde a la página actual y refresca.
        string? url = MyWebView.Source?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            return;

        Models.ProductStore? store = FindStoreForUrl(product, url) ?? product.Stores.FirstOrDefault();
        if (store is null)
            return;

        ProductService products = App.GetService<ProductService>();
        products.SetPriceSelector(store, message);

        // Diagnóstico (al log, gateado por el toggle de logging): el selector CSS generado para el elemento pulsado.
        ExceptionService.LogToFile(null, $"[Selector] elegido en {store.Label}: {message}");

        await products.RefreshProductAsync(product);
    }

    /// <summary>Tienda del producto que corresponde a la URL actual (mismo host), o null.</summary>
    private static Models.ProductStore? FindStoreForUrl(Models.Product product, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? current))
            return null;

        foreach (Models.ProductStore store in product.Stores)
            if (Uri.TryCreate(store.Url, UriKind.Absolute, out Uri? storeUri) &&
                string.Equals(storeUri.Host, current.Host, StringComparison.OrdinalIgnoreCase))
                return store;

        return null;
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;
    #endregion

    #region Price picker script
    /// <summary>
    /// JS del modo "seleccionar precio": resalta el elemento bajo el cursor y, al hacer clic, calcula un selector CSS
    /// único de ese elemento y lo envía a la app con <c>window.chrome.webview.postMessage</c>. Escape cancela (envía "").
    /// </summary>
    private const string PricePickerScript = @"
(function(){
  if (window.__ppActive) return;
  window.__ppActive = true;
  var last = null;
  function outline(el, on){ if (el && el.style) el.style.outline = on ? '3px solid #ff3ea5' : ''; }
  function esc(s){ return (window.CSS && CSS.escape) ? CSS.escape(s) : s; }
  function unique(sel){ try { return sel && document.querySelectorAll(sel).length === 1; } catch(e){ return false; } }
  // Clases estables del elemento (descarta las de estado/framework, que cambian entre cargas).
  function classes(el){
    var out = '';
    if (el.classList) {
      for (var i = 0; i < el.classList.length; i++) {
        var c = el.classList[i];
        if (!c || /^(is-|has-|js-|active|selected|open|hover|focus|ng-|css-|sc-)/i.test(c)) continue;
        out += '.' + esc(c);
      }
    }
    return out;
  }
  // Fragmento de un elemento: etiqueta + sus clases estables (+ :nth-of-type si hay hermanos del mismo tipo).
  function part(el){
    var p = el.tagName.toLowerCase() + classes(el);
    var parent = el.parentElement;
    if (parent) {
      var same = Array.prototype.filter.call(parent.children, function(c){ return c.tagName === el.tagName; });
      if (same.length > 1) { p += ':nth-of-type(' + (Array.prototype.indexOf.call(same, el) + 1) + ')'; }
    }
    return p;
  }
  // Selector del elemento: preferir id único; si no, subir por ancestros añadiendo etiqueta+clases y PARAR en cuanto
  // el selector acumulado identifica un único elemento (evita rutas :nth-of-type frágiles que casan con otro al recargar).
  function selectorFor(el){
    if (!el || el.nodeType !== 1) return '';
    if (el.id && unique('#' + esc(el.id))) return '#' + esc(el.id);
    var parts = [], cur = el;
    while (cur && cur.nodeType === 1 && parts.length < 8) {
      if (cur.id && unique('#' + esc(cur.id))) { parts.unshift('#' + esc(cur.id)); break; }
      parts.unshift(part(cur));
      var cand = parts.join(' > ');
      if (unique(cand)) return cand;
      cur = cur.parentElement;
    }
    return parts.join(' > ');
  }
  function move(e){ if (last !== e.target) { outline(last, false); last = e.target; outline(last, true); } }
  function click(e){ e.preventDefault(); e.stopPropagation(); var s = selectorFor(e.target); cleanup(); window.chrome.webview.postMessage(s || ''); }
  function key(e){ if (e.key === 'Escape') { cleanup(); window.chrome.webview.postMessage(''); } }
  function cleanup(){
    outline(last, false);
    document.removeEventListener('mousemove', move, true);
    document.removeEventListener('click', click, true);
    document.removeEventListener('keydown', key, true);
    window.__ppActive = false;
  }
  document.addEventListener('mousemove', move, true);
  document.addEventListener('click', click, true);
  document.addEventListener('keydown', key, true);
})();
";

    /// <summary>
    /// JS del modo "seleccionar imagen": resalta el elemento bajo el cursor y, al hacer clic, resuelve la URL de su
    /// imagen —el propio &lt;img&gt;, un &lt;img&gt; que contenga, o su <c>background-image</c>— y la envía a la app
    /// (URL absoluta) con <c>postMessage</c>. Escape cancela (envía "").
    /// </summary>
    private const string ImagePickerScript = @"
(function(){
  if (window.__ipActive) return;
  window.__ipActive = true;
  var last = null;
  function outline(el, on){ if (el && el.style) el.style.outline = on ? '3px solid #33c1ff' : ''; }
  function abs(u){ if (!u) return ''; try { return new URL(u, document.baseURI).href; } catch(e) { return u; } }
  function fromImg(im){ return im ? abs(im.currentSrc || im.src || im.getAttribute('data-old-hires') || im.getAttribute('data-src') || '') : ''; }
  function imageUrl(el){
    if (!el) return '';
    if (el.tagName === 'IMG') return fromImg(el);
    var inner = el.querySelector && el.querySelector('img');
    if (inner) return fromImg(inner);
    var bg = '';
    try { bg = getComputedStyle(el).backgroundImage; } catch(e) {}
    var m = bg && bg.match(/url\((['""]?)(.*?)\1\)/);
    return (m && m[2]) ? abs(m[2]) : '';
  }
  function move(e){ if (last !== e.target) { outline(last, false); last = e.target; outline(last, true); } }
  function click(e){ e.preventDefault(); e.stopPropagation(); var u = imageUrl(e.target); cleanup(); window.chrome.webview.postMessage(u || ''); }
  function key(e){ if (e.key === 'Escape') { cleanup(); window.chrome.webview.postMessage(''); } }
  function cleanup(){
    outline(last, false);
    document.removeEventListener('mousemove', move, true);
    document.removeEventListener('click', click, true);
    document.removeEventListener('keydown', key, true);
    window.__ipActive = false;
  }
  document.addEventListener('mousemove', move, true);
  document.addEventListener('click', click, true);
  document.addEventListener('keydown', key, true);
})();
";
    #endregion

    #region Methods (private)
    /// <summary>Marca en el grupo de toggles el país guardado en configuración (o el primero si no es válido), sin navegar.</summary>
    private void InitializeCountrySelector()
    {
        string code = App.GetService<IOptions<AppSettings>>().Value.WebViewControl.Country;
        SelectCountryVisual(code);

        // Si el código guardado no coincide con ningún toggle, marca el primero por defecto.
        if (!CountryToggles.Children.OfType<ToggleButton>().Any(toggle => toggle.IsChecked == true) &&
            CountryToggles.Children.OfType<ToggleButton>().FirstOrDefault() is ToggleButton first)
            first.IsChecked = true;
    }

    /// <summary>
    /// Sincroniza el grupo de toggles con el marketplace de la página actualmente abierta: si la URL es de un
    /// marketplace de Amazon soportado, marca su país (sin re-navegar) y persiste la preferencia. Si no es un
    /// marketplace conocido, no toca los toggles.
    /// </summary>
    private void SyncCountrySelectorToUrl(Uri? uri)
    {
        string? code = Amazon.CountryForHost(uri);
        if (code is null)
            return;

        SelectCountryVisual(code);
        App.GetService<IOptions<AppSettings>>().Value.WebViewControl.Country = code;
    }

    /// <summary>Hace exclusiva la selección de país en el grupo de toggles: marca el del código dado y desmarca el resto.</summary>
    private void SelectCountryVisual(string code)
    {
        foreach (ToggleButton toggle in CountryToggles.Children.OfType<ToggleButton>())
            toggle.IsChecked = toggle.Tag is string tag && string.Equals(tag, code, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Habilita los botones de seleccionar imagen/precio solo fuera de Amazon (en Amazon se leen solos).</summary>
    private void UpdatePickPriceButtonState(Uri? uri)
    {
        bool enabled = uri is null || !Amazon.IsAmazon(uri);
        PickPriceButton.IsEnabled = enabled;
        PickImageButton.IsEnabled = enabled;
    }

    /// <summary>URL del producto actual en el marketplace <paramref name="host"/> (conserva la ruta si es Amazon).</summary>
    private string BuildMarketplaceUrl(string host)
    {
        Uri? current = MyWebView.Source;
        if (current is not null && current.Host.Contains("amazon", StringComparison.OrdinalIgnoreCase))
            return $"https://{host}{current.PathAndQuery}";

        return $"https://{host}/";
    }

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
