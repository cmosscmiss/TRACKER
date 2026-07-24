using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MM4LB.Services;

/// <summary>
/// Servicio centralizado para gestionar excepciones de la aplicación.
/// 
/// Responsabilidades:
/// - Recibir excepciones desde cualquier servicio o componente.
/// - Registrar la excepción para depuración mediante <see cref="Debug"/>.
/// - Notificar a la capa de presentación mediante un evento, sin acoplarse directamente a la UI.
/// 
/// Este servicio no muestra diálogos ni conoce las ventanas de la aplicación.
/// La presentación visual del error debe gestionarse desde otro servicio suscrito a
/// <see cref="ErrorMessageRaised"/>.
/// </summary>
public class ExceptionService
{
    #region Attributes
    /// <summary>
    /// Evento que se dispara cuando se debe mostrar o procesar un mensaje de error.
    /// El parámetro del evento contiene el mensaje que debería presentarse al usuario.
    /// </summary>
    public event Action<string>? ErrorMessageRaised;

    /// <summary>
    /// Cerrojo para serializar las escrituras concurrentes al fichero de log (las excepciones pueden
    /// llegar desde varios hilos a la vez).
    /// </summary>
    private static readonly object _fileLock = new();

    /// <summary>
    /// Activa/desactiva la escritura al fichero de log. Se sincroniza desde
    /// <c>AppSettings.General.ExceptionLoggingEnabled</c> al cargar la configuración. Por defecto activado, para
    /// capturar también los fallos previos a la carga de settings.
    /// </summary>
    public static bool LoggingEnabled { get; set; } = true;

    /// <summary>
    /// Ruta del fichero de log de excepciones (<c>MM4LB.log</c>), en la MISMA carpeta que el fichero de
    /// configuración y el backup (<see cref="PersistAndRestoreService.SettingsFolderPath"/>).
    /// </summary>
    private static string LogFilePath => Path.Combine(PersistAndRestoreService.SettingsFolderPath, "MM4LB.log");
    #endregion

    #region Methods (public)
    /// <summary>
    /// Gestiona una excepción capturada por la aplicación.
    /// La excepción completa se escribe en la salida de depuración, mientras que a la UI
    /// se envía un mensaje simplificado. Si se proporciona <paramref name="userMessage"/>,
    /// se usará ese texto como mensaje para el usuario; en caso contrario, se usará
    /// <see cref="Exception.Message"/>.
    /// </summary>
    /// <param name="ex">
    /// Excepción capturada que se desea registrar y notificar.
    /// </param>
    /// <param name="userMessage">
    /// Mensaje opcional, más amigable o contextual, que se mostrará al usuario.
    /// Si es <c>null</c>, se utilizará el mensaje original de la excepción.
    /// </param>
    public void Handle(Exception ex, string? userMessage = null)
    {
        // La cancelación del usuario (p. ej. abortar una descarga desde el ConsoleLog) no es un error: ni se
        // registra como fallo ni se notifica a la UI con un diálogo. La entry del log ya refleja el estado.
        if (ex is OperationCanceledException)
        {
            return;
        }

        // Registrar la excepción completa para depuración y en el fichero de log.
        Debug.WriteLine(ex);
        LogToFile(ex, userMessage);

        // Notificar a los servicios suscritos para que decidan cómo presentar el error.
        ErrorMessageRaised?.Invoke(userMessage ?? ex.Message);
    }

    /// <summary>
    /// Añade una entrada con marca de tiempo al fichero de log de excepciones
    /// (<c>%LocalAppData%\MM4LB\MM4LB.log</c>).
    ///
    /// Es estático y autocontenido (no depende de la inyección de dependencias) para poder usarse desde
    /// los manejadores globales de excepciones no controladas, incluso si la aplicación está cerrándose o
    /// en mal estado. Nunca lanza: cualquier fallo al escribir se ignora para no provocar un cierre
    /// secundario.
    /// </summary>
    /// <param name="ex">Excepción a registrar (puede ser <c>null</c>).</param>
    /// <param name="context">Texto opcional que describe el origen o contexto de la excepción.</param>
    public static void LogToFile(Exception? ex, string? context = null)
    {
        if (!LoggingEnabled)
        {
            return;
        }

        try
        {
            string logFilePath = LogFilePath;
            string? folder = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(folder)) { Directory.CreateDirectory(folder); }

            StringBuilder entry = new();
            entry.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
            entry.AppendLine(context ?? "Exception");
            entry.AppendLine(ex?.ToString() ?? "(no exception object)");
            entry.AppendLine(new string('-', 80));

            lock (_fileLock)
            {
                File.AppendAllText(logFilePath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // El logging nunca debe provocar una excepción secundaria.
        }
    }
    #endregion
}