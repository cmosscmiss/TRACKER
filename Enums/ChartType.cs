namespace MM4LB.Enums;

/// <summary>
/// Visual type used to render a chart, chosen per chart with its own chart-type toolbar (the toolbar lives inside
/// <c>ChartTypeSelectorControl</c>, which also hosts the chart it drives). <see cref="Column"/> is the default.
/// <see cref="Pie"/>/<see cref="Doughnut"/> render on a pie chart (no axes); the rest render on a cartesian chart.
/// </summary>
public enum ChartType
{
    /// <summary>Vertical bars (the default).</summary>
    Column,

    /// <summary>Horizontal bars.</summary>
    Row,

    /// <summary>A line joining the points.</summary>
    Line,

    /// <summary>A line with the area below it filled.</summary>
    Area,

    /// <summary>Pie slices (no axes).</summary>
    Pie,

    /// <summary>Pie slices with a hollow centre.</summary>
    Doughnut,
}
