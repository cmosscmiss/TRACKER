using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace MM4LB.Services;

/// <summary>
/// Servicio encargado de cargar toda la información de LaunchBox.
/// 
/// Responsabilidades:
/// - Leer Platforms.xml
/// - Cargar carpetas de imágenes (<PlatformFolder>)
/// - Cargar cada plataforma (<Platform>) desde su XML individual
/// - Cargar los juegos de cada plataforma
/// - Resolver imágenes y assets asociados
///
/// Este servicio NO contiene lógica de UI.
/// Se limita a orquestar la carga y reportar progreso mediante ProgressService.
/// </summary>
public sealed class PlatformLoadingService
{
    #region Attributes
    private readonly ProgressService _progressService;
    private readonly ImageLoadingService _imageLoadingService;
    private readonly ImageBinaryLoadingService _imageBinaryLoadingService;
    private readonly FileSystemService _fileSystemService;
    private readonly ExceptionService _exceptionService;
    private readonly AppSettings _appSettings;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el servicio con todos los servicios necesarios para cargar plataformas,
    /// imágenes y archivos XML de LaunchBox.
    /// </summary>
    public PlatformLoadingService(FileSystemService fileSystemService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, ProgressService progressService, ExceptionService exceptionService, IOptions<AppSettings> appSettings)
    {
        _imageLoadingService = imageLoadingService;
        _imageBinaryLoadingService = imageBinaryLoadingService;
        _fileSystemService = fileSystemService;
        _progressService = progressService;
        _exceptionService = exceptionService;
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Carga todas las plataformas (<Platform>) y sus juegos.
    /// Cada plataforma se carga desde su XML individual (Future Pinball.xml, ScummVM.xml, etc.).
    /// 
    /// El progreso se actualiza proporcionalmente al número de plataformas procesadas.
    /// Este método NO inicia ni finaliza operaciones de progreso: usa el notifier recibido.
    /// </summary>
    private async Task<List<Platform>> LoadPlatformsAsync(
        XmlNodeList nodes,
        List<PlatformImageFolder> imageFolders,
        ProgressNotifier notifier)
    {
        var platforms = new List<Platform>();

        // Pre-filtrar los nodos cuya plataforma tiene fichero .xml: así el denominador (total) y el contador
        // (index) cuentan SOLO lo que realmente se va a cargar. Antes, index se incrementaba para todos los nodos
        // (incluidos los saltados por no existir el fichero) y total = nodes.Count, con lo que el progreso podía
        // quedarse por debajo de 100 (p. ej. si los últimos nodos no tenían fichero) y la barra nunca llegaba
        // arriba antes de pasar a la fase de juegos.
        var nodesToLoad = nodes.Cast<XmlNode>()
            .Select(node => (node, name: node["Name"]?.InnerText ?? ""))
            .Select(x => (x.node, x.name, file: Path.Combine(_appSettings.LaunchBox.LaunchBoxPlatformsFolder, $"{x.name}.xml")))
            .Where(x => File.Exists(x.file))
            .ToList();

        int total = nodesToLoad.Count;
        int index = 0;

        foreach (var (node, name, file) in nodesToLoad)
        {
            index++;

            var platform = new Platform(file, _appSettings.LaunchBox.LaunchBoxFolder)
            {
                ImageFolderStrings = imageFolders.Where(f => f.Platform == name).ToList()
            };

            // Metadatos de la plataforma (Developer, Cpu, Memory, ..., Notes) desde su <Platform> en Platforms.xml.
            platform.SetMetadata(node);

            // El progreso se sube al INICIAR la carga de cada plataforma; con el denominador ya saneado, la última
            // (index == total) deja la barra en 100 antes de continuar.
            notifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformLoading_LoadingPlatform_Progress] ?? "Loading platforms ({0})...", platform.Name);
            notifier.Progress = (int)(100.0 * index / total);
            _progressService.ProgressNotifier.Report(notifier);

            XmlDocument platformDoc = await _fileSystemService.LoadXmlDocument(file);
            platform.SetGames(platformDoc.GetElementsByTagName("Game"));
            await LoadPlatformImageFilesAsync(platform, platform.ImageFolderStrings);
            LoadPlatformVideoSets(platform);

            _imageLoadingService.GetPlatformImageAssets(platform);
            if (platform.Icon != null)
                await _imageBinaryLoadingService.LoadImageAsync(platform.Icon, Enums.ImageResolutionSettings.High);

            platforms.Add(platform);
        }

