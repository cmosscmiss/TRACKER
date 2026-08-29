using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Tracker.Controls.Templates;

/// <summary>
/// Utility class responsible for rendering predefined layout previews into a target <see cref="Grid"/>.
/// Each generated slot is represented by a <see cref="Border"/> tagged with its logical slot index,
/// allowing external controls to identify, style, and use the rendered cells as drop targets.
/// </summary>
public static class LayoutBuilder
{
    #region Attributes & Constants
    private const double Gap = 4;
    private const double Radius = 8;

    private static readonly IReadOnlyDictionary<int, Action<Grid, LayoutBuildContext>> Builders =
        new Dictionary<int, Action<Grid, LayoutBuildContext>>
        {
            [0] = BuildOneColumn,
            [1] = BuildTwoColumns50,
            [2] = BuildRightColumnSplit,
            [3] = BuildLeftColumnSplit,
            [4] = BuildThreeColumnsEqualGrid,
            [5] = BuildGrid2x2,
            [6] = BuildWideLeftRightColumnSplit,
            [7] = BuildWideLeftMiddleColumnSplit,
        };

    /// <summary>
    /// Holds the resource values needed during a single layout build operation.
    /// </summary>
    /// <param name="BorderBrush">The brush used for the default slot border.</param>
    /// <param name="BackgroundBrush">The brush used for the default slot background.</param>
    private sealed record LayoutBuildContext(Brush BorderBrush, Brush BackgroundBrush);
    #endregion

    #region Methods (public)
    /// <summary>
    /// Builds the visual representation of a layout inside the provided host grid.
    /// The host is cleared before rendering the selected layout.
    /// </summary>
    /// <param name="host">The target grid where the layout preview will be rendered.</param>
    /// <param name="layoutIndex">The identifier of the layout to render.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="host"/> is null.</exception>
    public static void Build(Grid host, int layoutIndex)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        ClearHost(host);

        var context = CreateContext();

        if (!Builders.TryGetValue(layoutIndex, out var builder))
            builder = BuildTwoColumns50;

        builder(host, context);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Removes all children, row definitions, and column definitions from the target host grid.
    /// </summary>
    /// <param name="host">The grid to clear.</param>
    private static void ClearHost(Grid host)
    {
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();
    }

    /// <summary>
    /// Creates a build context by resolving the current theme resources.
    /// </summary>
    /// <returns>A context containing the brushes used by the generated cells.</returns>
    private static LayoutBuildContext CreateContext()
    {
        return new LayoutBuildContext((Brush)Application.Current.Resources["TextSecondaryBrush"], (Brush)Application.Current.Resources["CardBackgroundLightBrush"]);
    }
    #endregion

    #region Layout helpers
    /// <summary>
    /// Creates and adds a slot cell to the target grid.
    /// </summary>
    /// <param name="host">The target grid where the cell will be added.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    /// <param name="slotIndex">The logical slot index represented by the cell.</param>
    /// <param name="row">The target grid row.</param>
    /// <param name="column">The target grid column.</param>
    /// <param name="margin">The margin applied to the cell.</param>
    /// <param name="cornerRadius">The corner radius applied to the cell.</param>
    /// <param name="rowSpan">The number of rows spanned by the cell.</param>
    /// <param name="columnSpan">The number of columns spanned by the cell.</param>
    private static void AddCell(Grid host, LayoutBuildContext context, int slotIndex, int row, int column, Thickness margin, CornerRadius cornerRadius, int rowSpan = 1, int columnSpan = 1)
    {
        var cell = CreateCell(context, slotIndex, margin, cornerRadius);

        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        Grid.SetRowSpan(cell, Math.Max(1, rowSpan));
        Grid.SetColumnSpan(cell, Math.Max(1, columnSpan));

        host.Children.Add(cell);
    }

