using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Tracker.Services;

/// <summary>
/// Servicio centralizado para configurar el comportamiento de las ventanas de la aplicación:
/// tamaño inicial, centrado, estilo de la barra de título, botones del sistema,
/// posibilidad de iniciar maximizada y límites mínimos/máximos de tamaño mediante Win32.
/// </summary>
public class WindowService
{
    #region Nested classes
    /// <summary>
    /// Representa la posición, tamaño y estado visual de una ventana.
    /// </summary>
    public sealed class WindowPlacement
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public bool IsMaximized { get; set; }
    }
    #endregion

    #region Win32 interop (límites de tamaño de la ventana)
    /// <summary>
    /// Mensaje de Windows usado para consultar y establecer los límites de tamaño
    /// (mínimo y máximo) de la ventana a través de la estructura MINMAXINFO.
    /// </summary>
    private const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>
    /// Índice usado por GetWindowLongPtr/SetWindowLongPtr para obtener o establecer
    /// el procedimiento de ventana (WndProc) asociado al HWND.
    /// </summary>
    private const int GWL_WNDPROC = -4;

    /// <summary>
    /// Diccionario que almacena, por HWND, las restricciones de tamaño (mínimo/máximo)
    /// que se aplicarán cuando se procese WM_GETMINMAXINFO.
    /// </summary>
    private static readonly Dictionary<IntPtr, SizeConstraints> _constraints = new();

    /// <summary>
    /// Diccionario que almacena, por HWND, el WndProc original antes de subclasificar
    /// la ventana, para poder encadenar correctamente la llamada y evitar recursión.
    /// </summary>
    private static readonly Dictionary<IntPtr, IntPtr> _originalWndProcs = new();

    /// <summary>
    /// Delegado que representa el nuevo procedimiento de ventana (WndProc) que intercepta
    /// WM_GETMINMAXINFO para aplicar los límites de tamaño configurados.
    /// </summary>
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Referencia estática al delegado de WndProc para evitar que el recolector de basura
    /// lo libere mientras la ventana siga viva.
    /// </summary>
    private static WndProcDelegate? _wndProcDelegate;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    /// <summary>
    /// Estructura inmutable que encapsula los límites de tamaño (mínimo y máximo)
    /// que se desean aplicar a una ventana concreta.
    /// </summary>
    private readonly record struct SizeConstraints(int? MinWidth, int? MinHeight, int? MaxWidth, int? MaxHeight);

    /// <summary>
    /// Procedimiento de ventana personalizado que intercepta WM_GETMINMAXINFO para
    /// aplicar los límites de tamaño configurados y, a continuación, delega en el
    /// WndProc original de la ventana.
    /// </summary>
    private static IntPtr CustomWndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO && _constraints.TryGetValue(hWnd, out var c))
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            if (c.MinWidth.HasValue) mmi.ptMinTrackSize.x = c.MinWidth.Value;
            if (c.MinHeight.HasValue) mmi.ptMinTrackSize.y = c.MinHeight.Value;
            if (c.MaxWidth.HasValue) mmi.ptMaxTrackSize.x = c.MaxWidth.Value;
            if (c.MaxHeight.HasValue) mmi.ptMaxTrackSize.y = c.MaxHeight.Value;

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        // Encadenar siempre con el WndProc original para no romper el comportamiento base.
        if (_originalWndProcs.TryGetValue(hWnd, out var originalProc))
        {
            return CallWindowProc(originalProc, hWnd, msg, wParam, lParam);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Asocia límites mínimos y máximos de tamaño a una ventana concreta, subclasificando
    /// su WndProc una única vez para interceptar WM_GETMINMAXINFO y aplicar las restricciones.
    /// </summary>
    private static void AttachSizeConstraints(Window window, int? minWidth, int? minHeight, int? maxWidth, int? maxHeight)
    {
        var hWnd = WindowNative.GetWindowHandle(window);

        _constraints[hWnd] = new SizeConstraints(minWidth, minHeight, maxWidth, maxHeight);

        // Solo subclasamos una vez por HWND para conservar el WndProc original.
        if (!_originalWndProcs.ContainsKey(hWnd))
        {
            _wndProcDelegate ??= CustomWndProc;
            var newProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

            var prevProc = GetWindowLongPtr(hWnd, GWL_WNDPROC);
            _originalWndProcs[hWnd] = prevProc;

            SetWindowLongPtr(hWnd, GWL_WNDPROC, newProc);
        }
    }
    #endregion


    #region Active window tracking
    /// <summary>
    /// Lista de ventanas registradas en el servicio.
    /// Se utiliza para mantener el seguimiento de las ventanas abiertas de la aplicación
    /// y poder determinar cuál debe considerarse activa en cada momento.
    /// </summary>
    private readonly List<Window> _registeredWindows = new();

    /// <summary>
    /// Evento que se dispara cuando cambia la ventana activa.
    /// Otros servicios pueden suscribirse a este evento para reaccionar cuando haya
    /// una nueva ventana disponible, por ejemplo para mostrar diálogos sobre ella.
    /// </summary>
    public event Action? ActiveWindowChanged;

    /// <summary>
    /// Ventana actualmente activa o última ventana registrada como activa.
    /// Esta propiedad permite a otros servicios acceder a la ventana sobre la que
    /// deben ejecutarse operaciones dependientes de UI, como mostrar un diálogo.
    /// </summary>
    public Window? ActiveWindow
    {
        get; private set;
    }

    /// <summary>
    /// Obtiene el <see cref="XamlRoot"/> asociado a la ventana activa.
    /// Es especialmente útil para mostrar controles como <see cref="ContentDialog"/>,
    /// que necesitan un <see cref="XamlRoot"/> válido para poder presentarse
    /// correctamente en WinUI.
    /// </summary>
    public XamlRoot? ActiveXamlRoot => (ActiveWindow?.Content as FrameworkElement)?.XamlRoot;

    /// <summary>
    /// Registra una ventana en el servicio para poder hacer seguimiento de su estado.
    /// Si la ventana ya estaba registrada, no se vuelve a añadir ni se duplican las
    /// suscripciones a eventos. Al registrarla, se establece inicialmente como ventana
    /// activa para que otros servicios puedan disponer de una referencia válida incluso
    /// antes de que se dispare el evento <see cref="Window.Activated"/>.
    /// </summary>
    /// <param name="window"> Ventana que se desea registrar para seguimiento. </param>
    public void Register(Window window)
    {
        if (_registeredWindows.Exists(w => ReferenceEquals(w, window)))
            return;

        _registeredWindows.Add(window);

        // Útil para que haya una ventana disponible incluso antes del primer Activated.
        ActiveWindow = window;
        ActiveWindowChanged?.Invoke();

        window.Activated += TrackedWindow_Activated;
        window.Closed += TrackedWindow_Closed;
    }

    /// <summary>
    /// Gestiona el evento <see cref="Window.Activated"/> de una ventana registrada.
    /// Cuando una ventana pasa a estar activa, se actualiza la propiedad
    /// <see cref="ActiveWindow"/> y se notifica el cambio mediante
    /// <see cref="ActiveWindowChanged"/>.
    /// 
    /// Si el evento indica que la ventana ha sido desactivada, no se realiza ningún cambio.
    /// </summary>
    /// <param name="sender"> Ventana que ha disparado el evento. </param>
    /// <param name="e"> Argumentos del evento de activación de la ventana. </param>
    private void TrackedWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (e.WindowActivationState == WindowActivationState.Deactivated)
            return;

        ActiveWindow = window;
        ActiveWindowChanged?.Invoke();
    }

    /// <summary>
    /// Gestiona el cierre de una ventana registrada.
    /// Al cerrarse la ventana:
    /// - Se eliminan las suscripciones a sus eventos.
    /// - Se elimina de la lista de ventanas registradas.
    /// - Se limpian las restricciones de tamaño asociadas a su HWND.
    /// - Si era la ventana activa, se selecciona otra ventana registrada como activa,
    ///   o se deja la referencia a <c>null</c> si no queda ninguna.
    /// </summary>
    /// <param name="sender"> Ventana que ha sido cerrada. </param>
    /// <param name="args"> Argumentos del evento de cierre. </param>
    private void TrackedWindow_Closed(object sender, WindowEventArgs args)
    {
        if (sender is not Window window)
            return;

        window.Activated -= TrackedWindow_Activated;
        window.Closed -= TrackedWindow_Closed;

        _registeredWindows.RemoveAll(w => ReferenceEquals(w, window));

        DetachSizeConstraints(window);

        if (ReferenceEquals(ActiveWindow, window))
        {
            ActiveWindow = _registeredWindows.Count > 0
                ? _registeredWindows[^1]
                : null;

            ActiveWindowChanged?.Invoke();
        }
    }

    /// <summary>
    /// Elimina las restricciones de tamaño asociadas a una ventana y restaura su WndProc original.
    /// Este método limpia la información almacenada para el HWND de la ventana en los
    /// diccionarios internos usados por la subclasificación Win32. Si la ventana tenía
    /// un WndProc original guardado, se restaura para evitar dejar la ventana apuntando
    /// al procedimiento personalizado después de cerrarse.
    /// </summary>
    /// <param name="window"> Ventana cuyas restricciones de tamaño deben limpiarse. </param>
    private static void DetachSizeConstraints(Window window)
    {
        var hWnd = WindowNative.GetWindowHandle(window);

        if (_originalWndProcs.TryGetValue(hWnd, out var originalProc))
        {
            try
            {
                SetWindowLongPtr(hWnd, GWL_WNDPROC, originalProc);
            }
            catch
            {
                // Evitar que un fallo de limpieza rompa el cierre de la ventana.
            }

            _originalWndProcs.Remove(hWnd);
        }

        _constraints.Remove(hWnd);
    }

    #endregion

    #region Methods (public)
    /// <summary>
    /// Aplica a una ventana una posición, tamaño y estado previamente guardados.
    /// </summary>
    /// <param name="window">
    /// Ventana a configurar.
    /// </param>
    /// <param name="placement">
    /// Posición, tamaño y estado que se desea aplicar.
    /// </param>
    public void ApplyWindowPlacement(Window window, WindowPlacement placement)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // El monitor de destino es el que CONTIENE la ventana guardada: las coordenadas son absolutas del
        // escritorio virtual, así que identifican el monitor donde se cerró la app (sin esto, una ventana cerrada
        // en un monitor secundario se recortaba al área del primario y "saltaba" a él al reiniciar). Se usa el
        // CENTRO del rectángulo guardado, no la esquina: una ventana MAXIMIZADA reporta una esquina con un pequeño
        // offset negativo (~-8px) que cae en el monitor contiguo y devolvía el monitor equivocado (típicamente el
        // primario); el centro siempre cae dentro del monitor correcto. GetFromPoint con None devuelve null si ese
        // centro ya NO cae en ningún monitor (el monitor se ha desconectado): entonces, fallback al monitor activo
        // —el que el sistema acaba de asignar a la ventana recién creada— para no dejarla fuera de pantalla.
        var centerPoint = new PointInt32(
            placement.X + placement.Width / 2,
            placement.Y + placement.Height / 2);

        var displayArea =
            DisplayArea.GetFromPoint(centerPoint, DisplayAreaFallback.None)
            ?? DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

        var workArea = displayArea.WorkArea;

        var width = Math.Clamp(placement.Width, 800, workArea.Width);
        var height = Math.Clamp(placement.Height, 600, workArea.Height);

        var x = Math.Clamp(
            placement.X,
            workArea.X,
            Math.Max(workArea.X, workArea.X + workArea.Width - width));

        var y = Math.Clamp(
            placement.Y,
            workArea.Y,
            Math.Max(workArea.Y, workArea.Y + workArea.Height - height));

        appWindow.MoveAndResize(new RectInt32(
            x,
            y,
            width,
            height));

        if (placement.IsMaximized &&
            appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    /// <summary>
    /// Configura una ventana de diálogo: tamaño fijo, centrada en pantalla y con
    /// barra de título colapsada (contenido extendido en el título).
    /// </summary>
    /// <param name="window">Instancia de la ventana a configurar.</param>
    /// <param name="width">Ancho inicial deseado.</param>
    /// <param name="height">Alto inicial deseado.</param>
    public void DialogWindow(Window window, int width, int height, PointInt32? anchorPoint = null)
    {
        CenterAndResizeWindow(window, width, height, blockResize: true, preferredHeightOption: TitleBarHeightOption.Collapsed, anchorPoint: anchorPoint);
    }

    /// <summary>
    /// Obtiene la posición, tamaño y estado actual de una ventana.
    /// </summary>
    /// <param name="window">
    /// Ventana cuya colocación se desea capturar.
    /// </param>
    /// <returns>
    /// Objeto con la posición, tamaño y estado de la ventana.
    /// </returns>
    public WindowPlacement GetWindowPlacement(Window window)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Ventana ESCONDIDA (bandeja) o minimizada: AppWindow.Position devuelve coordenadas fuera de pantalla
        // (-32000), así que se lee el placement NATIVO, cuyo rcNormalPosition conserva la geometría "restaurada"
        // real. Pasa al salir desde el menú de la bandeja sin volver a mostrar la ventana.
        bool minimized = appWindow.Presenter is OverlappedPresenter minimizedPresenter
                         && minimizedPresenter.State == OverlappedPresenterState.Minimized;
        if ((!appWindow.IsVisible || minimized) && TryGetNativePlacement(hWnd) is WindowPlacement native)
            return native;

        var placement = new WindowPlacement
        {
            X = appWindow.Position.X,
            Y = appWindow.Position.Y,
            Width = appWindow.Size.Width,
            Height = appWindow.Size.Height,
            IsMaximized = false
        };

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            placement.IsMaximized =
                presenter.State == OverlappedPresenterState.Maximized;
        }

        return placement;
    }

    /// <summary>
    /// Indica si la ventana debería volver a mostrarse MAXIMIZADA: o lo está ahora mismo, o está escondida/minimizada
    /// con el "restaurar a maximizada" pendiente. Lo consulta la restauración desde la bandeja, donde un
    /// <c>SW_RESTORE</c> a secas desmaximizaría una ventana que se escondió maximizada.
    /// </summary>
    public bool ShouldRestoreMaximized(Window window)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.IsVisible
            && appWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Maximized)
        {
            return true;
        }

        // Escondida en la bandeja: el estilo WS_MAXIMIZE sobrevive al SW_HIDE (a diferencia del showCmd del placement
        // nativo, que puede reportar ya el estado "oculta"), así que es la señal más fiable de que se escondió estando
        // maximizada. Si NO lo estaba —tamaño ajustado a mano—, aquí se devuelve false y la ventana vuelve tal cual.
        if ((GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64() & WS_MAXIMIZE) != 0)
            return true;

        // Minimizada: showCmd/WPF_RESTORETOMAXIMIZED dicen si al restaurarla volvería a maximizarse.
        return TryGetNativePlacement(hWnd)?.IsMaximized ?? false;
    }

    /// <summary>
    /// Placement NATIVO de la ventana (Win32 <c>GetWindowPlacement</c>): devuelve la geometría "restaurada"
    /// (<c>rcNormalPosition</c>) y si la ventana volvería a maximizarse, valores que siguen siendo correctos con la
    /// ventana escondida o minimizada (a diferencia de <c>AppWindow.Position</c>). null si la llamada falla.
    /// </summary>
    private static WindowPlacement? TryGetNativePlacement(IntPtr hWnd)
    {
        var native = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hWnd, ref native))
            return null;

        return new WindowPlacement
        {
            X = native.rcNormalPosition.Left,
            Y = native.rcNormalPosition.Top,
            Width = native.rcNormalPosition.Right - native.rcNormalPosition.Left,
            Height = native.rcNormalPosition.Bottom - native.rcNormalPosition.Top,
            // Maximizada ahora, o minimizada pero con "restaurar a maximizada" pendiente.
            IsMaximized = native.showCmd == SW_SHOWMAXIMIZED || (native.flags & WPF_RESTORETOMAXIMIZED) != 0
        };
    }

    #region Win32 window placement
    /// <summary>Índice de GetWindowLongPtr para leer los estilos de la ventana.</summary>
    private const int GWL_STYLE = -16;

    /// <summary>Estilo presente mientras la ventana está maximizada (se conserva aunque esté escondida).</summary>
    private const long WS_MAXIMIZE = 0x01000000;

    private const int SW_SHOWMAXIMIZED = 3;
    private const int WPF_RESTORETOMAXIMIZED = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTSTRUCT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINTSTRUCT ptMinPosition;
        public POINTSTRUCT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    #endregion

    /// <summary>
    /// Configura la ventana principal: centrada, con barra de título alta, posibilidad
    /// de iniciar maximizada y límites mínimos/máximos de tamaño mediante Win32.
    /// </summary>
    /// <param name="window">Instancia de la ventana principal.</param>
    /// <param name="width">Ancho inicial deseado (si no se inicia maximizada).</param>
    /// <param name="height">Alto inicial deseado (si no se inicia maximizada).</param>
    /// <param name="startMaximized">Indica si la ventana debe abrirse maximizada.</param>
    /// <param name="minWidth">Ancho mínimo permitido para la ventana (en píxeles).</param>
    /// <param name="minHeight">Alto mínimo permitido para la ventana (en píxeles).</param>
    /// <param name="maxWidth">Ancho máximo permitido para la ventana (en píxeles).</param>
    /// <param name="maxHeight">Alto máximo permitido para la ventana (en píxeles).</param>
    public void MainWindow(Window window, int width, int height, bool startMaximized = true, int? minWidth = 1800, int? minHeight = 1000, int? maxWidth = null, int? maxHeight = null, WindowPlacement? placement = null)
    {
        CenterAndResizeWindow(window, width, height, blockResize: false, preferredHeightOption: TitleBarHeightOption.Tall, startMaximized: startMaximized, minWidth: minWidth, minHeight: minHeight, maxWidth: maxWidth, maxHeight: maxHeight);

        if (placement is not null)
        {
            ApplyWindowPlacement(window, placement);
        }
        else if (startMaximized)
        {
            var hWnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
    }
    /// <summary>
    /// Aplica el icono de la aplicación a la ventana nativa asociada a AppWindow.
    /// </summary>
    /// <param name="appWindow">Ventana de nivel AppWindow sobre la que se aplicará el icono.</param>
    private void ApplyWindowIcon(AppWindow appWindow)
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TRACKER.ico");
            appWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting window icon: {ex}");
        }
    }

    /// <summary>
    /// Configura una ventana de escritorio: aplica icono, estilo de barra de título,
    /// transparencia en los botones del sistema, límites de tamaño (opcional),
    /// comportamiento de redimensionamiento, tamaño inicial y centrado en pantalla.
    /// Puede opcionalmente iniciar la ventana en estado maximizado.
    /// </summary>
    /// <param name="window">Instancia de la ventana XAML a configurar.</param>
    /// <param name="width">Ancho inicial deseado (si no se inicia maximizada).</param>
    /// <param name="height">Alto inicial deseado (si no se inicia maximizada).</param>
    /// <param name="blockResize">Indica si se debe impedir que el usuario redimensione la ventana.</param>
    /// <param name="preferredHeightOption">Altura preferida para la barra de título del AppWindow.</param>
    /// <param name="startMaximized">Indica si la ventana debe abrirse directamente maximizada.</param>
    /// <param name="minWidth">Ancho mínimo permitido para la ventana (en píxeles).</param>
    /// <param name="minHeight">Alto mínimo permitido para la ventana (en píxeles).</param>
    /// <param name="maxWidth">Ancho máximo permitido para la ventana (en píxeles).</param>
    /// <param name="maxHeight">Alto máximo permitido para la ventana (en píxeles).</param>
    private void CenterAndResizeWindow(Window window, int width, int height, bool blockResize = true, TitleBarHeightOption? preferredHeightOption = TitleBarHeightOption.Collapsed, bool startMaximized = false, int? minWidth = null, int? minHeight = null, int? maxWidth = null, int? maxHeight = null, PointInt32? anchorPoint = null)
    {
        Register(window);

        var hWnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        ApplyWindowIcon(appWindow);

        // Extender el contenido en la barra de título y ajustar su altura.
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.PreferredHeightOption = preferredHeightOption ?? TitleBarHeightOption.Collapsed;

        // Botones del sistema (cerrar, minimizar, maximizar) con fondo transparente
        // y color de primer plano consistente con el tema.
        appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        appWindow.TitleBar.ButtonHoverBackgroundColor = Microsoft.UI.Colors.Transparent;
        appWindow.TitleBar.ButtonPressedBackgroundColor = Microsoft.UI.Colors.Transparent;

        appWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
        appWindow.TitleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
        appWindow.TitleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;

        // Aplicar límites de tamaño a nivel de HWND mediante WM_GETMINMAXINFO, si se han definido.
        if (minWidth.HasValue || minHeight.HasValue || maxWidth.HasValue || maxHeight.HasValue)
        {
            AttachSizeConstraints(window, minWidth, minHeight, maxWidth, maxHeight);
        }

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            // Controlar si la ventana puede ser redimensionada por el usuario.
            presenter.IsResizable = !blockResize;

            // Si se solicita, iniciar directamente en estado maximizado.
            if (startMaximized)
            {
                presenter.Maximize();
                return;
            }
        }

        // Si no se inicia maximizada, aplicar tamaño inicial y centrar en el área de trabajo.
        appWindow.Resize(new SizeInt32(width, height));

        // Centrar en el monitor indicado por anchorPoint (p. ej. donde se va a restaurar la ventana principal)
        // si se proporciona y ese monitor sigue existiendo; en caso contrario, en el monitor de la propia ventana
        // (normalmente el primario). Se SUMA el origen del área de trabajo (WorkArea.X/Y) para centrar de verdad
        // dentro de ese monitor: sin ello, en un monitor secundario el centro caería en coordenadas del primario.
        var displayArea = anchorPoint.HasValue
            ? (DisplayArea.GetFromPoint(anchorPoint.Value, DisplayAreaFallback.None)
               ?? DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary))
            : DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

        var workArea = displayArea.WorkArea;
        var centerX = workArea.X + (workArea.Width - width) / 2;
        var centerY = workArea.Y + (workArea.Height - height) / 2;

        appWindow.Move(new PointInt32(centerX, centerY));
    }
    #endregion
}
