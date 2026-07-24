using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Enums;
using MM4LB.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace MM4LB.Services;

/// <summary>
/// Centralized service for ALL image-related operations.
///
/// Responsibilities:
/// - Load image file paths for a platform (PlatformImageSet creation)
/// - Match images with games
/// - Load binary image data (resolution-dependent)
/// - Load image metadata (dimensions)
/// - Report progress to the UI
///
/// This service contains ALL heavy logic related to images.
/// Platform, PlatformImages and PlatformImageSet remain pure DTOs.
/// </summary>
public sealed class ImageLoadingService
{
    #region Attributes
    private readonly ProgressService _progressService;
    private readonly ImageBinaryLoadingService _imageBinaryLoadingService;
    private readonly FileSystemService _fileSystemService;
    private readonly SharedDataService _sharedDataService;
    private readonly YoutubeDownloadService _youtubeDownloadService;
    private readonly AppSettings _appSettings;

    /// <summary>
    /// Shared client used to download images dragged from the in-app web browser.
    /// A single instance is reused as recommended to avoid socket exhaustion.
    /// </summary>
    private static readonly HttpClient _httpClient = CreateHttpClient();
    #endregion

    #region Events
    /// <summary>
    /// Raised after an image has been added to a game's model collections
    /// (<see cref="Game.AllImages"/> / <see cref="Game.Images"/>). Lets views such as the image
    /// gallery reflect newly added images without a full refresh.
    /// </summary>
    public event Action<Game, GameImage>? ImageAddedToGame;

    /// <summary>
    /// Raised after an image has been removed from a game's model collections (p. ej. al deshacer un alta).
    /// Inverso de <see cref="ImageAddedToGame"/>; deja a las vistas por-juego (galerías, dashboard, audit)
    /// reflejar la baja sin recargar.
    /// </summary>
    public event Action<Game, GameImage>? ImageRemovedFromGame;

    /// <summary>
    /// Raised when images are removed from the platform's collection (no per-game context, e.g. a batch of
    /// orphan deletions). Lets aggregate views such as the stats widget refresh their platform-wide totals
    /// (count / distinct types / size) without a rescan. Additions carry context through
    /// <see cref="ImageAddedToGame"/> instead.
    /// </summary>
    public event Action? PlatformImagesChanged;

    /// <summary>Raises <see cref="PlatformImagesChanged"/>. Call once after a batch of removals.</summary>
    public void NotifyPlatformImagesChanged() => PlatformImagesChanged?.Invoke();
    #endregion