    /// <summary>
    /// Adds star-sized column definitions to the target grid.
    /// </summary>
    /// <param name="host">The grid that receives the column definitions.</param>
    /// <param name="weights">The star weights to apply to each column.</param>
    private static void AddStarColumns(Grid host, params double[] weights)
    {
        foreach (double weight in weights)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(weight, GridUnitType.Star)
            });
        }
    }

    /// <summary>
    /// Adds star-sized row definitions to the target grid.
    /// </summary>
    /// <param name="host">The grid that receives the row definitions.</param>
    /// <param name="weights">The star weights to apply to each row.</param>
    private static void AddStarRows(Grid host, params double[] weights)
    {
        foreach (double weight in weights)
        {
            host.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(weight, GridUnitType.Star)
            });
        }
    }    

    /// <summary>
    /// Creates a visual cell for a layout slot.
    /// The slot index is stored in the border tag so the cell can later be identified by other controls.
    /// </summary>
    /// <param name="context">The build context containing the brushes to apply.</param>
    /// <param name="slotIndex">The logical slot index represented by the cell.</param>
    /// <param name="margin">The margin applied to the cell to create spacing between slots.</param>
    /// <param name="cornerRadius">The corner radius applied to the cell.</param>
    /// <returns>A configured <see cref="Border"/> representing a layout slot.</returns>
    private static Border CreateCell(LayoutBuildContext context, int slotIndex, Thickness margin, CornerRadius cornerRadius)
    {
        return new Border
        {
            Tag = slotIndex,
            Margin = margin,
            CornerRadius = cornerRadius,
            BorderThickness = new Thickness(1),
            BorderBrush = context.BorderBrush,
            Background = context.BackgroundBrush
        };
    }
    #endregion

    #region Layout Builders
    /// <summary>
    /// Builds a single-column layout containing one full-size slot.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildOneColumn(Grid host, LayoutBuildContext context)
    {
        AddStarRows(host, 1);
        AddStarColumns(host, 1);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, margin: new Thickness(0), cornerRadius: new CornerRadius(Radius));
    }

    /// <summary>
    /// Builds a two-column layout where both columns have equal width.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildTwoColumns50(Grid host, LayoutBuildContext context)
    {
        BuildTwoColumns(host, context, leftWeight: 1, rightWeight: 1);
    }

    /// <summary>
    /// Builds a generic two-column layout with configurable column weights.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    /// <param name="leftWeight">The star weight of the left column.</param>
    /// <param name="rightWeight">The star weight of the right column.</param>
    private static void BuildTwoColumns(Grid host, LayoutBuildContext context, double leftWeight, double rightWeight)
    {
        AddStarRows(host, 1);
        AddStarColumns(host, leftWeight, rightWeight);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, margin: new Thickness(0, 0, Gap / 2, 0), cornerRadius: new CornerRadius(Radius, 0, 0, Radius));
        AddCell(host, context, slotIndex: 1, row: 0, column: 1, margin: new Thickness(Gap / 2, 0, 0, 0), cornerRadius: new CornerRadius(0, Radius, Radius, 0));
    }

    /// <summary>
    /// Builds a layout with a full-height left slot and a vertically split right column.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildRightColumnSplit(Grid host, LayoutBuildContext context)
    {
        AddStarRows(host, 1, 1);
        AddStarColumns(host, 1, 1);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, rowSpan: 2, margin: new Thickness(0, 0, Gap / 2, 0), cornerRadius: new CornerRadius(Radius, 0, 0, Radius));
        AddCell(host, context, slotIndex: 1, row: 0, column: 1, margin: new Thickness(Gap / 2, 0, 0, Gap / 2), cornerRadius: new CornerRadius(0, Radius, 0, 0));
        AddCell(host, context, slotIndex: 2, row: 1, column: 1, margin: new Thickness(Gap / 2, Gap / 2, 0, 0), cornerRadius: new CornerRadius(0, 0, Radius, 0));
    }

    /// <summary>
    /// Builds a layout with a vertically split left column and a full-height right slot.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildLeftColumnSplit(Grid host, LayoutBuildContext context)
    {
        AddStarRows(host, 1, 1);
        AddStarColumns(host, 1, 1);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, margin: new Thickness(0, 0, Gap / 2, Gap / 2), cornerRadius: new CornerRadius(Radius, 0, 0, 0));
        AddCell(host, context, slotIndex: 1, row: 1, column: 0, margin: new Thickness(0, Gap / 2, Gap / 2, 0), cornerRadius: new CornerRadius(0, 0, 0, Radius));
        AddCell(host, context, slotIndex: 2, row: 0, column: 1, rowSpan: 2, margin: new Thickness(Gap / 2, 0, 0, 0), cornerRadius: new CornerRadius(0, Radius, Radius, 0));
    }

    /// <summary>
    /// Builds a two-by-two grid layout containing four equally sized slots.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildGrid2x2(Grid host, LayoutBuildContext context)
    {
        AddStarRows(host, 1, 1);
        AddStarColumns(host, 1, 1);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, margin: new Thickness(0, 0, Gap / 2, Gap / 2), cornerRadius: new CornerRadius(Radius, 0, 0, 0));
        AddCell(host, context, slotIndex: 1, row: 0, column: 1, margin: new Thickness(Gap / 2, 0, 0, Gap / 2), cornerRadius: new CornerRadius(0, Radius, 0, 0));
        AddCell(host, context, slotIndex: 2, row: 1, column: 0, margin: new Thickness(0, Gap / 2, Gap / 2, 0), cornerRadius: new CornerRadius(0, 0, 0, Radius));
        AddCell(host, context, slotIndex: 3, row: 1, column: 1, margin: new Thickness(Gap / 2, Gap / 2, 0, 0), cornerRadius: new CornerRadius(0, 0, Radius, 0));
    }

    /// <summary>
    /// Builds a single-row layout with three equally sized columns.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildThreeColumnsEqualGrid(Grid host, LayoutBuildContext context)
    {
        BuildThreeColumns(host, context, leftWeight: 1, middleWeight: 1, rightWeight: 1);
    }

    /// <summary>
    /// Builds a generic three-column layout with configurable column weights.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    /// <param name="leftWeight">The star weight of the left column.</param>
    /// <param name="middleWeight">The star weight of the middle column.</param>
    /// <param name="rightWeight">The star weight of the right column.</param>
    private static void BuildThreeColumns(Grid host, LayoutBuildContext context, double leftWeight, double middleWeight, double rightWeight)
    {
        AddStarRows(host, 1);
        AddStarColumns(host, leftWeight, middleWeight, rightWeight);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, margin: new Thickness(0, 0, Gap / 2, 0), cornerRadius: new CornerRadius(Radius, 0, 0, Radius));
        AddCell(host, context, slotIndex: 1, row: 0, column: 1, margin: new Thickness(Gap / 2, 0, Gap / 2, 0), cornerRadius: new CornerRadius(0));
        AddCell(host, context, slotIndex: 2, row: 0, column: 2, margin: new Thickness(Gap / 2, 0, 0, 0), cornerRadius: new CornerRadius(0, Radius, Radius, 0));
    }

    /// <summary>
    /// Builds a three-column layout with a full-height wide left slot (50%) and a full-height middle slot
    /// (25%); the right column (25%) is split into two stacked slots.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildWideLeftRightColumnSplit(Grid host, LayoutBuildContext context)
    {
        AddStarRows(host, 1, 1);
        AddStarColumns(host, 2, 1, 1);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, rowSpan: 2, margin: new Thickness(0, 0, Gap / 2, 0), cornerRadius: new CornerRadius(Radius, 0, 0, Radius));
        AddCell(host, context, slotIndex: 1, row: 0, column: 1, rowSpan: 2, margin: new Thickness(Gap / 2, 0, Gap / 2, 0), cornerRadius: new CornerRadius(0));
        AddCell(host, context, slotIndex: 2, row: 0, column: 2, margin: new Thickness(Gap / 2, 0, 0, Gap / 2), cornerRadius: new CornerRadius(0, Radius, 0, 0));
        AddCell(host, context, slotIndex: 3, row: 1, column: 2, margin: new Thickness(Gap / 2, Gap / 2, 0, 0), cornerRadius: new CornerRadius(0, 0, Radius, 0));
    }

    /// <summary>
    /// Builds a three-column layout with a full-height wide left slot (50%) and a full-height right slot
    /// (25%); the middle column (25%) is split into two stacked slots. Mirror of
    /// <see cref="BuildWideLeftRightColumnSplit"/> with the second and third columns swapped.
    /// </summary>
    /// <param name="host">The target grid where the layout will be rendered.</param>
    /// <param name="context">The build context containing the brushes to apply.</param>
    private static void BuildWideLeftMiddleColumnSplit(Grid host, LayoutBuildContext context)
    {
        AddStarRows(host, 1, 1);
        AddStarColumns(host, 2, 1, 1);
        AddCell(host, context, slotIndex: 0, row: 0, column: 0, rowSpan: 2, margin: new Thickness(0, 0, Gap / 2, 0), cornerRadius: new CornerRadius(Radius, 0, 0, Radius));
        AddCell(host, context, slotIndex: 1, row: 0, column: 1, margin: new Thickness(Gap / 2, 0, Gap / 2, Gap / 2), cornerRadius: new CornerRadius(0));
        AddCell(host, context, slotIndex: 2, row: 1, column: 1, margin: new Thickness(Gap / 2, Gap / 2, Gap / 2, 0), cornerRadius: new CornerRadius(0));
        AddCell(host, context, slotIndex: 3, row: 0, column: 2, rowSpan: 2, margin: new Thickness(Gap / 2, 0, 0, 0), cornerRadius: new CornerRadius(0, Radius, Radius, 0));
    }

    #endregion
}
