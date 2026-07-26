using System;
using System.Linq;
using System.Threading.Tasks;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Application-level operations on the tracked products: creating a product from a URL and removing one, keeping
/// the shared <see cref="SharedDataService.ProductSet"/> (bound to the UI list) and the database in sync.
/// </summary>
public sealed class ProductService
{
    #region Attributes
    private readonly ProductDatabaseService _database;
    private readonly SharedDataService _sharedDataService;
    private readonly ProductParsingService _parsing;
    #endregion

    #region Constructor
    public ProductService(ProductDatabaseService database, SharedDataService sharedDataService, ProductParsingService parsing)
    {
        _database = database;
        _sharedDataService = sharedDataService;
        _parsing = parsing;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Creates a product tracked from the given URL (as its first store), persists it, adds it to the shared
    /// product list and selects it. The name defaults to the store label (host) until the page is parsed. Returns
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

        string label = DeriveStoreLabel(uri);

        var product = new Product { Name = label };
        product.Stores.Add(new ProductStore(normalized, label));

        _database.InsertProduct(product);
        _sharedDataService.ProductSet.Products.Add(product);
        _sharedDataService.SelectedProduct = product;

        return product;
    }

    /// <summary>
    /// Re-reads every store of the product through the browser parser and persists what it finds: the product's
    /// name/image (from the first store that yields them) and a fresh price reading per store (updating the store's
    /// current price and appending to the price history, in memory and in the database). Must run on the UI thread
    /// (the parser drives the WebView2). Stores that fail to parse are skipped.
    /// </summary>
    public async Task RefreshProductAsync(Product product)
    {
        bool infoUpdated = false;

        foreach (ProductStore store in product.Stores.ToList())
        {
            ProductParseResult? result = await _parsing.ParseAsync(store.Url);
            if (result is null)
                continue;

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

                DateTime timestamp = DateTime.UtcNow;
                product.RecordPrice(store, price, timestamp);
                _database.SavePriceReading(store, price, timestamp);
            }
        }
    }

    /// <summary>Removes a product from the shared list and the database (its stores and history cascade).</summary>
    public void RemoveProduct(Product product)
    {
        _database.DeleteProduct(product);
        _sharedDataService.ProductSet.Products.Remove(product);
        if (ReferenceEquals(_sharedDataService.SelectedProduct, product))
            _sharedDataService.SelectedProduct = null;
    }
    #endregion

    #region Methods (private)
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
    #endregion
}
