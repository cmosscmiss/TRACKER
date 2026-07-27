using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Periodically refreshes the price of every tracked product, so the price history keeps growing while the app is
/// running (including minimized to the system tray). Prices are read every <see cref="Interval"/> (12 h); on
/// startup it also does a catch-up pass for products whose last reading is older than that (or missing).
///
/// The refresh drives the WebView2 parser, which is UI-thread affine, so the work runs on the UI
/// <see cref="DispatcherQueue"/> (the periodic timer ticks there and the initial pass is started from the UI
/// thread). A re-entrancy guard prevents overlapping passes if one run is still going when the next is due.
/// </summary>
public sealed class PriceSchedulerService
{
    #region Constants
    /// <summary>How often every product's price is re-read.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(12);
    #endregion

    #region Attributes
    private readonly SharedDataService _sharedDataService;
    private readonly ProductService _productService;

    private DispatcherQueueTimer? _timer;
    private bool _running;
    #endregion

    #region Constructor
    public PriceSchedulerService(SharedDataService sharedDataService, ProductService productService)
    {
        _sharedDataService = sharedDataService;
        _productService = productService;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Starts the scheduler on the given UI dispatcher (must be the thread that owns the scraper WebView2). Runs an
    /// immediate catch-up pass for products that are due and then a refresh of everything every 12 h. Idempotent.
    /// </summary>
    public void Start(DispatcherQueue uiDispatcher)
    {
        if (_timer is not null)
            return;

        // Catch-up: refresh anything whose last reading is missing or older than the interval, right after startup.
        _ = RunAsync(dueOnly: true);

        _timer = uiDispatcher.CreateTimer();
        _timer.Interval = Interval;
        _timer.Tick += (_, _) => _ = RunAsync(dueOnly: false);
        _timer.Start();
    }

    /// <summary>Fuerza un refresco inmediato de TODOS los productos (lo mismo que la pasada periódica). Debe llamarse en el hilo de UI.</summary>
    public Task RefreshAllAsync() => RunAsync(dueOnly: false);
    #endregion

    #region Methods (private)
    /// <summary>
    /// Refreshes tracked products through the parser. When <paramref name="dueOnly"/> is true only products with a
    /// store whose last reading is missing/older than the interval are refreshed (used for the startup catch-up);
    /// otherwise every product is refreshed (the periodic pass). Runs sequentially on the UI thread.
    /// </summary>
    private async Task RunAsync(bool dueOnly)
    {
        if (_running)
            return;

        _running = true;
        try
        {
            foreach (Product product in _sharedDataService.ProductSet.Products.ToList())
            {
                if (dueOnly && !IsDue(product))
                    continue;

                await _productService.RefreshProductAsync(product);
            }
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>A product is due if it has a store whose last reading is missing or at least one interval old.</summary>
    private static bool IsDue(Product product)
    {
        if (product.Stores.Count == 0)
            return false;

        DateTime now = DateTime.UtcNow;
        foreach (ProductStore store in product.Stores)
            if (store.LastChecked is not DateTime checkedAt || now - checkedAt >= Interval)
                return true;

        return false;
    }
    #endregion
}
