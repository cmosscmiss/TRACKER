using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MM4LB.Services;

/// <summary>
/// Descarga vídeos de YouTube con YoutubeExplode y los deja en un MP4 progresivo CON audio, listo para que la
/// app los reproduzca y miniaturice.
///
/// YouTube solo combina audio+vídeo (muxed) a 360p; el resto de resoluciones vienen como pistas separadas en
/// MP4 fragmentado (fMP4/DASH), que ni el póster (MediaComposition) ni el player digieren. Por eso, salvo el
/// caso 360p (que usa el muxed directo), se descargan las pistas de vídeo y audio por separado y se remuxean a
/// MP4 progresivo con ffmpeg (-c copy, sin recodificar).
///
/// ffmpeg NO se distribuye con la app: vive en el directorio de settings (%LocalAppData%\MM4LB\Tools\ffmpeg\
/// ffmpeg.exe). Si no está, se descarga ahí (build estática de BtbN) al arrancar la ventana principal o, como
/// red de seguridad, la primera vez que se necesite remuxear HD. El 360p (muxed) no lo necesita. Una vez
/// descargado se reutiliza sin volver a bajarlo.
/// </summary>
public sealed class YoutubeDownloadService
{
    // URL de la build estática win64 de BtbN (github.com/BtbN/FFmpeg-Builds). Es un .zip que lleva ffmpeg.exe en
    // bin\; descargamos el zip, extraemos SOLO el .exe y tiramos el resto. Anclado a un tag fijo para que no
    // cambie bajo los pies (para actualizar ffmpeg basta cambiar este tag por otro release de BtbN).
    private const string FfmpegZipUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    private readonly YoutubeClient _youtube = new();
    private readonly string _ffmpegPath;

    // Serializa la obtención de ffmpeg: el chequeo de arranque y una posible descarga HD no deben bajar el zip a
    // la vez (escribirían al mismo destino). Quien llegue segundo espera y, al liberarse, ve el binario ya listo.
    private readonly SemaphoreSlim _ffmpegLock = new(1, 1);

    /// <summary>Indica si ffmpeg ya está disponible en el directorio de settings, sin descargar nada.</summary>
    public bool IsFfmpegAvailable => File.Exists(_ffmpegPath);

    public YoutubeDownloadService()
    {
        // Ruta única de ffmpeg: el directorio de settings (%LocalAppData%\MM4LB\Tools\ffmpeg). No se distribuye
        // con la app; si no existe ahí, se descarga (ver EnsureFfmpegAvailableAsync) y se reutiliza después.
        _ffmpegPath = Path.Combine(PersistAndRestoreService.SettingsFolderPath, "Tools", "ffmpeg", "ffmpeg.exe");
    }

    /// <summary>Indica si la cadena es una URL (o id) de vídeo de YouTube reconocible.</summary>
    public static bool IsYoutubeVideoUrl(string? url)
        => !string.IsNullOrWhiteSpace(url) && VideoId.TryParse(url) is not null;

