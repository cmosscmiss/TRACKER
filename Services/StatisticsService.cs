using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MM4LB.Contracts.Services;
using MM4LB.Enums;
using MM4LB.Helpers;
using MM4LB.Models;

namespace MM4LB.Services;

public sealed class StatisticsService : IStatisticsService
{
    /// <summary>Texto localizado de un pill (o la clave si aún no hay servicio). Los stats se recomputan al cambiar los datos.</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;

    /// <summary>Texto localizado con formato (p. ej. "[{0}] as region").</summary>
    private static string F(string key, params object[] args)
        => LocalizationService.Instance is LocalizationService loc ? loc.Format(key, args) : string.Format(key, args);

    // ----------------------------------------------------
    // Game-audit caches (singleton service). The audit games of a platform (Platform.GamesInLauchboxDb) are
    // filled once at load and never change afterwards, so their statistics are memoized for the app's lifetime.
    // The global totals are invalidated automatically when the platform set is replaced (reload): the cached
    // reference no longer matches the one passed in, triggering a recompute.
    // ----------------------------------------------------
    private readonly Dictionary<Platform, GameAuditStats> _gameCollectionStatsByPlatform = new();
    private IReadOnlyCollection<Platform>? _globalGameTotalsPlatformsRef;
    private (int Collection, int Launchbox, int NotInLb) _globalGameTotals;

    public Stats GetGameImageStatistics(Game game, IReadOnlyCollection<MediaType> favourites)
    {
        Stats stats = new() { Name = "GameImages" };

        // Only real images (key < 100) count; videos, manuals and music are out of scope here.
        List<GameImage> images = game?.AllImages?
            .Where(i => i.Type != null && MediaType.IsImage(i.Type.Key))
            .ToList() ?? new List<GameImage>();

        int total = images.Count;
        // La lista ya está filtrada a Type != null arriba, de ahí el '!'.
        HashSet<int> presentTypeKeys = images.Select(i => i.Type!.Key).ToHashSet();

        int favouritesTotal = favourites?.Count ?? 0;
        int favouritesPresent = favourites?.Count(t => presentTypeKeys.Contains(t.Key)) ?? 0;

        // Fixed item order (the widget binds by index): 0 total, 1 distinct types, 2 favourites.
        stats.Items.Add(CreateStat("Total images", total, total));
        stats.Items.Add(CreateStat(L(LocKeys.Stats_ImageTypes_Title), presentTypeKeys.Count, presentTypeKeys.Count));
        stats.Items.Add(CreateStat("Favourite types", favouritesPresent, favouritesTotal));

        return stats;
    }

    /// <summary>
    /// Per-platform games-audit statistics (in collection / in LB DB / not in LB DB) over the platform's full
    /// audit set (<see cref="Platform.GamesInLauchboxDb"/> = LB DB games + collection orphans). The result is
    /// memoized per platform: this set is filled once at load and never changes, so the counts are computed only
    /// once. <c>Total</c> is the audit-set size; the label/description are owned here, not in the UI.
    /// </summary>
    public GameAuditStats GetGameCollectionStatistics(Platform platform)
    {
        if (platform == null)
            return BuildGameCollectionStats(System.Array.Empty<Game>());

        if (_gameCollectionStatsByPlatform.TryGetValue(platform, out GameAuditStats? cached))
            return cached;

        GameAuditStats stats = BuildGameCollectionStats(platform.GamesInLauchboxDb);
        _gameCollectionStatsByPlatform[platform] = stats;
        return stats;
    }

    /// <summary>
    /// Whole-collection games-audit pills shown by the global stats widget: per metric, <c>Value</c> is the
    /// selected platform's count and <c>Total</c> is the sum across every platform ("Platform / All"). The grand
    /// totals are memoized and reused from the per-platform cache; they are recomputed only when
    /// <paramref name="platforms"/> is a different instance (platform set reloaded). Building the three-item
    /// result per call is cheap (no game iteration once cached).
    /// </summary>
    public GameAuditStats GetGlobalGameCollectionStatistics(IReadOnlyCollection<Platform> platforms, Platform? selectedPlatform)
    {
        (int totCollection, int totLaunchbox, int totNotInLb) = GetGlobalGameTotals(platforms);

        int selCollection = 0, selLaunchbox = 0, selNotInLb = 0;
        if (selectedPlatform != null)
        {
            GameAuditStats selected = GetGameCollectionStatistics(selectedPlatform);
            selCollection = selected.InCollection.Value;
            selLaunchbox = selected.InLaunchBox.Value;
            selNotInLb = selected.NotInLaunchBox.Value;
        }

        return new GameAuditStats
        {
            InCollection = new Stat { Title = L(LocKeys.Stats_InMyCollection_Title), Description = L(LocKeys.Common_PlatformAll_Description), Value = selCollection, Total = totCollection },
            InLaunchBox = new Stat { Title = L(LocKeys.Stats_InLaunchBox_Title), Description = L(LocKeys.Common_PlatformAll_Description), Value = selLaunchbox, Total = totLaunchbox },
            NotInLaunchBox = new Stat { Title = L(LocKeys.Stats_NotInLaunchBox_Title), Description = L(LocKeys.Common_PlatformAll_Description), Value = selNotInLb, Total = totNotInLb }
        };
    }

