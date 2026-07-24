using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Views;

/// <summary>
/// Overlay visual de zona de drop: borde punteado de acento + signo "+" central.
/// Su fondo no nulo garantiza el hit-test del drop aunque el área subyacente esté vacía
/// (p. ej. cuando no hay imagen seleccionada ni miniaturas). El cableado del drag&amp;drop
/// (AllowDrop/DragOver/Drop) se conecta en el lugar de uso, no aquí.
/// </summary>
public sealed partial class DropTargetOverlayControl : UserControl
{
    #region Constructors
    public DropTargetOverlayControl()
    {
        InitializeComponent();
    }
    #endregion
}
