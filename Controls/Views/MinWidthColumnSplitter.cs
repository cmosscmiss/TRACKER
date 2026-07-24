using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace Tracker.Controls.Views;

/// <summary>
/// GridSplitter de columnas que impide que cualquiera de las dos columnas que redimensiona baje de
/// <see cref="MinColumnWidth"/>. El GridSplitter del toolkit no respeta de forma fiable el MinWidth de las
/// columnas en estrella, así que acotamos el desplazamiento antes de aplicarlo, igual que el drag de filas del
/// panel clampea su ratio.
///
/// Debe ser una clase PÚBLICA de nivel superior: un control que hereda de un tipo WinRT y se declara anidado o
/// privado no se proyecta por CsWinRT, y la capa nativa lo trata como su base <c>Control</c> — lo que rompe la
/// asignación del Style (con TargetType de la subclase) y la resolución del override.
/// </summary>
public sealed partial class MinWidthColumnSplitter : GridSplitter
{
    /// <summary>Anchura mínima (px) que se garantiza a ambas columnas adyacentes al arrastrar este splitter.</summary>
    public double MinColumnWidth { get; set; } = 150;

    // Anchos y peso estrella del par de columnas capturados AL INICIAR el arrastre. El cambio que reporta el
    // toolkit es acumulativo desde ese inicio (e.Cumulative.Translation.X), así que hay que aplicarlo sobre estos
    // valores base, no sobre los actuales (sumar el acumulado a los actuales en cada evento se realimenta y hace
    // que la columna se dispare sola hasta el mínimo/máximo). Solo se toma el control cuando ambas columnas son en
    // estrella (el caso del panel); si no, _dragCaptured queda en false y se delega en el toolkit.
    private bool _dragCaptured;
    private double _startLeftWidth;
    private double _startRightWidth;
    private double _startTotalStar;

    /// <summary>
    /// Al empezar la manipulación, fija los anchos base del par sobre los que se aplicará el cambio acumulado.
    /// </summary>
    protected override void OnManipulationStarted(ManipulationStartedRoutedEventArgs e)
    {
        CaptureDragStart();
        base.OnManipulationStarted(e);
    }

    /// <summary>
    /// Al terminar, descarta la captura para que un arrastre posterior no reutilice valores obsoletos.
    /// </summary>
    protected override void OnManipulationCompleted(ManipulationCompletedRoutedEventArgs e)
    {
        _dragCaptured = false;
        base.OnManipulationCompleted(e);
    }

    private void CaptureDragStart()
    {
        _dragCaptured = false;

        if (Parent is not Grid grid)
            return;

        int column = Grid.GetColumn(this);
        if (column < 1 || column >= grid.ColumnDefinitions.Count)
            return;

        ColumnDefinition left = grid.ColumnDefinitions[column - 1];
        ColumnDefinition right = grid.ColumnDefinitions[column];

        if (!left.Width.IsStar || !right.Width.IsStar)
            return;

        _startLeftWidth = left.ActualWidth;
        _startRightWidth = right.ActualWidth;
        _startTotalStar = left.Width.Value + right.Width.Value;

        _dragCaptured = _startLeftWidth + _startRightWidth > 0 && _startTotalStar > 0;
    }

    protected override bool OnDragHorizontal(double horizontalChange)
    {
        // Sin captura válida (columnas no estrella, o no se pudo medir): comportamiento del toolkit.
        if (!_dragCaptured || Parent is not Grid grid)
            return base.OnDragHorizontal(horizontalChange);

        int column = Grid.GetColumn(this);
        if (column < 1 || column >= grid.ColumnDefinitions.Count)
            return base.OnDragHorizontal(horizontalChange);

        ColumnDefinition left = grid.ColumnDefinitions[column - 1];
        ColumnDefinition right = grid.ColumnDefinitions[column];

        double total = _startLeftWidth + _startRightWidth;

        // No hay margen para respetar el mínimo en ambas: no se redimensiona.
        if (total <= 2 * MinColumnWidth)
            return false;

        // El splitter redimensiona la columna previa y la actual: arrastrar a la derecha (cambio > 0) agranda la
        // izquierda y encoge la derecha. horizontalChange es ACUMULATIVO desde el inicio → se aplica sobre el
        // ancho inicial de la izquierda y se acota para que ninguna de las dos baje del mínimo.
        double newLeft = Math.Clamp(_startLeftWidth + horizontalChange, MinColumnWidth, total - MinColumnWidth);
        double newRight = total - newLeft;

        // Se reparte el peso estrella COMBINADO del par según los anchos resultantes: así el ActualWidth de cada
        // columna coincide con el objetivo (nunca por debajo del mínimo) y el resto de columnas no se ve afectado
        // porque el peso estrella total del par se conserva.
        left.Width = new GridLength(_startTotalStar * (newLeft / total), GridUnitType.Star);
        right.Width = new GridLength(_startTotalStar * (newRight / total), GridUnitType.Star);

        return true;
    }
}