    /// <summary>
    /// Grand totals (summed over every platform) of the three games-audit metrics, memoized. Recomputed only when
    /// the <paramref name="platforms"/> instance changes (set reloaded), reusing the per-platform cache.
    /// </summary>
    private (int Collection, int Launchbox, int NotInLb) GetGlobalGameTotals(IReadOnlyCollection<Platform> platforms)
    {
        if (platforms == null)
            return (0, 0, 0);

        if (ReferenceEquals(_globalGameTotalsPlatformsRef, platforms))
            return _globalGameTotals;

        int collection = 0, launchbox = 0, notInLb = 0;
        foreach (Platform platform in platforms)
        {
            GameAuditStats stats = GetGameCollectionStatistics(platform);
            collection += stats.InCollection.Value;
            launchbox += stats.InLaunchBox.Value;
            notInLb += stats.NotInLaunchBox.Value;
        }

        _globalGameTotals = (collection, launchbox, notInLb);
        _globalGameTotalsPlatformsRef = platforms;
        return _globalGameTotals;
    }

    /// <summary>
    /// Pure builder of the three games-audit stats over an explicit set of games (no caching), as a typed result
    /// whose metrics are exposed by name (in collection / in LaunchBox / not in LaunchBox).
    /// </summary>
    private static GameAuditStats BuildGameCollectionStats(IReadOnlyCollection<Game> games)
    {
        int total = games.Count;

        return new GameAuditStats
        {
            InCollection = CreateStat(
                title: L(LocKeys.Stats_InMyCollection_Title),
                description: "Matching / total",
                value: games.Count(x => x.InCollection),
                total: total),
            InLaunchBox = CreateStat(
                title: L(LocKeys.Stats_InLaunchBox_Title),
                description: "Matching / total",
                value: games.Count(x => x.InLaunchboxDb),
                total: total),
            NotInLaunchBox = CreateStat(
                title: L(LocKeys.Stats_NotInLaunchBox_Title),
                description: "Matching / total",
                value: games.Count(x => !x.InLaunchboxDb),
                total: total)
        };
    }

    public ImageAuditStats GetImageCollectionStatistics(IReadOnlyCollection<GameImage> images)
    {
        Stat extension = BuildMostCommonExtensionStat(images);
        Stat dimensions = BuildMostCommonDimensionsStat(images);
        Stat quality = BuildMostCommonQualityStat(images);
        Stat duration = BuildMostCommonDurationStat(images);

        // Most common region (excluding the images with no region).
        IGrouping<string, GameImage>? byRegion = images.GroupBy(x => x.Region.Value)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault(g => g.Key != ImageRegion.NoRegion.Value);
        Stat region = new()
        {
            Title = byRegion == null ? L(LocKeys.Stats_WithRegion_Title) : F(LocKeys.Stats_AsRegion_Format, byRegion.Key),
            Value = byRegion == null ? 0 : byRegion.Count(),
            Total = images.Count
        };

        // Images without region.
        Stat noRegion = new()
        {
            Title = L(LocKeys.Common_NoRegion_Label),
            Value = images.Count(x => x.Region == ImageRegion.NoRegion),
            Total = images.Count
        };

        // Every pill shows "matching that characteristic / total images"; the description is owned here.
        extension.Description = dimensions.Description = region.Description = noRegion.Description
            = quality.Description = duration.Description = L(LocKeys.Stats_MatchingTotal_Description);

        return new ImageAuditStats
        {
            Extension = extension,
            Dimensions = dimensions,
            Region = region,
            NoRegion = noRegion,
            Quality = quality,
            Duration = duration
        };
    }

