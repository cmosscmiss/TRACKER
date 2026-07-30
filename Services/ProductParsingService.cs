using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace MM4LB.Services;

/// <summary>Result of parsing a product page: the fields we could extract (any may be null).</summary>
public sealed class ProductParseResult
{
    public string? Name { get; init; }
    public string? ImageUrl { get; init; }
    public decimal? Price { get; init; }
    public string? Currency { get; init; }
    public bool IsPrime { get; init; }

    /// <summary>Whether the product is available for purchase (has a buy-box price and is not out of stock).</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Whether the page shows a promotion, deal, coupon or voucher on the product.</summary>
    public bool HasPromo { get; init; }
}

/// <summary>
/// Extracts a product's name, price and image from its (usually Amazon) page by driving a hidden WebView2:
/// navigating to the URL and running an extraction script in the real browser. Using a real Chromium browser
/// (instead of a raw HttpClient) gets the actual rendered page and avoids most of Amazon's plain-scraper blocking.
///
/// A POOL of hidden WebView2 instances lives in the main window; <see cref="Attach"/> hands each one to this
/// singleton once it is initialized. <see cref="ParseAsync"/> leases a free browser from the pool, so several stores
/// of the same product (or several products) can be parsed CONCURRENTLY (one browser per store, up to the pool size),
/// instead of serialising every navigation through a single browser. Callers must invoke <see cref="ParseAsync"/> on
/// the UI thread (WebView2 is UI-thread affine; the concurrency is cooperative async, not extra threads).
/// </summary>
public sealed class ProductParsingService
{
    #region Constants
    private const int NavigationTimeoutSeconds = 30;

    /// <summary>Margen tras NavigationCompleted para que Amazon termine de pintar el precio (a veces es dinámico).</summary>
    private const int SettleDelayMs = 1500;

    /// <summary>
    /// Script de extracción: lee el título, la imagen principal y el precio (por partes estructuradas de Amazon
    /// <c>.a-price</c> y, en su defecto, el texto <c>.a-offscreen</c>). Devuelve un objeto que WebView2 serializa a
    /// JSON. Cualquier campo puede venir null si el selector no existe (layout distinto / página de robot).
    /// </summary>
    private const string ExtractionScript = @"
(function () {
    function txt(sel) { var e = document.querySelector(sel); return e ? e.textContent.trim() : null; }
    var name = txt('#productTitle');
    var img = document.querySelector('#landingImage') || document.querySelector('#imgTagWrapperId img');
    var imageUrl = img ? (img.getAttribute('data-old-hires') || img.currentSrc || img.src) : null;

    // Precio SOLO del buy-box (la oferta principal comprable). Deliberadamente NO se cae a un '.a-price' cualquiera
    // de la página: eso capturaba el precio de 'otras opciones de compra'/otros vendedores cuando el producto no
    // está disponible en la oferta principal.
    var priceEl = document.querySelector('#corePrice_feature_div .a-price')
               || document.querySelector('#corePriceDisplay_desktop_feature_div .a-price')
               || document.querySelector('#price_inside_buybox')
               || document.querySelector('#apex_desktop .a-price')
               || document.querySelector('#buybox .a-price');
    var whole = null, frac = null, symbol = null, offscreen = null;
    if (priceEl) {
        var w = priceEl.querySelector('.a-price-whole'); if (w) whole = w.textContent.replace(/[^0-9]/g, '');
        var f = priceEl.querySelector('.a-price-fraction'); if (f) frac = f.textContent.replace(/[^0-9]/g, '');
        var s = priceEl.querySelector('.a-price-symbol'); if (s) symbol = s.textContent.trim();
        var o = priceEl.querySelector('.a-offscreen'); if (o) offscreen = o.textContent.trim();
        if (!offscreen && priceEl.classList && priceEl.classList.contains('a-price') === false) offscreen = priceEl.textContent.trim();
    }

    var prime = !!(document.querySelector('#corePriceDisplay_desktop_feature_div .a-icon-prime')
                || document.querySelector('#priceBadging_feature_div .a-icon-prime')
                || document.querySelector('#deliveryBlockMessage .a-icon-prime')
                || document.querySelector('.a-icon-prime'));

    // Disponibilidad: hay precio de buy-box y no está marcado como agotado (#outOfStock es el bloque de Amazon
    // 'Actualmente no disponible'). Independiente del idioma del marketplace.
    var outOfStock = !!(document.querySelector('#outOfStock')
                     || document.querySelector('#exports_desktop_outOfStock_buybox'));
    var available = !!priceEl && !outOfStock;

    // Promoción / oferta / cupón / voucher aplicable al producto.
    var hasPromo = !!(document.querySelector('#promoPriceBlockMessage_feature_div')
                   || document.querySelector('.promoPriceBlockMessage')
                   || document.querySelector('#couponFeature')
                   || document.querySelector('[id^=""couponText""]')
                   || document.querySelector('.couponLabelText')
                   || document.querySelector('#vpcButton')
                   || document.querySelector('#dealBadge_feature_div')
                   || document.querySelector('.dealBadge')
                   || document.querySelector('.savingsPercentage'));

    return { name: name, imageUrl: imageUrl, whole: whole, frac: frac, symbol: symbol, offscreen: offscreen, isPrime: prime, available: available, hasPromo: hasPromo };
})();
";
    #endregion

