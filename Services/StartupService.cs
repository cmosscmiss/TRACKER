using System;
using Microsoft.Win32;

namespace Tracker.Services;

/// <summary>
/// Arranque automático con Windows para la app SIN empaquetar: registra (o quita) el ejecutable en la clave de
/// usuario <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, que es el mecanismo estándar para apps
/// unpackaged (no requiere permisos de administrador, es por usuario).
///
/// Lo gobierna <c>AppSettings.GeneralSettings.StartWithWindows</c>: se sincroniza al arrancar
/// (<see cref="Tracker.Services.ApplicationHostService"/>) y al aceptar la ventana de configuración. Todas las
/// operaciones son best-effort: si el registro no es accesible, se deja constancia en el log y la app sigue.
/// </summary>
public static class StartupService
{
    #region Constants
    /// <summary>Clave de usuario donde Windows lee los programas a lanzar al iniciar sesión.</summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Nombre del valor bajo la clave Run (identifica a esta app; estable entre versiones).</summary>
    private const string RunValueName = "Tracker";

    /// <summary>
    /// Argumento con el que Windows lanza la app desde la clave Run: es lo único que distingue un arranque automático
    /// (al iniciar sesión) de uno manual, y lo consulta <see cref="IsAutoStartLaunch"/> para decidir si la app debe
    /// arrancar escondida en la bandeja (<c>AppSettings.GeneralSettings.StartMinimizedOnAutoStart</c>).
    /// </summary>
    public const string AutoStartArgument = "--autostart";
    #endregion

    #region Methods (public)
    /// <summary>
    /// Indica si ESTA ejecución la ha lanzado Windows al iniciar sesión, es decir, si la línea de comandos trae
    /// <see cref="AutoStartArgument"/> (lo escribe <see cref="Apply"/> en la clave Run). Un arranque manual —doble
    /// clic en el ejecutable o desde Visual Studio— no lleva el argumento y devuelve false.
    /// </summary>
    public static bool IsAutoStartLaunch()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            // El índice 0 es la ruta del propio ejecutable: se salta.
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], AutoStartArgument, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not read the command line arguments.");
            return false;
        }
    }

    /// <summary>Indica si la app está registrada actualmente para arrancar con Windows.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not read the Windows startup registry key.");
            return false;
        }
    }

    /// <summary>
    /// Sincroniza el registro con el ajuste: si <paramref name="enabled"/>, escribe la ruta ACTUAL del ejecutable
    /// (así se corrige sola si la app se movió de carpeta); si no, borra el valor. Idempotente.
    /// </summary>
    public static void Apply(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                     ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
                return;

            if (!enabled)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                return;
            }

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            // Entre comillas: la ruta puede llevar espacios (p. ej. "C:\Program Files\..."). El argumento marca la
            // ejecución como arranque automático, para que la app pueda quedarse escondida en la bandeja.
            string command = $"\"{exePath}\" {AutoStartArgument}";
            if (key.GetValue(RunValueName) as string != command)
                key.SetValue(RunValueName, command, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, $"Could not {(enabled ? "register" : "remove")} the app in the Windows startup registry key.");
        }
    }
    #endregion
}
