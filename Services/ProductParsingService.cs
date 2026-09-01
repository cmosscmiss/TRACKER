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

namespace Tracker.Services;

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

    /// <summary>Whether the product is a pre-order (not yet released; available to reserve).</summary>
    public bool IsPreorder { get; init; }

    /// <summary>Coste de envío detectado (Amazon), o <c>null</c> si es gratis/desconocido. Best-effort (depende de dirección/idioma).</summary>
    public decimal? ShippingCost { get; init; }

    /// <summary>Texto CRUDO capturado del elemento del precio (para diagnóstico del selector personalizado).</summary>
    public string? RawPriceText { get; init; }
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

    /// <summary>
    /// Tope para el script de extracción. <c>ExecuteScriptAsync</c> no vuelve NUNCA si la página deja de responder
    /// (pestaña bloqueada, renderer colgado), y esa espera se propaga hasta el planificador: su pasada de precios
    /// seguiría "en curso" para siempre y la cuenta atrás del footer se quedaría clavada. Al vencer el tope, la
    /// tienda se trata como "no leída".
    /// </summary>
    private const int ScriptTimeoutSeconds = 30;

    /// <summary>Margen tras NavigationCompleted para que Amazon termine de pintar el precio (a veces es dinámico).</summary>
    private const int SettleDelayMs = 1500;

    /// <summary>
    /// Script de extracción: lee el título, la imagen principal y el precio (por partes estructuradas de Amazon
    /// <c>.a-price</c> y, en su defecto, el texto <c>.a-offscreen</c>). Devuelve un objeto que WebView2 serializa a
    /// JSON. Cualquier campo puede venir null si el selector no existe (layout distinto / página de robot).
    /// </summary>
    private const string ExtractionScriptTemplate = @"