    #region Attributes
    /// <summary>Todos los WebView2 del pool (para consultar disponibilidad). Se rellena con cada <see cref="Attach"/>.</summary>
    private readonly List<WebView2> _all = new();

    /// <summary>Navegadores libres del pool: se saca uno para parsear y se devuelve al terminar.</summary>
    private readonly ConcurrentQueue<WebView2> _available = new();

    /// <summary>Permisos = nº de navegadores del pool; acota la concurrencia de <see cref="ParseAsync"/> al tamaño del pool.</summary>
    private readonly SemaphoreSlim _gate = new(0);
    #endregion

    #region Methods (public)
    /// <summary>Registra un WebView2 inicializado en el pool de scraping. La ventana principal llama una vez por instancia.</summary>
    public void Attach(WebView2 webView)
    {
        _all.Add(webView);
        _available.Enqueue(webView);
        _gate.Release();   // un navegador más disponible
    }

    /// <summary>Whether at least one scraper WebView2 is attached and ready.</summary>
    public bool IsReady => _all.Exists(webView => webView.CoreWebView2 is not null);

    /// <summary>
    /// Navigates the hidden browser to <paramref name="url"/>, waits for it to load and extracts the product
    /// fields. Returns <c>null</c> if the browser is not ready, the URL is invalid, navigation fails/times out or
    /// nothing could be extracted. Must be called on the UI thread (WebView2 is UI-thread affine).
    /// </summary>
    public async Task<ProductParseResult?> ParseAsync(string url)
    {
        if (!IsReady)
            return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        // Toma un navegador libre del pool (espera si todos están ocupados). La concurrencia efectiva la limita el
        // nº de permisos del semáforo (= tamaño del pool), no un hilo por tienda. El timeout evita que un fallo del
        // pool pueda dejar la espera colgada indefinidamente (el llamante trata null como "no leído").
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(90)))
            return null;
        if (!_available.TryDequeue(out WebView2? webView))
        {
            _gate.Release();
            return null;
        }

        try
        {
            if (webView.CoreWebView2 is not CoreWebView2 core)
                return null;

            var navigation = new TaskCompletionSource<bool>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> onCompleted = null!;
            onCompleted = (_, args) =>
            {
                core.NavigationCompleted -= onCompleted;
                navigation.TrySetResult(args.IsSuccess);
            };
            core.NavigationCompleted += onCompleted;
            core.Navigate(uri.ToString());

            Task finished = await Task.WhenAny(navigation.Task, Task.Delay(TimeSpan.FromSeconds(NavigationTimeoutSeconds)));
            if (finished != navigation.Task)
            {
                core.NavigationCompleted -= onCompleted;
                return null;
            }
            if (!navigation.Task.Result)
                return null;

            await Task.Delay(SettleDelayMs);

            string json = await core.ExecuteScriptAsync(ExtractionScript);
            return ParseResult(json);
        }
        catch
        {
            return null;
        }
        finally
        {
            _available.Enqueue(webView);   // devuelve el navegador al pool
            _gate.Release();
        }
    }

    /// <summary>
    /// Extracts the product fields from the page CURRENTLY loaded in <paramref name="webView"/> (no navigation).
    /// Used by the interactive "add" buttons of the visible web browser, which act on the page the user is viewing.
    /// Returns <c>null</c> if the browser is not ready or nothing could be extracted. Must run on the UI thread.
    /// </summary>
    public async Task<ProductParseResult?> ExtractAsync(WebView2 webView)
    {
        if (webView?.CoreWebView2 is not CoreWebView2 core)
            return null;

        try
        {
            string json = await core.ExecuteScriptAsync(ExtractionScript);
            return ParseResult(json);
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Methods (private)
    /// <summary>Turns the JSON returned by the extraction script into a <see cref="ProductParseResult"/>.</summary>
    private static ProductParseResult? ParseResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string? name = GetString(root, "name");
            string? imageUrl = GetString(root, "imageUrl");
            string? symbol = GetString(root, "symbol");
            string? whole = GetString(root, "whole");
            string? frac = GetString(root, "frac");
            string? offscreen = GetString(root, "offscreen");

            string? currency = symbol;
            decimal? price = BuildPrice(whole, frac);
            if (price is null)
                price = TryParsePriceText(offscreen, ref currency);

            bool available = root.TryGetProperty("available", out JsonElement availableElement) && availableElement.ValueKind == JsonValueKind.True;

            // Si el producto no está disponible, se descarta el precio (no se registra ninguna lectura): el precio
            // capturado, si lo hubiera, sería de otras opciones/vendedores, no de la oferta principal.
            if (!available)
                price = null;

            if (name is null && price is null && imageUrl is null)
                return null;

            bool isPrime = root.TryGetProperty("isPrime", out JsonElement primeElement) && primeElement.ValueKind == JsonValueKind.True;
            bool hasPromo = root.TryGetProperty("hasPromo", out JsonElement promoElement) && promoElement.ValueKind == JsonValueKind.True;

            return new ProductParseResult
            {
                Name = name,
                ImageUrl = imageUrl,
                Price = price,
                Currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim(),
                IsPrime = isPrime,
                IsAvailable = available,
                HasPromo = hasPromo
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string property)
        => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Builds a price from Amazon's structured whole/fraction parts (e.g. "39" + "99" → 39.99).</summary>
    private static decimal? BuildPrice(string? whole, string? frac)
    {
        if (string.IsNullOrEmpty(whole))
            return null;

        string fraction = string.IsNullOrEmpty(frac) ? "0" : frac;
        return decimal.TryParse($"{whole}.{fraction}", NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    /// <summary>
    /// Heuristic parse of a formatted price string like "39,99 €", "€39.99" or "$1,299.00". Extracts the currency
    /// symbol into <paramref name="currency"/> and normalizes the number (the last '.'/',' is the decimal separator).
    /// </summary>
    private static decimal? TryParsePriceText(string? text, ref string? currency)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string symbol = new string(text.Where(ch => !char.IsDigit(ch) && ch != '.' && ch != ',' && ch != ' ' && ch != ' ').ToArray()).Trim();
        if (symbol.Length > 0)
            currency = symbol;

        string number = new string(text.Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray());
        if (number.Length == 0)
            return null;

        int lastDot = number.LastIndexOf('.');
        int lastComma = number.LastIndexOf(',');
        char decimalSeparator = lastDot >= lastComma ? '.' : ',';
        char thousandsSeparator = decimalSeparator == '.' ? ',' : '.';

        number = number.Replace(thousandsSeparator.ToString(), string.Empty).Replace(decimalSeparator, '.');
        return decimal.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }
    #endregion
}
