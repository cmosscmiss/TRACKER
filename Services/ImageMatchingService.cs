using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>Resultado del escaneo de huérfanos de una plataforma: los huérfanos + totales para las comparaciones "X/Y".</summary>
public readonly record struct OrphanScan(List<GameImage> Orphans, int TotalMediaCount, long TotalMediaSizeKb, int TypesWithMedia);

/// <summary>
/// Resultado del escaneo de media COMPARTIDA (emparejada con ≥2 juegos) de una plataforma: cada
/// <see cref="GameImage"/> lleva sus juegos en <see cref="GameImage.LinkedGames"/>. Incluye los totales de la
/// plataforma para las comparaciones "X/Y" de las pastillas.
/// </summary>
public readonly record struct SharedMediaScan(List<GameImage> Shared, int TotalMediaCount, long TotalMediaSizeKb, int TypesWithMedia, int TotalGames);

/// <summary>
/// Empareja los ficheros de imagen/vídeo de una plataforma con sus juegos. Extraído de ImageLoadingService
/// (§8.1): solo lógica de matching (juego ↔ imagen), sin decode/caché ni descarga/mutación de disco. Es la ruta
/// crítica de rendimiento del Tier B (índices invertidos), verificable con la herramienta de auditoría.
/// </summary>
public sealed class ImageMatchingService
{
    #region Attributes
    private readonly ProgressService _progressService;

    // Serializa MatchImagesWithGamesAsync: cambiar de tipo de imagen dispara un re-emparejado que muta
    // game.Images; dos a la vez corromperían el modelo.
    private readonly SemaphoreSlim _matchLock = new(1, 1);

    // Último set emparejado. game.Images SOLO se limpia/repuebla dentro de MatchImagesWithGamesAsync, así que este
    // campo refleja de forma fiable con qué set están pobladas las imágenes de los juegos. Sirve para NO re-emparejar
    // el mismo set de forma redundante: el setter de SelectedImageSet solo emite el evento en cambios reales (a un
    // set distinto), de modo que la única fuente de re-emparejados del MISMO set es NotifyInitialState() (lo llaman
    // 3 constructores de VM en el arranque), que duplicaba el mensaje "N media files for M games loaded".
    private PlatformImageSet? _lastMatchedImageSet;
    #endregion

