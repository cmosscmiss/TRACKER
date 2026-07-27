using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private bool _countryInitializing;
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

            InitializeCountrySelector();
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
        }

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
    /// Cambia el marketplace de Amazon: persiste el país y navega al MISMO producto en ese dominio (si la página
    /// actual es de Amazon se conserva la ruta —incluye /dp/ASIN, común entre marketplaces—; si no, va a la portada).
    /// </summary>
    private void OnCountryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_countryInitializing)
            return;

        if (CountrySelector.SelectedItem is not ComboBoxItem item || item.Tag is not string code)
            return;

        string? host = Amazon.HostForCountry(code);
        if (host is null)
            return;

        App.GetService<IOptions<AppSettings>>().Value.WebViewControl.Country = code;
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
        Models.Product? product = products.AddProductFromUrl(url);
        if (product is not null)
            await products.RefreshProductAsync(product);
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
    #endregion

    #region Methods (private)
    /// <summary>Selecciona en el combo el país guardado en configuración, sin disparar navegación.</summary>
    private void InitializeCountrySelector()
    {
        string code = App.GetService<IOptions<AppSettings>>().Value.WebViewControl.Country;

        _countryInitializing = true;
        foreach (object obj in CountrySelector.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag && string.Equals(tag, code, StringComparison.OrdinalIgnoreCase))
            {
                CountrySelector.SelectedItem = item;
                break;
            }
        }
        if (CountrySelector.SelectedItem is null && CountrySelector.Items.Count > 0)
            CountrySelector.SelectedIndex = 0;
        _countryInitializing = false;
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
