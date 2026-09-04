using System;
using System.Runtime.InteropServices;

namespace Tracker.Services;

/// <summary>
/// Minimal system-tray integration for the (unpackaged) main window, via Win32 interop (no extra NuGet). While the
/// app is running it shows a tray icon; con el ajuste "minimizar a la bandeja" activo, minimizar (y cerrar, que lo
/// gestiona la ventana) esconde la ventana de la barra de tareas, y pulsar el icono la restaura. Mantener el proceso
/// vivo en la bandeja es lo que permite al planificador seguir leyendo precios en segundo plano. El botón DERECHO
/// sobre el icono abre un menú nativo con Show/Hide y Exit (la única forma de salir con ese ajuste activo).
///
/// It subclasses the window procedure (via comctl32 <c>SetWindowSubclass</c>, which chains safely with WinUI's own
/// proc) to receive the tray callback and the minimize notification. Call <see cref="Initialize"/> once with the
/// window handle and <see cref="Dispose"/> on close.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    #region Win32 constants
    private const uint WM_TRAY = 0x8000 + 1;   // WM_APP + 1
    private const uint WM_SIZE = 0x0005;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NULL = 0x0000;
    private const int SIZE_MINIMIZED = 1;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    /// <summary>Identificadores de los comandos del menú del icono (botón derecho).</summary>
    private const uint MenuShowHideId = 1;
    private const uint MenuExitId = 2;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const int SW_SHOWMAXIMIZED = 3;

    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;
    private const uint NIF_INFO = 0x0010;
    private const int NIIF_NONE = 0x0000;   // sin icono extra del sistema: el toast usa el icono de la bandeja (el de la app)

    private const int TrayIconId = 1;
    #endregion

    #region Attributes
    private IntPtr _hwnd;
    private IntPtr _appIcon;
    private bool _iconAdded;
    private bool _disposed;

    // Guardado como campo para que el delegate no lo recolecte el GC mientras está registrado como subclass.
    private SubclassProcDelegate? _subclassProc;
    #endregion

    #region Properties
    /// <summary>
    /// Consulta EN VIVO el ajuste "minimizar a la bandeja" (<c>AppSettings.GeneralSettings.MinimizeToTray</c>). Si
    /// devuelve true, minimizar esconde la ventana en la bandeja; si devuelve false (o no se ha fijado), minimizar se
    /// comporta como en cualquier ventana (barra de tareas). El icono de la bandeja está SIEMPRE, para poder
    /// restaurar o salir desde su menú.
    /// </summary>
    public Func<bool>? MinimizeToTrayProvider { get; set; }

    /// <summary>
    /// Consulta EN VIVO si la ventana debe salir de la bandeja MAXIMIZADA (ajuste
    /// <c>AppSettings.GeneralSettings.RestoreMaximized</c> + estado real de la ventana). Es necesario porque un
    /// <c>SW_RESTORE</c> a secas desmaximiza: sin esto, una ventana escondida estando maximizada volvía en tamaño
    /// normal. Si devuelve false (o no se ha fijado), se restaura como siempre.
    /// </summary>
    public Func<bool>? RestoreMaximizedProvider { get; set; }

    /// <summary>Lo dispara la opción "Exit" del menú del icono (botón derecho): la ventana debe cerrarse de verdad.</summary>
    public event Action? ExitRequested;
    #endregion

    #region Methods (public)
    /// <summary>Adds the tray icon and starts intercepting minimize / tray-click for the given window handle.</summary>
    public void Initialize(IntPtr hwnd, string tooltip)
    {
        if (_hwnd != IntPtr.Zero)
            return;

        _hwnd = hwnd;
        _subclassProc = SubclassProc;
        SetWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);
        AddIcon(tooltip);

        // Backstop: si el proceso termina sin pasar por Dispose (cierre abrupto), se quita igualmente el icono para que
        // no quede huérfano en la bandeja.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    /// <summary>Restaura y trae al frente la ventana (equivale a pulsar el icono de la bandeja). Para activarla desde otra instancia.</summary>
    public void Restore() => RestoreFromTray();

    /// <summary>Esconde la ventana en la bandeja (la usa el cierre "a bandeja" del botón X de la ventana).</summary>
    public void Hide() => HideToTray();

    /// <summary>Muestra una notificación de Windows (globo del icono de bandeja, popup abajo a la derecha).</summary>
    public void ShowNotification(string title, string message)
    {
        if (!_iconAdded || _hwnd == IntPtr.Zero)
            return;

        var data = CreateData();
        data.uFlags = NIF_INFO | NIF_ICON;
        data.hIcon = AppIcon();   // asegura el icono de la bandeja (el de la app), que es el que muestra el toast
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        data.dwInfoFlags = NIIF_NONE;   // sin el icono "i" del sistema; el toast usa el icono de la app
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value.Substring(0, max));

    /// <summary>Removes the tray icon and the window-proc subclass.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        if (_iconAdded)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _iconAdded = false;
        }

        if (_hwnd != IntPtr.Zero && _subclassProc is not null)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero);
            _subclassProc = null;
        }
    }
    #endregion

    #region Methods (private)
    private void AddIcon(string tooltip)
    {
        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAY;
        data.hIcon = AppIcon();
        data.szTip = tooltip;
        _iconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = TrayIconId
    };

    /// <summary>Icono de la app (cacheado): se usa tanto para el icono de la bandeja como para el de las notificaciones.</summary>
    private IntPtr AppIcon()
    {
        if (_appIcon == IntPtr.Zero)
            _appIcon = LoadAppIcon();
        return _appIcon;
    }

    private static IntPtr LoadAppIcon()
    {
        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            IntPtr icon = ExtractIcon(GetModuleHandle(null), exePath, 0);
            if (icon != IntPtr.Zero && icon != new IntPtr(1))
                return icon;
        }
        // Fallback al icono de aplicación del sistema.
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    private void HideToTray() => ShowWindow(_hwnd, SW_HIDE);

    private void RestoreFromTray()
    {
        // Maximizada: un solo SW_SHOWMAXIMIZED (mostrar + maximizar). El SW_RESTORE del camino normal es justo lo
        // contrario —restaura el tamaño no maximizado—, así que aquí no se encadena.
        if (RestoreMaximizedProvider?.Invoke() ?? false)
        {
            ShowWindow(_hwnd, SW_SHOWMAXIMIZED);
        }
        else
        {
            ShowWindow(_hwnd, SW_SHOW);
            ShowWindow(_hwnd, SW_RESTORE);
        }

        SetForegroundWindow(_hwnd);
    }

    /// <summary>
    /// Menú del icono de la bandeja (botón derecho): "Show"/"Hide" (según esté visible la ventana) y "Exit". Es un
    /// menú NATIVO (no XAML) porque debe poder mostrarse con la ventana escondida, cuando no hay UI viva.
    ///
    /// Las dos llamadas alrededor de <c>TrackPopupMenuEx</c> son el requisito documentado de Shell_NotifyIcon:
    /// <c>SetForegroundWindow</c> antes (si no, el menú no recibe el foco) y un <c>WM_NULL</c> después (si no, el
    /// menú no se cierra al hacer clic fuera). <c>TPM_RETURNCMD</c> devuelve el comando elegido en vez de postear
    /// un WM_COMMAND, así que la acción se resuelve aquí mismo.
    /// </summary>
    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            bool visible = IsWindowVisible(_hwnd);
            AppendMenu(menu, MF_STRING, new IntPtr(MenuShowHideId), L(visible ? Helpers.LocKeys.Tray_Hide_Label : Helpers.LocKeys.Tray_Show_Label));
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, new IntPtr(MenuExitId), L(Helpers.LocKeys.Tray_Exit_Label));

            SetForegroundWindow(_hwnd);
            GetCursorPos(out POINT cursor);
            int command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, cursor.X, cursor.Y, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (command == MenuShowHideId)
            {
                if (visible)
                    HideToTray();
                else
                    RestoreFromTray();
            }
            else if (command == MenuExitId)
            {
                ExitRequested?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>Texto localizado de una clave (o la propia clave si aún no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;

    private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (uMsg)
        {
            case WM_TRAY:
                uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
                    RestoreFromTray();
                else if (mouse is WM_RBUTTONUP or WM_CONTEXTMENU)
                    ShowContextMenu();
                return IntPtr.Zero;

            // Minimizar esconde la ventana en la bandeja SOLO si el ajuste está activo; si no, se deja pasar el
            // mensaje y la ventana se minimiza a la barra de tareas como cualquier otra.
            case WM_SIZE when wParam.ToInt32() == SIZE_MINIMIZED && (MinimizeToTrayProvider?.Invoke() ?? false):
                HideToTray();
                return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
    #endregion

    #region Win32 interop
    private delegate IntPtr SubclassProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    #endregion
}
