using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MM4LB.Models;
using MM4LB.Services;
using MM4LB.ViewModels;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using WinRT.Interop;

namespace MM4LB.Views;

/// <summary>
/// Ventana de carga inicial de la aplicación.
/// Gestiona:
/// - El centrado y tamaño inicial mediante <see cref="WindowService"/>.
/// - La generación o carga de la región de recorte basada en un PNG y su persistencia.
/// - La inicialización del <see cref="LoadingWindowViewModel"/>.
/// - La transición visual de entrada (fade‑in).
/// - La escucha de errores globales a través de <see cref="ExceptionService"/>.
/// - La activación de la ventana principal una vez completada la carga.
/// </summary>
public sealed partial class LoadingWindow : Window
{
    #region Attributes
    // Win32 interop para manipulación de regiones de ventana
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("user32.dll")]
    static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    private const int RGN_OR = 2;
    private const string RegionFilePath = "Assets/MM4LB-Splash-Screen.bin";
    private const string PngPath = "Assets/MM4LB-Splash-Screen.png";

    private readonly ExceptionService _exceptionService;
    private readonly LoadingWindowViewModel _viewModel;
    private readonly WindowService _windowService;
    private readonly AppSettings _appSettings;

    private bool _initialized;
    private bool _regionApplied = false;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa la ventana de carga configurando servicios, enlazando el ViewModel,
    /// suscribiendo eventos del AppWindow y registrando el manejador de errores globales.
    /// También establece el DataContext y prepara la desuscripción de eventos al cerrar la ventana.
    /// </summary>
    public LoadingWindow(ExceptionService exceptionService, LoadingWindowViewModel viewModel, WindowService windowService, IOptions<AppSettings> appSettings)
    {
        _exceptionService = exceptionService;
        _viewModel = viewModel;
        _windowService = windowService;
        _appSettings = appSettings.Value;

        InitializeComponent();

        // Obtener AppWindow para eventos de cambio de estado
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Asignar DataContext al elemento raíz
        if (this.Content is FrameworkElement root)
        {
            root.DataContext = _viewModel;
        }


        // Suscribirse a eventos
        _viewModel.LoadCompleted += ViewModel_LoadCompleted;
        appWindow.Changed += AppWindow_Changed;
        Activated += LoadingWindow_Activated;

        // Desuscribirse de eventos al cerrar la ventana para evitar fugas de memoria
        this.Closed += (_, __) =>
        {
            _viewModel.LoadCompleted -= ViewModel_LoadCompleted;
            appWindow.Changed -= AppWindow_Changed;
            Activated -= LoadingWindow_Activated;
        };
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Evento disparado cuando el AppWindow cambia de estado.
    /// Se utiliza para aplicar la región de recorte de la ventana (solo una vez):
    /// - Si existe el archivo binario → cargar y aplicar la región.
    /// - Si no existe → generar la región desde el PNG, persistirla y aplicarla.
    /// </summary>
    private async void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_regionApplied)
            return;

        _regionApplied = true;

        var hwnd = WindowNative.GetWindowHandle(this);

        if (File.Exists(Path.Combine(AppContext.BaseDirectory, RegionFilePath)))
        {
            var region = LoadRegionFromFile(RegionFilePath);
            SetWindowRgn(hwnd, region, true);
        }
        else
        {
            var region = await GenerateRegionFromPngAsync(PngPath);
            SaveRegionToFile(RegionFilePath, region.rects);
            SetWindowRgn(hwnd, region.hRegion, true);
        }
    }

    /// <summary>
    /// Evento de activación de la ventana.
    /// Se ejecuta una sola vez para centrar la ventana mediante WindowService
    /// e iniciar la carga asíncrona del ViewModel.
    /// </summary>
    private async void LoadingWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;

        // El splash se centra en el MISMO monitor donde se va a restaurar la ventana principal (no siempre en el
        // primario): se le pasa como ancla la posición guardada de la principal.
        _windowService.DialogWindow(this, 1800, 920, GetMainWindowAnchorPoint());

        // Marco de neón: se construye ahora que la ventana ya está dimensionada (métricas de ventana/DPI estables).
        BuildFrameBorder();

        // Manejador ÚNICO de errores de arranque. Esto es un async void (handler de evento de ventana):
        // cualquier excepción que escape de InitializeAsync tiraría el proceso en silencio. Todos los fallos
        // duros del arranque (Platforms.xml ausente/corrupto, sin plataformas configuradas...) suben hasta aquí,
        // donde se registran, se muestran con el diálogo propio de la app y se cierra limpiamente.
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            // El arranque ha fallado (Platforms.xml corrupto, sin plataformas configuradas, etc.). Decisión de
            // producto: mostrar un mensaje con el DIÁLOGO PROPIO de la app y cerrar. Llamamos directamente a
            // DialogsService.AlertAsync (el mismo diálogo que muestra ExceptionDialogService) porque es
            // ESPERABLE: ExceptionService.Handle es fire-and-forget/encolado y no podría esperarse antes de
            // Environment.Exit —la única forma segura de cerrar desde el splash (Window.Close() hace fail-fast
            // 0xc000027b)—. El try/catch anidado garantiza que, si el propio diálogo fallara, la app se cierra
            // igualmente en vez de quedarse colgada en el splash.
            ExceptionService.LogToFile(ex, "Error during application initialization (LoadingWindow).");
            try
            {
                var xamlRoot = (Content as FrameworkElement)?.XamlRoot;
                if (xamlRoot is not null)
                {
                    await App.GetService<DialogsService>()
                        .AlertAsync(xamlRoot, "Error", "MM4LB could not start.\n\n" + ex.Message, "Aceptar");
                }
            }
            catch (Exception dialogEx)
            {
                ExceptionService.LogToFile(dialogEx, "Failed to show startup error dialog.");
            }
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Punto de anclaje para centrar el splash en el monitor donde se restaurará la ventana principal: el CENTRO
    /// del rectángulo guardado de la principal (coordenadas absolutas del escritorio virtual, que identifican su
    /// monitor). Se usa el centro y no la esquina porque una ventana maximizada reporta una esquina con un pequeño
    /// offset negativo que cae en el monitor contiguo. Devuelve null si no hay colocación guardada (primer
    /// arranque), de modo que el splash se centre en el monitor primario, igual que hará la ventana principal.
    /// </summary>
    private PointInt32? GetMainWindowAnchorPoint()
    {
        var window = _appSettings.Window;
        return window is { HasSavedPlacement: true }
            ? new PointInt32(window.X + window.Width / 2, window.Y + window.Height / 2)
            : null;
    }

    /// <summary>
    /// Evento disparado cuando el ViewModel completa la carga.
    /// Activa la ventana principal, registra la lógica de apagado del host
    /// al cerrar dicha ventana y finalmente cierra la ventana de carga.
    /// </summary>
    private void ViewModel_LoadCompleted(object? sender, EventArgs e)
    {
        var mainWindow = App.GetService<MainWindow>();

        // Apagado del host + salida del proceso al cerrar la ventana principal. La salida explícita es necesaria
        // porque el splash NO se cierra (se OCULTA, ver abajo): una ventana oculta pero abierta impediría que la
        // app termine sola. NO se usa Application.Current.Exit(): este handler corre dentro del WndProc
        // personalizado (WindowService.CustomWndProc) y Application.Exit() ahí hace fail-fast 0xc000027b. Se usa
        // Environment.Exit, que termina el proceso sin pasar por ese camino de WinUI. La config ya se persistió
        // en host.StopAsync().
        mainWindow.Closed += async (_, _) =>
        {
            var app = App.Current as App;
            if (app is not null)
            {
                try { await app.Host.StopAsync(); } catch (Exception ex) { ExceptionService.LogToFile(ex, "Error stopping the host on shutdown (settings may not have been saved)."); }
                try { app.Host.Dispose(); } catch (Exception ex) { ExceptionService.LogToFile(ex, "Error disposing the host on shutdown."); }
            }
            Environment.Exit(0);
        };

        // Window.Close() sobre este splash hace fail-fast nativo 0xc000027b (WinAppSDK 1.8), sin importar el
        // timing (probado: síncrono, diferido, y tras el Activated de la principal). En su lugar OCULTAMOS el
        // splash con AppWindow.Hide(), que es benigno. Se hace al activarse la principal (sin parpadeo) y diferido.
        void HideSplashOnce(object s, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            mainWindow.Activated -= HideSplashOnce;
            DispatcherQueue.TryEnqueue(() =>
            {
                try { AppWindow.Hide(); } catch { }
            });
        }

        mainWindow.Activated += HideSplashOnce;
        mainWindow.Activate();
    }

    #endregion

    #region Methods (private)
    /// <summary>
    /// Construye el marco de neón del splash con el contorno EXACTO de la región de recorte: lee los mismos rects por
    /// scanline del <c>MM4LB-Splash-Screen.bin</c> (los que aplica <see cref="SetWindowRgn"/>), calcula por fila el
    /// borde izquierdo/derecho de la silueta y traza un polígono cerrado (bajando por la izquierda y subiendo por la
    /// derecha), que reproduce las esquinas redondeadas. Va en el espacio de la ventana (Canvas 1800x920); el Viewbox
    /// (Fill) lo mapea sobre el cliente.
    /// </summary>
    private void BuildFrameBorder()
    {
        try
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, RegionFilePath);
            if (!File.Exists(fullPath))
                return;

            var left = new SortedDictionary<int, int>();
            var right = new SortedDictionary<int, int>();
            var segments = new Dictionary<int, int>();   // nº de tramos por fila (para descartar el fleco antialias)

            using (var fs = File.OpenRead(fullPath))
            using (var br = new BinaryReader(fs))
            {
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int l = br.ReadInt32();
                    int t = br.ReadInt32();
                    int r = br.ReadInt32();
                    _ = br.ReadInt32(); // bottom (t+1), no se usa

                    segments[t] = segments.TryGetValue(t, out int sc) ? sc + 1 : 1;

                    // Silueta EXTERIOR: mínimo izquierdo y máximo derecho por fila.
                    if (!left.TryGetValue(t, out int cl) || l < cl) left[t] = l;
                    if (!right.TryGetValue(t, out int cr) || r > cr) right[t] = r;
                }
            }

            // Descarta las filas FRAGMENTADAS (el borde antialias inferior aparece como decenas de tramos diminutos en
            // una fila; su min/max falsea el contorno y "rompía" el borde por abajo). El resto son filas de 1 tramo.
            var fragmented = new List<int>();
            foreach (var kv in segments) if (kv.Value > 1) fragmented.Add(kv.Key);
            foreach (int t in fragmented) { left.Remove(t); right.Remove(t); }

            if (left.Count == 0)
                return;

            // Transform coords de REGIÓN (píxeles de VENTANA física, que es donde SetWindowRgn recorta) -> coords de
            // CLIENTE (DIP, el espacio del XAML): clientDIP = (regionPx - origenClienteEnVentana) / rasterizationScale.
            // Con esto el contorno del trazo cae EXACTAMENTE sobre el borde de recorte a cualquier DPI, y la propia
            // región recorta la mitad exterior del trazo dejándolo a ras en todo el perímetro (sin offset manual).
            var hwnd = WindowNative.GetWindowHandle(this);
            GetWindowRect(hwnd, out RECT windowRect);
            POINT clientOrigin = new() { x = 0, y = 0 };
            ClientToScreen(hwnd, ref clientOrigin);
            double frameLeft = clientOrigin.x - windowRect.left;
            double frameTop = clientOrigin.y - windowRect.top;
            double scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;

            Windows.Foundation.Point Map(int rx, int ry) =>
                new Windows.Foundation.Point((rx - frameLeft) / scale, (ry - frameTop) / scale);

            var ys = new List<int>(left.Keys);   // filas en orden ascendente
            var points = new List<Windows.Foundation.Point>(ys.Count * 2);
            for (int i = 0; i < ys.Count; i++) points.Add(Map(left[ys[i]], ys[i]));          // izquierda: arriba -> abajo
            for (int i = ys.Count - 1; i >= 0; i--) points.Add(Map(right[ys[i]], ys[i]));    // derecha: abajo -> arriba

            FrameGlow.Data = BuildClosedGeometry(points);
            FrameLine.Data = BuildClosedGeometry(points);
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not build the splash frame border.");
        }
    }

    /// <summary>Crea una <see cref="Microsoft.UI.Xaml.Media.PathGeometry"/> cerrada a partir de una lista de puntos.</summary>
    private static Microsoft.UI.Xaml.Media.Geometry BuildClosedGeometry(List<Windows.Foundation.Point> points)
    {
        var figure = new Microsoft.UI.Xaml.Media.PathFigure
        {
            StartPoint = points[0],
            IsClosed = true
        };
        for (int i = 1; i < points.Count; i++)
            figure.Segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = points[i] });

        var geometry = new Microsoft.UI.Xaml.Media.PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>
    /// Genera una región HRGN a partir de un PNG analizando su canal alfa.
    /// Recorre la imagen línea por línea detectando segmentos opacos y construyendo
    /// la región mediante rectángulos. Devuelve el HRGN final y la lista de rectángulos generados.
    /// </summary>
    private async Task<(IntPtr hRegion, List<(int L, int T, int R, int B)> rects)> GenerateRegionFromPngAsync(string relativePath)
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("PNG not found", fullPath);

        using var fileStream = File.OpenRead(fullPath);
        using var ras = fileStream.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(ras);
        var pixels = await decoder.GetPixelDataAsync();
        byte[] data = pixels.DetachPixelData();

        int width = (int)decoder.PixelWidth;
        int height = (int)decoder.PixelHeight;

        IntPtr finalRegion = CreateRectRgn(0, 0, 0, 0);
        int stride = width * 4;

        var rects = new List<(int L, int T, int R, int B)>();

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            int startX = -1;

            for (int x = 0; x < width; x++)
            {
                byte alpha = data[row + x * 4 + 3];

                if (alpha > 10)
                {
                    if (startX < 0)
                        startX = x;
                }
                else
                {
                    if (startX >= 0)
                    {
                        rects.Add((startX, y, x, y + 1));
                        IntPtr rect = CreateRectRgn(startX, y, x, y + 1);
                        CombineRgn(finalRegion, finalRegion, rect, RGN_OR);
                        startX = -1;
                    }
                }
            }

            if (startX >= 0)
            {
                rects.Add((startX, y, width, y + 1));
                IntPtr rect = CreateRectRgn(startX, y, width, y + 1);
                CombineRgn(finalRegion, finalRegion, rect, RGN_OR);
            }
        }

        return (finalRegion, rects);
    }

    /// <summary>
    /// Carga una región HRGN desde un archivo binario previamente generado.
    /// </summary>
    private IntPtr LoadRegionFromFile(string relativePath)
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);

        using var fs = File.OpenRead(fullPath);
        using var br = new BinaryReader(fs);

        int count = br.ReadInt32();

        IntPtr finalRegion = CreateRectRgn(0, 0, 0, 0);

        for (int i = 0; i < count; i++)
        {
            int L = br.ReadInt32();
            int T = br.ReadInt32();
            int R = br.ReadInt32();
            int B = br.ReadInt32();

            IntPtr rect = CreateRectRgn(L, T, R, B);
            CombineRgn(finalRegion, finalRegion, rect, RGN_OR);
        }

        return finalRegion;
    }

    /// <summary>
    /// Guarda la lista de rectángulos que componen la región en un archivo binario.
    /// </summary>
    private void SaveRegionToFile(string relativePath, List<(int L, int T, int R, int B)> rects)
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var fs = File.Open(fullPath, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        bw.Write(rects.Count);

        foreach (var r in rects)
        {
            bw.Write(r.L);
            bw.Write(r.T);
            bw.Write(r.R);
            bw.Write(r.B);
        }
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Espera a que la imagen tintada esté lista, activa la ventana
    /// y ejecuta la animación de fade‑in del overlay.
    /// </summary>
    public async void PrepareAndShowWhenReady()
    {
        var tcs = new TaskCompletionSource<bool>();

        TintedImage.ImageReady += (_, __) =>
        {
            tcs.TrySetResult(true);
        };

        await tcs.Task;

        // Activar la ventana cuando todo está listo
        this.Activate();

        // Fade-in de ventana usando overlay
        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(1000)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            }
        };

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(fade);

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, FadeOverlay);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");

        storyboard.Begin();
    }
    #endregion
}