    /// <summary>
    /// Descarga el vídeo en <paramref name="targetFolder"/> a la resolución <paramref name="targetHeight"/>
    /// (360/720/1080) y devuelve la ruta del MP4 progresivo con audio creado. Si la resolución exacta no existe,
    /// cae a la siguiente disponible con más calidad y, si no la hay, a la siguiente con menos.
    /// </summary>
    /// <param name="youtubeUrl">URL o id del vídeo de YouTube.</param>
    /// <param name="targetFolder">Carpeta destino (se crea si no existe).</param>
    /// <param name="targetHeight">Altura objetivo en píxeles (360, 720 o 1080).</param>
    /// <param name="progress">Progreso opcional 0..1.</param>
    /// <param name="sizeProgress">Reporta, una vez resuelto el stream y ANTES de descargar, el tamaño total estimado en bytes.</param>
    /// <param name="statusProgress">Reporta cambios de fase legibles (p. ej. la descarga puntual de ffmpeg) para mostrarlos en la consola.</param>
    public async Task<string> DownloadAsync(string youtubeUrl, string targetFolder, int targetHeight, IProgress<double>? progress = null, IProgress<long>? sizeProgress = null, IProgress<string>? statusProgress = null, CancellationToken cancellationToken = default)
    {
        Video video = await _youtube.Videos.GetAsync(youtubeUrl, cancellationToken);
        StreamManifest manifest = await _youtube.Videos.Streams.GetManifestAsync(video.Id, cancellationToken);

        Directory.CreateDirectory(targetFolder);
        string baseName = SanitizeFileName(video.Title);

        // Solo-vídeo (preferimos mp4 H.264) y el mejor muxed (con audio, ~360p) como vía progresiva sin remux.
        List<IVideoStreamInfo> videoStreams = GetVideoOnlyStreams(manifest);
        var muxedList = manifest.GetMuxedStreams().ToList();
        IVideoStreamInfo? muxedStream = muxedList.Count > 0 ? muxedList.GetWithHighestVideoQuality() : null;

        var heights = new HashSet<int>(videoStreams.Select(s => s.VideoQuality.MaxHeight));
        if (muxedStream is not null)
        {
            heights.Add(muxedStream.VideoQuality.MaxHeight);
        }

        if (heights.Count == 0)
        {
            throw new InvalidOperationException(LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Youtube_NoVideoStream_Error] ?? "This video has no downloadable video stream.");
        }

        int effective = ResolveHeight(heights, targetHeight);

        // Si esa altura la sirve el muxed (típicamente 360p), lo usamos directo: ya es progresivo y con audio.
        if (muxedStream is not null && muxedStream.VideoQuality.MaxHeight == effective)
        {
            // Tamaño conocido del manifiesto: lo reportamos antes de empezar a bajar bytes.
            sizeProgress?.Report(muxedStream.Size.Bytes);
            return await DownloadStreamAsync(muxedStream, targetFolder, baseName, progress, cancellationToken);
        }

        // Esta calidad requiere remux con ffmpeg: garantízalo ANTES de bajar las pistas (normalmente ya estará,
        // descargado al arrancar; si no, lo baja ahora) para no malgastar la descarga de vídeo+audio.
        await EnsureFfmpegAvailableAsync(progress, statusProgress, cancellationToken);

        // En caso contrario, solo-vídeo de esa altura + mejor audio AAC, remuxeado a MP4 progresivo.
        IVideoStreamInfo videoStream = videoStreams
            .Where(s => s.VideoQuality.MaxHeight == effective)
            .GetWithHighestVideoQuality();
        IAudioStreamInfo audioStream = SelectAudioStream(manifest);