    public FolderImportStats GetFolderImportStatistics(IReadOnlyCollection<GameImage> images, int matchedImagesCount, int matchedGamesCount, int totalGames)
    {
        Stat extension = BuildMostCommonExtensionStat(images);
        Stat dimensions = BuildMostCommonDimensionsStat(images);
        extension.Description = dimensions.Description = L(LocKeys.Stats_MatchingTotal_Description);

        // Images of the loaded folder that match at least a game (out of all loaded images).
        Stat matchedImages = new()
        {
            Title = L(LocKeys.Stats_MatchedImages_Title),
            Description = L(LocKeys.Stats_MatchedTotal_Description),
            Value = matchedImagesCount,
            Total = images.Count
        };

        // Games that got at least a matching image (out of all the platform's games).
        Stat matchedGames = new()
        {
            Title = L(LocKeys.Stats_GamesWithMatch_Title),
            Description = L(LocKeys.Stats_MatchedTotal_Description),
            Value = matchedGamesCount,
            Total = totalGames
        };

        return new FolderImportStats
        {
            Extension = extension,
            Dimensions = dimensions,
            MatchedImages = matchedImages,
            MatchedGames = matchedGames
        };
    }

    // ----------------------------------------------------
    // Platform-wide aggregations (pure reads of already-scanned models; background-thread safe)
    // ----------------------------------------------------

    /// <summary>
    /// Returns the total number of image files in the platform per media type (type key -> image count),
    /// summed across every image set of each type. Pure aggregation over the already-scanned image sets:
    /// it counts files on disk in each set's folder, not images matched to games.
    /// </summary>
    public Dictionary<int, int> GetPlatformImageCountsByType(Platform platform)
    {
        var counts = new Dictionary<int, int>();

        if (platform?.Images?.ImageSets == null)
            return counts;

        foreach (PlatformImageSet set in platform.Images.ImageSets)
        {
            if (set.Type == null)
                continue;

            counts.TryGetValue(set.Type.Key, out int current);
            counts[set.Type.Key] = current + set.ImagesCount;
        }

        return counts;
    }

    /// <summary>
    /// Coverage ratio (0..1) of a platform's OWN special image types: the fraction of the platform-level types
    /// (the 7 platform image types + the platform video) it has at least one own image of, from
    /// <see cref="Platform.OwnImages"/>. These are the platform's own artwork managed by the platform details, not
    /// game images. Pure in-memory read; background-thread safe.
    /// </summary>
    public double GetPlatformOwnImageCoverageRatio(Platform platform)
    {
        if (platform?.OwnImages == null)
            return 0;

        var presentTypes = new HashSet<int>();
        foreach (GameImage image in platform.OwnImages)
        {
            int? key = image.Type?.Key;
            if (key.HasValue && (MediaType.IsPlatformImage(key.Value) || MediaType.IsPlatformVideo(key.Value)))
                presentTypes.Add(key.Value);
        }

        int totalTypes = MediaType.PlatformImageTypes.Length + 1;   // + el vídeo de plataforma
        return totalTypes == 0 ? 0 : (double)presentTypes.Count / totalTypes;
    }

