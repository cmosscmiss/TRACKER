namespace Tracker.Enums;

/// <summary>
/// How a chart's items are ordered (chosen per chart with its sort toolbar). <see cref="None"/> keeps the data's
/// original order; the others order the items by their value.
/// </summary>
public enum SortMode
{
    /// <summary>Original order of the data (no sorting).</summary>
    None,

    /// <summary>Ascending by value (smallest first).</summary>
    Ascending,

    /// <summary>Descending by value (largest first).</summary>
    Descending,
}
