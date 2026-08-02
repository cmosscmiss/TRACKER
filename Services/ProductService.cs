using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MM4LB.Helpers;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Application-level operations on the tracked products: creating a product from a URL (fanning an Amazon product
/// out to every supported marketplace), reading its prices and removing it, keeping the shared
/// <see cref="SharedDataService.ProductSet"/> (bound to the UI list) and the database in sync. Emits activity-log
/// events for the operations it performs.
/// </summary>
public sealed class ProductService
{
    #region Constants
    /// <summary>Máximo de productos que se pueden marcar como favoritos.</summary>
    public const int MaxFavorites = 10;
    #endregion

    #region Attributes
    private readonly ProductDatabaseService _database;
    private readonly SharedDataService _sharedDataService;
    private readonly ProductParsingService _parsing;
    private readonly ProgressService _progressService;
    #endregion

    #region Constructor
    public ProductService(ProductDatabaseService database, SharedDataService sharedDataService, ProductParsingService parsing, ProgressService progressService)
    {
        _database = database;
        _sharedDataService = sharedDataService;
        _parsing = parsing;
        _progressService = progressService;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Creates a tracked product from a URL. If it is an Amazon product (an ASIN can be found in the URL), it is
    /// fanned out to a store per supported marketplace (es/de/fr/be/nl) built from that ASIN, so a single link
    /// tracks the product in every country; otherwise a single store is created from the URL. Persists it, adds it
    /// to the shared list and selects it. Prices are read afterwards via <see cref="RefreshProductAsync"/>. Returns
    /// the created product, or <c>null</c> if the URL is blank or not a valid http(s) URL.
    /// </summary>
    /// <summary>
    /// Comprueba si ya se rastrea un producto que cubra la URL dada: para Amazon, por ASIN (el mismo artículo en
    /// cualquier marketplace cuenta como duplicado); para otras tiendas, por la URL exacta (sin distinguir
    /// mayúsculas). Las URLs no válidas devuelven false (el alta ya las rechaza aparte).
    /// </summary>
    public bool ContainsProductForUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        string normalized = url.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _))
            return false;

        var products = _sharedDataService.ProductSet.Products;
        string? asin = Amazon.ExtractAsin(normalized);

        if (asin is not null)
            return products.Any(product => product.Stores.Any(store =>
                string.Equals(Amazon.ExtractAsin(store.Url), asin, StringComparison.OrdinalIgnoreCase)));

        return products.Any(product => product.Stores.Any(store =>
            string.Equals(store.Url, normalized, StringComparison.OrdinalIgnoreCase)));
    }

    public Product? AddProductFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        string normalized = url.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var product = new Product();
        string? asin = Amazon.ExtractAsin(normalized);
        if (asin is not null)
        {
            product.Name = $"Amazon {asin}";
            foreach ((string storeUrl, string label) in Amazon.ProductUrlsForAsin(asin))
                product.Stores.Add(new ProductStore(storeUrl, label) { Currency = Amazon.Currency });
        }
        else
        {
            string label = DeriveStoreLabel(uri);
            product.Name = label;
            product.Stores.Add(new ProductStore(normalized, label));
        }

        _database.InsertProduct(product);
        _sharedDataService.ProductSet.AddSorted(product);
        _sharedDataService.SelectedProduct = product;

        // No se registra aquí el evento de "producto añadido": lo hace la operación de refresco (una única entrada
        // que cubre alta + carga de precios) cuando se llama con addedProduct=true.
        return product;
    }

    /// <summary>
    /// Adds the given page as an ALTERNATIVE store link of an existing product (e.g. the same item in another Amazon
    /// marketplace), persists the new store and records its price from the already-extracted <paramref name="parsed"/>
    /// data. Does not touch the product's name/image. Returns the created store, or <c>null</c> if the URL is invalid.
    /// </summary>
    public ProductStore? AddAlternativeLink(Product product, string url, ProductParseResult? parsed)
    {
        if (product is null || string.IsNullOrWhiteSpace(url))
            return null;

        string normalized = url.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var store = new ProductStore(normalized, DeriveStoreLabel(uri));
        product.Stores.Add(store);
        _database.InsertStore(product, store);
        ApplyParsed(product, store, parsed, updateInfo: false, DateTime.UtcNow);

        _progressService.LogEvent(string.Format(L(Helpers.LocKeys.ProductLog_AltLinkAdded_Progress), product.Name));
        return store;
    }

    /// <summary>
    /// Re-reads every store of the product through the browser parser and persists what it finds: the product's
    /// name/image (from the first store that yields them) and a fresh price reading per store. The stores are parsed
    /// CONCURRENTLY (one WebView2 of the pool per store) and then their results are applied sequentially with a single
    /// shared timestamp, so the stores' points line up as columns in the price chart. Must run on the UI thread (the
    /// parser drives WebView2). Stores that fail to parse are skipped. Reports progress to the log and, when
    /// <paramref name="reportGlobalProgress"/> is true (standalone refreshes: add product, per-favourite refresh), to
    /// the footer progress bar; the batch pass (<see cref="PriceSchedulerService"/>) drives that bar itself and passes false.
    /// </summary>
    public async Task RefreshProductAsync(Product product, bool reportGlobalProgress = true, bool addedProduct = false)
    {
        // Los productos comprados no actualizan precios.
        if (product.IsPurchased)
            return;

        // Operación con barra del footer (StartOperation) para los refrescos sueltos (alta, refresco por favorito); la
        // pasada global (PriceSchedulerService) ya lleva la barra, así que sus productos usan solo la entrada de log.
        ProgressNotifier operation = reportGlobalProgress
            ? _progressService.StartOperation()
            : _progressService.StartBackgroundOperation();
        // Alta: la MISMA entrada de log cubre "producto añadido" + la carga de precios (log único).
        operation.Message = addedProduct
            ? string.Format(L(Helpers.LocKeys.ProductLog_Added_Progress), product.Name)
            : string.Format(L(Helpers.LocKeys.ProductLog_Refreshing_Progress), product.Name);
        if (reportGlobalProgress)
            _progressService.ProgressNotifier.Report(operation);

        DateTime timestamp = DateTime.UtcNow;
        var stores = product.Stores.ToList();
        int total = stores.Count;
        int done = 0;

        // Lee todas las tiendas EN PARALELO (una por navegador del pool); la concurrencia real la acota el tamaño del
        // pool. El progreso avanza a medida que cada tienda termina.
        (ProductStore Store, ProductParseResult? Result)[] parsed = await Task.WhenAll(stores.Select(async store =>
        {
            ProductParseResult? result = await _parsing.ParseAsync(store.Url, store.PriceSelector);

            int completed = Interlocked.Increment(ref done);
            operation.Progress = total == 0 ? 100 : (int)(completed * 100.0 / total);
            operation.Message = string.Format(L(Helpers.LocKeys.ProductLog_ReadingStore_Progress), product.Name, store.Label);
            if (reportGlobalProgress)
                _progressService.ProgressNotifier.Report(operation);

            return (store, result);
        }));

        // Aplica los resultados EN SERIE (RecordPrice / BD) con un único timestamp por pasada. Cada tienda va en su
        // propio try/catch: un fallo puntual (p. ej. de BD) NO debe tumbar el refresco entero ni dejar el evento
        // colgado. El bloque finally garantiza que la operación y la barra siempre se cierran.
        int read = 0;
        try
        {
            bool infoUpdated = false;
            foreach ((ProductStore store, ProductParseResult? result) in parsed)
            {
                if (result is null)
                    continue;

                try
                {
                    store.IsPrime = result.IsPrime;
                    store.IsAvailable = result.IsAvailable;
                    store.HasPromo = result.HasPromo;
                    store.IsPreorder = result.IsPreorder;
                    store.ShippingCost = result.ShippingCost;

                    if (!infoUpdated)
                    {
                        if (!string.IsNullOrWhiteSpace(result.Name))
                            product.Name = result.Name!;
                        // Solo se rellena la imagen si el producto NO tiene ya una: así un refresco (p. ej. al fijar el
                        // selector de precio en una web no-Amazon) no machaca la imagen existente (o la elegida a mano).
                        if (string.IsNullOrWhiteSpace(product.ImageUrl) && !string.IsNullOrWhiteSpace(result.ImageUrl))
                            product.ImageUrl = result.ImageUrl;
                        _database.UpdateProductInfo(product);
                        infoUpdated = true;
                    }

                    if (result.Price is decimal price)
                    {
                        store.Currency = ResolveStoreCurrency(store, result.Currency);

                        product.RecordPrice(store, price, timestamp);
                        _database.SavePriceReading(store, price, timestamp);
                        read++;
                    }
                    else
                    {
                        // Sin precio válido (p. ej. producto no disponible): no se registra lectura, pero sí se
                        // persiste el estado leído (disponibilidad / promo / Prime) para que quede al día en disco.
                        _database.UpdateStoreStatus(store, timestamp);
                    }
                }
                catch (Exception ex)
                {
                    ExceptionService.LogToFile(ex, $"Could not persist the price reading for store '{store.Label}' of '{product.Name}'.");
                }
            }
        }
        finally
        {
            // Marca la severidad del evento (el ConsoleControl la colorea vía LogEntrySeverityConverter + el punto de
            // estado): ROJO si no se pudo leer NINGUNA página (fallo total, p. ej. Amazon bloqueó la carga), AMARILLO
            // si la lectura fue parcial (faltan precios: no disponible o alguna tienda falló), y normal si todo OK.
            int parsedOk = parsed.Count(pair => pair.Result is not null);
            if (total > 0 && parsedOk == 0)
                operation.IsException = true;
            else if (read < total)
                operation.IsWarning = true;

            operation.Progress = 100;
            operation.Message = addedProduct
                ? string.Format(L(Helpers.LocKeys.ProductLog_AddedAndRead_Progress), product.Name, read, total)
                : string.Format(L(Helpers.LocKeys.ProductLog_Refreshed_Progress), product.Name, read, total);
            operation.FinishOperation();
            if (reportGlobalProgress)
            {
                _progressService.ProgressNotifier.Report(operation);
                _progressService.FinishOperation();   // oculta la barra si no queda ninguna operación en curso
            }
        }
    }

    /// <summary>Número de productos marcados como favoritos actualmente.</summary>
    public int FavoriteCount => _sharedDataService.ProductSet.Products.Count(product => product.IsFavorite);

    /// <summary>
    /// Alterna el favorito de un producto y lo persiste. No permite superar <see cref="MaxFavorites"/> favoritos
    /// (si ya se alcanzó y el producto no era favorito, no hace nada y devuelve false). Notifica el cambio.
    /// </summary>
    public bool ToggleFavorite(Product product)
    {
        if (product is null)
            return false;

        // Un producto comprado no puede ser favorito.
        if (product.IsPurchased)
            return false;

        if (!product.IsFavorite && FavoriteCount >= MaxFavorites)
            return false;

        product.IsFavorite = !product.IsFavorite;
        _database.SetFavorite(product, product.IsFavorite);
        _sharedDataService.NotifyFavoritesChanged();
        return true;
    }

    /// <summary>Cambia el título (nombre) de un producto y lo persiste. Ignora nombres vacíos.</summary>
    public void RenameProduct(Product product, string newName)
    {
        if (product is null || string.IsNullOrWhiteSpace(newName))
            return;

        product.Name = newName.Trim();
        _database.UpdateProductInfo(product);
    }

    /// <summary>Fija la imagen de un producto (URL elegida por el usuario en una página no-Amazon) y la persiste.</summary>
    public void SetProductImage(Product product, string imageUrl)
    {
        if (product is null || string.IsNullOrWhiteSpace(imageUrl))
            return;

        product.ImageUrl = imageUrl.Trim();
        _database.UpdateProductInfo(product);
    }

    /// <summary>Establece (o borra, con cadena vacía) el selector de precio elegido por el usuario para una tienda, y lo persiste.</summary>
    public void SetPriceSelector(ProductStore store, string? selector)
    {
        if (store is null)
            return;

        store.PriceSelector = string.IsNullOrWhiteSpace(selector) ? null : selector.Trim();
        _database.SetPriceSelector(store, store.PriceSelector);
    }

    /// <summary>Establece (o borra, con null) el precio de alerta de un producto y lo persiste.</summary>
    public void SetAlertPrice(Product product, decimal? alertPrice)
    {
        if (product is null)
            return;

        product.AlertPrice = alertPrice;
        _database.SetAlertPrice(product, alertPrice);
    }

    /// <summary>Removes a product from the shared list and the database (its stores and history cascade).</summary>
    public void RemoveProduct(Product product)
    {
        _database.DeleteProduct(product);
        RemoveFromListSelectingNext(product);

        _progressService.LogEvent(string.Format(L(Helpers.LocKeys.ProductLog_Removed_Progress), product.Name));
    }

    /// <summary>
    /// Marca o desmarca un producto como comprado (toggle). El producto SIGUE en la lista (con el título tachado) pero
    /// sus precios no se actualizan. Al marcarlo se le quita el favorito (un comprado no puede ser favorito); al
    /// revertirlo vuelve a comportarse como el resto. Guarda el precio de compra al marcarlo.
    /// </summary>
    public void SetPurchased(Product product, bool purchased, decimal? purchasePrice)
    {
        if (product is null)
            return;

        bool favoriteCleared = false;
        if (purchased && product.IsFavorite)
        {
            product.IsFavorite = false;
            _database.SetFavorite(product, false);
            favoriteCleared = true;
        }

        product.PurchasePrice = purchased ? purchasePrice : null;
        product.IsPurchased = purchased;
        _database.SetPurchased(product, purchased, purchasePrice);

        if (favoriteCleared)
            _sharedDataService.NotifyFavoritesChanged();

        _progressService.LogEvent(string.Format(
            L(purchased ? Helpers.LocKeys.ProductLog_Purchased_Progress : Helpers.LocKeys.ProductLog_Unpurchased_Progress),
            product.Name));
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Quita el producto de la lista y selecciona el SIGUIENTE (el que ocupa su posición), o el último si era el
    /// último, o ninguno si la lista queda vacía.
    /// </summary>
    private void RemoveFromListSelectingNext(Product product)
    {
        var products = _sharedDataService.ProductSet.Products;
        int index = products.IndexOf(product);
        if (index < 0)
            return;

        products.RemoveAt(index);
        _sharedDataService.SelectedProduct = products.Count == 0
            ? null
            : products[Math.Min(index, products.Count - 1)];
    }

    /// <summary>
    /// Applies a parse result to a product/store: optionally its name/image (from the page) and, if a price was
    /// found, records it (in memory and the database) with the given timestamp. Persists whatever it changes.
    /// </summary>
    private void ApplyParsed(Product product, ProductStore store, ProductParseResult? parsed, bool updateInfo, DateTime timestamp)
    {
        if (parsed is null)
            return;

        store.IsPrime = parsed.IsPrime;
        store.IsAvailable = parsed.IsAvailable;
        store.HasPromo = parsed.HasPromo;
        store.IsPreorder = parsed.IsPreorder;
        store.ShippingCost = parsed.ShippingCost;

        if (updateInfo)
        {
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(parsed.Name))
            {
                product.Name = parsed.Name!;
                changed = true;
            }
            // Solo se rellena la imagen si el producto NO tiene ya una (no machacar la existente / la elegida a mano).
            if (string.IsNullOrWhiteSpace(product.ImageUrl) && !string.IsNullOrWhiteSpace(parsed.ImageUrl))
            {
                product.ImageUrl = parsed.ImageUrl;
                changed = true;
            }
            if (changed)
                _database.UpdateProductInfo(product);
        }

        if (parsed.Price is decimal price)
        {
            store.Currency = ResolveStoreCurrency(store, parsed.Currency);

            product.RecordPrice(store, price, timestamp);
            _database.SavePriceReading(store, price, timestamp);
        }
        else
        {
            _database.UpdateStoreStatus(store, timestamp);
        }
    }

    /// <summary>
    /// Moneda de la tienda: € FIJO para los marketplaces de Amazon (todos eurozona, más fiable que parsear el
    /// símbolo); para otras tiendas, la parseada de la página (o la que ya tuviera si no se pudo parsear).
    /// </summary>
    private static string? ResolveStoreCurrency(ProductStore store, string? parsedCurrency)
    {
        if (Uri.TryCreate(store.Url, UriKind.Absolute, out Uri? uri) && Amazon.IsAmazon(uri))
            return Amazon.Currency;

        return string.IsNullOrWhiteSpace(parsedCurrency) ? store.Currency : parsedCurrency;
    }

    /// <summary>Derives a friendly store label from the URL host, e.g. "www.amazon.es" → "Amazon.es".</summary>
    private static string DeriveStoreLabel(Uri uri)
    {
        string host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];
        if (host.Length == 0)
            return uri.Host;
        return char.ToUpperInvariant(host[0]) + host[1..];
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;
    #endregion
}