(function () {
    var customSel = __PRICE_SELECTOR__;   // selector CSS de precio elegido por el usuario (o null)
    function txt(sel) { var e = document.querySelector(sel); return e ? e.textContent.trim() : null; }
    function meta(sel) { var e = document.querySelector(sel); return e ? (e.getAttribute('content') || '').trim() : null; }
    // Meta de Open Graph / Twitter por propiedad (property o name); estándar de facto en tiendas no-Amazon.
    function metaProp(p) { return meta('meta[property=""' + p + '""]') || meta('meta[name=""' + p + '""]'); }
    function absUrl(u) { if (!u) return null; try { return new URL(u, document.baseURI).href; } catch (e) { return u; } }

    // Título: el de Amazon y, si no, og:title / <title> (mismo criterio para páginas no-Amazon).
    var name = txt('#productTitle') || metaProp('og:title') || (document.title ? document.title.trim() : null) || null;

    // Imagen: la principal de Amazon y, si no, og:image / twitter:image (páginas no-Amazon), resuelta a URL absoluta.
    var img = document.querySelector('#landingImage') || document.querySelector('#imgTagWrapperId img');
    var imageUrl = img ? (img.getAttribute('data-old-hires') || img.currentSrc || img.src) : (metaProp('og:image') || metaProp('twitter:image'));
    imageUrl = absUrl(imageUrl);

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

    // Disponibilidad: hay precio de buy-box, no está marcado como agotado (#outOfStock es el bloque de Amazon
    // 'Actualmente no disponible') y la oferta principal se puede COMPRAR. Independiente del idioma del marketplace.
    var outOfStock = !!(document.querySelector('#outOfStock')
                     || document.querySelector('#exports_desktop_outOfStock_buybox'));

    // Sin oferta destacada (Amazon deja todo lo comprable detrás de 'Ver todas las opciones de compra': otros
    // vendedores, segunda mano...) NO hay botón de compra, pero la página SIGUE dejando algún .a-price suelto en los
    // contenedores amplios de arriba (#apex_desktop / #buybox); así se colaba el precio de un artículo USADO como si
    // fuera el del producto nuevo, y se registraba día tras día. Por eso se exige además un control de compra real.
    // Solo se comprueba en páginas de Amazon: las demás no tienen estos ids y van por el selector personalizado.
    var isAmazonPage = !!(document.getElementById('productTitle')
                       || document.getElementById('buybox')
                       || document.getElementById('desktop_buybox'));
    var canBuy = !!(document.getElementById('add-to-cart-button')
                 || document.getElementById('buy-now-button')
                 || document.getElementById('preorder_now_button')
                 || document.getElementById('one-click-button'));
    var available = !!priceEl && !outOfStock && (!isAmazonPage || canBuy);

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

    // Pre-order (reserva): botón/mensaje de pre-order del buy-box. Detección estructural + texto multi-idioma
    // (en/es/de/fr/nl, incluye Bélgica). Ej.: 'Pre-order', 'Reservar', 'Vorbestellen', 'Précommander', 'Vooraf bestellen'.
    var preRegex = /pre-?order|reserva|vorbestell|pr[eé]command|vooraf\s*bestell|reserveer/i;
    var buyboxText = ((document.getElementById('buybox') || document.getElementById('desktop_buybox') || {}).textContent || '');
    var atc = document.getElementById('add-to-cart-button');
    var isPreorder = !!(document.getElementById('preorderMessage')
                     || document.getElementById('preorder_now_button')
                     || document.querySelector('[id*=preorder]')
                     || (atc && preRegex.test(atc.value || ''))
                     || preRegex.test(buyboxText));

    // Coste de envío (Amazon), best-effort: usa el atributo estructurado del bloque de entrega si existe y, si no, su
    // texto. Envío gratis (o desconocido) => ''; con coste => el importe (la app lo parsea). Solo si hay buy-box.
    function shippingCost() {
        var el = document.querySelector('[data-csa-c-delivery-price]');
        var text = el ? (el.getAttribute('data-csa-c-delivery-price') || '') : '';
        if (!text) {
            var db = document.getElementById('mir-layout-DELIVERY_BLOCK')
                  || document.getElementById('deliveryBlockMessage')
                  || document.getElementById('price-shipping-message');
            text = db ? db.textContent : '';
        }
        if (!text) return '';
        if (/gratis|free|kostenlos|gratuit/i.test(text)) return '';   // envío gratis => sin pastilla
        var m = text.match(/(?:eur|€|\$|£)\s*([0-9]+(?:[.,][0-9]{1,2})?)|([0-9]+(?:[.,][0-9]{1,2})?)\s*(?:eur|€)/i);
        return m ? (m[1] || m[2]) : '';
    }
    var shipping = available ? shippingCost() : '';

    // Selector definido por el usuario (páginas no-Amazon): si existe el elemento, su texto ES el precio; disponible
    // = que el elemento exista. Tiene prioridad sobre la extracción integrada.
    if (customSel) {
        var ce = document.querySelector(customSel);
        if (ce) {
            // textContent (fiable en el navegador oculto del pool, que mide 1x1: innerText podría venir vacío). Si el
            // elemento contuviera varios precios, el lado C# (TryParsePriceText) coge el PRIMER número, no la mezcla.
            offscreen = ce.textContent.trim();
            whole = null; frac = null; symbol = null;
            available = true;
        } else {
            available = false;
        }
    }

    return { name: name, imageUrl: imageUrl, whole: whole, frac: frac, symbol: symbol, offscreen: offscreen, isPrime: prime, available: available, hasPromo: hasPromo, isPreorder: isPreorder, shipping: shipping };
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
    public async Task<ProductParseResult?> ParseAsync(string url, string? priceSelector = null)
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

            return await RunExtractionAsync(core, priceSelector);
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
    public async Task<ProductParseResult?> ExtractAsync(WebView2 webView, string? priceSelector = null)
    {
        if (webView?.CoreWebView2 is not CoreWebView2 core)
            return null;

        try
        {
            return await RunExtractionAsync(core, priceSelector);
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Ejecuta el script de extracción en la página ya cargada, con el tope de <see cref="ScriptTimeoutSeconds"/>
    /// (ver el porqué allí). Devuelve <c>null</c> si el script no responde a tiempo o no extrajo nada.
    /// </summary>
    private static async Task<ProductParseResult?> RunExtractionAsync(CoreWebView2 core, string? priceSelector)
    {
        Task<string> script = core.ExecuteScriptAsync(BuildExtractionScript(priceSelector)).AsTask();
        if (await Task.WhenAny(script, Task.Delay(TimeSpan.FromSeconds(ScriptTimeoutSeconds))) != script)
            return null;

        return ParseResult(script.Result);
    }

    /// <summary>Construye el script de extracción inyectando el selector de precio del usuario (o <c>null</c>) como literal JS seguro.</summary>
    private static string BuildExtractionScript(string? priceSelector)
        => ExtractionScriptTemplate.Replace("__PRICE_SELECTOR__", JsonSerializer.Serialize(priceSelector));

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
            bool isPreorder = root.TryGetProperty("isPreorder", out JsonElement preorderElement) && preorderElement.ValueKind == JsonValueKind.True;
            decimal? shippingCost = ParseShipping(GetString(root, "shipping"));

            return new ProductParseResult
            {
                Name = name,
                ImageUrl = imageUrl,
                Price = price,
                Currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim(),
                IsPrime = isPrime,
                IsAvailable = available,
                HasPromo = hasPromo,
                IsPreorder = isPreorder,
                ShippingCost = shippingCost,
                RawPriceText = offscreen
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

    /// <summary>Parsea el importe de envío extraído (p. ej. "3,99"). Devuelve el valor solo si es &gt; 0; en otro caso null (gratis/desconocido).</summary>
    private static decimal? ParseShipping(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string number = new string(text.Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray());
        if (number.Length == 0)
            return null;

        int lastDot = number.LastIndexOf('.');
        int lastComma = number.LastIndexOf(',');
        char decimalSeparator = lastDot >= lastComma ? '.' : ',';
        char thousandsSeparator = decimalSeparator == '.' ? ',' : '.';
        number = number.Replace(thousandsSeparator.ToString(), string.Empty).Replace(decimalSeparator, '.');

        return decimal.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value) && value > 0
            ? value
            : null;
    }

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

        // PRIMERA línea no vacía: cuando el elemento contiene varios precios en bloques (p. ej. el de oferta + el
        // 'regular' oculto), suelen ir en líneas distintas; quedarnos con la primera evita mezclarlos. Dentro de la
        // línea se conserva el tratamiento clásico, que admite separador de miles con punto, coma o espacio.
        string line = text.Replace("\r", "\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? text;
        string number = new string(line.Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray());
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
