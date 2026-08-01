using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;

namespace MM4LB.Services;

/// <summary>
/// Notificaciones de Windows mediante <see cref="AppNotificationManager"/> (Windows App SDK): toasts nativos que
/// además quedan guardados en el Centro de notificaciones, con varias líneas, una imagen y un botón "Abrir" que trae
/// la app al frente. Hay que <see cref="Register"/> al arrancar y <see cref="Unregister"/> al cerrar.
///
/// Notas para apps unpackaged: la atribución/icono puede salir como el ejecutable si no se registra un AppUserModelID
/// + acceso directo; y las imágenes REMOTAS (http) no se cargan en el toast, así que se descargan a un fichero local y
/// se referencian por <c>file://</c> (las <c>ms-appx</c> sí valen directamente).
/// </summary>
public sealed class NotificationService
{
    private static readonly HttpClient Http = new();

    private bool _registered;

    /// <summary>Se dispara cuando el usuario pulsa la notificación o su botón (para traer la app al frente).</summary>
    public event Action? Activated;

    /// <summary>Registra la app para mostrar notificaciones y escuchar las pulsaciones. Idempotente.</summary>
    public void Register()
    {
        if (_registered)
            return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not register the Windows notification manager.");
        }
    }

    /// <summary>Libera el registro de notificaciones. Idempotente.</summary>
    public void Unregister()
    {
        if (!_registered)
            return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch { /* best-effort al cerrar */ }
        _registered = false;
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => Activated?.Invoke();

    /// <summary>
    /// Muestra un toast: <paramref name="title"/> destacado, la primera línea como subtítulo, el resto en un bloque
    /// (con separadores ya incluidos entre tipos), un botón "Abrir" y, si se indica, una imagen (<paramref name="imageUri"/>,
    /// remota o ms-appx). Silencioso si el registro falló.
    /// </summary>
    public async Task ShowAsync(string title, IReadOnlyList<string> lines, string openLabel, string? imageUri)
    {
        if (!_registered)
            return;

        try
        {
            string? localPath = await ResolveImageAsync(imageUri);
            string? imageSrc = localPath is null ? null : new Uri(localPath).AbsoluteUri;

            // El título va como <text> de nivel superior (si no, sin ningún <text> fuera de grupos Windows muestra el
            // genérico "New notification"). Las líneas van en la columna de texto del grupo.
            string titleText = $"<text>{Escape(title)}</text>";
            string texts = string.Concat(lines.Select(line => $"<text>{Escape(line)}</text>"));

            // Layout en dos columnas: imagen en una columna ESTRECHA (hint-weight bajo) y, apilado desde ARRIBA, el
            // texto en una columna ancha; así el icono es pequeño y queda arriba. Sin imagen, solo texto.
            string visual = imageSrc is null
                ? titleText + texts
                : titleText +
                  "<group>" +
                    $"<subgroup hint-weight=\"1\"><image src=\"{Escape(imageSrc)}\" hint-removeMargin=\"true\"/></subgroup>" +
                    $"<subgroup hint-weight=\"5\">{texts}</subgroup>" +
                  "</group>";

            string payload =
                "<toast launch=\"action=open\" activationType=\"foreground\">" +
                    "<visual><binding template=\"ToastGeneric\">" + visual + "</binding></visual>" +
                    "<actions><action content=\"" + Escape(openLabel) + "\" arguments=\"action=open\" activationType=\"foreground\"/></actions>" +
                "</toast>";

            AppNotificationManager.Default.Show(new AppNotification(payload));
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not show a Windows notification.");
        }
    }

    /// <summary>Escapa un texto para incrustarlo en el XML del toast.</summary>
    private static string Escape(string value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");

    /// <summary>
    /// Devuelve la RUTA LOCAL del fichero de imagen para el toast: las <c>ms-appx</c> se mapean al fichero físico de la
    /// carpeta de la app (el toast, en apps unpackaged, no resuelve ms-appx); las <c>file</c> a su ruta; las remotas
    /// (http) se descargan a un temporal. null si no hay o falla.
    /// </summary>
    private static async Task<string?> ResolveImageAsync(string? imageUri)
    {
        if (string.IsNullOrWhiteSpace(imageUri))
            return null;

        try
        {
            if (imageUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string local = new Uri(imageUri).LocalPath;
                return File.Exists(local) ? local : null;
            }

            if (imageUri.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase))
            {
                string relative = new Uri(imageUri).AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string full = Path.Combine(AppContext.BaseDirectory, relative);
                return File.Exists(full) ? full : null;
            }

            if (imageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                byte[] data = await Http.GetByteArrayAsync(imageUri);
                string dir = Path.Combine(Path.GetTempPath(), "Tracker");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "notification-image.img");
                await File.WriteAllBytesAsync(path, data);
                return path;
            }
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not resolve the notification image.");
        }

        return null;
    }

}
