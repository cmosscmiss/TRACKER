using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using MM4LB.Helpers;
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
    private readonly ProgressService _progressService;

    private DispatcherQueueTimer? _timer;
    private bool _running;
    #endregion

    #region Constructor
    public PriceSchedulerService(SharedDataService sharedDataService, ProductService productService, ProgressService progressService)
    {
        _sharedDataService = sharedDataService;
        _productService = productService;
        _progressService = progressService;
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
            List<Product> toRefresh = _sharedDataService.ProductSet.Products.ToList();
            if (dueOnly)
                toRefresh = toRefresh.Where(IsDue).ToList();

            if (toRefresh.Count == 0)
                return;

            // Operación global de progreso del refresco de todos los precios: avanza por producto (0..100) e indica
            // el producto en curso. Es esta pasada (StartOperation) la que gobierna la barra del footer vía la
            // propiedad observable, así que cada RefreshProductAsync se llama con reportGlobalProgress:false.
            ProgressNotifier operation = _progressService.StartOperation();
            int total = toRefresh.Count;

            for (int i = 0; i < total; i++)
            {
                Product product = toRefresh[i];
                operation.Message = string.Format(L(LocKeys.ProductLog_RefreshingAll_Progress), product.Name, i + 1, total);
                operation.Progress = (int)(i * 100.0 / total);
                _progressService.ProgressNotifier.Report(operation);

                await _productService.RefreshProductAsync(product, reportGlobalProgress: false);
            }

            operation.Progress = 100;
            operation.Message = string.Format(L(LocKeys.ProductLog_RefreshedAll_Progress), total);
            operation.FinishOperation();
            _progressService.ProgressNotifier.Report(operation);
            _progressService.FinishOperation();
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;

    /// <summary>
    /// Indica si el producto toca refrescarse en la pasada de arranque (catch-up):
    /// - Si NUNCA se ha leído (ninguna tienda con lectura): sí (es nuevo, hay que cargarlo).
    /// - Si ya se leyó alguna vez: solo si alguna tienda YA leída ha caducado (≥ intervalo).
    /// Las tiendas que nunca cargaron (LastChecked nulo) se IGNORAN: no fuerzan un refresco en cada arranque (una
    /// tienda que falla siempre no debe reintentarse indefinidamente; su recarga se deja a la pasada periódica de 12 h).
    /// </summary>
    private static bool IsDue(Product product)
    {
        if (product.Stores.Count == 0)
            return false;

        // Producto nunca cargado: hay que cargarlo.
        if (product.Stores.All(store => store.LastChecked is null))
            return true;

        // Ya cargado alguna vez: due solo si alguna tienda con lectura previa ha caducado.
        DateTime now = DateTime.UtcNow;
        return product.Stores.Any(store => store.LastChecked is DateTime checkedAt && now - checkedAt >= Interval);
    }
    #endregion
}