    #region Constructor
    public ImageLoadingService(ProgressService progressService, ImageBinaryLoadingService imageBinaryLoadingService, FileSystemService fileSystemService, SharedDataService sharedDataService, YoutubeDownloadService youtubeDownloadService, IOptions<AppSettings> appSettings)
    {
        _progressService = progressService;
        _imageBinaryLoadingService = imageBinaryLoadingService;
        _fileSystemService = fileSystemService;
        _sharedDataService = sharedDataService;
        _youtubeDownloadService = youtubeDownloadService;
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Prefijo "Platform  |  " para los mensajes de progreso (vacío si no hay plataforma seleccionada).
    /// Las operaciones de imagen (descarga/copia/alta-res) son siempre de la plataforma seleccionada.
    /// </summary>
    private string PlatformPrefix => string.IsNullOrWhiteSpace(_sharedDataService.SelectedPlatform?.Name)
        ? string.Empty
        : $"{_sharedDataService.SelectedPlatform.Name}  |  ";

    #endregion

    #region Methods (public)
    public void GetPlatformImageAssets(Platform platform)
    {
        ImageAsset ResolvePlatformImage(string physicalFolder, string assetSubfolder, string name, string fallbackAsset = "none.png")
        {
            // 1. Custom folder (LaunchBox user pack)
            if (!string.IsNullOrWhiteSpace(physicalFolder))
            {
                string customFile = Path.Combine(physicalFolder, $"{name}.png");

                if (File.Exists(customFile))
                    return new ImageAsset(customFile);
            }

            // 2. App assets (pack incluido en la app). Se devuelve la RUTA FÍSICA en el output, no una URI
            //    ms-appx: el binario de estos assets lo decodifica ImageBinaryLoadingService.LoadImageAsync, que
            //    abre por File.OpenRead y NO entiende ms-appx (haría que el icono no cargara nunca).
            if (!string.IsNullOrWhiteSpace(assetSubfolder))
            {
                string assetsPhysicalPath = Path.Combine(AppContext.BaseDirectory, "Assets", assetSubfolder, $"{name}.png");

                if (File.Exists(assetsPhysicalPath))
                    return new ImageAsset(assetsPhysicalPath);
            }

            // 3. Fallback asset (también por ruta física, por el mismo motivo).
            return new ImageAsset(Path.Combine(AppContext.BaseDirectory, "Assets", assetSubfolder, fallbackAsset));
        }

        var fanartFolder = Path.Combine(_appSettings.LaunchBox.LaunchBoxFolder, "Images", "Platforms", platform.Name, "Fanart");

        if (!Directory.Exists(fanartFolder))
        {
            platform.Fanart = new ImageAsset(Path.Combine(fanartFolder, $"{platform.Name}.png"));
        }
        else
        {
            var firstImage = Directory.EnumerateFiles(fanartFolder).FirstOrDefault();
            var imagePath = firstImage ?? Path.Combine(fanartFolder, $"{platform.Name}.png");
            platform.Fanart = new ImageAsset(imagePath);
        }

        platform.Icon = ResolvePlatformImage(_appSettings.LaunchBox.PlatformIconPackFolder, "Platform Icons", platform.Name);
        platform.Logo = ResolvePlatformImage(_appSettings.LaunchBox.PlatformLogoPackFolder, "Platform Logos", platform.Name);

        GetPlatformOwnImages(platform);
    }

    /// <summary>
    /// Populates <see cref="Platform.OwnImages"/> with the platform's own artwork. Each platform-level
    /// <see cref="MediaType"/> maps to a subfolder under Images\Platforms\{Platform}\{Type.Value}\ (the same
    /// layout as the Fanart folder, swapping "Fanart" for the type name). The first image found in each
    /// existing folder is used; folders with no image are skipped.
    /// </summary>
    private void GetPlatformOwnImages(Platform platform)
    {
        platform.OwnImages.Clear();

        // The platform video (Videos\Platforms\{Name}.<ext>) goes first so it is the default preview.
        var platformVideo = GetPlatformVideo(platform);
        if (platformVideo != null)
            platform.OwnImages.Add(platformVideo);

        var platformImagesFolder = Path.Combine(_appSettings.LaunchBox.LaunchBoxFolder, "Images", "Platforms", platform.Name);

        foreach (var type in MediaType.PlatformImageTypes)
        {
            var typeFolder = Path.Combine(platformImagesFolder, type.Value);

            if (!Directory.Exists(typeFolder))
                continue;

            // All images of the type (not just the first) so the strip reflects added (Keep) vs replaced (Discard).
            foreach (var imagePath in Directory.EnumerateFiles(typeFolder)
                         .Where(f => _appSettings.LaunchBox.AllowedImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                platform.OwnImages.Add(new GameImage(imagePath, type));
        }
    }

    /// <summary>
    /// Resolves the platform's own video (Videos\Platforms\{Name}.&lt;ext&gt;, named exactly as the platform),
    /// trying the supported video extensions in order. Returned as a <see cref="GameImage"/> tagged with
    /// <see cref="MediaType.PlatformVideo"/> so it lives in <see cref="Platform.OwnImages"/> alongside the
    /// images; it is played (not decoded as a bitmap). Returns <c>null</c> when no video exists.
    /// </summary>
    private GameImage? GetPlatformVideo(Platform platform)
    {
        var platformVideosFolder = Path.Combine(_appSettings.LaunchBox.LaunchBoxFolder, "Videos", "Platforms");

        foreach (var extension in _appSettings.LaunchBox.AllowedVideoExtensions)
        {
            var videoPath = Path.Combine(platformVideosFolder, $"{platform.Name}{extension}");

            if (File.Exists(videoPath))
                return new GameImage(videoPath, MediaType.PlatformVideo);
        }

        return null;
    }

    /// <summary>
    /// Re-scans a platform's own images/video from disk into <see cref="Platform.OwnImages"/>. Public entry
    /// point so drop operations can refresh the model after copying/replacing files.
    /// </summary>
    public void RefreshPlatformOwnImages(Platform platform)
    {
        if (platform != null)
            GetPlatformOwnImages(platform);
    }

    /// <summary>
    /// Replaces the platform's video with the dropped file: backs up and deletes any existing video, copies
    /// the new one as Videos\Platforms\{Name}{ext}, refreshes the model and reports progress. Undoable.
    /// </summary>
    public async Task ReplacePlatformVideoAsync(Platform platform, string sourcePath)
    {
        if (platform == null || string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            return;

        string context = $"{platform.Name}  |  {MediaType.PlatformVideo.Value}";
        ProgressNotifier notifier = _progressService.StartBlockingOperation();
        notifier.IsIndeterminate = true;
        notifier.Message = $"{context}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ReplacingVideo_Progress] ?? "Replacing video..."}";
        _progressService.ProgressNotifier.Report(notifier);

        var videosFolder = Path.Combine(_appSettings.LaunchBox.LaunchBoxFolder, "Videos", "Platforms");
        Directory.CreateDirectory(videosFolder);

        var discarded = new List<(GameImage old, string backupPath)>();
        GameImage? created = null;

        try
        {
            // Backup + delete every existing platform video (any supported extension).
            foreach (var extension in _appSettings.LaunchBox.AllowedVideoExtensions)
            {
                var existingPath = Path.Combine(videosFolder, $"{platform.Name}{extension}");
                if (!File.Exists(existingPath))
                    continue;

                var old = new GameImage(existingPath, MediaType.PlatformVideo);
                string? backupPath = await _fileSystemService.DeleteImageFileAsync(old);
                if (backupPath != null)
                    discarded.Add((old, backupPath));
            }

            var targetPath = Path.Combine(videosFolder, $"{platform.Name}{Path.GetExtension(sourcePath)}");
            await Task.Run(() => File.Copy(sourcePath, targetPath, overwrite: true));
            created = new GameImage(targetPath, MediaType.PlatformVideo);

            RefreshPlatformOwnImages(platform);

            notifier.Message = $"{context}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_VideoReplaced_Progress] ?? "Video replaced"}";

            notifier.UndoNeedsBackup = discarded.Count > 0;
            notifier.UndoAction = async () =>
            {
                if (created != null)
                    await Task.Run(() => { try { if (File.Exists(created.File)) File.Delete(created.File); } catch { } });

                foreach (var (old, backupPath) in discarded)
                    await _fileSystemService.RestoreImageFileAsync(backupPath, old.File);

                RefreshPlatformOwnImages(platform);
                NotifyPlatformImagesChanged();
            };
        }
        catch
        {
            notifier.IsException = true;
            notifier.Message = $"{context}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ReplaceVideoFailed_Error] ?? "Could not replace the video"}";
        }
        finally
        {
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishBlockingOperation();
            NotifyPlatformImagesChanged();
        }
    }

    /// <summary>
    /// Adds a dropped image to a platform image type. When <paramref name="discardExisting"/> is true the
    /// existing files of that type are backed up and deleted first (replace); otherwise the image is just
    /// added (unique name). Refreshes the model and reports progress. Undoable.
    /// </summary>
    public async Task<GameImage?> AddPlatformImageAsync(Platform platform, MediaType type, string sourcePath, bool discardExisting)
    {
        if (platform == null || type == null || string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            return null;

        string context = $"{platform.Name}  |  {type.Value}";
        ProgressNotifier notifier = _progressService.StartBlockingOperation();
        notifier.IsIndeterminate = true;
        notifier.Message = $"{context}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ImportingMediaFile_Progress] ?? "Importing media file..."}";
        _progressService.ProgressNotifier.Report(notifier);

        var typeFolder = Path.Combine(_appSettings.LaunchBox.LaunchBoxFolder, "Images", "Platforms", platform.Name, type.Value);
        Directory.CreateDirectory(typeFolder);

        var discarded = new List<(GameImage old, string backupPath)>();
        GameImage? created = null;

        try
        {
            if (discardExisting)
            {
                foreach (var existingPath in Directory.EnumerateFiles(typeFolder).ToList())
                {
                    var old = new GameImage(existingPath, type);
                    string? backupPath = await _fileSystemService.DeleteImageFileAsync(old);
                    if (backupPath != null)
                        discarded.Add((old, backupPath));
                }
            }

            var targetPath = _fileSystemService.GetNewFileName(sourcePath, typeFolder, platform.Name);
            await Task.Run(() => File.Copy(sourcePath, targetPath, overwrite: false));
            created = new GameImage(targetPath, type);

            RefreshPlatformOwnImages(platform);

            notifier.Message = $"{context}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaFileImported_Progress] ?? "Media file imported"}";

            notifier.UndoNeedsBackup = discarded.Count > 0;
            notifier.UndoAction = async () =>
            {
                if (created != null)
                    await Task.Run(() => { try { if (File.Exists(created.File)) File.Delete(created.File); } catch { } });

                foreach (var (old, backupPath) in discarded)
                    await _fileSystemService.RestoreImageFileAsync(backupPath, old.File);

                RefreshPlatformOwnImages(platform);
                NotifyPlatformImagesChanged();
            };
        }
        catch
        {
            notifier.IsException = true;
            notifier.Message = $"{context}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ImportMediaFileFailed_Error] ?? "Could not import the media file"}";
        }
        finally
        {
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishBlockingOperation();
            NotifyPlatformImagesChanged();
        }

        return created;
    }

    /// <summary>
    /// Downloads an image from a remote URL into the target folder, creates a GameImage for it
    /// and loads its high-resolution binary.
    ///
    /// The file name is derived from <paramref name="baseFileName"/> following the LaunchBox
    /// "&lt;name&gt;-NN" convention and is made unique within the folder, so the image is matched
    /// back to its game the next time the platform images are loaded.
    /// </summary>
    /// <param name="url">The absolute HTTP(S) URL of the image to download.</param>
    /// <param name="targetFolder">The absolute folder where the image must be stored.</param>
    /// <param name="baseFileName">The base file name (typically the game title) used to build the file name.</param>
    /// <param name="type">The media type assigned to the created image.</param>
    /// <returns>The created and loaded game image.</returns>
    public async Task<(GameImage image, ProgressNotifier notifier)> CreateImageFromUrlAsync(string url, string targetFolder, string baseFileName, MediaType type)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(targetFolder))
        {
            throw new ArgumentException(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_UrlAndFolderRequired_Error] ?? "A valid image URL and target folder are required.");
        }

        // Progreso indeterminado (la descarga baja el buffer completo, sin %). Aquí, a nivel de servicio,
        // cubre TODAS las vías: drop del dashboard y las 3 del WebView (menú contextual, doble clic, Ctrl+clic).
        string context = string.IsNullOrWhiteSpace(baseFileName) ? $"{PlatformPrefix}{type.Value}  |  " : $"{PlatformPrefix}{type.Value}  |  {baseFileName}  |  ";
        ProgressNotifier notifier = _progressService.StartOperation();
        notifier.IsIndeterminate = true;

        // CancellationTokenSource para abortar la descarga desde el botón del ConsoleLog (útil si la conexión se
        // cuelga). Registrar la acción antes del primer Report hace que el botón de cancelar aparezca ya al empezar.
        using var cts = new CancellationTokenSource();
        notifier.CancelAction = () => cts.Cancel();

        notifier.Message = $"{context}{string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_DownloadingMedia_Progress] ?? "Downloading media file... ({0})", UrlHost(url))}";
        _progressService.ProgressNotifier.Report(notifier);

        try
        {
            byte[] imageBytes;
            string extension;

            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                (imageBytes, extension) = DecodeDataUri(url);
            }
            else
            {
                // ResponseHeadersRead: los headers llegan antes del cuerpo, así podemos mostrar el tamaño al empezar.
                using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                // Si el servidor declara el tamaño (Content-Length), lo mostramos antes de bajar el cuerpo.
                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength is > 0)
                {
                    notifier.Message = $"{context}{string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_DownloadingMediaSized_Progress] ?? "Downloading media file... ({0}, {1})", UrlHost(url), FormatFileSize(contentLength.Value))}";
                    _progressService.ProgressNotifier.Report(notifier);
                }

                imageBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                extension = ResolveImageExtension(url, response.Content.Headers.ContentType?.MediaType);
            }

            string targetPath = BuildUniqueImagePath(targetFolder, baseFileName, extension);

            Directory.CreateDirectory(targetFolder);
            await File.WriteAllBytesAsync(targetPath, imageBytes, cts.Token);

            GameImage image = new(targetPath, type);
            await _imageBinaryLoadingService.LoadImageAsync(image, ImageResolutionSettings.High);

            notifier.Message = $"{context}{string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaDownloaded_Progress] ?? "{0} downloaded", Path.GetFileName(targetPath))}";
            return (image, notifier);
        }
        catch (OperationCanceledException)
        {
            // Cancelación del usuario: warning en el log, no error. ExceptionService ignora la excepción, así que
            // no abre diálogo de fallo.
            notifier.IsWarning = true;
            notifier.Message = $"{context}{MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaDownloadCancelled_Progress] ?? "Media file download cancelled"}";
            throw;
        }
        catch
        {
            notifier.IsException = true;
            notifier.Message = $"{context}{MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaDownloadFailed_Error] ?? "Media file download failed"}";
            throw;
        }
        finally
        {
            notifier.CancelAction = null; // la operación terminó: ya no es cancelable
            notifier.IsIndeterminate = false;
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishOperation();
        }
    }

