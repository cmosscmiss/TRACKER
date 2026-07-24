using System.Collections.Generic;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Contracts.Services;

public interface IStatisticsService
{
    /// <summary>
    /// Builds the per-game image statistics shown by the game image stats widget: total images, distinct
    /// image types present, and coverage of the favourite type set (only image types, key&lt;100, are
    /// considered).
    /// </summary>
    Stats GetGameImageStatistics(Game game, IReadOnlyCollection<MediaType> favourites);

    /// <summary>
    /// Display-ready image pills (images / image types / size) for the game-images gallery, scoped game-vs-platform.
    /// Labels, descriptions and the adaptively formatted size text are produced here; the gallery only displays them.
    /// </summary>
    ImagePills GetGameImagePills(Game game, Platform platform);

    /// <summary>
    /// Display-ready image pills (images / image types / size) for the platform-level widgets, scoped
    /// platform-vs-all. Labels, descriptions and the adaptively formatted size text are produced here.
    /// </summary>
    ImagePills GetPlatformImagePills(Platform? platform, IReadOnlyCollection<Platform> platforms);

    /// <summary>
    /// Display-ready "Games" pill for the platform widget: the platform's game count / the total across all
    /// platforms ("Platform / All"). Label and description are owned here.
    /// </summary>
    Stat GetPlatformGamesPill(Platform platform, IReadOnlyCollection<Platform> platforms);

    /// <summary>
    /// Per-platform games-audit statistics (in collection / in LB DB / not in LB DB) over the platform's full
    /// audit set (<see cref="Platform.GamesInLauchboxDb"/>). Memoized per platform (the set never changes after
    /// load). Label and description are owned here, not in the UI.
    /// </summary>
    GameAuditStats GetGameCollectionStatistics(Platform platform);

    /// <summary>
    /// Whole-collection games-audit pills for the global stats widget: per metric, <c>Value</c> is the selected
    /// platform's count and <c>Total</c> is the sum across every platform. The grand totals are memoized and
    /// auto-invalidated when <paramref name="platforms"/> is a different instance (platform set reloaded).
    /// </summary>
    GameAuditStats GetGlobalGameCollectionStatistics(IReadOnlyCollection<Platform> platforms, Platform? selectedPlatform);

    /// <summary>
    /// Builds the statistics shown by the image audit grid over the images it currently displays
    /// (most common extension and dimensions, plus region breakdown).
    /// </summary>
    ImageAuditStats GetImageCollectionStatistics(IReadOnlyCollection<GameImage> images);

    /// <summary>
    /// Builds the statistics shown by the folder-import grid over the imported images (most common
    /// extension and dimensions, plus how many images/games were matched against the collection).
    /// </summary>
    FolderImportStats GetFolderImportStatistics(IReadOnlyCollection<GameImage> images, int matchedImagesCount, int matchedGamesCount, int totalGames);

    /// <summary>
    /// Total number of image files in the platform per media type (type key -> image count), summed across
    /// every image set of each type. Pure aggregation over the already-scanned image sets.
    /// </summary>
    Dictionary<int, int> GetPlatformImageCountsByType(Platform platform);

    /// <summary>
    /// Coverage ratio (0..1) of a platform's OWN special image types: the fraction of the platform-level types
    /// (the 7 platform image types + the platform video) it has at least one own image of. The platform's own
    /// artwork managed by the platform details, not game images. Pure in-memory read; background-thread safe.
    /// </summary>
    double GetPlatformOwnImageCoverageRatio(Platform platform);

    /// <summary>
    /// For each special platform-level image type (key -> count), the number of platforms that have at least one
    /// OWN image of that type. Used to chart the share of platforms covering each own image type. Pure read.
    /// </summary>
    Dictionary<int, int> GetOwnImagePlatformCountByType(IReadOnlyCollection<Platform> platforms);

    /// <summary>
    /// For each special platform-level image type (key -> count), the TOTAL number of OWN image files across all
    /// platforms. Used to chart how populated each own image type is. Pure read.
    /// </summary>
    Dictionary<int, int> GetOwnImageFileCountByType(IReadOnlyCollection<Platform> platforms);