        platforms.Sort((a, b) => a.Name.CompareTo(b.Name));
        return platforms;
    }

    /// <summary>
    /// Carga todos los archivos de imagen de las carpetas asociadas a una plataforma.
    /// Cada carpeta representa un tipo de imagen (ClearLogo, BoxFront, Fanart, etc.).
    /// </summary>
    private async Task LoadPlatformImageFilesAsync(
        Platform platform,
        List<PlatformImageFolder> imageFolders)
    {
        var allowedExtensions = _appSettings.LaunchBox.AllowedImageExtensions;

        await Task.WhenAll(imageFolders.Select(async imageFolder =>
        {
            PlatformImageSet imageSet = new(imageFolder, platform.Name);
            platform.Images.ImageSets.Add(imageSet);

            (List<string> files, long sizeKb) = GetFilesFromFolder(imageFolder.FolderPath, allowedExtensions, recursive: true);
            imageSet.ImageFiles = files;
            imageSet.SizeOnDiskKb = sizeKb;

            platform.Images.AddToTotalImages(imageSet.ImageFiles.Count);
        }));
    }

    /// <summary>
    /// Crea los <see cref="PlatformImageSet"/> de vídeo de la plataforma (Video, Theme Video) a partir de las
    /// carpetas de vídeo declaradas en Platforms.xml (<see cref="Platform.VideoFolderStrings"/>), ya normalizadas
    /// a rutas absolutas. Los sets se añaden SIEMPRE (aunque la carpeta esté vacía o no exista) para que el tipo
    /// aparezca en el selector y se puedan soltar vídeos en él.
    ///
    /// La carpeta Theme Video cuelga de la de Video (Videos\{Plataforma}\Theme), así que la raíz Video se escanea
    /// SOLO a nivel superior (recursive: false) o se tragaría los vídeos de Theme; Theme sí se escanea recursiva.
    /// Los vídeos NO se suman al total de imágenes de la plataforma (no son imágenes).
    /// </summary>
    private void LoadPlatformVideoSets(Platform platform)
    {
        var allowedExtensions = _appSettings.LaunchBox.AllowedVideoExtensions;

        foreach (PlatformImageFolder videoFolder in platform.VideoFolderStrings)
        {
            // Solo Theme Video se escanea recursivamente; la raíz Video, no (o duplicaría los vídeos de Theme).
            bool recursive = videoFolder.ImageType?.Key == MediaType.ThemeVideo.Key;

            PlatformImageSet imageSet = new(videoFolder, platform.Name);

            (List<string> files, long sizeKb) = GetFilesFromFolder(videoFolder.FolderPath, allowedExtensions, recursive);
            imageSet.ImageFiles = files;
            imageSet.SizeOnDiskKb = sizeKb;

            platform.Images.ImageSets.Add(imageSet);
        }
    }

    /// <summary>
    /// Enumera los archivos de una carpeta cuyo nombre termina en una de las <paramref name="allowedExtensions"/>,
    /// devolviendo también el tamaño total en KB. Lee el tamaño durante la propia enumeración: Windows ya devuelve
    /// el tamaño en los datos del directorio, así que sumarlo no cuesta ningún stat extra por fichero (un
    /// new FileInfo(path).Length aparte multiplicaría el tiempo de arranque en colecciones grandes).
    /// </summary>
    private static (List<string> Files, long SizeKb) GetFilesFromFolder(string folder, string[] allowedExtensions, bool recursive)
    {
        if (!Directory.Exists(folder))
            return (new List<string>(), 0);

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = new List<string>();
        long sizeKb = 0;
        foreach (FileInfo fileInfo in new DirectoryInfo(folder).EnumerateFiles("*.*", searchOption))
        {
            if (!allowedExtensions.Any(ext => fileInfo.FullName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                continue;

            files.Add(fileInfo.FullName);
            sizeKb += fileInfo.Length / 1000; // KB, consistente con LocalFile.FileSize
        }

        files.Sort();
        return (files, sizeKb);
    }

    /// <summary>
    /// Carga todos los nodos <PlatformFolder> y los convierte en DTOs PlatformImageFolder.
    /// El progreso se actualiza proporcionalmente al número de carpetas procesadas.
    /// Este método NO inicia ni finaliza operaciones de progreso.
    /// </summary>
    private async Task<List<PlatformImageFolder>> LoadPlatformImageFoldersAsync(
        XmlNodeList nodes,
        ProgressNotifier notifier)
    {
        var folders = new List<PlatformImageFolder>();
        int total = nodes.Count;
        int index = 0;

        foreach (XmlNode node in nodes)
        {
            index++;
            folders.Add(new PlatformImageFolder(node));

            notifier.Progress = (int)(100.0 * index / total);
            _progressService.ProgressNotifier.Report(notifier);
        }

        return folders;
    }

    /// <summary>
    /// Carga información adicional de LaunchBox (packs de iconos y logos)
    /// desde LaunchBoxSettings.xml si existe.
    /// </summary>
    private async Task LoadPlatformPacksAsync()
    {
        if (!File.Exists(_appSettings.LaunchBox.LaunchboxSettingsXmlFile))
            return;

        XmlDocument doc = await _fileSystemService.LoadXmlDocument(_appSettings.LaunchBox.LaunchboxSettingsXmlFile);

        var settingsNode = doc.SelectSingleNode("/LaunchBox/Settings");
        if (settingsNode == null)
            return;

        _appSettings.LaunchBox.PlatformIconPackFolder = settingsNode["PlatformIconPack"]?.InnerText ?? "";
        _appSettings.LaunchBox.PlatformLogoPackFolder = settingsNode["PlatformLogoPack"]?.InnerText ?? "";
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Punto de entrada principal para cargar LaunchBox.
    /// 
    /// Flujo:
    /// 1. Inicia una operación de progreso (bloquea UI y muestra barra)
    /// 2. Carga LaunchBoxSettings.xml (packs de iconos)
    /// 3. Carga Platforms.xml
    /// 4. Carga carpetas de imágenes (<PlatformFolder>)
    /// 5. Carga plataformas y juegos
    /// 6. Actualiza mensajes y porcentaje de progreso
    /// 7. Finaliza la operación de progreso
    ///
    /// Devuelve un PlatformSet completamente poblado.
    /// </summary>
    public async Task<PlatformSet> LoadPlatformSetAsync()
    {
        var notifier = _progressService.StartBlockingOperation();

        try
        {
            // Estas líneas van AHORA dentro del try (antes estaban fuera): si Platforms.xml falta, está
            // bloqueado o corrupto, LoadXmlDocument lanza y, sin el finally de abajo, FinishBlockingOperation
            // no se ejecutaría nunca, dejando la UI bloqueada (IsUIEnabled = false) para siempre.
            await LoadPlatformPacksAsync();
            XmlDocument doc = await _fileSystemService.LoadXmlDocument(_appSettings.LaunchBox.LaunchboxPlatformsXmlFile);

            // XPath constante sobre un documento ya cargado: SelectNodes solo devuelve null con un XPath inválido,
            // así que el '!' es seguro (y evita propagar nullable a los métodos de carga que iteran los nodos).
            XmlNodeList platformNodes = doc.SelectNodes("/LaunchBox/Platform")!;
            XmlNodeList platformFolderNodes = doc.SelectNodes("/LaunchBox/PlatformFolder")!;

            notifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformLoading_ProcessingPlatformsXml_Progress] ?? "Processing platform.xml file...";
            var folders = await LoadPlatformImageFoldersAsync(platformFolderNodes, notifier);

            notifier.Progress = 0;
            notifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformLoading_LoadingPlatforms_Progress] ?? "Loading platforms...";
            _progressService.ProgressNotifier.Report(notifier);

            var platforms = await LoadPlatformsAsync(platformNodes, folders, notifier);

            notifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformLoading_PreparingUi_Progress] ?? "Preparing UI...";
            notifier.Progress = 100;
            _progressService.ProgressNotifier.Report(notifier);

            notifier.FinishOperation();

            return new PlatformSet(_appSettings.LaunchBox.LaunchboxPlatformsXmlFile)
            {
                PlatformImageFolders = folders,
                Platforms = platforms
            };
        }
        finally
        {
            _progressService.FinishBlockingOperation();
        }
        // Sin catch a propósito: un fallo aquí (Platforms.xml ausente/corrupto) sube al único manejador de
        // error de arranque (LoadingWindow_Activated), que muestra un mensaje y cierra la app. El finally
        // garantiza que la UI no quede bloqueada por el camino.
    }

    /// <summary>
    /// Loads all the games for the platforms available in the LaunchBox DB.
    ///
    /// LaunchBox no longer stores its metadata in an XML file: it now keeps it in a local
    /// SQLite database (LaunchBox.Metadata.db, inside the Metadata folder). This method reads
    /// the "Games" table of that database and, for every game, locates the matching platform
    /// in the provided <paramref name="platformSet"/>:
    /// - If the game already exists in the user's collection, it is flagged as InLaunchboxDb.
    /// - Otherwise it is registered as a LaunchBox-only game (not in the collection).
    ///
    /// The database is opened read-only so the user's LaunchBox database is never locked nor modified.
    /// </summary>
    public async Task LoadGamesLbDatabaseAsync(PlatformSet platformSet)
    {
        ProgressNotifier progressNotifier = _progressService.StartBlockingOperation();
        int gamesCount = 0;
        string databaseFile = _appSettings.LaunchBox.LaunchboxGamesDbFile;

        try
        {
            if (!File.Exists(databaseFile))
                throw new FileNotFoundException("LaunchBox metadata database not found.", databaseFile);

            // Read-only mode so we never lock or write to the user's live LaunchBox database.
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databaseFile,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync();

            // Total number of games, used only to report progress.
            int totalGames;
            await using (SqliteCommand countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(*) FROM Games";
                totalGames = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DatabaseID, Name, Platform FROM Games";

            await using SqliteDataReader reader = await command.ExecuteReaderAsync();

            // Start from a clean slate so re-running the load never duplicates games. De paso se indexan las
            // plataformas por nombre y sus juegos por DatabaseId (first-wins, como los Find lineales que
            // sustituye) para emparejar cada fila de la BBDD en O(1) en vez de O(plataformas + juegos por fila).
            var platformByName = new Dictionary<string, Platform>();
            var gamesByIdByPlatform = new Dictionary<string, Dictionary<string, Game>>();
            foreach (Platform platform in platformSet.Platforms)
            {
                platform.GamesInLauchboxDb.Clear();
                if (platformByName.TryAdd(platform.Name, platform))
                {
                    var byId = new Dictionary<string, Game>();
                    foreach (Game g in platform.Games) { byId.TryAdd(g.DatabaseId, g); }
                    gamesByIdByPlatform[platform.Name] = byId;
                }
            }

            int rowsRead = 0;
            int lastProgress = -1;
            while (await reader.ReadAsync())
            {
                // DatabaseID is an INTEGER in the database; the platform XML stores it as a string,
                // so we normalize to string to keep the existing matching logic untouched.
                string databaseId = reader.IsDBNull(0) ? "" : reader.GetInt64(0).ToString();
                string title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string platformName = reader.IsDBNull(2) ? "" : reader.GetString(2);

                if (platformName != string.Empty)
                {
                    if (platformByName.TryGetValue(platformName, out Platform? platform))
                    {
                        gamesByIdByPlatform[platformName].TryGetValue(databaseId, out Game? game);
                        if (game != null)
                        {
                            game.InLaunchboxDb = true;
                            platform.GamesInLauchboxDb.Add(new Game(databaseId, game.RomFileName, game.Title, game.Version, true, true));
                        }
                        else
                        {
                            platform.GamesInLauchboxDb.Add(new Game(databaseId, "", title, "", false, true));
                        }
                        gamesCount++;
                    }
                }

                // Report on progress.
                rowsRead++;
                if (totalGames > 0)
                {
                    int progress = Convert.ToInt32(100.0 * rowsRead / totalGames);
                    if (progress != lastProgress)
                    {
                        lastProgress = progress;
                        progressNotifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformLoading_LoadingGamesDb_Progress] ?? "Loading LaunchBox games database...";
                        progressNotifier.Progress = progress;
                        _progressService.ProgressNotifier.Report(progressNotifier);
                    }
                }
            }

            foreach (Platform platform in platformSet.Platforms)
            {
                platform.AddOrphanGames();
            }
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformLoading_LoadGamesDbError_Error] ?? "There was an error loading the games from the LaunchBox database.");
        }
        progressNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.PlatformLoading_GamesDbLoaded_Progress] ?? "{0} games from the LaunchBox database loaded", gamesCount);
        progressNotifier.FinishOperation();
        _progressService.ProgressNotifier.Report(progressNotifier);
        _progressService.FinishBlockingOperation();
    }
    #endregion
}
