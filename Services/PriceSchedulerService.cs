using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;
using MM4LB.Helpers;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Periodically refreshes the price of every tracked product, so the price history keeps growing while the app is
/// running (including minimized to the system tray). Prices are read every <see cref="Interval"/> (configurable, 24 h
/// by default); on startup it also does a catch-up pass for products whose last reading is older than that (or missing).
///
/// The refresh drives the WebView2 parser, which is UI-thread affine, so the work runs on the UI
/// <see cref="DispatcherQueue"/> (the periodic timer ticks there and the initial pass is started from the UI
/// thread). A re-entrancy guard prevents overlapping passes if one run is still going when the next is due.
/// </summary>
public sealed class PriceSchedulerService
{
    #region Attributes
    private readonly SharedDataService _sharedDataService;
    private readonly ProductService _productService;
    private readonly ProgressService _progressService;
    private readonly IOptions<AppSettings> _appSettings;

    private DispatcherQueueTimer? _timer;
    private bool _running;
    #endregion

    #region Properties
    /// <summary>Cada cuánto se re-lee el precio de cada producto (configurable en ajustes; mínimo 1 h).</summary>
    public TimeSpan Interval => TimeSpan.FromHours(Math.Max(1, _appSettings.Value.General.AutoRefreshHours));
    #endregion

    #region Events
    /// <summary>
    /// Se dispara al terminar una actualización TOTAL (manual o automática) con el resumen ya formateado (título +
    /// cuerpo) para mostrar UNA notificación de Windows: resumen (actualizados/bajadas/avisos) + precio de alerta
    /// alcanzado, nuevo mínimo histórico, vuelta a stock y pre-orders publicados. La capa de UI solo la muestra.
    /// </summary>
    public event Action<string, string>? NotificationReady;
    #endregion

    #region Properties
    /// <summary>
    /// Momento (UTC) en que está programada la PRÓXIMA pasada periódica automática, o null si el planificador aún no ha
    /// arrancado. Sirve para mostrar en el footer el tiempo que queda hasta la siguiente actualización de precios.
    /// </summary>
    public DateTime? NextRunUtc { get; private set; }
    #endregion

    #region Constructor
    public PriceSchedulerService(SharedDataService sharedDataService, ProductService productService, ProgressService progressService, IOptions<AppSettings> appSettings)
    {
        _sharedDataService = sharedDataService;
        _productService = productService;
        _progressService = progressService;
        _appSettings = appSettings;
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

        // La primera pasada automática no se ancla al arranque, sino a la ÚLTIMA lectura de precios: si el último
        // refresco fue hace 5 h, la siguiente toca en 7 h (no en 12). Si ya venció (o nunca se leyó), queda a un
        // intervalo completo (el catch-up de abajo refresca ahora lo que esté caducado).
        TimeSpan first = Interval;
        if (LastGlobalUpdateUtc() is DateTime last)
        {
            TimeSpan remaining = last + Interval - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
                first = remaining;
        }

        // Catch-up: refresh anything whose last reading is missing or older than the interval, right after startup.
        _ = RunAsync(dueOnly: true);

        _timer = uiDispatcher.CreateTimer();
        _timer.Interval = first;
        _timer.Tick += (_, _) => _ = RunAsync(dueOnly: false);
        NextRunUtc = DateTime.UtcNow + first;
        _timer.Start();
    }

    /// <summary>Momento (UTC) de la lectura de precio más reciente entre todas las tiendas de todos los productos, o null si ninguna se ha leído.</summary>
    private DateTime? LastGlobalUpdateUtc()
    {
        DateTime? max = null;
        foreach (Product product in _sharedDataService.ProductSet.Products)
            foreach (ProductStore store in product.Stores)
                if (store.LastChecked is DateTime checkedAt && (max is null || checkedAt > max))
                    max = checkedAt;
        return max;
    }

