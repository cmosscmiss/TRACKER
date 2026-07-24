using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Templates;

/// <summary>
/// Capa de "app ocupada" que se coloca sobre el contenido principal (ver MainWindow). Es un
/// <see cref="Grid"/> derivado cuyo único cometido extra es mostrar el cursor de espera mientras
/// está visible: como la capa solo es visible y hit-testable cuando la UI está bloqueada
/// (<c>SharedDataService.IsUIEnabled == false</c>), el cursor "wait" solo aparece entonces.
///
/// <para><see cref="Microsoft.UI.Xaml.UIElement.ProtectedCursor"/> es <c>protected</c>, así que la
/// única vía limpia en WinUI 3 para fijar el cursor es desde una subclase como esta.</para>
/// </summary>
public sealed class BusyOverlayPanel : Grid
{
    public BusyOverlayPanel()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Wait);
    }
}