    /// <summary>
    /// Image aggregates of a single platform (only image types, key &lt; 100): image file count, number of distinct
    /// types that have images, and on-disk size (KB), in one pass over the already-scanned image sets. Pure read.
    /// </summary>
    PlatformImageStats GetPlatformImageStats(Platform? platform);

    /// <summary>
    /// Image aggregates summed across every platform in <paramref name="platforms"/> (total images, distinct types
    /// present in any platform, total size in KB, total games). The "/ All" denominator of the platform pills.
    /// </summary>
    GlobalImageStats GetGlobalImageStats(IReadOnlyCollection<Platform> platforms);

    /// <summary>
    /// Coverage of every game of the platform over a given set of image <paramref name="types"/> (0..1 ratio per
    /// game, in the platform's order): the fraction of those types each game has at least one matching image for.
    /// The type set is the caller's (favourites or present types). Pure read; background-thread safe.
    /// </summary>
    IReadOnlyList<(Game Game, double Coverage)> GetPlatformCoveragePerGame(Platform platform, IReadOnlyCollection<MediaType> types);

    /// <summary>
    /// Average favourite-type coverage across every game of the platform (0..1); convenience over
    /// <see cref="GetTypeCoverageRatio"/> with the favourites scope. Pure read; background-thread safe.
    /// </summary>
    double GetPlatformAverageFavouriteCoverage(Platform platform, IReadOnlyCollection<MediaType> favourites);

    /// <summary>
    /// Canonical platform coverage ratio (0..1) for a type scope, from the per-type "games covered" counts
    /// (<see cref="GetPlatformGameCountByImageType"/>). Every widget derives platform/global coverage from here so
    /// they never disagree. Favourites scope divides by the favourite count; Present scope by the present-type count.
    /// </summary>
    double GetTypeCoverageRatio(IReadOnlyDictionary<int, int> gameCountByType, int totalGames, CoverageTypeScope scope, IReadOnlyCollection<MediaType> favourites);

    /// <summary>Formats a 0..1 coverage ratio as an integer percentage (e.g. "62%").</summary>
    string FormatPercent(double ratio);

    /// <summary>
    /// For every image type (key&lt;100) the platform has an image set for, the number of distinct games of the
    /// platform with at least one matching image of that type (divide by <c>platform.Games.Count</c> for the
    /// per-type coverage). Pure read; background-thread safe.
    /// </summary>
    IReadOnlyDictionary<int, int> GetPlatformGameCountByImageType(Platform platform);

    /// <summary>
    /// Number of an image set's files per file extension (lowercase, no dot; e.g. "png"). Derived from the scanned
    /// file paths; no decoding. Pure read; background-thread safe.
    /// </summary>
    IReadOnlyDictionary<string, int> GetImageSetCountByExtension(PlatformImageSet imageSet);

    /// <summary>
    /// Number of an image set's files per region, including a "no region" bucket (<c>ImageRegion.NoRegion.Value</c>).
    /// The region is the image's leaf folder name (as in <see cref="GameImage"/>), derived from the scanned file
    /// paths; no decoding. Pure read; background-thread safe.
    /// </summary>
    IReadOnlyDictionary<string, int> GetImageSetCountByRegion(PlatformImageSet imageSet);

    /// <summary>
    /// Number of an image set's already-decoded images per "WxH" dimensions. Images whose dimensions have not been
    /// decoded yet are NOT included (the caller treats them as "Unknown" = total − sum). Pure read.
    /// </summary>
    IReadOnlyDictionary<string, int> GetImageSetCountByDimensions(PlatformImageSet imageSet);

    /// <summary>
    /// Number of a video set's already-measured items per quality (vertical resolution, e.g. "1080p"). Videos whose
    /// height has not been read yet are NOT included (the caller treats them as "Unknown" = total − sum). Pure read.
    /// </summary>
    IReadOnlyDictionary<string, int> GetImageSetCountByQuality(PlatformImageSet imageSet);

    /// <summary>
    /// Number of a video set's already-measured items per 10-second duration range ("0-10s", "11-20s"…). Videos whose
    /// duration has not been read yet are NOT included (the caller treats them as "Unknown" = total − sum). Pure read.
    /// </summary>
    IReadOnlyDictionary<string, int> GetImageSetCountByDurationRange(PlatformImageSet imageSet);
}
