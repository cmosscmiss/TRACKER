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
