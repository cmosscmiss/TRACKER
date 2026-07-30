using System;
using System.Globalization;
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

    #region Constructor
    public PriceChartControl()
    {
        InitializeComponent();
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
    /// Marca el producto seleccionado como comprado: pide el precio de compra (prerrelleno con el mejor precio
    /// actual), lo almacena y el producto deja de aparecer en la lista. Cancelar no hace nada.
    /// </summary>
    private async void OnMarkPurchasedClick(object sender, RoutedEventArgs e)
    {
        Product? product = ViewModel?.Product;
        if (product is null || XamlRoot is null)
            return;

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

        App.GetService<ProductService>().MarkPurchased(product, ParsePrice(entered));
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