    /// <summary>
    /// Reinicia la cuenta atrás y el temporizador: la siguiente pasada automática queda a un <see cref="Interval"/>
    /// completo desde AHORA. Se llama al lanzar un refresco de TODOS los productos (manual o periódico), para que el
    /// reloj del footer arranque de nuevo desde 12 h.
    /// </summary>
    private void RearmSchedule()
    {
        NextRunUtc = DateTime.UtcNow + Interval;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Interval = Interval;   // el primer intervalo pudo ser menor (anclado al último update); a partir de aquí, 12 h
            _timer.Start();
        }
    }

    /// <summary>Fuerza un refresco inmediato de TODOS los productos (lo mismo que la pasada periódica). Debe llamarse en el hilo de UI.</summary>
    public Task RefreshAllAsync() => RunAsync(dueOnly: false);

    /// <summary>Reaplica el intervalo tras cambiarlo en ajustes: reinicia la cuenta atrás y el temporizador desde ahora.</summary>
    public void ApplyIntervalChange()
    {
        if (_timer is not null)
            RearmSchedule();
    }
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

        // Una pasada COMPLETA (manual desde el botón o la periódica) reinicia el reloj/temporizador hacia la siguiente
        // automática; la de arranque (catch-up, dueOnly) no lo toca (NextRunUtc ya se fija en Start).
        if (!dueOnly)
            RearmSchedule();

        _running = true;
        try
        {
            List<Product> toRefresh = _sharedDataService.ProductSet.Products.ToList();
            if (dueOnly)
                toRefresh = toRefresh.Where(IsDue).ToList();

            if (toRefresh.Count == 0)
                return;

            // Pasada TOTAL: guarda el estado ANTES (mejor precio, si está bajo alerta y si es pre-order) para detectar
            // al terminar los eventos a notificar (bajadas, alerta alcanzada, nuevo mínimo, vuelta a stock, publicado).
            Dictionary<Product, (decimal? Best, bool BelowAlert, bool Preorder)>? before = dueOnly
                ? null
                : toRefresh.ToDictionary(p => p, p => (p.BestPrice, p.IsBelowAlert, p.IsPreorder));

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

            // Resumen de la pasada total, en UNA notificación (resumen + eventos destacados).
            if (before is not null && total > 0)
                BuildAndRaiseNotification(toRefresh, before, total);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>
    /// Compara el estado previo con el actual y compone una única notificación: línea de resumen
    /// (actualizados/bajadas/avisos) + precio de alerta alcanzado, nuevo mínimo histórico, vuelta a stock y pre-orders
    /// publicados. Prioriza lo más relevante primero (el cuerpo se trunca a ~255 car. en el globo del tray).
    /// </summary>
    private void BuildAndRaiseNotification(List<Product> products, Dictionary<Product, (decimal? Best, bool BelowAlert, bool Preorder)> before, int total)
    {
        int drops = 0, issues = 0;
        var alerts = new List<string>();
        var lows = new List<string>();
        var back = new List<string>();
        var released = new List<string>();

        foreach (Product product in products)
        {
            (decimal? oldBest, bool oldBelow, bool oldPreorder) = before[product];
            decimal? now = product.BestPrice;
            string currency = product.BestStore?.Currency ?? string.Empty;

            if (oldBest is decimal ob && now is decimal nb && nb < ob)
                drops++;
            if (product.HasIssues)
                issues++;

            // Precio de alerta recién alcanzado.
            if (!oldBelow && product.IsBelowAlert && now is decimal ap)
                alerts.Add(string.Format(L(LocKeys.Notify_AlertReached_Line), product.Name, FormatPrice(ap, currency)));

            // Nuevo mínimo histórico (bajó respecto a la pasada previa y ahora es el mínimo de todo el histórico).
            if (now is decimal nl && oldBest is decimal obl && nl < obl && product.IsHistoricalLow)
                lows.Add(string.Format(L(LocKeys.Notify_NewLow_Line), product.Name, FormatPrice(nl, currency)));

            // Vuelta a stock: antes sin precio (no disponible), ahora con precio.
            if (oldBest is null && now is not null)
                back.Add(string.Format(L(LocKeys.Notify_BackInStock_Line), product.Name));

            // Pre-order publicado: antes en reserva, ahora ya no.
            if (oldPreorder && !product.IsPreorder)
                released.Add(string.Format(L(LocKeys.Notify_PreorderReleased_Line), product.Name));
        }

        var lines = new List<string> { string.Format(L(LocKeys.Notify_Summary_Line), total, drops, issues) };
        lines.AddRange(alerts);
        lines.AddRange(lows);
        lines.AddRange(back);
        lines.AddRange(released);

        NotificationReady?.Invoke(L(LocKeys.Notify_Summary_Title), string.Join(Environment.NewLine, lines));
    }

    /// <summary>Formatea un precio con su moneda ("39,99 €").</summary>
    private static string FormatPrice(decimal value, string? currency)
    {
        string text = value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(currency) ? text : $"{text} {currency}";
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
    private bool IsDue(Product product)
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
