using System;
using System.Linq;
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
                product.Stores.Add(new ProductStore(storeUrl, label));
        }
        else
        {
            string label = DeriveStoreLabel(uri);
            product.Name = label;
            product.Stores.Add(new ProductStore(normalized, label));
        }

        _database.InsertProduct(product);
        _sharedDataService.ProductSet.Products.Add(product);
        _sharedDataService.SelectedProduct = product;

        _progressService.LogEvent(string.Format(L(Helpers.LocKeys.ProductLog_Added_Progress), product.Name));
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
    /// name/image (from the first store that yields them) and a fresh price reading per store. All readings of one
    /// pass share a single timestamp, so the stores' points line up as columns in the price chart. Must run on the
    /// UI thread (the parser drives the WebView2). Stores that fail to parse are skipped. Reports progress to the log.
    /// </summary>
    public async Task RefreshProductAsync(Product product)
    {
        ProgressNotifier operation = _progressService.StartBackgroundOperation();
        operation.Message = string.Format(L(Helpers.LocKeys.ProductLog_Refreshing_Progress), product.Name);

        DateTime timestamp = DateTime.UtcNow;
        var stores = product.Stores.ToList();
        int read = 0;
        bool infoUpdated = false;

        foreach (ProductStore store in stores)
        {
            operation.Message = string.Format(L(Helpers.LocKeys.ProductLog_ReadingStore_Progress), store.Label);

            ProductParseResult? result = await _parsing.ParseAsync(store.Url);
            if (result is null)
                continue;

            store.IsPrime = result.IsPrime;
            store.IsAvailable = result.IsAvailable;
            store.HasPromo = result.HasPromo;

            if (!infoUpdated)
            {
                if (!string.IsNullOrWhiteSpace(result.Name))
                    product.Name = result.Name!;
                if (!string.IsNullOrWhiteSpace(result.ImageUrl))
                    product.ImageUrl = result.ImageUrl;
                _database.UpdateProductInfo(product);
                infoUpdated = true;
            }

            if (result.Price is decimal price)
            {
                if (!string.IsNullOrWhiteSpace(result.Currency))
                    store.Currency = result.Currency;

                product.RecordPrice(store, price, timestamp);
                _database.SavePriceReading(store, price, timestamp);
                read++;
            }
            else
            {
                // Sin precio válido (p. ej. producto no disponible): no se registra lectura, pero sí se persiste el
                // estado leído (disponibilidad / promo / Prime) para que quede al día en disco.
                _database.UpdateStoreStatus(store, timestamp);
            }
        }

        operation.Message = string.Format(L(Helpers.LocKeys.ProductLog_Refreshed_Progress), product.Name, read, stores.Count);
        operation.FinishOperation();
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

        if (!product.IsFavorite && FavoriteCount >= MaxFavorites)
            return false;

        product.IsFavorite = !product.IsFavorite;
        _database.SetFavorite(product, product.IsFavorite);
        _sharedDataService.NotifyFavoritesChanged();
        return true;
    }

    /// <summary>Removes a product from the shared list and the database (its stores and history cascade).</summary>
    public void RemoveProduct(Product product)
    {
        _database.DeleteProduct(product);
        RemoveFromListSelectingNext(product);

        _progressService.LogEvent(string.Format(L(Helpers.LocKeys.ProductLog_Removed_Progress), product.Name));
    }

    /// <summary>
    /// Marks a product as purchased (storing the purchase price if given): it is kept in the database for the record
    /// but removed from the shared list so it no longer appears among the tracked products.
    /// </summary>
    public void MarkPurchased(Product product, decimal? purchasePrice)
    {
        _database.MarkPurchased(product, purchasePrice);
        RemoveFromListSelectingNext(product);

        _progressService.LogEvent(string.Format(L(Helpers.LocKeys.ProductLog_Purchased_Progress), product.Name));
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

        if (updateInfo)
        {
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(parsed.Name))
            {
                product.Name = parsed.Name!;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(parsed.ImageUrl))
            {
                product.ImageUrl = parsed.ImageUrl;
                changed = true;
            }
            if (changed)
                _database.UpdateProductInfo(product);
        }

        if (parsed.Price is decimal price)
        {
            if (!string.IsNullOrWhiteSpace(parsed.Currency))
                store.Currency = parsed.Currency;

            product.RecordPrice(store, price, timestamp);
            _database.SavePriceReading(store, price, timestamp);
        }
        else
        {
            _database.UpdateStoreStatus(store, timestamp);
        }
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
