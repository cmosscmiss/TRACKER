using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Helpers;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

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
    private MM4LB.Controls.ViewModels.WebViewViewModel? _webView;

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
        _webView = App.GetService<MM4LB.Controls.ViewModels.WebViewViewModel>();
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
        if (e.PropertyName == nameof(MM4LB.Controls.ViewModels.WebViewViewModel.CurrentUrl))
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

        MM4LB.Controls.ViewModels.WebViewViewModel webView = App.GetService<MM4LB.Controls.ViewModels.WebViewViewModel>();
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

        App.GetService<MM4LB.Controls.ViewModels.WebViewViewModel>().RequestNavigation(url);
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

    /// <summary>Alterna el favorito del producto mostrado (deshabilitado si ya hay el máximo de favoritos).</summary>
    private void OnToggleFavoriteClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is not null)
            App.GetService<ProductService>().ToggleFavorite(product);
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

    /// <summary>Parsea un precio introducido a mano (cultura actual o invariante), o null si no es válido.</summary>
    private static decimal? ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string value = text.Trim();
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal parsed))
            return parsed;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
            return parsed;
        return null;
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;
    #endregion
}