    /// <summary>
    /// For each special platform-level image type (key -> count), the number of platforms in
    /// <paramref name="platforms"/> that have at least one OWN image of that type. Used to chart the share of
    /// platforms covering each own image type. Pure in-memory read over <see cref="Platform.OwnImages"/>.
    /// </summary>
    public Dictionary<int, int> GetOwnImagePlatformCountByType(IReadOnlyCollection<Platform> platforms)
    {
        var counts = new Dictionary<int, int>();

        if (platforms == null)
            return counts;

        foreach (Platform platform in platforms)
        {
            if (platform?.OwnImages == null)
                continue;

            // Cada tipo cuenta como mucho una vez por plataforma (presencia), no por nº de ficheros.
            var seen = new HashSet<int>();
            foreach (GameImage image in platform.OwnImages)
            {
                int? key = image.Type?.Key;
                if (key.HasValue && (MediaType.IsPlatformImage(key.Value) || MediaType.IsPlatformVideo(key.Value)) && seen.Add(key.Value))
                {
                    counts.TryGetValue(key.Value, out int current);
                    counts[key.Value] = current + 1;
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// For each special platform-level image type (key -> count), the TOTAL number of OWN image files across all
    /// <paramref name="platforms"/> (a platform can have more than one file of a type when images are kept). Used
    /// to chart how populated each own image type is. Pure in-memory read over <see cref="Platform.OwnImages"/>.
    /// </summary>
    public Dictionary<int, int> GetOwnImageFileCountByType(IReadOnlyCollection<Platform> platforms)
    {
        var counts = new Dictionary<int, int>();

        if (platforms == null)
            return counts;

        foreach (Platform platform in platforms)
        {
            if (platform?.OwnImages == null)
                continue;

            foreach (GameImage image in platform.OwnImages)
            {
                int? key = image.Type?.Key;
                if (key.HasValue && (MediaType.IsPlatformImage(key.Value) || MediaType.IsPlatformVideo(key.Value)))
                {
                    counts.TryGetValue(key.Value, out int current);
                    counts[key.Value] = current + 1;
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// Image aggregates of a single platform (image AND video types): file count, number of distinct types that
    /// have files, and on-disk size in KB, in a single pass over the already-scanned image/video sets. Pure
    /// in-memory aggregation. Not memoized (changes on image add/delete; cheap).
    /// </summary>
    public PlatformImageStats GetPlatformImageStats(Platform? platform)
    {
        int imageCount = 0;
        long sizeKb = 0;
        var typeKeys = new HashSet<int>();
        AccumulatePlatformImages(platform, ref imageCount, ref sizeKb, typeKeys);

        return new PlatformImageStats
        {
            ImageCount = imageCount,
            ImageTypeCount = typeKeys.Count,
            SizeKb = sizeKb
        };
    }

    /// <summary>
    /// Image aggregates summed across every platform in <paramref name="platforms"/>: total image files, distinct
    /// image types present in ANY platform (union), total on-disk size (KB) and total game count. The "/ All"
    /// denominator of the platform-vs-all pills. Pure in-memory aggregation; not memoized.
    /// </summary>
    public GlobalImageStats GetGlobalImageStats(IReadOnlyCollection<Platform> platforms)
    {
        int imageCount = 0;
        long sizeKb = 0;
        int gameCount = 0;
        var typeKeys = new HashSet<int>();   // union of image types present across all platforms

        if (platforms != null)
            foreach (Platform platform in platforms)
            {
                gameCount += platform?.Games.Count ?? 0;
                AccumulatePlatformImages(platform, ref imageCount, ref sizeKb, typeKeys);
            }

        return new GlobalImageStats
        {
            ImageCount = imageCount,
            ImageTypeCount = typeKeys.Count,
            SizeKb = sizeKb,
            GameCount = gameCount
        };
    }

    /// <summary>
    /// Display-ready image pills for the game-images gallery: the selected game's images / image types / on-disk
    /// size against its platform's totals ("Game / Platform"). Labels, descriptions and the (adaptively formatted)
    /// size text are produced here so the gallery only binds and shows them.
    /// </summary>
    public ImagePills GetGameImagePills(Game game, Platform platform)
    {
        // Images and videos both count as image types; manuals and music are out of scope here.
        List<GameImage> images = game?.AllImages?
            .Where(i => i.Type != null && (MediaType.IsImage(i.Type.Key) || MediaType.IsVideo(i.Type.Key)))
            .ToList() ?? new List<GameImage>();

        int imageCount = images.Count;
        // La lista ya está filtrada a Type != null arriba, de ahí el '!'.
        int typeCount = images.Select(i => i.Type!.Key).Distinct().Count();
        long sizeKb = images.Sum(i => i.FileSize);

        PlatformImageStats plat = GetPlatformImageStats(platform);

        return new ImagePills
        {
            Images = new Stat { Title = L(LocKeys.Stats_Images_Title), Description = L(LocKeys.Common_GamePlatform_Description), Value = imageCount, Total = plat.ImageCount },
            ImageTypes = new Stat { Title = L(LocKeys.Stats_ImageTypes_Title), Description = L(LocKeys.Common_GamePlatform_Description), Value = typeCount, Total = plat.ImageTypeCount },
            Size = new Stat { Title = L(LocKeys.Stats_Size_Title), Description = L(LocKeys.Common_GamePlatform_Description), ValueText = $"{FormatSizeKb(sizeKb)} / {FormatSizeKb(plat.SizeKb)}" }
        };
    }

    /// <summary>
    /// Display-ready "Games" pill for the platform widget: the selected platform's game count / the total across
    /// all platforms ("Platform / All"). Label and description are owned here.
    /// </summary>
    public Stat GetPlatformGamesPill(Platform platform, IReadOnlyCollection<Platform> platforms)
    {
        int platformGames = platform?.Games.Count ?? 0;

        int totalGames = 0;
        if (platforms != null)
            foreach (Platform p in platforms)
                totalGames += p?.Games.Count ?? 0;

        return new Stat { Title = L(LocKeys.Stats_Games_Title), Description = L(LocKeys.Common_PlatformAll_Description), Value = platformGames, Total = totalGames };
    }

    /// <summary>
    /// Display-ready image pills for the platform-level widgets (StatsPlatform, StatsGlobal): the selected
    /// platform's images / image types / size against the totals of all platforms ("Platform / All"). Labels,
    /// descriptions and the (adaptively formatted) size text are produced here.
    /// </summary>
    public ImagePills GetPlatformImagePills(Platform? platform, IReadOnlyCollection<Platform> platforms)
    {
        PlatformImageStats plat = GetPlatformImageStats(platform);
        GlobalImageStats all = GetGlobalImageStats(platforms);

        return new ImagePills
        {
            Images = new Stat { Title = L(LocKeys.Stats_MediaSet_Title), Description = L(LocKeys.Common_PlatformAll_Description), Value = plat.ImageCount, Total = all.ImageCount },
            ImageTypes = new Stat { Title = L(LocKeys.Stats_MediaTypes_Title), Description = L(LocKeys.Common_PlatformAll_Description), Value = plat.ImageTypeCount, Total = all.ImageTypeCount },
            Size = new Stat { Title = L(LocKeys.Stats_MediaSetSize_Title), Description = L(LocKeys.Common_PlatformAll_Description), ValueText = $"{FormatSizeKb(plat.SizeKb)} / {FormatSizeKb(all.SizeKb)}" }
        };
    }

    /// <summary>
    /// Formats a size given in KB adaptively: KB below 1 MB, MB below 1 GB, otherwise GB (one decimal for MB/GB).
    /// Single home for size formatting so every size pill reads the same.
    /// </summary>
    private static string FormatSizeKb(long kilobytes)
    {
        if (kilobytes <= 0)
            return "0 KB";
        if (kilobytes < 1024)
            return $"{kilobytes} KB";

        double megabytes = kilobytes / 1024.0;
        if (megabytes < 1024)
            return $"{megabytes:0.#} MB";

        return $"{megabytes / 1024.0:0.#} GB";
    }

    /// <summary>
    /// Adds a platform's image aggregates (image AND video types) into the running totals: file count and on-disk
    /// size over its already-scanned image/video sets, plus the keys of the types that actually have files
    /// (ImagesCount &gt; 0) into <paramref name="typeKeys"/>. Videos are first-class image types now, so they count
    /// toward the image-set pills; manual/music sets contribute nothing. Pure read.
    /// </summary>
    private static void AccumulatePlatformImages(Platform? platform, ref int imageCount, ref long sizeKb, HashSet<int> typeKeys)
    {
        if (platform?.Images?.ImageSets == null)
            return;

        foreach (PlatformImageSet set in platform.Images.ImageSets)
        {
            if (set.Type == null || !(MediaType.IsImage(set.Type.Key) || MediaType.IsVideo(set.Type.Key)))
                continue;

            imageCount += set.ImagesCount;
            sizeKb += set.SizeOnDiskKb;
            if (set.ImagesCount > 0)
                typeKeys.Add(set.Type.Key);
        }
    }

    /// <summary>
    /// Computes the coverage of every game of the platform over a given set of image <paramref name="types"/>, as a
    /// 0..1 ratio per game: the fraction of those types each game has at least one matching image for. The type set
    /// is the caller's choice (favourites or the types present in the platform), hence the generic name. Games are
    /// returned in the platform's own order. Pure read of the already-scanned image sets and the games' search
    /// strings — mutates nothing — so it is safe on a background thread (unlike <c>ImageLoadingService.MatchGameImages</c>).
    /// </summary>
    public IReadOnlyList<(Game Game, double Coverage)> GetPlatformCoveragePerGame(Platform platform, IReadOnlyCollection<MediaType> types)
    {
        if (platform == null || types == null || types.Count == 0 || platform.Games.Count == 0)
            return Array.Empty<(Game, double)>();

        HashSet<int> typeKeys = types.Select(t => t.Key).ToHashSet();
        int typeTotal = typeKeys.Count;

        // Reverse index: each search string -> the games that declare it. Built once so every matched image
        // file maps back to its game(s) in O(1) instead of rescanning every game for each file.
        var gamesBySearchString = new Dictionary<string, List<Game>>();
        foreach (Game game in platform.Games)
        {
            foreach (string searchString in game.SearchStrings)
            {
                if (!gamesBySearchString.TryGetValue(searchString, out List<Game>? games))
                {
                    games = new List<Game>();
                    gamesBySearchString[searchString] = games;
                }
                games.Add(game);
            }
        }

        // For each game, the set of favourite type keys it has at least one matching image for.
        var coveredTypesByGame = new Dictionary<Game, HashSet<int>>();

        foreach (PlatformImageSet set in platform.Images.ImageSets)
        {
            if (set.Type == null || !typeKeys.Contains(set.Type.Key))
                continue;

            int typeKey = set.Type.Key;
            IReadOnlyList<string> files = set.GetLowercaseFiles();

            for (int i = 0; i < files.Count; i++)
            {
                if (!gamesBySearchString.TryGetValue(files[i], out List<Game>? games))
                    continue;

                foreach (Game game in games)
                {
                    if (!coveredTypesByGame.TryGetValue(game, out HashSet<int>? covered))
                    {
                        covered = new HashSet<int>();
                        coveredTypesByGame[game] = covered;
                    }
                    covered.Add(typeKey);
                }
            }
        }

        var perGame = new List<(Game Game, double Coverage)>(platform.Games.Count);
        foreach (Game game in platform.Games)
        {
            int covered = coveredTypesByGame.TryGetValue(game, out HashSet<int>? coveredSet) ? coveredSet.Count : 0;
            perGame.Add((game, (double)covered / typeTotal));
        }

        return perGame;
    }

    /// <summary>
    /// Average favourite-type coverage across every game of the platform (0..1). Convenience over the canonical
    /// <see cref="GetTypeCoverageRatio"/> with the <see cref="CoverageTypeScope.Favourites"/> scope, so it yields
    /// the exact same number the platform widgets show for favourites. Pure read; safe on a background thread.
    /// </summary>
    public double GetPlatformAverageFavouriteCoverage(Platform platform, IReadOnlyCollection<MediaType> favourites)
    {
        if (platform == null || favourites == null || favourites.Count == 0 || platform.Games.Count == 0)
            return 0;

        IReadOnlyDictionary<int, int> countByType = GetPlatformGameCountByImageType(platform);
        return GetTypeCoverageRatio(countByType, platform.Games.Count, CoverageTypeScope.Favourites, favourites);
    }

    /// <summary>
    /// Canonical platform coverage ratio (0..1) for a type scope, from the per-type "games covered" counts
    /// (<see cref="GetPlatformGameCountByImageType"/>): the average over the platform's games of the fraction of
    /// in-scope types each game has at least one image of. It equals both the mean of the per-game coverages and
    /// (Σ covered-incidences) / (types-in-scope × games) — every widget derives platform/global coverage from
    /// here so they never disagree. <see cref="CoverageTypeScope.Favourites"/> divides by the favourite count
    /// (absent favourites count 0); <see cref="CoverageTypeScope.Present"/> divides by the present-type count.
    /// </summary>
    public double GetTypeCoverageRatio(IReadOnlyDictionary<int, int> gameCountByType, int totalGames, CoverageTypeScope scope, IReadOnlyCollection<MediaType> favourites)
    {
        if (gameCountByType == null || totalGames == 0)
            return 0;

        if (scope == CoverageTypeScope.Present)
        {
            int presentTypes = gameCountByType.Count;
            if (presentTypes == 0)
                return 0;

            int covered = 0;
            foreach (int c in gameCountByType.Values)
                covered += c;
            return (double)covered / (presentTypes * totalGames);
        }

        // Favourites: the divisor is the number of favourites (absent ones count 0).
        if (favourites == null || favourites.Count == 0)
            return 0;

        int sum = 0;
        foreach (MediaType fav in favourites)
            sum += gameCountByType.TryGetValue(fav.Key, out int c) ? c : 0;
        return (double)sum / (favourites.Count * totalGames);
    }

    /// <summary>Formats a 0..1 coverage ratio as an integer percentage (e.g. "62%"). Single home for the format.</summary>
    public string FormatPercent(double ratio) => $"{ratio * 100:0}%";

    /// <summary>
    /// For every game image OR game video type (image types key &lt; 100, plus Video Snap / Theme Video) that the
    /// platform actually has files of (set with <c>ImagesCount &gt; 0</c>), the number of distinct games of the
    /// platform that have at least one matching file of that type. Dividing each count by
    /// <c>platform.Games.Count</c> yields the per-type coverage (0..1).
    /// Types with no images are NOT in the result (a present type with images but no matching game maps to 0);
    /// callers wanting a fixed type list (e.g. all favourites) treat missing keys as 0. Pure read of the
    /// already-scanned image sets and the games' search strings — it mutates nothing, so it is background-safe.
    /// </summary>
    public IReadOnlyDictionary<int, int> GetPlatformGameCountByImageType(Platform platform)
    {
        var result = new Dictionary<int, int>();

        if (platform?.Images?.ImageSets == null || platform.Games.Count == 0)
            return result;

        // Reverse index: each search string -> the games that declare it (built once, O(1) lookup per file).
        var gamesBySearchString = new Dictionary<string, List<Game>>();
        foreach (Game game in platform.Games)
        {
            foreach (string searchString in game.SearchStrings)
            {
                if (!gamesBySearchString.TryGetValue(searchString, out List<Game>? games))
                {
                    games = new List<Game>();
                    gamesBySearchString[searchString] = games;
                }
                games.Add(game);
            }
        }

        // Per image type, the distinct set of games covered by at least one of its files.
        var gamesByType = new Dictionary<int, HashSet<Game>>();

        foreach (PlatformImageSet set in platform.Images.ImageSets)
        {
            // A type counts as "present" only if it actually has files; empty sets (ImagesCount == 0) are
            // skipped so they don't show up as present-but-0%-coverage types. Game videos (Video Snap, Theme
            // Video) count as types too, so their coverage shows up alongside the image types in the per-type
            // charts and feeds into the average coverage.
            if (set.Type == null || !(MediaType.IsImage(set.Type.Key) || MediaType.IsVideo(set.Type.Key)) || set.ImagesCount == 0)
                continue;

            int typeKey = set.Type.Key;
            if (!gamesByType.TryGetValue(typeKey, out HashSet<Game>? covered))
            {
                covered = new HashSet<Game>();
                gamesByType[typeKey] = covered;   // present type with images: appears even if no game matches (count 0)
            }

            IReadOnlyList<string> files = set.GetLowercaseFiles();
            for (int i = 0; i < files.Count; i++)
            {
                if (!gamesBySearchString.TryGetValue(files[i], out List<Game>? games))
                    continue;

                foreach (Game game in games)
                    covered.Add(game);
            }
        }

        foreach (KeyValuePair<int, HashSet<Game>> pair in gamesByType)
            result[pair.Key] = pair.Value.Count;

        return result;
    }

    /// <summary>
    /// Nº de ficheros de un image set por extensión (minúsculas, sin punto; sin extensión → "(none)"). Sobre las
    /// rutas ya escaneadas del set. No decodifica. Pura suma en memoria.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetImageSetCountByExtension(PlatformImageSet imageSet)
    {
        var counts = new Dictionary<string, int>();
        if (imageSet == null)
            return counts;

        foreach (string file in imageSet.ImageFiles)
        {
            string ext = Path.GetExtension(file);
            ext = string.IsNullOrEmpty(ext) ? "(none)" : ext.TrimStart('.').ToLowerInvariant();
            counts.TryGetValue(ext, out int c);
            counts[ext] = c + 1;
        }

        return counts;
    }

    /// <summary>
    /// Nº de ficheros de un image set por región, incluyendo el cubo "sin región"
    /// (<see cref="ImageRegion.NoRegion"/>). La región es el nombre de la carpeta hoja del fichero (igual que en
    /// <see cref="GameImage"/>), deducido de las rutas escaneadas. No decodifica. Pura suma en memoria.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetImageSetCountByRegion(PlatformImageSet imageSet)
    {
        var counts = new Dictionary<string, int>();
        if (imageSet == null)
            return counts;

        // Nombres de carpeta que corresponden a una región (excluida "sin región"), para clasificar la carpeta hoja.
        var regionFolders = new HashSet<string>();
        foreach (ImageRegion region in Enumeration.GetAll<ImageRegion>())
            if (region != ImageRegion.NoRegion)
                regionFolders.Add(region.Value);

        string noRegion = ImageRegion.NoRegion.Value;

        foreach (string file in imageSet.ImageFiles)
        {
            string leaf = Path.GetFileName(Path.GetDirectoryName(file)) ?? string.Empty;
            string region = regionFolders.Contains(leaf) ? leaf : noRegion;
            counts.TryGetValue(region, out int c);
            counts[region] = c + 1;
        }

        return counts;
    }

    /// <summary>
    /// Nº de imágenes YA decodificadas de un image set por dimensiones ("AnchoxAlto"). Las imágenes cuyas
    /// dimensiones aún no se han leído (ancho/alto = 0) NO se incluyen (el llamante las trata como "Unknown" =
    /// total − suma). Pura lectura en memoria.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetImageSetCountByDimensions(PlatformImageSet imageSet)
    {
        var counts = new Dictionary<string, int>();
        if (imageSet == null)
            return counts;

        foreach (GameImage image in imageSet.Images)
        {
            if (image.Width <= 0 || image.Height <= 0)
                continue;   // sin decodificar → Unknown (lo añade el llamante)

            string dimensions = $"{image.Width}x{image.Height}";
            counts.TryGetValue(dimensions, out int c);
            counts[dimensions] = c + 1;
        }

        return counts;
    }

    /// <summary>
    /// Nº de vídeos YA medidos de un image set por calidad (resolución vertical, "1080p"). Los que aún no tienen
    /// la altura leída (0) NO se incluyen (el llamante los trata como "Unknown" = total − suma). Pura lectura.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetImageSetCountByQuality(PlatformImageSet imageSet)
    {
        var counts = new Dictionary<string, int>();
        if (imageSet == null)
            return counts;

        foreach (GameImage image in imageSet.Images)
        {
            if (image.Height <= 0)
                continue;   // sin medir → Unknown (lo añade el llamante)

            string quality = $"{image.Height}p";
            counts.TryGetValue(quality, out int c);
            counts[quality] = c + 1;
        }

        return counts;
    }

    /// <summary>
    /// Nº de vídeos YA medidos de un image set por rango de duración (cubos de 10 s: "0-10s", "11-20s"…). Los que
    /// aún no tienen la duración leída (0) NO se incluyen (el llamante los trata como "Unknown" = total − suma).
    /// Pura lectura.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetImageSetCountByDurationRange(PlatformImageSet imageSet)
    {
        var counts = new Dictionary<string, int>();
        if (imageSet == null)
            return counts;

        foreach (GameImage image in imageSet.Images)
        {
            if (image.Duration <= TimeSpan.Zero)
                continue;   // sin medir → Unknown (lo añade el llamante)

            string range = DurationBucketLabel(DurationBucket(image.Duration));
            counts.TryGetValue(range, out int c);
            counts[range] = c + 1;
        }

        return counts;
    }

    // ----------------------------------------------------
    // Helpers
    // ----------------------------------------------------

    /// <summary>Stat for the most frequent image file extension in the collection ("[png] as extension").</summary>
    private static Stat BuildMostCommonExtensionStat(IReadOnlyCollection<GameImage> images)
    {
        IGrouping<string, GameImage>? byExtension = images.GroupBy(x => x.FileExtension)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return new Stat
        {
            Title = byExtension == null ? L(LocKeys.Stats_EmptyCollection_Title) : F(LocKeys.Stats_AsExtension_Format, byExtension.Key),
            Value = byExtension == null ? 0 : byExtension.Count(),
            Total = images.Count
        };
    }

    /// <summary>Stat for the most frequent image dimensions in the collection ("[1920x1080] as dimensions").</summary>
    private static Stat BuildMostCommonDimensionsStat(IReadOnlyCollection<GameImage> images)
    {
        IGrouping<string, GameImage>? byDimensions = images.GroupBy(x => x.Dimensions)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return new Stat
        {
            Title = byDimensions == null ? L(LocKeys.Stats_EmptyCollection_Title) : F(LocKeys.Stats_AsDimensions_Format, byDimensions.Key),
            Value = byDimensions == null ? 0 : byDimensions.Count(),
            Total = images.Count
        };
    }

    /// <summary>
    /// Stat for the most frequent video quality (vertical resolution, "[1080p] as quality") in the collection.
    /// Only meaningful for video sets; the audit shows it in place of the region pills for them.
    /// </summary>
    private static Stat BuildMostCommonQualityStat(IReadOnlyCollection<GameImage> images)
    {
        IGrouping<int, GameImage>? byQuality = images.GroupBy(x => x.Height)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return new Stat
        {
            Title = byQuality == null ? L(LocKeys.Stats_EmptyCollection_Title) : F(LocKeys.Stats_AsQuality_Format, $"{byQuality.Key}p"),
            Value = byQuality == null ? 0 : byQuality.Count(),
            Total = images.Count
        };
    }

    /// <summary>
    /// Stat for the most frequent duration range (10-second buckets: 0-10s, 11-20s, 21-30s…) in the collection,
    /// e.g. "[11-20s] as duration". Only meaningful for video sets (images have no duration).
    /// </summary>
    private static Stat BuildMostCommonDurationStat(IReadOnlyCollection<GameImage> images)
    {
        IGrouping<int, GameImage>? byBucket = images.GroupBy(x => DurationBucket(x.Duration))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return new Stat
        {
            Title = byBucket == null ? L(LocKeys.Stats_EmptyCollection_Title) : F(LocKeys.Stats_AsDuration_Format, DurationBucketLabel(byBucket.Key)),
            Value = byBucket == null ? 0 : byBucket.Count(),
            Total = images.Count
        };
    }

    /// <summary>Index of the 10-second duration bucket a clip falls in: bucket 0 = 0-10s, 1 = 11-20s, 2 = 21-30s…</summary>
    private static int DurationBucket(TimeSpan duration)
    {
        int seconds = (int)duration.TotalSeconds;
        return seconds <= 10 ? 0 : (seconds - 1) / 10;
    }

    /// <summary>Human label for a 10-second duration bucket index ("0-10s", "11-20s", "21-30s"…).</summary>
    private static string DurationBucketLabel(int bucket)
        => bucket == 0 ? "0-10s" : $"{bucket * 10 + 1}-{(bucket + 1) * 10}s";

    private static Stat CreateStat(string title, int value, int total)
    {
        return new Stat
        {
            Title = title,
            Value = value,
            Total = total
        };
    }

    private static Stat CreateStat(string title, string description, int value, int total)
    {
        return new Stat
        {
            Title = title,
            Description = description,
            Value = value,
            Total = total
        };
    }
}