    #region Constructor
    public ImageMatchingService(ProgressService progressService)
    {
        _progressService = progressService;
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Matches a single game with all image sets.
    ///
    /// Returns:
    /// - A list of (PlatformImageSet, filePath) pairs
    ///
    /// This is used by LoadGameImagesAsync() to know which files to load.
    ///
    /// It does NOT:
    /// - Load binaries
    /// - Modify DTOs
    /// - Report progress
    /// </summary>
    private List<(PlatformImageSet set, string file)> MatchImagesForGame(Platform platform, Game game)
    {
        var result = new List<(PlatformImageSet, string)>();

        foreach (var set in platform.Images.ImageSets)
        {
            var lower = set.GetLowercaseFiles();

            for (int i = 0; i < lower.Count; i++)
            {
                if (game.SearchStringsSet.Contains(lower[i]))
                    result.Add((set, set.ImageFiles[i]));
            }
        }

        return result;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Discovers the image files that match the game and instantiates the corresponding
    /// <see cref="GameImage"/> entries in <see cref="Game.AllImages"/>, WITHOUT decoding their
    /// binaries. The binaries are decoded lazily, on demand, as the gallery scrolls each image
    /// into view (see ImageGridViewModel.LoadImageBinaryOnDemandAsync).
    /// </summary>
    public void MatchGameImages(Platform platform, Game game)
    {
        var matchedFiles = MatchImagesForGame(platform, game);

        foreach (var (set, file) in matchedFiles)
        {
            if (!game.AllImages.Any(img => img.File == file))
                game.AllImages.Add(new GameImage(file, set.Type));
        }
    }

    /// <summary>
    /// Cuenta, por tipo de media (<see cref="MediaType.Key"/>), las imágenes y vídeos que la app emparejaría
    /// con el juego, SIN mutar el modelo (a diferencia de <see cref="MatchGameImages"/>, que puebla
    /// <see cref="Game.AllImages"/>). Usa el MISMO primitivo de matching (<see cref="MatchImagesForGame"/>),
    /// así que el conteo coincide con lo que se mostraría. Es solo lectura → seguro en un hilo de fondo.
    /// Pensado para la auditoría contra el Excel de LaunchBox (<see cref="MediaAuditService"/>).
    /// </summary>
    public Dictionary<int, int> CountMatchedImagesByType(Platform platform, Game game)
    {
        var counts = new Dictionary<int, int>();
        foreach (var (set, _) in MatchImagesForGame(platform, game))
        {
            if (set.Type == null) { continue; }
            counts.TryGetValue(set.Type.Key, out int c);
            counts[set.Type.Key] = c + 1;
        }
        return counts;
    }

    /// <summary>
    /// Escaneo READ-ONLY de los medios huérfanos de la plataforma: por cada set de imagen o vídeo DE JUEGO, marca
    /// como huérfano (con un <see cref="GameImage"/> fresco, sin mutar el modelo) cada fichero cuyo game-string no
    /// empareja ningún juego, y de paso agrega los totales de la plataforma (nº de ficheros, tamaño en disco y nº de
    /// tipos con ficheros) para las comparaciones "X/Y". Usa el índice invertido del Tier B; recorre cada fichero una
    /// vez leyendo su tamaño (coste de I/O proporcional al total de medios). Seguro en un hilo de fondo. Excluye el
    /// vídeo de plataforma (se gestiona en Platform Details) y los tipos no soportados (manual/música).
    /// </summary>
    public OrphanScan ScanPlatformOrphans(Platform platform)
    {
        var orphans = new List<GameImage>();
        int totalMediaCount = 0;
        long totalMediaSizeKb = 0;
        int typesWithMedia = 0;

        if (platform?.Images?.ImageSets == null)
            return new OrphanScan(orphans, 0, 0, 0);

        Dictionary<string, List<Game>> gamesBySearchString = Platform.BuildSearchStringIndex(platform.Games);

        foreach (PlatformImageSet set in platform.Images.ImageSets)
        {
            // Solo imágenes y vídeos de juego (no vídeo de plataforma, manuales ni música).
            if (set.Type == null || !(MediaType.IsImage(set.Type.Key) || MediaType.IsVideo(set.Type.Key)))
                continue;

            IReadOnlyList<string> lower = set.GetLowercaseFiles();
            if (lower.Count == 0)
                continue;

            typesWithMedia++;
            totalMediaCount += lower.Count;

            for (int i = 0; i < lower.Count; i++)
            {
                string file = set.ImageFiles[i];

                try
                {
                    if (System.IO.File.Exists(file))
                        totalMediaSizeKb += new System.IO.FileInfo(file).Length / 1000;
                }
                catch { }

                if (!gamesBySearchString.ContainsKey(lower[i]))
                    orphans.Add(new GameImage(file, set.Type));
            }
        }

        return new OrphanScan(orphans, totalMediaCount, totalMediaSizeKb, typesWithMedia);
    }

    /// <summary>
    /// Escaneo READ-ONLY de la media COMPARTIDA de la plataforma: por cada set de imagen o vídeo DE JUEGO, marca
    /// como compartido (con un <see cref="GameImage"/> fresco cuyos <see cref="GameImage.LinkedGames"/> son los
    /// juegos emparejados) cada fichero cuyo game-string empareja con ≥2 juegos, y de paso agrega los totales de la
    /// plataforma (nº de ficheros, tamaño en disco, nº de tipos con ficheros y nº de juegos) para las comparaciones
    /// "X/Y". Mismo primitivo de matching e índice invertido que <see cref="ScanPlatformOrphans"/> (solo cambia el
    /// criterio: ≥2 juegos en vez de 0). Seguro en un hilo de fondo. Excluye vídeo de plataforma, manuales y música.
    /// </summary>
    public SharedMediaScan ScanPlatformSharedMedia(Platform platform)
    {
        var shared = new List<GameImage>();
        int totalMediaCount = 0;
        long totalMediaSizeKb = 0;
        int typesWithMedia = 0;

        if (platform?.Images?.ImageSets == null)
            return new SharedMediaScan(shared, 0, 0, 0, 0);

        Dictionary<string, List<Game>> gamesBySearchString = Platform.BuildSearchStringIndex(platform.Games);

        foreach (PlatformImageSet set in platform.Images.ImageSets)
        {
            // Solo imágenes y vídeos de juego (no vídeo de plataforma, manuales ni música).
            if (set.Type == null || !(MediaType.IsImage(set.Type.Key) || MediaType.IsVideo(set.Type.Key)))
                continue;

            IReadOnlyList<string> lower = set.GetLowercaseFiles();
            if (lower.Count == 0)
                continue;

            typesWithMedia++;
            totalMediaCount += lower.Count;

            for (int i = 0; i < lower.Count; i++)
            {
                string file = set.ImageFiles[i];

                try
                {
                    if (System.IO.File.Exists(file))
                        totalMediaSizeKb += new System.IO.FileInfo(file).Length / 1000;
                }
                catch { }

                if (gamesBySearchString.TryGetValue(lower[i], out List<Game>? games) && games.Count >= 2)
                {
                    var image = new GameImage(file, set.Type);
                    image.LinkedGames.AddRange(games);
                    shared.Add(image);
                }
            }
        }

        return new SharedMediaScan(shared, totalMediaCount, totalMediaSizeKb, typesWithMedia, platform.Games.Count);
    }

    /// <summary>
    /// Matches the images found (of a type, within a folder) with the existing games of the platform.
    /// </summary>
    /// <param name="games"></param>
    /// <returns></returns>
    public async Task MatchImagesWithGamesAsync(Platform platform)
    {
        await _matchLock.WaitAsync();
        bool blockingStarted = false;
        try
        {
            var imageSet = platform.SelectedImageSet;
            if (imageSet == null)
                return;

            // Set ya emparejado (game.Images ya reflejan este set): re-emparejar sería trabajo redundante y
            // volvería a emitir "N media files for M games loaded" (el duplicado del arranque por NotifyInitialState).
            if (ReferenceEquals(imageSet, _lastMatchedImageSet))
                return;

            ProgressNotifier progressNotifier = _progressService.StartBlockingOperation();
            blockingStarted = true;
            // Clears the images of all games of the platform (required when changing the image type).
            foreach (Game game in platform.Games)
            {
                game.Images.Clear();
            }

            if (!imageSet.IsLoaded && imageSet.ImageFilesLowerCase.Count > 0)
            {
                imageSet.CreateImages();

                // Índice fichero→GameImage: imageSet.Images está ordenado por nombre (no paralelo a ImageFiles).
                var imageByFile = new Dictionary<string, GameImage>();
                foreach (GameImage img in imageSet.Images) { imageByFile.TryAdd(img.File, img); }

                // Índice invertido search-string→juegos (cómputo puro, en hilo de fondo). Con él, en vez de
                // comprobar cada pareja (juego × fichero) —O(N·M)—, se recorre el set UNA vez saltando directo a
                // los juegos de cada fichero —O(N·S + M)—.
                Dictionary<string, List<Game>> gamesBySearchString =
                    await Task.Run(() => Platform.BuildSearchStringIndex(platform.Games));

                // Recorrido de los ficheros en ORDEN DE ESCANEO (cada juego recibe sus imágenes en el mismo orden
                // que antes) + aplicación al modelo en el hilo de UI (game.Images es ObservableCollection y no se
                // puede mutar fuera de la UI; ahora el bucle es O(M) por el índice, así que no bloquea). La cadena
                // emparejada ES la del fichero, así que no hay que buscar su índice.
                int lastProgress = -1;
                for (int i = 0; i < imageSet.ImageFilesLowerCase.Count; i++)
                {
                    string gameString = imageSet.ImageFilesLowerCase[i];
                    if (gamesBySearchString.TryGetValue(gameString, out List<Game>? matchedGames)
                        && imageByFile.TryGetValue(imageSet.ImageFiles[i], out GameImage? image))
                    {
                        foreach (Game game in matchedGames)
                        {
                            image.LinkedGames.Add(game);
                            image.SetSearchStrings(gameString);
                            game.Images.Add(image);
                        }
                    }

                    // Reports on progress SOLO al cambiar el % (sobre ficheros procesados).
                    int progress = Convert.ToInt32((double)100 / imageSet.ImageFilesLowerCase.Count * (i + 1));
                    if (progress != lastProgress)
                    {
                        lastProgress = progress;
                        progressNotifier.Progress = progress;
                        progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageMatching_LoadingMedia_Progress] ?? "{0}  |  {1}  |  Loading media files for {2} games", platform.Name, imageSet.Type, platform.Games.Count);
                        _progressService.ProgressNotifierWithStats.Report(progressNotifier);
                    }
                }
                imageSet.IsLoaded = true;
            }
            else
            {
                // Associating the games of the platform with the images.
                for (int c0 = 0; c0 < imageSet.Images.Count; c0++)
                {
                    GameImage image = imageSet.Images[c0];
                    foreach (Game game in image.LinkedGames)
                    {
                        game.Images.Add(image);
                    }
                }
            }
            _lastMatchedImageSet = imageSet;

            progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageMatching_MediaLoaded_Progress] ?? "{0}  |  {1}  |  {2} media files for {3} games loaded", platform.Name, imageSet.Type, imageSet.Images.Count, platform.Games.Count);
            progressNotifier.FinishOperation();
            _progressService.ProgressNotifier.Report(progressNotifier);
        }
        finally
        {
            // FinishBlockingOperation debe ejecutarse pase lo que pase (antes estaba en el camino feliz): si el
            // cuerpo lanzaba, la UI quedaba bloqueada (IsUIEnabled = false) para siempre. Solo se llama si de
            // verdad se inició la operación bloqueante (no si salimos temprano por imageSet == null).
            if (blockingStarted)
                _progressService.FinishBlockingOperation();
            _matchLock.Release();
        }
    }
    #endregion
}