        // Estimación del MP4 final ≈ suma de ambas pistas (el remux con -c copy no recodifica).
        sizeProgress?.Report(videoStream.Size.Bytes + audioStream.Size.Bytes);
        return await DownloadAndRemuxAsync(videoStream, audioStream, targetFolder, baseName, progress, cancellationToken);
    }

    /// <summary>
    /// Resuelve la altura efectiva: la exacta si existe; si no, la menor disponible por encima del objetivo
    /// (siguiente con más calidad); si tampoco hay, la mayor por debajo (siguiente con menos calidad).
    /// </summary>
    private static int ResolveHeight(ICollection<int> heights, int target)
    {
        if (heights.Contains(target))
        {
            return target;
        }

        var higher = heights.Where(h => h > target).ToList();
        if (higher.Count > 0)
        {
            return higher.Min();
        }

        return heights.Max();
    }

    /// <summary>Streams de solo-vídeo, prefiriendo mp4 (H.264) por compatibilidad; si no hay, todos.</summary>
    private static List<IVideoStreamInfo> GetVideoOnlyStreams(StreamManifest manifest)
    {
        var videoOnly = manifest.GetVideoOnlyStreams();
        var mp4 = videoOnly.Where(s => s.Container == Container.Mp4).Cast<IVideoStreamInfo>().ToList();
        return mp4.Count > 0 ? mp4 : videoOnly.Cast<IVideoStreamInfo>().ToList();
    }

    /// <summary>
    /// Mejor pista de audio, prefiriendo AAC (contenedor mp4/m4a) para poder remuxear a mp4 con -c copy; el Opus
    /// (webm) da problemas dentro de un mp4. Cae al mejor audio disponible si no hay AAC.
    /// </summary>
    private static IAudioStreamInfo SelectAudioStream(StreamManifest manifest)
    {
        var audioOnly = manifest.GetAudioOnlyStreams();
        var aac = audioOnly.Where(s => s.Container == Container.Mp4).Cast<IAudioStreamInfo>().ToList();
        var pool = aac.Count > 0 ? aac : audioOnly.Cast<IAudioStreamInfo>().ToList();

        if (pool.Count == 0)
        {
            throw new InvalidOperationException(LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Youtube_NoAudioTrack_Error] ?? "This video has no downloadable audio track.");
        }

        // GetWithHighestBitrate está tipada como IStreamInfo; los elementos del pool son IAudioStreamInfo.
        return (IAudioStreamInfo)pool.GetWithHighestBitrate();
    }

    /// <summary>Descarga un stream concreto (p. ej. el muxed) a la carpeta destino y devuelve su ruta.</summary>
    private async Task<string> DownloadStreamAsync(IStreamInfo streamInfo, string targetFolder, string baseName, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string path = Path.Combine(targetFolder, $"{baseName}.{streamInfo.Container.Name}");
        await _youtube.Videos.Streams.DownloadAsync(streamInfo, path, progress, cancellationToken);
        return path;
    }

    /// <summary>
    /// Descarga la pista de vídeo (el grueso; su progreso representa el total) y la de audio, y las remuxea a un
    /// MP4 progresivo. Borra los temporales y, si algo falla, también el output a medias.
    /// </summary>
    private async Task<string> DownloadAndRemuxAsync(IVideoStreamInfo videoStream, IAudioStreamInfo audioStream, string targetFolder, string baseName, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string tempVideo = Path.Combine(targetFolder, $"{baseName}.video.{videoStream.Container.Name}");
        string tempAudio = Path.Combine(targetFolder, $"{baseName}.audio.{audioStream.Container.Name}");
        string output = Path.Combine(targetFolder, $"{baseName}.mp4");

        try
        {
            await _youtube.Videos.Streams.DownloadAsync(videoStream, tempVideo, progress, cancellationToken);
            await _youtube.Videos.Streams.DownloadAsync(audioStream, tempAudio, null, cancellationToken);
            await RemuxAsync(tempVideo, tempAudio, output, cancellationToken);
            return output;
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        finally
        {
            TryDelete(tempVideo);
            TryDelete(tempAudio);
        }
    }

    /// <summary>
    /// Garantiza que ffmpeg.exe esté disponible. Si ya está (cacheado o junto al .exe) no hace nada; si no, baja
    /// la build estática de BtbN (un .zip), extrae SOLO ffmpeg.exe a la carpeta de caché y descarta el resto.
    /// Es idempotente y seguro frente a llamadas concurrentes (el chequeo de arranque y una descarga HD pueden
    /// coincidir): se serializa con un semáforo, de modo que solo se descarga una vez. Reporta progreso 0..1 y
    /// mensajes de fase legibles. Cancelable: el token solo aborta ESTA llamada/descarga.
    /// </summary>
    public async Task EnsureFfmpegAvailableAsync(IProgress<double>? progress = null, IProgress<string>? statusProgress = null, CancellationToken cancellationToken = default)
    {
        if (File.Exists(_ffmpegPath))
        {
            return;
        }

        await _ffmpegLock.WaitAsync(cancellationToken);
        try
        {
            // Pudo dejarlo listo otra llamada mientras esperábamos el semáforo.
            if (File.Exists(_ffmpegPath))
            {
                return;
            }

            await DownloadAndInstallFfmpegAsync(progress, statusProgress, cancellationToken);
        }
        finally
        {
            _ffmpegLock.Release();
        }
    }

    /// <summary>
    /// Descarga la build de BtbN, extrae ffmpeg.exe y lo instala en la caché. Trabaja sobre temporales (.zip /
    /// .part) y solo "publica" el binario tras validarlo, de modo que una descarga cancelada o corrupta nunca
    /// deja un ffmpeg a medias. No comprueba existencia ni concurrencia: eso lo hace EnsureFfmpegAvailableAsync.
    /// </summary>
    private async Task DownloadAndInstallFfmpegAsync(IProgress<double>? progress, IProgress<string>? statusProgress, CancellationToken cancellationToken)
    {
        string targetDir = Path.GetDirectoryName(_ffmpegPath)!;
        Directory.CreateDirectory(targetDir);
        string zipPath = Path.Combine(targetDir, "ffmpeg-download.zip");
        string partPath = _ffmpegPath + ".part";

        statusProgress?.Report("Downloading ffmpeg (one-time setup)...");

        try
        {
            // 1) Descarga del zip, con progreso y cancelación. ResponseHeadersRead para no bufferizar todo en RAM.
            using (var http = new HttpClient())
            using (var response = await http.GetAsync(FfmpegZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;

                using (var src = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long readTotal = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        readTotal += read;
                        if (total is > 0)
                        {
                            progress?.Report((double)readTotal / total.Value);
                        }
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 2) Extrae únicamente la entrada ffmpeg.exe (BtbN la deja en una subcarpeta bin\); buscamos por nombre
            //    para no depender del nombre exacto de la carpeta raíz del zip.
            statusProgress?.Report("Extracting ffmpeg...");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                    e => string.Equals(e.Name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    throw new InvalidOperationException(LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Youtube_FfmpegZipMissing_Error] ?? "The downloaded ffmpeg zip does not contain ffmpeg.exe.");
                }

                entry.ExtractToFile(partPath, overwrite: true);
            }

            // 3) Validación: un binario que no responde a -version es inservible; mejor fallar ahora que al remuxear.
            await ValidateFfmpegAsync(partPath, cancellationToken);

            // 4) Publicación atómica: solo ahora el binario pasa a su nombre definitivo.
            TryDelete(_ffmpegPath);
            File.Move(partPath, _ffmpegPath);
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    /// <summary>Comprueba que el ffmpeg recién extraído arranca y responde a "-version" (exit code 0).</summary>
    private static async Task ValidateFfmpegAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = "-version",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Youtube_FfmpegNotExecutable_Error] ?? "The downloaded ffmpeg binary could not be run.", ex);
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(LocalizationService.Instance is LocalizationService invalidLoc
                ? invalidLoc.Format(MM4LB.Helpers.LocKeys.Youtube_FfmpegInvalid_Error, process.ExitCode)
                : $"The downloaded ffmpeg binary is not valid (code {process.ExitCode}).");
        }
    }

    /// <summary>
    /// Remuxea (sin recodificar) la pista de vídeo y la de audio a un MP4 progresivo con ffmpeg. "-c copy" copia
    /// los flujos tal cual (instantáneo, sin pérdida) y "+faststart" produce un MP4 progresivo (no fragmentado),
    /// que es lo que la app sabe reproducir y miniaturizar. El binario lo garantiza EnsureFfmpegAsync.
    /// </summary>
    private async Task RemuxAsync(string videoPath, string audioPath, string outputPath, CancellationToken cancellationToken)
    {
        // A estas alturas EnsureFfmpegAsync ya garantizó el binario; esto es solo una red de seguridad.
        if (!File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException(LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Youtube_FfmpegNotFound_Error] ?? "ffmpeg was not found; video cannot be downloaded at this quality.", _ffmpegPath);
        }

        string args = $"-y -hide_banner -loglevel error -i \"{videoPath}\" -i \"{audioPath}\" -c copy -map 0:v:0 -map 1:a:0 -movflags +faststart \"{outputPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException((LocalizationService.Instance is LocalizationService procLoc
                ? procLoc.Format(MM4LB.Helpers.LocKeys.Youtube_FfmpegProcessFailed_Error, process.ExitCode, stderr)
                : $"ffmpeg could not process the video (code {process.ExitCode}). {stderr}").Trim());
        }
    }

    /// <summary>Borra un fichero temporal si existe, ignorando errores.</summary>
    private static void TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort: un temporal que no se pudo borrar no debe romper la operación.
        }
    }

    /// <summary>Reemplaza los caracteres no válidos para un nombre de archivo.</summary>
    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "video" : name.Trim();
    }
}
