using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MM4LB.Models;

// ----------------------------------------------------------------------------------------------------
// Single home for the statistics model types produced by IStatisticsService, to keep the Models folder from
// exploding into one file per helper. Three layers:
//   - Stat            : the primitive (one metric: label, description, value/total).
//   - Stats           : a VARIABLE-cardinality list of Stat, for groups whose items/labels are dynamic
//                       (region / extension / dimensions breakdowns).
//   - GameAuditStats  : a FIXED-cardinality result exposing its metrics as NAMED Stat properties, so widgets
//                       bind by name (compile-checked, immune to reordering) instead of by list index.
//   - PlatformImageStats / GlobalImageStats : raw image aggregates (count / distinct types / size) for one
//                       platform or summed across all; the widgets compose and format the pills from them.
// Add new stats helper types here.
// ----------------------------------------------------------------------------------------------------

/// <summary>
/// Holds a single statistic (used by the game and image statistics components).
/// </summary>
public class Stat
{
    public SolidColorBrush Color { get; set; } = new SolidColorBrush(new Color() { R = 50, G = 161, B = 39, A = 255 });
    public int Progress => Value > 0 && Total > 0 ? Value * 100 / Total : 0;

    /// <summary>
    /// The pill label (e.g. "In my collection"). Owned by the producing <see cref="IStatisticsService"/> method.
    /// </summary>
    public string Title
    {
        get; set;
    } = string.Empty;

    /// <summary>
    /// The pill secondary line, explaining what the "value / total" ratio means (e.g. "Platform / All").
    /// Owned by the producing <see cref="IStatisticsService"/> method so the UI no longer hardcodes it.
    /// </summary>
    public string Description
    {
        get; set;
    } = string.Empty;

    /// <summary>
    /// The "value / total" text shown by the pill, formatted by the producing <see cref="IStatisticsService"/>
    /// method (so the controls only display it). Defaults to the plain integer ratio "Value / Total"; size-style
    /// pills override it with their own formatted text (e.g. "1.2 GB / 3.4 GB"), which the int Value/Total cannot
    /// represent.
    /// </summary>
    public string ValueText
    {
        get => _valueText ?? $"{Value} / {Total}";
        set => _valueText = value;
    }
    private string? _valueText;

    public int Total
    {
        get; set;
    }
    public int Value
    {
        get; set;
    }
}

/// <summary>
/// A variable-length list of <see cref="Stat"/> instances, for statistics groups whose item count and labels
/// are not known up front (region / extension / dimensions breakdowns). Fixed groups use a named result such as
/// <see cref="GameAuditStats"/> instead.
/// </summary>
public class Stats
{
    public string Name
    {
        get; set;
    } = string.Empty;
    public int Count
    {
        get; set;
    }
    public List<Stat> Items { get; } = new();
}

/// <summary>
/// Games-audit pills (in collection / in LaunchBox / not in LaunchBox) as named <see cref="Stat"/> properties.
/// Each <see cref="Stat"/> carries its own label, description and value/total. Siempre las fija el productor
/// (<see cref="IStatisticsService"/>) en el object initializer, de ahí el <c>= null!</c>.
/// </summary>
public sealed class GameAuditStats
{
    public Stat InCollection { get; init; } = null!;
    public Stat InLaunchBox { get; init; } = null!;
    public Stat NotInLaunchBox { get; init; } = null!;
}

/// <summary>
/// Image-audit pills (most common extension / dimensions / region, plus the no-region bucket) as named
/// <see cref="Stat"/> properties. Each title is data-dependent (e.g. "[png] as extension"). Bound by name so a
/// reorder/rename fails at compile time. <see cref="Quality"/> (most common video height, "[1080p] as quality")
/// and <see cref="Duration"/> (most common 10s duration range) only make sense for video sets and are shown by
/// the UI in place of the region pills for them; they are always computed (cheap) and ignored for image sets.
/// </summary>
public sealed class ImageAuditStats
{
    public Stat Extension { get; init; } = null!;
    public Stat Dimensions { get; init; } = null!;
    public Stat Region { get; init; } = null!;
    public Stat NoRegion { get; init; } = null!;
    public Stat Quality { get; init; } = null!;
    public Stat Duration { get; init; } = null!;
}

/// <summary>
/// Folder-import pills as named <see cref="Stat"/> properties: the most common extension / dimensions of the
/// externally-loaded collection (shared with <see cref="ImageAuditStats"/>), plus how many of its images matched
/// a game and how many games got a match. Same first two pills as the audit, last two are import-specific (the
/// games one divides by the total games, not images).
/// </summary>
public sealed class FolderImportStats
{
    public Stat Extension { get; init; } = null!;
    public Stat Dimensions { get; init; } = null!;
    public Stat MatchedImages { get; init; } = null!;
    public Stat MatchedGames { get; init; } = null!;
}

/// <summary>
/// Display-ready image pills (number of images / distinct image types / on-disk size) as named <see cref="Stat"/>
/// properties: label, description and a fully formatted <see cref="Stat.ValueText"/> ("X / Y", or "1.2 GB / 3.4 GB"
/// for size). Produced by <see cref="IStatisticsService"/> for a given scope (game-vs-platform or platform-vs-all)
/// so the controls only bind and display — no composing or formatting in the view models.
/// </summary>
public sealed class ImagePills
{
    public Stat Images { get; init; } = null!;
    public Stat ImageTypes { get; init; } = null!;
    public Stat Size { get; init; } = null!;
}

/// <summary>
/// Image aggregates of a single platform (only image types, key &lt; 100), over its already-scanned image sets:
/// number of image files, number of distinct types that actually have images, and on-disk size in KB. Raw data
/// (the widgets format/compose the pills); <c>SizeKb</c> is a <see cref="long"/> because a platform's images can
/// exceed <see cref="int"/>. Dynamic: changes on image add/delete, so it is NOT memoized (cheap in-memory sum).
/// </summary>
public sealed class PlatformImageStats
{
    public int ImageCount { get; init; }
    public int ImageTypeCount { get; init; }
    public long SizeKb { get; init; }
}

/// <summary>
/// Image aggregates summed across a set of platforms: total image files, distinct image types present in ANY
/// platform (the union, not the sum of per-platform type counts), total on-disk size in KB, and total game count.
/// Used as the "/ All" denominator of the platform-vs-all pills. Dynamic; not memoized.
/// </summary>
public sealed class GlobalImageStats
{
    public int ImageCount { get; init; }
    public int ImageTypeCount { get; init; }
    public long SizeKb { get; init; }
    public int GameCount { get; init; }
}