    /// <summary>
    /// Host de una URL para los mensajes de progreso de descarga; "image" si no es una URL con host.
    /// </summary>
    private static string UrlHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : "image";

    /// <summary>
    /// Formatea un tamaño en bytes a una cadena legible (B/KB/MB/GB, base 1000 como el resto de la app)
    /// para los mensajes de descarga de la consola. Una sola cifra decimal a partir de KB.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1000)
        {
            return $"{bytes} B";
        }

        string[] units = { "KB", "MB", "GB", "TB" };
        double size = bytes / 1000.0;
        int unit = 0;
        while (size >= 1000 && unit < units.Length - 1)
        {
            size /= 1000.0;
            unit++;
        }

        return $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    /// <summary>
    /// Copies a local image file into the target folder, creates a GameImage for it and loads its
    /// high-resolution binary. The file name follows the LaunchBox "&lt;name&gt;-NN" convention and
    /// is made unique within the folder, so the copy is matched back to its game on the next load.
    /// </summary>
    /// <param name="sourceFilePath">The absolute path of the image to copy.</param>
    /// <param name="targetFolder">The absolute folder where the copy must be stored.</param>
    /// <param name="baseFileName">The base file name (typically the game title) used to build the file name.</param>
    /// <param name="type">The media type assigned to the created image.</param>
    /// <returns>The created and loaded game image.</returns>
    public async Task<(GameImage image, ProgressNotifier notifier)> CreateImageFromFileAsync(string sourceFilePath, string targetFolder, string baseFileName, MediaType type)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || string.IsNullOrWhiteSpace(targetFolder))
        {
            throw new ArgumentException(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_SourceAndFolderRequired_Error] ?? "A valid source file and target folder are required.");
        }

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_SourceFileNotFound_Error] ?? "The source image file was not found.", sourceFilePath);
        }

        bool isVideo = MediaType.IsVideo(type.Key) || MediaType.IsPlatformVideo(type.Key);
        string mediaWord = isVideo ? "video" : "image";
        string mediaWordCap = isVideo ? "Video" : "Image";

        string context = string.IsNullOrWhiteSpace(baseFileName) ? $"{PlatformPrefix}{type.Value}  |  " : $"{PlatformPrefix}{type.Value}  |  {baseFileName}  |  ";
        ProgressNotifier notifier = _progressService.StartOperation();
        notifier.IsIndeterminate = true;
        notifier.Message = $"{context}{string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ImportingMedia_Progress] ?? "Importing {0}...", mediaWord)}";
        _progressService.ProgressNotifier.Report(notifier);

        try
        {
            string extension = Path.GetExtension(sourceFilePath);

            // Para imágenes, una extensión no reconocida cae a .png; para vídeos se conserva la original (la
            // validación del drop ya garantiza que es un contenedor de vídeo soportado).
            if (!isVideo && !IsAllowedImageExtension(extension))
            {
                extension = ".png";
            }

            string targetPath = BuildUniqueImagePath(targetFolder, baseFileName, extension);

            Directory.CreateDirectory(targetFolder);
            File.Copy(sourceFilePath, targetPath, overwrite: false);

            GameImage image = new(targetPath, type);
            // Decodifica el binario correcto según el tipo: fotograma para vídeo, bitmap para imagen.
            await _imageBinaryLoadingService.LoadGameImageBinaryAsync(image, ImageResolutionSettings.High);

            notifier.Message = $"{context}{string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaImported_Progress] ?? "{0} imported", Path.GetFileName(targetPath))}";
            return (image, notifier);
        }
        catch
        {
            notifier.IsException = true;
            notifier.Message = $"{context}{string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaImportFailed_Error] ?? "{0} import failed", mediaWordCap)}";
            throw;
        }
        finally
        {
            notifier.IsIndeterminate = false;
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishOperation();
        }
    }

    /// <summary>
    /// Downloads an image from a URL into the game's selected image set folder, registers it on the
    /// game model (<see cref="Game.AllImages"/> and <see cref="Game.Images"/>) and returns it.
    /// </summary>
    /// <param name="url">The absolute image URL to download.</param>
    /// <param name="game">The game the image is added to.</param>
    /// <param name="imageSet">The image set whose folder and media type are used.</param>
    /// <returns>The created, loaded and registered game image.</returns>
    public async Task<GameImage> AddImageFromUrlToGameAsync(string url, Game game, PlatformImageSet imageSet, string? destinationFolder = null)
    {
        (GameImage image, ProgressNotifier notifier) = await CreateImageFromUrlAsync(url, destinationFolder ?? imageSet.FolderPath, ResolveBaseFileName(game), imageSet.Type);
        imageSet.AddImage(image);
        RegisterImageOnGame(game, image); // emite ImageAddedToGame(game, image): las vistas agregadas refrescan desde ahí
        AttachAddImageUndo(notifier, game, imageSet, image);

        return image;
    }

    /// <summary>
    /// Copies a local image file into the game's selected image set folder, registers it on the
    /// game model (<see cref="Game.AllImages"/> and <see cref="Game.Images"/>) and returns it.
    /// </summary>
    /// <param name="sourceFilePath">The absolute path of the image to copy.</param>
    /// <param name="game">The game the image is added to.</param>
    /// <param name="imageSet">The image set whose folder and media type are used.</param>
    /// <returns>The created, loaded and registered game image.</returns>
    public async Task<GameImage> AddImageFromFileToGameAsync(string sourceFilePath, Game game, PlatformImageSet imageSet, string? destinationFolder = null)
    {
        (GameImage image, ProgressNotifier notifier) = await CreateImageFromFileAsync(sourceFilePath, destinationFolder ?? imageSet.FolderPath, ResolveBaseFileName(game), imageSet.Type);
        imageSet.AddImage(image);
        RegisterImageOnGame(game, image); // emite ImageAddedToGame(game, image): las vistas agregadas refrescan desde ahí
        AttachAddImageUndo(notifier, game, imageSet, image);

        return image;
    }

    /// <summary>
    /// Descarga un vídeo de YouTube y lo añade al juego como un media file más, reutilizando el mismo pipeline
    /// que un archivo local (copia con el nombre del juego, miniatura, registro y undo). El progreso de la
    /// descarga se muestra en la consola; el vídeo se baja a un temporal que se borra tras copiarlo al set.
    /// </summary>
    /// <param name="youtubeUrl">URL (o id) del vídeo de YouTube.</param>
    /// <param name="game">Juego al que se añade el vídeo.</param>
    /// <param name="imageSet">Image set (de tipo vídeo) cuya carpeta y tipo se usan.</param>
    public async Task<GameImage> AddVideoFromYoutubeToGameAsync(string youtubeUrl, Game game, PlatformImageSet imageSet)
    {
        string context = $"{PlatformPrefix}{imageSet.Type?.Value}  |  ";
        ProgressNotifier notifier = _progressService.StartOperation();

        // CancellationTokenSource para que el usuario pueda abortar la descarga desde el botón del ConsoleLog.
        // Registrar la acción ANTES del primer Report hace que el botón de cancelar aparezca ya al empezar.
        using var cts = new CancellationTokenSource();
        notifier.CancelAction = () => cts.Cancel();

        notifier.Message = $"{context}{MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_DownloadingYoutube_Progress] ?? "Downloading video from YouTube..."}";
        _progressService.ProgressNotifier.Report(notifier);

        string tempFolder = Path.Combine(Path.GetTempPath(), "Tracker", "YouTube");
        string? downloadedPath = null;
        // La resolución se configura en el GameImagesDashboard y se guarda con su config; se lee aquí en vivo.
        // El Key del enum ES la altura objetivo en píxeles (360/720/1080).
        int targetHeight = _appSettings.GameImagesDashboardControl?.VideoDownloadQuality?.Key ?? VideoDownloadQualitySettings.P1080.Key;
        try
        {
            var progress = new Progress<double>(fraction =>
            {
                notifier.Progress = (int)(fraction * 100);
                _progressService.ProgressNotifier.Report(notifier);
            });

            // En cuanto el descargador resuelve el stream sabe cuánto va a pesar: lo mostramos al empezar.
            var sizeProgress = new Progress<long>(sizeBytes =>
            {
                notifier.Message = $"{context}{string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_DownloadingYoutubeSized_Progress] ?? "Downloading video from YouTube... ({0})", FormatFileSize(sizeBytes))}";
                _progressService.ProgressNotifier.Report(notifier);
            });

            // Cambios de fase legibles (p. ej. la descarga puntual de ffmpeg la primera vez que se baja HD).
            var statusProgress = new Progress<string>(message =>
            {
                notifier.Message = $"{context}{message}";
                _progressService.ProgressNotifier.Report(notifier);
            });

            downloadedPath = await _youtubeDownloadService.DownloadAsync(youtubeUrl, tempFolder, targetHeight, progress, sizeProgress, statusProgress, cts.Token);
            notifier.Message = $"{context}{MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_YoutubeDownloaded_Progress] ?? "Video downloaded from YouTube"}";

            // Reutiliza el pipeline de archivo local (copia con el nombre del juego, miniatura, registro y undo).
            return await AddImageFromFileToGameAsync(downloadedPath, game, imageSet);
        }
        catch (OperationCanceledException)
        {
            // Cancelación del usuario: la entry se queda en el log mostrada como warning, pero NO se propaga como
            // fallo real (ExceptionService ignora OperationCanceledException, así que no abre diálogo de error).
            notifier.IsWarning = true;
            notifier.Message = $"{context}{MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_YoutubeDownloadCancelled_Progress] ?? "Video download cancelled"}";
            throw;
        }
        catch (Exception ex)
        {
            notifier.IsException = true;
            // Mostramos el motivo real (p. ej. "HD con audio requiere ffmpeg") en lugar de un mensaje genérico.
            notifier.Message = $"{context}{ex.Message}";
            throw;
        }
        finally
        {
            notifier.CancelAction = null; // la operación terminó: ya no es cancelable
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishOperation();

            if (downloadedPath != null)
            {
                await Task.Run(() => { try { if (File.Exists(downloadedPath)) File.Delete(downloadedPath); } catch { } });
            }
        }
    }

    /// <summary>
    /// Cuelga del notifier el undo de "añadir imagen a un juego": borra el fichero creado y desregistra la
    /// imagen del set y del juego (mismo patrón que el undo de las importadas).
    /// </summary>
    private void AttachAddImageUndo(ProgressNotifier notifier, Game game, PlatformImageSet imageSet, GameImage image)
    {
        notifier.UndoAction = async () =>
        {
            await Task.Run(() => { try { if (File.Exists(image.File)) File.Delete(image.File); } catch { } });
            imageSet.RemoveImage(image);
            UnregisterImageFromGame(game, image);
            NotifyPlatformImagesChanged();
        };
    }

    /// <summary>
    /// Borra del disco (con backup para poder deshacer) el medio <paramref name="image"/>: lo quita de su image
    /// set y lo desvincula de TODOS los juegos a los que está enlazado —no solo del seleccionado—, de modo que la
    /// galería del juego, el dashboard y el audit se refrescan por sus propios eventos
    /// (<see cref="ImageRemovedFromGame"/>). La operación es deshacible desde el activity log: el undo restaura el
    /// fichero, vuelve a meter la imagen en el set y la re-vincula a los mismos juegos
    /// (<see cref="ImageAddedToGame"/>).
    /// </summary>
    /// <param name="image">Medio (imagen o vídeo) a borrar. Si es null no hace nada.</param>
    public async Task DeleteImageAsync(GameImage image)
    {
        if (image is null)
            return;

        ProgressNotifier notifier = _progressService.StartOperation(false);

        DeletedMediaUndo? undo = await DeleteMediaToBackup(image);

        notifier.Message = $"{PlatformPrefix}{image.Type?.Value}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaDeleted_Progress] ?? "Media file deleted: {0}{1}", image.Name, image.FileExtension)}";

        if (undo?.BackupPath != null)
        {
            notifier.UndoNeedsBackup = true;
            notifier.UndoAction = () => RestoreDeletedMediaAsync(undo);
        }

        notifier.FinishOperation();
        _progressService.ProgressNotifier.Report(notifier);
        _progressService.FinishOperation();
    }

    /// <summary>
    /// Procesa un juego: conserva el medio <paramref name="keep"/> (elegido por el llamante según criterios) y
    /// borra del disco —con backup— TODOS los demás medios del juego, renombrando/moviendo además el conservado
    /// según <paramref name="renameCriteria"/> (región/sufijo/nombre). Toda la operación reporta un único progreso
    /// y cuelga un único undo que restaura todos los borrados y revierte el rename del conservado.
    /// </summary>
    /// <param name="game">Juego a procesar.</param>
    /// <param name="keep">Medio que se conserva (no se borra). Si es null no se conserva ninguno explícitamente.</param>
    /// <param name="renameCriteria">Criterios de renombrado aplicados al conservado.</param>
    public async Task ProcessGameAsync(Game game, GameImage keep, List<GameImageCriterion> renameCriteria)
    {
        if (game is null)
            return;

        ProgressNotifier notifier = _progressService.StartBlockingOperation(true);

        try
        {
            // Medios a borrar: todos los del juego salvo el conservado (por ruta de archivo, deduplicados).
            string? keepFile = keep?.File;
            List<GameImage> toDelete = game.Images
                .Where(i => i != null && (keepFile == null || i.File != keepFile))
                .GroupBy(i => i.File)
                .Select(g => g.First())
                .ToList();

            List<DeletedMediaUndo> deletedUndos = new();
            int total = toDelete.Count;
            int done = 0;
            foreach (GameImage media in toDelete)
            {
                DeletedMediaUndo? u = await DeleteMediaToBackup(media);
                if (u != null)
                    deletedUndos.Add(u);

                done++;
                notifier.Progress = total > 0 ? done * 100 / total : 100;
                notifier.Message = $"{PlatformPrefix}{game.Title}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ProcessingGame_Progress] ?? "Processing game ({0}/{1})", done, total)}";
                _progressService.ProgressNotifier.Report(notifier);
            }

            // Renombra/mueve el conservado según los criterios de procesado.
            RenamedMediaUndo? renameUndo = null;
            if (keep != null && renameCriteria != null)
            {
                string oldFile = keep.File;
                string oldName = keep.Name;
                ImageRegion oldRegion = keep.Region;
                string oldLeaf = keep.FileLeafFolder;

                string newFile = Utilities.ImageFileNameToProcessedImageFileName(keep, game, renameCriteria);
                if (!string.Equals(newFile, oldFile, StringComparison.Ordinal))
                {
                    keep.SetFileName(newFile, renameCriteria.Find(x => x.Type.Value == SettingsType.Region.Value));
                    await _fileSystemService.RenameFileAsync(oldFile, keep.File);
                    renameUndo = new RenamedMediaUndo
                    {
                        Image = keep,
                        OldFile = oldFile,
                        OldName = oldName,
                        OldRegion = oldRegion,
                        OldLeaf = oldLeaf,
                        NewFile = keep.File,
                    };
                }
            }

            notifier.Progress = 100;
            notifier.Message = $"{PlatformPrefix}{game.Title}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_GameProcessed_Progress] ?? "Game processed ({0} media deleted)", deletedUndos.Count)}";

            bool hasBackups = deletedUndos.Any(u => u.BackupPath != null);
            if (hasBackups || renameUndo != null)
            {
                notifier.UndoNeedsBackup = hasBackups;
                notifier.UndoAction = async () =>
                {
                    // Revierte primero el rename (devuelve el conservado a su ruta original) y luego restaura los borrados.
                    if (renameUndo != null)
                    {
                        await _fileSystemService.RenameFileAsync(renameUndo.NewFile, renameUndo.OldFile);
                        renameUndo.Image.RestoreFileName(renameUndo.OldFile, renameUndo.OldName, renameUndo.OldRegion, renameUndo.OldLeaf);
                    }

                    foreach (DeletedMediaUndo u in deletedUndos)
                        await RestoreDeletedMediaAsync(u);

                    NotifyPlatformImagesChanged();
                };
            }
        }
        catch (Exception ex)
        {
            // Sin esto, si algo lanza (RenameFileAsync, DeleteMediaToBackup...), FinishBlockingOperation no se
            // ejecutaría y la UI quedaría bloqueada (IsUIEnabled = false) para siempre.
            ExceptionService.LogToFile(ex, "Error processing game media.");
            notifier.Message = $"{PlatformPrefix}{game.Title}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ProcessGameFailed_Error] ?? "Error processing game"}";
        }
        finally
        {
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishBlockingOperation();
        }
    }

    /// <summary>
    /// Variante "por regiones" de <see cref="ProcessGameAsync"/> para el GameImagesRegionDashboard: conserva un
    /// conjunto de medios (<paramref name="keepImages"/>, uno por región) y borra —con backup— otro conjunto
    /// (<paramref name="deleteImages"/>), renombrando cada conservado según <paramref name="renameCriteria"/> (que
    /// para ese dashboard fija keep-region, de modo que cada conservado se queda en su subcarpeta de región). El
    /// llamante (VM) calcula los conjuntos aplicando las reglas por bucket (favoritas / otras / sin región) y la
    /// preselección. Toda la operación reporta un único progreso y cuelga un único undo combinado.
    /// </summary>
    public async Task ProcessGameMediaAsync(Game game, IReadOnlyCollection<GameImage> keepImages, IReadOnlyCollection<GameImage> deleteImages, List<GameImageCriterion> renameCriteria)
    {
        if (game is null)
            return;

        ProgressNotifier notifier = _progressService.StartBlockingOperation(true);

        try
        {
            // Nunca borrar lo que se conserva: excluir por ruta de archivo. Dedup de borrados por archivo.
            var keepFiles = new HashSet<string>(
                keepImages.Where(i => i != null).Select(i => i.File), StringComparer.OrdinalIgnoreCase);

            List<GameImage> toDelete = deleteImages
                .Where(i => i != null && !keepFiles.Contains(i.File))
                .GroupBy(i => i.File)
                .Select(g => g.First())
                .ToList();

            List<DeletedMediaUndo> deletedUndos = new();
            int total = toDelete.Count;
            int done = 0;
            foreach (GameImage media in toDelete)
            {
                DeletedMediaUndo? u = await DeleteMediaToBackup(media);
                if (u != null)
                    deletedUndos.Add(u);

                done++;
                notifier.Progress = total > 0 ? done * 100 / total : 100;
                notifier.Message = $"{PlatformPrefix}{game.Title}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ProcessingRegions_Progress] ?? "Processing regions ({0}/{1})", done, total)}";
                _progressService.ProgressNotifier.Report(notifier);
            }

            // Renombra/mueve cada conservado según los criterios (keep-region => se queda en su subcarpeta).
            List<RenamedMediaUndo> renameUndos = new();
            if (renameCriteria != null)
            {
                GameImageCriterion? regionCriterion = renameCriteria.Find(x => x.Type.Value == SettingsType.Region.Value);
                foreach (GameImage keep in keepImages.Where(i => i != null).GroupBy(i => i.File).Select(g => g.First()))
                {
                    string oldFile = keep.File;
                    string oldName = keep.Name;
                    ImageRegion oldRegion = keep.Region;
                    string oldLeaf = keep.FileLeafFolder;

                    string newFile = Utilities.ImageFileNameToProcessedImageFileName(keep, game, renameCriteria);
                    if (!string.Equals(newFile, oldFile, StringComparison.Ordinal))
                    {
                        keep.SetFileName(newFile, regionCriterion);
                        await _fileSystemService.RenameFileAsync(oldFile, keep.File);
                        renameUndos.Add(new RenamedMediaUndo
                        {
                            Image = keep,
                            OldFile = oldFile,
                            OldName = oldName,
                            OldRegion = oldRegion,
                            OldLeaf = oldLeaf,
                            NewFile = keep.File,
                        });
                    }
                }
            }

            notifier.Progress = 100;
            notifier.Message = $"{PlatformPrefix}{game.Title}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_RegionsProcessed_Progress] ?? "Regions processed ({0} media deleted)", deletedUndos.Count)}";

            bool hasBackups = deletedUndos.Any(u => u.BackupPath != null);
            if (hasBackups || renameUndos.Count > 0)
            {
                notifier.UndoNeedsBackup = hasBackups;
                notifier.UndoAction = async () =>
                {
                    // Revierte primero los renames y luego restaura los borrados.
                    foreach (RenamedMediaUndo r in renameUndos)
                    {
                        await _fileSystemService.RenameFileAsync(r.NewFile, r.OldFile);
                        r.Image.RestoreFileName(r.OldFile, r.OldName, r.OldRegion, r.OldLeaf);
                    }

                    foreach (DeletedMediaUndo u in deletedUndos)
                        await RestoreDeletedMediaAsync(u);

                    NotifyPlatformImagesChanged();
                };
            }
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Error processing game regions.");
            notifier.Message = $"{PlatformPrefix}{game.Title}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ProcessRegionsFailed_Error] ?? "Error processing regions"}";
        }
        finally
        {
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishBlockingOperation();
        }
    }

    /// <summary>
    /// Borra del disco (con backup) un medio operando por ruta de archivo y desvinculándolo de todos los juegos que
    /// lo referencian, notificando la baja a las vistas (<see cref="ImageRemovedFromGame"/>). NO crea progreso: es
    /// el núcleo reutilizable que envuelven <see cref="DeleteImageAsync"/> (un medio) y <see cref="ProcessGameAsync"/>
    /// (varios). Devuelve la información necesaria para deshacerlo, o un registro con BackupPath null si no se pudo
    /// respaldar (no deshacible). Nota: el mismo archivo puede estar representado por varias instancias de GameImage
    /// (canónica del set, con LinkedGames, vs copias por-juego de Game.AllImages); por eso se trabaja por archivo.
    /// </summary>
    private async Task<DeletedMediaUndo?> DeleteMediaToBackup(GameImage image)
    {
        if (image is null)
            return null;

        string file = image.File;
        Platform? platform = _sharedDataService.SelectedPlatform;
        PlatformImageSet? set = platform?.Images.ImageSets.Find(s => s.Type?.Value == image.Type?.Value);

        GameImage? canonical = set?.Images.Find(i => i.File == file);

        // Juegos candidatos: los enlazados a la canónica + el seleccionado (cuya galería/dashboard puede tener su
        // propia instancia del mismo archivo). Dedup por referencia.
        List<Game> candidateGames = new();
        if (canonical != null)
            candidateGames.AddRange(canonical.LinkedGames);
        if (_sharedDataService.SelectedGame != null && !candidateGames.Any(g => ReferenceEquals(g, _sharedDataService.SelectedGame)))
            candidateGames.Add(_sharedDataService.SelectedGame);

        // Quita el archivo del modelo de cada juego candidato y guarda la instancia real para notificar/rehacer.
        List<(Game game, GameImage instance)> removals = new();
        HashSet<GameImage> seenInstances = new(ReferenceEqualityComparer.Instance);
        foreach (Game game in candidateGames)
        {
            GameImage? inGame = game.AllImages.Find(i => i.File == file)
                ?? FirstByFile(game.Images, file);
            if (inGame == null)
                continue;

            game.AllImages.RemoveAll(i => i.File == file);
            RemoveAllByFile(game.Images, file);

            removals.Add((game, inGame));
            seenInstances.Add(inGame);
        }

        if (canonical != null)
            set?.RemoveImage(canonical);

        // Notifica la baja con cada instancia real (galería/dashboard) y, si la canónica no se notificó ya, también
        // con ella (el audit retira por Contains sobre la canónica del set).
        foreach (var (game, instance) in removals)
            ImageRemovedFromGame?.Invoke(game, instance);

        Game? canonicalNotifyGame = candidateGames.FirstOrDefault() ?? _sharedDataService.SelectedGame;
        bool canonicalNotifiedSeparately = false;
        if (canonical != null && canonicalNotifyGame != null && !seenInstances.Contains(canonical))
        {
            ImageRemovedFromGame?.Invoke(canonicalNotifyGame, canonical);
            canonicalNotifiedSeparately = true;
        }

        string? backupPath = await _fileSystemService.DeleteImageFileAsync(image);

        NotifyPlatformImagesChanged();

        return new DeletedMediaUndo
        {
            BackupPath = backupPath,
            File = file,
            Set = set,
            Canonical = canonical,
            CanonicalNotifiedSeparately = canonicalNotifiedSeparately,
            CanonicalNotifyGame = canonicalNotifyGame,
            Removals = removals,
        };
    }

    /// <summary>Restaura un medio borrado por <see cref="DeleteMediaToBackup"/> (inverso). No hace nada si no hubo backup.</summary>
    private async Task RestoreDeletedMediaAsync(DeletedMediaUndo undo)
    {
        if (undo?.BackupPath == null)
            return;

        await _fileSystemService.RestoreImageFileAsync(undo.BackupPath, undo.File);

        if (undo.Canonical != null)
            undo.Set?.AddImage(undo.Canonical);

        foreach (var (game, instance) in undo.Removals)
            RegisterImageOnGame(game, instance); // re-añade a AllImages/Images + emite ImageAddedToGame

        if (undo.CanonicalNotifiedSeparately)
            ImageAddedToGame?.Invoke(undo.CanonicalNotifyGame!, undo.Canonical!); // refresca el audit por la canónica

        NotifyPlatformImagesChanged();
    }

    /// <summary>Información para deshacer el borrado de un medio (ver <see cref="DeleteMediaToBackup"/>).</summary>
    private sealed class DeletedMediaUndo
    {
        public string? BackupPath;
        public string File = string.Empty;
        public PlatformImageSet? Set;
        public GameImage? Canonical;
        public bool CanonicalNotifiedSeparately;
        public Game? CanonicalNotifyGame;
        public List<(Game game, GameImage instance)> Removals = new();
    }

    /// <summary>Información para deshacer el renombrado del medio conservado en <see cref="ProcessGameAsync"/>.</summary>
    private sealed class RenamedMediaUndo
    {
        public GameImage Image = null!;
        public string OldFile = string.Empty;
        public string OldName = string.Empty;
        public ImageRegion OldRegion = null!;
        public string OldLeaf = string.Empty;
        public string NewFile = string.Empty;
    }

    /// <summary>Returns the first image in the collection whose file path matches, or null.</summary>
    private static GameImage? FirstByFile(System.Collections.ObjectModel.ObservableCollection<GameImage> images, string file)
    {
        for (int i = 0; i < images.Count; i++)
        {
            if (images[i].File == file)
                return images[i];
        }
        return null;
    }

    /// <summary>Removes every image with the given file path from an observable collection.</summary>
    private static void RemoveAllByFile(System.Collections.ObjectModel.ObservableCollection<GameImage> images, string file)
    {
        for (int i = images.Count - 1; i >= 0; i--)
        {
            if (images[i].File == file)
                images.RemoveAt(i);
        }
    }

    /// <summary>
    /// Importa al <paramref name="set"/> las imágenes ya emparejadas de cada juego (residen en
    /// <see cref="Game.Images"/> tras el matching del import, con su <c>.File</c> apuntando al origen).
    /// Reporta progreso DETERMINADO y bloqueante: una sola operación <c>({i}/{N})</c> sobre el total de
    /// imágenes, con prefijo unificado <c>Platform | Type</c>.
    /// </summary>
    /// <param name="games">Juegos con imágenes de origen emparejadas en <see cref="Game.Images"/>.</param>
    /// <param name="set">Set destino (su carpeta y tipo de medio).</param>
    /// <param name="discardExisting">Si es true, borra antes (con backup) las imágenes existentes del juego de ese tipo.</param>
    public async Task ImportMatchedImagesAsync(IList<Game> games, PlatformImageSet set, bool discardExisting, string? destinationFolder = null)
    {
        if (games is null || set?.Type is null)
            return;

        // Carpeta destino: la raíz del set o, si se indica, una subcarpeta de región (import del dashboard de
        // regiones). Se crea si no existe. Con destino de región, el Discard se acota a ESA subcarpeta.
        string targetFolder = string.IsNullOrWhiteSpace(destinationFolder) ? set.FolderPath : destinationFolder!;

        // Captura las imágenes de origen ANTES de tocar las colecciones (el matching las dejó en game.Images).
        var work = games
            .Select(game => (game, sources: game.Images.ToList()))
            .Where(w => w.sources.Count > 0)
            .ToList();

        int total = work.Sum(w => w.sources.Count);
        if (total == 0)
            return;

        string context = $"{PlatformPrefix}{set.Type?.Value}";
        ProgressNotifier notifier = _progressService.StartBlockingOperation();

        Directory.CreateDirectory(targetFolder);
        bool removedExisting = false;
        int done = 0;
        int failed = 0;

        // Registro para el undo: imágenes importadas (creadas) y, si hubo Discard, las existentes borradas.
        var created = new List<(Game game, GameImage image)>();
        var discarded = new List<(Game game, GameImage old, string backupPath)>();

        // El import opera sobre instancias de GamesInLauchboxDb (objetos nuevos creados por el loader); para que
        // las altas/bajas se vean en toda la app hay que registrarlas en la instancia canónica de platform.Games
        // —la que usan GameList, el dashboard y las galerías (SharedDataService.SelectedGame)—, mapeada por Equals.
        Platform? platform = _sharedDataService.SelectedPlatform;

        try
        {
            foreach (var (importGame, sources) in work)
            {
                Game game = platform?.Games.Find(g => g.Equals(importGame)) ?? importGame;

                // Discard: borra (con backup, vía FileSystemService) las imágenes existentes del juego de este tipo.
                // Las existentes del set seleccionado están en game.Images (el matching principal puebla Images,
                // no AllImages), que es además lo que muestra el dashboard.
                if (discardExisting)
                {
                    var existing = game.Images
                        .Where(img => string.Equals(img.Type?.Value, set.Type?.Value, StringComparison.Ordinal))
                        // Con destino de región, solo se descartan las existentes de ESA subcarpeta (no las de
                        // otras regiones ni la raíz); sin destino, todas las del tipo (comportamiento clásico).
                        .Where(img => string.IsNullOrWhiteSpace(destinationFolder)
                            || string.Equals(Path.GetDirectoryName(img.File), targetFolder, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (GameImage old in existing)
                    {
                        string? backupPath = await _fileSystemService.DeleteImageFileAsync(old);
                        set.RemoveImage(old);
                        UnregisterImageFromGame(game, old); // quita de game + emite ImageRemovedFromGame (refresca dashboard/galerías)
                        removedExisting = true;
                        if (backupPath != null)
                            discarded.Add((game, old, backupPath));
                    }
                }

                // Las imágenes de origen ocupan importGame.Images (instancia del import); quítalas de ahí.
                foreach (GameImage source in sources)
                    importGame.Images.Remove(source);

                foreach (GameImage source in sources)
                {
                    try
                    {
                        string targetPath = _fileSystemService.GetNewFileName(source.File, targetFolder, ResolveBaseFileName(game));

                        await Task.Run(() => File.Copy(source.File, targetPath, overwrite: false));

                        GameImage image = new(targetPath, set.Type);
                        set.AddImage(image);
                        RegisterImageOnGame(game, image); // emite ImageAddedToGame(game, image) sobre la instancia canónica
                        created.Add((game, image));
                    }
                    catch
                    {
                        failed++;
                    }

                    done++;
                    notifier.Progress = done * 100 / total;
                    notifier.Message = $"{context}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ImportingMediaFiles_Progress] ?? "Importing media files ({0}/{1})", done, total)}";
                    _progressService.ProgressNotifier.Report(notifier);
                }
            }

            int imported = total - failed;
            notifier.IsException = failed > 0;
            notifier.Message = failed == 0
                ? $"{context}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ImagesImported_Progress] ?? "{0} images imported", imported)}"
                : $"{context}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ImagesImportedPartial_Error] ?? "{0}/{1} images imported ({2} failed)", imported, total, failed)}";

            // Undo: primero quita las importadas (borra el fichero copiado y las desregistra, liberando sus
            // nombres en disco) y luego restaura las descartadas desde su backup y las vuelve a registrar.
            if (created.Count > 0 || discarded.Count > 0)
            {
                // El undo restaura las descartadas desde su backup; si las hubo, depende del backup.
                notifier.UndoNeedsBackup = discarded.Count > 0;
                notifier.UndoAction = async () =>
                {
                    foreach (var (game, image) in created)
                    {
                        await Task.Run(() => { try { if (File.Exists(image.File)) File.Delete(image.File); } catch { } });
                        set.RemoveImage(image);
                        UnregisterImageFromGame(game, image);
                    }

                    foreach (var (game, old, backupPath) in discarded)
                    {
                        await _fileSystemService.RestoreImageFileAsync(backupPath, old.File);
                        set.AddImage(old);
                        RegisterImageOnGame(game, old);
                    }

                    if (created.Count > 0)
                        NotifyPlatformImagesChanged();
                };
            }
        }
        finally
        {
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishBlockingOperation();

            if (removedExisting)
                NotifyPlatformImagesChanged();
        }
    }

    /// <summary>
    /// Enumerates the supported media files contained in <paramref name="folder"/> (recursively) and returns
    /// them as <see cref="GameImage"/> instances with their dimensions already resolved.
    ///
    /// The kind of media scanned follows <paramref name="mediaType"/>: a video type scans the allowed VIDEO
    /// extensions (and tags each result with that type, so the gallery shows it as a playable video and its
    /// dimensions/duration are read from the video container); anything else scans the allowed IMAGE extensions.
    ///
    /// Binaries are NOT decoded here: the gallery loads them on demand through its load-images command.
    /// This keeps folder selection cheap (only the file headers are read to obtain the dimensions used
    /// by the import statistics and matching).
    ///
    /// Progress (folder scan + dimension resolution) is reported through the <see cref="ProgressService"/>.
    /// </summary>
    /// <param name="folder">The absolute path of the folder to scan.</param>
    /// <param name="mediaType">The media type of the currently selected image set; decides image vs video.</param>
    /// <returns>The media found in the folder, ordered by file path.</returns>
    public async Task<List<GameImage>> LoadFolderMediaAsync(string folder, MediaType mediaType)
    {
        List<GameImage> media = new();

        ProgressNotifier notifier = _progressService.StartOperation();
        string folderName = Path.GetFileName(folder?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            notifier.Message = $"{folderName}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_FolderNotFound_Error] ?? "Folder not found"}";
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishOperation();
            return media;
        }

        bool isVideo = mediaType != null && MediaType.IsVideo(mediaType.Key);
        string[] allowedExtensions = isVideo
            ? _appSettings.LaunchBox.AllowedVideoExtensions
            : _appSettings.LaunchBox.AllowedImageExtensions;

        notifier.Message = $"{folderName}  |  {MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_ScanningFolder_Progress] ?? "Scanning folder for media files"}";
        _progressService.ProgressNotifier.Report(notifier);

        // Enumerate on a background thread: scanning a large media folder is I/O bound and would
        // otherwise block the UI thread the picker returns on.
        List<string> files = await Task.Run(() => Directory
            .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(file => allowedExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f)
            .ToList());

        foreach (string file in files)
        {
            // Videos must carry their type so the gallery plays them and dimensions are read from the container;
            // images keep the historical untyped form (resolved as bitmaps).
            media.Add(isVideo ? new GameImage(file, mediaType) : new GameImage(file));
        }

        // Adapt the dimension-resolution 0-100 percentage onto the operation notifier so the UI shows
        // a live progress bar while the file headers are read.
        Progress<int> dimensionsProgress = new(percent =>
        {
            notifier.Progress = percent;
            notifier.Message = $"{folderName}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_LoadingMedia_Progress] ?? "Loading {0} media files", media.Count)}";
            _progressService.ProgressNotifier.Report(notifier);
        });

        await _fileSystemService.LoadImageDimensionsAsync(media, dimensionsProgress);

        notifier.Message = $"{folderName}  |  {string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_MediaLoaded_Progress] ?? "{0} media files loaded", media.Count)}";
        notifier.FinishOperation();
        _progressService.ProgressNotifier.Report(notifier);
        _progressService.FinishOperation();

        return media;
    }
    #endregion

    #region Methods (web download helpers)
    /// <summary>
    /// Resolves the base file name used when storing an image for a game, preferring the title.
    /// </summary>
    private static string ResolveBaseFileName(Game game)
    {
        return game?.Title ?? game?.Rom ?? "image";
    }

    /// <summary>
    /// Registers the image on the game model so it becomes part of the game's image collections,
    /// then notifies listeners through <see cref="ImageAddedToGame"/>.
    /// </summary>
    private void RegisterImageOnGame(Game game, GameImage image)
    {
        if (game is null || image is null)
        {
            return;
        }

        if (!game.AllImages.Contains(image))
        {
            InsertImageOrderedByType(game.AllImages, image);
        }

        if (!game.Images.Contains(image))
        {
            game.Images.Add(image);
        }

        // Vincula el juego a la imagen: MatchImagesWithGamesAsync reconstruye game.Images desde el set vía
        // image.LinkedGames al cambiar de tipo/plataforma, así que sin esto una imagen recién añadida (drop,
        // descarga, URL) se perdía de la vista del juego hasta recargar de disco. Idempotente (Contains).
        if (!image.LinkedGames.Contains(game))
        {
            image.LinkedGames.Add(game);
        }

        ImageAddedToGame?.Invoke(game, image);
    }

    /// <summary>
    /// Inverso de <see cref="RegisterImageOnGame"/>: quita la imagen de las colecciones del juego y avisa a
    /// las vistas por-juego mediante <see cref="ImageRemovedFromGame"/>.
    /// </summary>
    private void UnregisterImageFromGame(Game game, GameImage image)
    {
        if (game is null || image is null)
        {
            return;
        }

        game.AllImages.Remove(image);
        game.Images.Remove(image);
        ImageRemovedFromGame?.Invoke(game, image);
    }

    /// <summary>
    /// Inserts the image into a list that is kept grouped/ordered by media type, placing it at the
    /// end of its own type group (i.e. before the first image whose type sorts after it).
    /// Mirrors the type ordering used when the game images are first loaded.
    /// </summary>
    private static void InsertImageOrderedByType(List<GameImage> images, GameImage image)
    {
        string typeValue = image.Type?.Value ?? string.Empty;

        int insertIndex = images.FindIndex(existing =>
            string.Compare(existing.Type?.Value ?? string.Empty, typeValue, StringComparison.CurrentCulture) > 0);

        if (insertIndex < 0)
        {
            images.Add(image);
        }
        else
        {
            images.Insert(insertIndex, image);
        }
    }
    /// <summary>
    /// Creates the shared <see cref="HttpClient"/> used for image downloads, configuring a browser
    /// user agent so that image hosts that reject default clients still serve the request.
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MM4LB");
        return client;
    }

    /// <summary>
    /// Resolves the image file extension from the URL, falling back to the response content type
    /// and finally to ".png" when neither yields an allowed image extension.
    /// </summary>
    private string ResolveImageExtension(string url, string? contentType)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            string urlExtension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();

            if (IsAllowedImageExtension(urlExtension))
            {
                return urlExtension;
            }
        }

        return contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".png"
        };
    }

    /// <summary>
    /// Decodes an inline "data:" image URI (typically base64) into its raw bytes and resolves the
    /// matching file extension from the embedded media type.
    /// </summary>
    private (byte[] bytes, string extension) DecodeDataUri(string dataUri)
    {
        int commaIndex = dataUri.IndexOf(',');

        if (commaIndex < 0)
        {
            throw new FormatException(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageLoading_DataUriNoPayload_Error] ?? "The data URI does not contain a payload.");
        }

        // Metadata sits between "data:" and the comma, e.g. "image/png;base64".
        string metadata = dataUri[5..commaIndex];
        string payload = dataUri[(commaIndex + 1)..];

        string mediaType = metadata.Split(';')[0];

        byte[] bytes = metadata.Contains("base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(payload)
            : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

        return (bytes, ResolveImageExtension(string.Empty, mediaType));
    }

    /// <summary>
    /// Determines whether the provided extension is one of the configured allowed image extensions.
    /// </summary>
    private bool IsAllowedImageExtension(string extension)
    {
        return !string.IsNullOrWhiteSpace(extension)
            && _appSettings.LaunchBox.AllowedImageExtensions.Contains(extension);
    }

    /// <summary>
    /// Builds a unique destination path of the form "&lt;sanitized name&gt;-NN&lt;extension&gt;",
    /// incrementing the numeric suffix until a free file name is found.
    /// </summary>
    private static string BuildUniqueImagePath(string folder, string baseFileName, string extension)
    {
        string sanitized = Utilities.ReplaceAllSpecialCharactersWithUnderscores(baseFileName).Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "image";
        }

        for (int index = 1; index < 1000; index++)
        {
            string candidate = Path.Combine(folder, $"{sanitized}-{index:00}{extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Extremely unlikely fallback to guarantee a unique name without looping forever.
        return Path.Combine(folder, $"{sanitized}-{Guid.NewGuid():N}{extension}");
    }
    #endregion
}