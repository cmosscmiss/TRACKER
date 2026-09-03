using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tracker.Controls.ViewModels;
using Tracker.Helpers;
using Tracker.Models;
using Tracker.Services;

namespace Tracker.Controls.Views;

/// <summary>
/// Widget que muestra la evolución del precio del producto seleccionado, reutilizando
/// <see cref="ChartTypeSelectorControl"/> (con su toolbar de tipo de gráfica) alimentado por
/// <see cref="PriceChartViewModel"/>. Incluye una barra con acciones sobre el producto (marcar comprado / borrar).
/// El ViewModel se inyecta desde fuera vía <see cref="ViewModel"/>.
/// </summary>
public sealed partial class PriceChartControl : UserControl
{
    #region Dependency Properties
    public PriceChartViewModel? ViewModel
    {
        get => (PriceChartViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(PriceChartViewModel), typeof(PriceChartControl), new PropertyMetadata(null));
    #endregion

    #region Attributes
    /// <summary>Instancia (singleton) del navegador, para navegar al pulsar una tienda y seguir su URL actual.</summary>
    private Tracker.Controls.ViewModels.WebViewViewModel? _webView;

    /// <summary>ViewModel al que estamos suscritos (para refrescar la selección de tienda al cambiar <c>Stores</c>).</summary>
    private PriceChartViewModel? _subscribedViewModel;

    /// <summary>Evita la reentrada al sincronizar la selección de la lista de tiendas con la URL del navegador.</summary>
    private bool _syncingStoreSelection;
    #endregion

    #region Constructor
    public PriceChartControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Store list <-> browser sync
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _webView = App.GetService<Tracker.Controls.ViewModels.WebViewViewModel>();
        _webView.PropertyChanged += OnWebViewPropertyChanged;

        _subscribedViewModel = ViewModel;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Selección inicial: la tienda cuya URL corresponde a la ya abierta en el navegador.
        SyncStoreSelectionToBrowser();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_webView is not null)
            _webView.PropertyChanged -= OnWebViewPropertyChanged;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    /// <summary>Cambió la URL abierta en el navegador: refleja en la lista la tienda correspondiente.</summary>
    private void OnWebViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Tracker.Controls.ViewModels.WebViewViewModel.CurrentUrl))
            SyncStoreSelectionToBrowser();
    }

    /// <summary>Cambió la lista de tiendas (otro producto / refresco): re-sincroniza la selección tras rehacerse la lista.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PriceChartViewModel.Stores))
            DispatcherQueue.TryEnqueue(SyncStoreSelectionToBrowser);
    }

    /// <summary>
    /// Selecciona en la lista la tienda cuya URL apunta al mismo host que la abierta en el navegador (o ninguna si no
    /// coincide). No propaga de vuelta una navegación (guard).
    /// </summary>
    private void SyncStoreSelectionToBrowser()
    {
        if (ViewModel is null || _webView is null)
            return;

        StoreChip? match = ViewModel.Stores.FirstOrDefault(chip => SameHost(chip.Url, _webView.CurrentUrl));

        _syncingStoreSelection = true;
        StoresList.SelectedItem = match;
        _syncingStoreSelection = false;
    }

    /// <summary>
    /// Captura la rueda del ratón sobre la lista de tiendas: la desplaza ella misma y marca el evento como tratado, para
    /// que NO se propague y termine desplazando la lista de productos (o el contenedor que haya detrás).
    /// </summary>
    private void OnStoresListPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        ScrollViewer? scroll = FindDescendantScrollViewer(StoresList);
        if (scroll is not null)
        {
            int delta = e.GetCurrentPoint(StoresList).Properties.MouseWheelDelta;
            scroll.ChangeView(null, scroll.VerticalOffset - delta, null);
        }

        e.Handled = true;
    }

    /// <summary>Primer <see cref="ScrollViewer"/> descendiente de <paramref name="root"/> (el interno de la lista), o null.</summary>
    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
            return scrollViewer;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            ScrollViewer? found = FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>El usuario seleccionó una tienda de la lista: abre ESA tienda en el navegador (si no está ya abierta).</summary>
    private void OnStoreSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStoreSelection || _webView is null)
            return;

        if (StoresList.SelectedItem is StoreChip chip && !string.IsNullOrWhiteSpace(chip.Url) && !SameHost(chip.Url, _webView.CurrentUrl))
            _webView.RequestNavigation(chip.Url);
    }
    #endregion

    #region UI events
    /// <summary>Fuerza el refresco de precios del producto mostrado en ESTA página (la del seleccionado o la de un favorito).</summary>
    private async void OnRefreshProductClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null)
            return;

        await App.GetService<ProductService>().RefreshProductAsync(product);

        // Si es el producto SELECCIONADO, al terminar navega a la tienda del mejor precio (si no es ya la abierta).
        if (ViewModel?.SharedDataService.SelectedProduct is Product selected && ReferenceEquals(selected, product))
            NavigateToBestPriceIfNeeded(product);
    }

    /// <summary>Navega en el widget navegador a la tienda del mejor precio del producto, salvo que ya esté abierta (mismo host).</summary>
    private static void NavigateToBestPriceIfNeeded(Product product)
    {
        string? bestUrl = product.BestStore?.Url;
        if (string.IsNullOrWhiteSpace(bestUrl))
            return;

        Tracker.Controls.ViewModels.WebViewViewModel webView = App.GetService<Tracker.Controls.ViewModels.WebViewViewModel>();
        if (!SameHost(bestUrl, webView.CurrentUrl))
            webView.RequestNavigation(bestUrl);
    }

    /// <summary>True si dos URLs apuntan al mismo host (misma tienda/marketplace).</summary>
    private static bool SameHost(string urlA, string? urlB)
    {
        if (string.IsNullOrWhiteSpace(urlB))
            return false;

        return Uri.TryCreate(urlA, UriKind.Absolute, out Uri? a)
            && Uri.TryCreate(urlB, UriKind.Absolute, out Uri? b)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lanza una búsqueda del título del producto en el marketplace de Amazon seleccionado actualmente (el del
    /// navegador), navegando el widget navegador a la página de resultados.
    /// </summary>
    private void OnSearchProductClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null || string.IsNullOrWhiteSpace(product.Name))
            return;

        string country = App.GetService<IOptions<AppSettings>>().Value.WebViewControl.Country;
        string host = Amazon.HostForCountry(country) ?? "www.amazon.es";
        string url = Amazon.SearchUrl(host, product.Name);

        App.GetService<Tracker.Controls.ViewModels.WebViewViewModel>().RequestNavigation(url);
    }

    /// <summary>Edita el título del producto mostrado mediante un diálogo (prerrelleno con el nombre actual).</summary>
    private async void OnEditNameClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null || XamlRoot is null)
            return;

        string? entered = await App.GetService<DialogsService>().PromptAsync(
            XamlRoot,
            L(LocKeys.PriceChart_EditDialog_Title),
            L(LocKeys.PriceChart_EditDialog_Message),
            L(LocKeys.PriceChart_EditName_Placeholder),
            L(LocKeys.Common_Save_Label),
            L(LocKeys.Common_Cancel_Label),
            product.Name);

        if (string.IsNullOrWhiteSpace(entered))
            return;   // cancelado o vacío

        App.GetService<ProductService>().RenameProduct(product, entered);
    }

    /// <summary>
    /// Define (o borra) el precio de alerta del producto mostrado: pide un precio (prerrelleno con el actual o el mejor
    /// precio); vacío = quitar la alerta. Cuando el mejor precio esté en/por debajo, se marca como objetivo alcanzado.
    /// </summary>
    private async void OnSetAlertClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null || XamlRoot is null)
            return;

        string initial = product.AlertPrice is decimal alert
            ? alert.ToString("0.00", CultureInfo.CurrentCulture)
            : product.BestPrice is decimal best ? best.ToString("0.00", CultureInfo.CurrentCulture) : string.Empty;

        string? entered = await App.GetService<DialogsService>().PromptAsync(
            XamlRoot,
            L(LocKeys.PriceChart_AlertDialog_Title),
            L(LocKeys.PriceChart_AlertDialog_Message),
            L(LocKeys.PriceChart_AlertPrice_Placeholder),
            L(LocKeys.Common_Save_Label),
            L(LocKeys.Common_Cancel_Label),
            initial);

        if (entered is null)
            return;   // cancelado

        decimal? newAlert;
        if (string.IsNullOrWhiteSpace(entered))
        {
            newAlert = null;   // vacío: quitar la alerta
        }
        else
        {
            decimal? parsed = ParsePrice(entered);
            if (parsed is null)
                return;   // valor no válido: no se toca la alerta
            newAlert = parsed;
        }

        App.GetService<ProductService>().SetAlertPrice(product, newAlert);
        ViewModel?.RefreshAlertState();
    }

    /// <summary>
    /// Elimina del producto el enlace ACTUAL (la tienda seleccionada en la lista, sincronizada con el navegador; si no
    /// hay selección, la del mejor precio), previa confirmación. No aplica si solo queda un enlace.
    /// </summary>
    private async void OnRemoveLinkClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null || XamlRoot is null || product.ActiveStores.Count() <= 1)
            return;

        ProductStore? store = ResolveCurrentStore(product);
        if (store is null)
            return;

        bool confirmed = await App.GetService<DialogsService>().ConfirmAsync(
            XamlRoot,
            L(LocKeys.PriceChart_RemoveLinkDialog_Title),
            string.Format(L(LocKeys.PriceChart_RemoveLinkDialog_Message), store.Label, product.Name),
            L(LocKeys.Common_Delete_Label),
            L(LocKeys.Common_Cancel_Label));

        if (confirmed)
            App.GetService<ProductService>().RemoveStore(product, store);
    }

    /// <summary>Enlace "actual": la tienda seleccionada en la lista (por URL); si no hay, la del mejor precio o la primera.</summary>
    private ProductStore? ResolveCurrentStore(Product product)
    {
        if (StoresList.SelectedItem is StoreChip chip)
        {
            ProductStore? match = product.Stores.FirstOrDefault(store => string.Equals(store.Url, chip.Url, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        // Solo tiendas ACTIVAS: una tienda oculta (marketplace alternativo desactivado) no se ve en la lista, así que
        // tampoco puede ser el enlace "actual" sobre el que actúan los botones.
        return product.BestStore ?? product.ActiveStores.FirstOrDefault();
    }

    /// <summary>Alterna el favorito del producto mostrado (deshabilitado si ya hay el máximo de favoritos).</summary>
    private void OnToggleFavoriteClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is not null)
            App.GetService<ProductService>().ToggleFavorite(product);
    }

    /// <summary>
    /// Alterna el seguimiento de los marketplaces alternativos (amazon.com / amazon.co.jp) del producto mostrado. No
    /// lanza un refresco: sus precios se leerán en la próxima actualización (manual o del planificador).
    /// </summary>
    private void OnToggleAlternativeStoresClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is not null)
            App.GetService<ProductService>().SetIncludeAlternativeStores(product, !product.IncludeAlternativeStores);
    }

    /// <summary>Borra el producto mostrado de la base de datos, previa confirmación.</summary>
    private async void OnDeleteProductClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null || XamlRoot is null)
            return;

        bool confirmed = await App.GetService<DialogsService>().ConfirmAsync(
            XamlRoot,
            L(LocKeys.PriceChart_DeleteDialog_Title),
            string.Format(L(LocKeys.PriceChart_DeleteDialog_Message), product.Name),
            L(LocKeys.Common_Delete_Label),
            L(LocKeys.Common_Cancel_Label));

        if (confirmed)
            App.GetService<ProductService>().RemoveProduct(product);
    }

    /// <summary>
    /// Alterna el estado "comprado" del producto mostrado. Si ya está comprado, lo revierte (vuelve a comportarse como
    /// el resto). Si no, pide el precio de compra (prerrelleno con el mejor precio actual) y lo marca; cancelar no hace nada.
    /// </summary>
    private async void OnMarkPurchasedClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null || XamlRoot is null)
            return;

        // Revertir: si ya está comprado, se desmarca directamente.
        if (product.IsPurchased)
        {
            App.GetService<ProductService>().SetPurchased(product, false, null);
            return;
        }

        string initial = product.BestPrice is decimal best ? best.ToString("0.00", CultureInfo.CurrentCulture) : string.Empty;

        string? entered = await App.GetService<DialogsService>().PromptAsync(
            XamlRoot,
            L(LocKeys.PriceChart_PurchasedDialog_Title),
            string.Format(L(LocKeys.PriceChart_PurchasedDialog_Message), product.Name),
            L(LocKeys.PriceChart_PurchasePrice_Placeholder),
            L(LocKeys.PriceChart_Purchased_Confirm_Label),
            L(LocKeys.Common_Cancel_Label),
            initial);

        if (entered is null)
            return;

        App.GetService<ProductService>().SetPurchased(product, true, ParsePrice(entered));
    }

    /// <summary>
    /// Parsea un precio escrito a mano aceptando indistintamente el punto y la coma como separador decimal, sea cual
    /// sea la cultura activa ("39.99" y "39,99" valen lo mismo). Ignora espacios, moneda y cualquier otro carácter.
    /// Reglas: si aparecen los dos separadores, el ÚLTIMO es el decimal y el otro se descarta como separador de miles
    /// ("1.234,50" y "1,234.50" son 1234,50); si uno aparece repetido, es separador de miles ("1.234.567"); si solo
    /// aparece una vez, es el decimal, salvo en el único caso ambiguo (tres cifras detrás, como "1.234"), donde manda
    /// la cultura activa. Devuelve null si no queda un número válido.
    /// </summary>
    private static decimal? ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Se queda solo con cifras, signo y separadores: fuera espacios (incluido el duro de "39,99 EUR") y moneda.
        string cleaned = new(text.Where(c => char.IsDigit(c) || c is '.' or ',' or '-' or '+').ToArray());
        if (cleaned.Length == 0)
            return null;

        int dots = cleaned.Count(c => c == '.');
        int commas = cleaned.Count(c => c == ',');
        char? decimalSeparator;

        if (dots > 0 && commas > 0)
            decimalSeparator = cleaned.LastIndexOf('.') > cleaned.LastIndexOf(',') ? '.' : ',';
        else if (dots + commas == 0 || dots > 1 || commas > 1)
            decimalSeparator = null;   // entero, o un separador repetido: solo hay agrupación
        else
        {
            char separator = dots == 1 ? '.' : ',';
            int decimals = cleaned.Length - cleaned.IndexOf(separator) - 1;
            bool ambiguous = decimals == 3
                && separator.ToString() != CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            decimalSeparator = ambiguous ? null : separator;
        }

        string normalized = decimalSeparator is char separatorChar
            ? cleaned.Replace(separatorChar == '.' ? "," : ".", string.Empty).Replace(separatorChar, '.')
            : cleaned.Replace(".", string.Empty).Replace(",", string.Empty);

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;
    #endregion
}
