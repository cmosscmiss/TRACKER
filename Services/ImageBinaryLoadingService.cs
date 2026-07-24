using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Enums;
using MM4LB.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Storage;

namespace MM4LB.Services;

/// <summary>
/// Carga y libera los binarios visuales (bitmaps) de imágenes y vídeos contra la caché de memoria. Extraído de
/// ImageLoadingService (§8.1): solo decodificación/caché, sin lógica de emparejado ni de mutación del modelo.
/// </summary>
public sealed class ImageBinaryLoadingService
{
    #region Attributes
    private readonly ProgressService _progressService;
    private readonly SharedDataService _sharedDataService;
    private readonly AppSettings _appSettings;
    #endregion

    #region Constructor
    public ImageBinaryLoadingService(ProgressService progressService, SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
    {
        _progressService = progressService;
        _sharedDataService = sharedDataService;
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
    /// <summary>
    /// Loads the binary of the image at the requested resolution.
    /// </summary>
    public async Task LoadImageAsync(ImageAsset image, ImageResolutionSettings imageResolution)
    {
        try
        {
            // Nothing to do only when the image already holds a binary at (or above) the requested
            // resolution. Crucially, never skip when there is no binary (e.g. after the cache evicted it):
            // it must be re-decoded or it would stay blank on scroll-back.
            if (image.HasBinary && image.Resolution.Key >= imageResolution.Key)
                return;

            if (!System.IO.File.Exists(image.File))
            {
                return;
            }

            // Decode from a managed file stream instead of Windows.Storage.StorageFile.
            // StorageFile.GetFileFromPathAsync fail-fasts intermittently (STATUS_STOWED_EXCEPTION
            // 0xc000027b) in this unpackaged app when called from the grid's ContainerContentChanging
            // during fast scroll, and the fail-fast bypasses the surrounding try/catch.
            using var fileStream = File.OpenRead(image.File);
            using var stream = fileStream.AsRandomAccessStream();

            var bmp = new BitmapImage();

            switch (imageResolution.Key)
            {
                case 1:
                    bmp.DecodePixelWidth = _appSettings.LaunchBox.ImageLowResDecodePixelWidth;
                    break;

                case 2:
                    bmp.DecodePixelWidth = _appSettings.LaunchBox.ImageHighResDecodePixelWidth;
                    break;
            }

            await bmp.SetSourceAsync(stream);
            image.Binary = bmp;
            image.Resolution = imageResolution;
        }
        catch (Exception e)
        {
            throw new Exception($"Error loading image binary for {image.File}.\n{e.Message}", e);
        }
    }

    /// <summary>
    /// Loads a representative still frame for a video (game video or platform video) and assigns it as the
    /// video's <see cref="ImageAsset.Binary"/>, so it renders as a regular thumbnail with the play badge over
    /// it. The frame is decoded from the video CONTENT (via <see cref="MediaComposition"/>), not the Windows
    /// shell thumbnail: the shell caches thumbnails by path, so a video replaced in place (same name) would
    /// otherwise keep showing the previous frame.
    ///
    /// IMPORTANT (crash safety): the <see cref="StorageFile"/>/<see cref="MediaComposition"/> work is pushed
    /// to a background thread (<see cref="Task.Run"/>). This method is now also called on demand as the grid
    /// scrolls (via <c>LoadGameImageBinaryAsync</c> ← <c>ContainerContentChanging</c>), and
    /// <c>StorageFile.GetFileFromPathAsync</c> fail-fasts intermittently (STATUS_STOWED_EXCEPTION 0xc000027b)
    /// in this unpackaged app when invoked on the UI thread during fast grid scroll — the same scenario that
    /// forced <see cref="LoadImageAsync"/> off StorageFile. Only the <see cref="BitmapImage"/> creation stays
    /// on the captured (UI) context, since it is a UI object.
    /// </summary>
    public async Task LoadVideoThumbnailAsync(GameImage video)
    {
        if (video == null || video.HasBinary || !File.Exists(video.File))
            return;

        try
        {
            var path = video.File;

            // Decode the frame off the UI thread: GetFileFromPathAsync must not run on the UI thread during
            // grid scroll (see method remarks). GetThumbnailAsync returns a thread-agnostic WinRT stream we
            // then hand to the BitmapImage back on the UI context.
            var frame = await Task.Run(async () =>
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                var clip = await MediaClip.CreateFromFileAsync(file);

                var composition = new MediaComposition();
                composition.Clips.Add(clip);

                // Grab a frame a bit into the clip (clamped for short videos) so it is not a black intro frame.
                var position = clip.OriginalDuration > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : TimeSpan.Zero;
                return await composition.GetThumbnailAsync(position, 320, 0, VideoFramePrecision.NearestFrame);
            });

            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(frame);
            video.Binary = bmp;
            frame.Dispose();

            // The binary is a 320px thumbnail, not the video, so it does not carry the real resolution (the
            // Binary setter leaves the dimensions untouched for videos). Read the native resolution from the
            // file once so the per-item dimensions (Game Image overlay) are correct; the pill/chart of the
            // Media Audit get it through the dimensions-loading path, which also raises ImageDimensionsChanged.
            if (video.Width <= 0 && await FileSystemService.TryReadVideoDimensionsAsync(video.File) is (int vw, int vh, TimeSpan dur))
            {
                video.SetDimensions(vw, vh);
                video.SetDuration(dur);
            }
        }
        catch
        {
            // No frame available (unsupported codec, etc.): the item keeps the play badge over an empty box.
        }
    }

    /// <summary>
    /// Carga el binario visual de una imagen de juego decidiendo por su tipo: para los vídeos (Video Snap,
    /// Theme Video y el vídeo de plataforma) extrae un fotograma representativo (<see cref="LoadVideoThumbnailAsync"/>),
    /// que el control pinta como una miniatura normal con la chapa de play encima; para las imágenes decodifica
    /// el bitmap (<see cref="LoadImageAsync"/>). Punto único para que ninguna ruta de galería llame a
    /// <see cref="LoadImageAsync"/> con un vídeo (lanzaría excepción al intentar decodificarlo como bitmap).
    /// </summary>
    public async Task LoadGameImageBinaryAsync(GameImage image, ImageResolutionSettings imageResolution)
    {
        if (image is null) { return; }
        if (image.Type != null && (MediaType.IsVideo(image.Type.Key) || MediaType.IsPlatformVideo(image.Type.Key)))
            await LoadVideoThumbnailAsync(image);
        else
            await LoadImageAsync(image, imageResolution);
    }

    /// <summary>
    /// Frees an image's decoded binary.
    /// </summary>
    public void ReleaseBinary(ImageAsset image)
    {
        image.ClearBinary();
    }

    /// <summary>
    /// Loads the high-resolution binaries of every image of the game not already at high resolution, reporting
    /// progress. Includes images whose binary was evicted from the cache (no binary, last decoded at High).
    /// </summary>
    public async Task LoadGameHighResImageBinariesAsync(Game game)
    {
        // Load high-res binaries for every image that is not already at high resolution. This includes
        // images whose binary was evicted from the cache (no binary, but last decoded at High), which the
        // previous filter wrongly skipped, leaving them blank.
        var imagesToLoad = game.Images.Where(x => !x.HasHighResBinary).ToList();

        if (imagesToLoad.Count == 0)
            return;

        // Las imágenes a cargar son todas del tipo seleccionado (game.Images = set seleccionado), así que el
        // tipo se toma una vez del SharedDataService en lugar de por imagen.
        var contextName = $"{PlatformPrefix}{_sharedDataService.SelectedImageSet?.Type?.Value}  |  {game.Title}";

        var notifier = _progressService.StartOperation();

        for (int i = 0; i < imagesToLoad.Count; i++)
        {
            await LoadGameImageBinaryAsync(imagesToLoad[i], ImageResolutionSettings.High);

            notifier.Progress = (i + 1) * 100 / imagesToLoad.Count;
            notifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageBinaryLoading_HighResProgress_Progress] ?? "{0}  |  Loading high-resolution binaries ({1}/{2})", contextName, i + 1, imagesToLoad.Count);
            _progressService.ProgressNotifier.Report(notifier);
        }

        notifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageBinaryLoading_HighResCompleted_Progress] ?? "{0}  |  Loading high-resolution binaries completed", contextName);
        notifier.FinishOperation();
        _progressService.ProgressNotifier.Report(notifier);
        _progressService.FinishOperation();
    }
    #endregion
}
