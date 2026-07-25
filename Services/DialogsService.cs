using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.Dialogs;
using System.Threading.Tasks;

namespace MM4LB.Services;

/// <summary>
/// Service exposing the different dialogs of the application, con el estilo propio de la app
/// (<see cref="AppDialog"/>, mostrado en un Popup sobre la ventana activa).
/// </summary>
public class DialogsService
{
    /// <summary>
    /// Diálogo de confirmación: devuelve true si el usuario pulsó el botón primario.
    /// </summary>
    public async Task<bool> ConfirmAsync(XamlRoot xamlRoot, string title, string message, string primaryText, string closeText)
    {
        AppDialog dialog = new();
        AppDialogResult result = await dialog.ShowAsync(xamlRoot, title, BuildText(message), primaryText, null, closeText);
        return result == AppDialogResult.Primary;
    }

    /// <summary>
    /// Diálogo informativo de un solo botón (p. ej. errores).
    /// </summary>
    public async Task AlertAsync(XamlRoot xamlRoot, string title, string message, string closeText)
    {
        AppDialog dialog = new();
        await dialog.ShowAsync(xamlRoot, title, BuildText(message), null, null, closeText);
    }

    /// <summary>Texto del diálogo como TextBlock con ajuste de línea (el color lo hereda de la tarjeta).</summary>
    private static TextBlock BuildText(string message) => new()
    {
        Text = message ?? string.Empty,
        TextWrapping = TextWrapping.Wrap
    };
}
