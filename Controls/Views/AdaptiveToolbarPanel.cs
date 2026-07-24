using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace MM4LB.Controls.Views;

/// <summary>
/// Panel de toolbar que se comporta como un <c>StackPanel</c> horizontal (con <see cref="Spacing"/>) y que, cuando el
/// ajuste global de grupos es <c>Auto</c>, coordina el colapso de TODOS los <see cref="ExclusiveOptionsControl"/> que
/// contiene: si el contenido expandido no cabe en el ancho disponible del widget, colapsa todos los grupos a su forma
/// <c>SplitButton</c>; cuando vuelve a caber, los expande.
///
/// El umbral de decisión es el ancho natural (expandido) del contenido, que se guarda como referencia fija mientras el
/// toolbar está expandido — así el colapso reduce el ancho sin volver a cruzar el umbral (histéresis anti-oscilación).
/// El cambio de modo se aplica diferido (dispatcher) para no re-entrar en el ciclo de medida.
/// </summary>
public sealed class AdaptiveToolbarPanel : Panel
{
    private const double Epsilon = 0.5;

    /// <summary>Grupos excluyentes en modo Auto descendientes de este panel (se recolectan de forma perezosa).</summary>
    private List<ExclusiveOptionsControl>? _autoGroups;

    /// <summary>Estado colapsado actual dirigido por este panel.</summary>
    private bool _collapsed;

    /// <summary>Ancho natural (expandido) del contenido, usado como umbral fijo para decidir el colapso.</summary>
    private double _expandedWidth;

    /// <summary>Evita encolar varios cambios de modo simultáneos en el dispatcher.</summary>
    private bool _modeChangePending;

    #region Spacing
    /// <summary>Separación horizontal entre hijos, equivalente a <c>StackPanel.Spacing</c>.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveToolbarPanel),
        new PropertyMetadata(0.0, (d, _) => ((AdaptiveToolbarPanel)d).InvalidateMeasure()));
    #endregion

    protected override Size MeasureOverride(Size availableSize)
    {
        double spacing = Spacing;
        double width = 0;
        double height = 0;
        int visibleCount = 0;

        var childAvailable = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (UIElement child in Children)
        {
            child.Measure(childAvailable);
            if (child.Visibility == Visibility.Collapsed || child.DesiredSize.Width <= 0)
            {
                continue;
            }

            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
            visibleCount++;
        }

        if (visibleCount > 1)
        {
            width += spacing * (visibleCount - 1);
        }

        EvaluateAutoCollapse(width, availableSize.Width);

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double spacing = Spacing;
        double x = 0;

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed || child.DesiredSize.Width <= 0)
            {
                continue;
            }

            double w = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w + spacing;
        }

        return finalSize;
    }

    /// <summary>
    /// Decide si el toolbar debe colapsar o expandir sus grupos según el ancho natural medido frente al disponible.
    /// Solo actúa si hay grupos en modo Auto y el ancho disponible es finito.
    /// </summary>
    /// <param name="contentWidth">Ancho medido del contenido en el estado actual (expandido o colapsado).</param>
    /// <param name="availableWidth">Ancho disponible del widget (finito si el panel está acotado).</param>
    private void EvaluateAutoCollapse(double contentWidth, double availableWidth)
    {
        EnsureAutoGroups();
        if (_autoGroups!.Count == 0 || double.IsInfinity(availableWidth) || double.IsNaN(availableWidth))
        {
            return;
        }

        if (!_collapsed)
        {
            // Estado expandido: el ancho medido ES el ancho natural; se guarda como umbral.
            _expandedWidth = contentWidth;
            if (contentWidth > availableWidth + Epsilon)
            {
                RequestMode(collapse: true);
            }
        }
        else
        {
            // Estado colapsado: se compara el disponible con el umbral expandido guardado (histéresis).
            if (availableWidth + Epsilon >= _expandedWidth)
            {
                RequestMode(collapse: false);
            }
        }
    }

    /// <summary>Aplica el cambio de modo de forma diferida para no re-entrar en el ciclo de medida.</summary>
    private void RequestMode(bool collapse)
    {
        if (_collapsed == collapse || _modeChangePending)
        {
            return;
        }

        _modeChangePending = true;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _modeChangePending = false;
            if (_collapsed == collapse)
            {
                return;
            }

            _collapsed = collapse;
            foreach (ExclusiveOptionsControl group in _autoGroups!)
            {
                group.ApplyAutoCollapsed(collapse);
            }
            InvalidateMeasure();
        });
    }

    /// <summary>
    /// Recolecta los <see cref="ExclusiveOptionsControl"/> en modo Auto del subárbol del panel. Solo cachea cuando
    /// encuentra alguno, para no fijar una lista vacía si el árbol visual aún no está realizado en el primer measure.
    /// </summary>
    private void EnsureAutoGroups()
    {
        if (_autoGroups is { Count: > 0 })
        {
            return;
        }

        var found = new List<ExclusiveOptionsControl>();
        foreach (UIElement child in Children)
        {
            CollectAutoGroups(child, found);
        }

        if (found.Count > 0)
        {
            _autoGroups = found;
        }
        else
        {
            _autoGroups ??= found; // lista vacía temporal; se reintentará en el próximo measure.
        }
    }

    private static void CollectAutoGroups(DependencyObject node, List<ExclusiveOptionsControl> found)
    {
        if (node is ExclusiveOptionsControl group)
        {
            if (group.IsAutoMode)
            {
                found.Add(group);
            }
            return; // los grupos no se anidan entre sí.
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            CollectAutoGroups(VisualTreeHelper.GetChild(node, i), found);
        }
    }
}
