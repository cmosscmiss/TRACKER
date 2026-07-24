using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace MM4LB.Services;

/// <summary>
/// Service exposing several methods to work with files.
/// </summary>
public class FileSystemService
{
    #region Attributes
    private readonly AppSettings _appSettings;
    private readonly SharedDataService _sharedDataService;
    private readonly BackupService _backupService;
    #endregion

    #region Constructor
    public FileSystemService(IOptions<AppSettings> appSettings, SharedDataService sharedDataService, BackupService backupService)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
        _sharedDataService = sharedDataService ?? throw new ArgumentNullException(nameof(sharedDataService));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
    }
    #endregion

    #region Events
    /// <summary>
    /// Raised once after a batch of image dimensions has been read (<see cref="LoadImageDimensionsAsync"/>), no
    /// matter who triggered it. Lets views that show dimension-based stats (e.g. the image audit pill and the
    /// "Dimensions" command availability) refresh without each control having to watch the others.
    /// </summary>
    public event Action? ImageDimensionsChanged;
    #endregion

    #region Methods
    /// <summary>
    /// Backs-up (if backup is enabled) and deletes the image of the game passed as parameter.
    /// </summary>
    /// <param name="image"></param>
    /// <returns></returns>
    /// <summary>
    /// Hace una copia de seguridad de la imagen y la borra. Devuelve la ruta del fichero de backup creado
    /// (para poder restaurarla en un undo), o <c>null</c> si no se borró nada (fichero inexistente o fallo).
    /// </summary>
    public async Task<string?> DeleteImageFileAsync(GameImage image)
    {
        string? backupFile = await Task.Run<string?>(() =>
        {
            try
            {
                if (File.Exists(image.File))
                {
                    // Backup the file before deletion into the app's BACKUP folder, a subfolder of where the
                    // configuration file lives (%LocalAppData%\MM4LB\BACKUP), organized by platform/type. The
                    // timestamp avoids collisions.
                    string platform = _sharedDataService.SelectedPlatform?.Name ?? string.Empty;
                    string type = image.Type?.Value ?? string.Empty;
                    string backupFolder = Path.Combine(PersistAndRestoreService.SettingsFolderPath, "BACKUP", platform, type);
                    string backupImageFile = Path.Combine(backupFolder, $"{image.Name}-{DateTime.Now.ToFileTimeUtc()}{image.FileExtension}");
                    if (!Directory.Exists(backupFolder)) { _ = Directory.CreateDirectory(backupFolder); }
                    if (!File.Exists(backupImageFile)) { File.Copy(image.File, backupImageFile); }

                    // Deleting the file.
                    File.Delete(image.File);
                    return backupImageFile;
                }
            }
            catch (Exception ex)
            {
                // Loguear: si el File.Copy del backup tuvo éxito pero el File.Delete falló, el llamador ve null
                // ("no se borró nada") aunque quede un backup huérfano y el original en disco. Al menos deja rastro.
                ExceptionService.LogToFile(ex, "Error backing up/deleting the image file.");
            }
            return null;
        });

        // Tras el Task.Run estamos de vuelta en el hilo del llamador (UI en los flujos que borran): registra el
        // backup en el BackupService para que la pastilla del log refleje el nuevo tamaño/contador.
        if (backupFile != null)
        {
            try { _backupService.RegisterBackup(new FileInfo(backupFile).Length); } catch { }
        }

        return backupFile;
    }

    /// <summary>
    /// Restaura un fichero desde su copia de backup a su ruta original (no borra el backup). Usado por el
    /// undo de operaciones que borraron imágenes.
    /// </summary>
    public async Task RestoreImageFileAsync(string backupFile, string targetFile)
    {
        await Task.Run(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(backupFile) && File.Exists(backupFile) && !File.Exists(targetFile))
                {
                    string? folder = Path.GetDirectoryName(targetFile);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) { _ = Directory.CreateDirectory(folder); }
                    File.Copy(backupFile, targetFile);
                }
            }
            catch (Exception ex)
            {
                ExceptionService.LogToFile(ex, "Error restoring the image file from backup.");
            }
        });
    }

    /// <summary>
    /// Returns an available file name taking into account the game file name and the folder where the images are.
    /// </summary>
    /// <param name="sourceFileExtension">The extension of the source file</param>
    /// <param name="destFilePath">The destination folder</param>
    /// <param name="destFileName">The destination file name (normally the game title converted into a file name string)</param>
    /// <returns></returns>
    public string GetNewFileName(string sourceFile, string destFilePath, string destFileName)
    {
        string destFileSuffix = string.Empty;
        string sourceFileExtension = Path.GetExtension(sourceFile);
        string file = $@"{destFilePath}\{destFileName}{destFileSuffix}{sourceFileExtension}";
        int c0 = 0;
        while (File.Exists(file))
        {
            c0++;
            destFileSuffix = $"-{c0:00}";
            file = $@"{destFilePath}\{destFileName}{destFileSuffix}{sourceFileExtension}";
        }
        return file;
    }

    /// <summary>
    /// Loads an xml file into an XmlDocument object
    /// </summary>
    /// <param name="xmlFile">Platform file name</param>
    /// <returns>The loaded XML document</returns>
    public Task<XmlDocument> LoadXmlDocument(string xmlFile)
    {
        // Load(path) respeta la declaración de encoding del XML y el BOM (a diferencia de ReadAllText + LoadXml,
        // que decodifica como UTF-8 a ciegas y produce mojibake / XmlException con contenido Latin-1/Windows-1252).
        // Se hace en un hilo de fondo para no bloquear al llamador, manteniendo la firma que devuelve Task.
        return Task.Run(() =>
        {
            XmlDocument xmldoc = new();
            xmldoc.Load(xmlFile);
            return xmldoc;
        });
    }

    /// <summary>
    /// Renames the file passed as parameter from the file system.
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    public async Task RenameFileAsync(string sourceFileName, string destinationFileName)
    {
        if (sourceFileName != destinationFileName)
        {
            await Task.Run(() =>
            {
                if (File.Exists(sourceFileName) && !File.Exists(destinationFileName))
                {
                    File.Move(sourceFileName, destinationFileName);
                }
                else
                {
                    // TODO: EXCEPTION HANDLING (INTO THE CONSOLE??)
                    //throw new FileNotFoundException();
                }
            });
        }
    }
    #endregion

    #region Methods (image dimensions)
    /// <summary>
    /// Reads the pixel dimensions of an image straight from its file header, without decoding the
    /// bitmap or going through the (slow) Windows Shell property system. Reads only a handful of
    /// bytes per file, which makes it dramatically faster than <c>GetImagePropertiesAsync()</c> when
    /// scanning many images. Supports the formats commonly used for game artwork: PNG, JPEG, GIF, BMP
    /// and WebP. Returns <c>null</c> when the file is missing, unreadable or in an unsupported format,
    /// so callers can fall back to a heavier, more tolerant method.
    /// </summary>
    /// <param name="filePath">Absolute path to the image file.</param>
    /// <returns>The (width, height) read from the header, or <c>null</c> when it could not be read.</returns>
    public (int Width, int Height)? GetImageDimensionsFromHeader(string filePath)
    {
        return TryReadHeader(filePath, out int width, out int height) ? (width, height) : null;
    }

    /// <summary>
    /// Resolves the dimensions of <paramref name="image"/> without loading the full binary. Returns
    /// <c>true</c> when the fast file-header path was used, or <c>false</c> when it had to fall back to
    /// the (much slower) Windows.Storage property system, or failed. Callers can use this to detect
    /// when the fast path stops working.
    /// </summary>
    public async Task<bool> GetImageDimensionsAsync(GameImage image)
    {
        if (image is null) { return false; }
        try
        {
            // Videos are not bitmaps: neither the header reader nor BitmapDecoder understand a video container
            // (they return null / throw), so the native resolution is read from the file's video properties.
            if (image?.Type != null && (MediaType.IsVideo(image.Type.Key) || MediaType.IsPlatformVideo(image.Type.Key)))
            {
                if (await TryReadVideoDimensionsAsync(image.File) is (int vw, int vh, TimeSpan dur))
                {
                    image.SetDimensions(vw, vh);
                    image.SetDuration(dur);
                }
                return false;   // the fast header path was not used
            }

            // Fast path: read the dimensions straight from the file header (PNG/JPEG/GIF/BMP/WebP).
            // This only touches a few bytes and avoids the Windows Shell property system entirely,
            // which is the dominant cost when scanning many images. Done on a background thread so
            // the caller (UI) stays responsive; SetDimensions runs back on the caller's context.
            (int Width, int Height)? header = await Task.Run(() => GetImageDimensionsFromHeader(image!.File));

            if (header is (int width, int height))
            {
                image!.SetDimensions(width, height);
                return true;
            }

            // Fallback for formats the header reader does not understand: read only the image
            // metadata with BitmapDecoder over a managed file stream. Avoids Windows.Storage.StorageFile
            // (which fail-fasts intermittently in this unpackaged app) and does not decode the bitmap.
            using var fileStream = File.OpenRead(image!.File);
            using var stream = fileStream.AsRandomAccessStream();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

            // Update the GameImage dimensions based on the metadata.
            image.SetDimensions((int)decoder.PixelWidth, (int)decoder.PixelHeight);
            return false;
        }
        catch
        {
            // Silent catch: any failure (missing file, invalid image, permissions)
            // simply leaves the dimensions unchanged.
            // Consider logging in the future if silent failures become problematic.
            return false;
        }
    }

    /// <summary>
    /// Resolves the dimensions of many images efficiently. Reading the file headers is I/O bound, so
    /// when the files are cold (not yet in the OS file cache) doing them sequentially takes many
    /// seconds; this overlaps the reads with bounded parallelism to hide that latency. Images whose
    /// dimensions are already known are skipped, so re-runs are cheap. Returns the number of images
    /// that had to use the slow Windows.Storage fallback (0 in the normal case). Raises
    /// <see cref="ImageDimensionsChanged"/> once when done.
    /// </summary>
    /// <param name="images">The images to resolve.</param>
    /// <param name="progress">Optional progress, reported as a 0-100 percentage.</param>
    public async Task<int> LoadImageDimensionsAsync(IReadOnlyList<GameImage> images, IProgress<int>? progress = null)
    {
        // Skip images whose dimensions are already known so re-running the command is cheap.
        List<GameImage> pending = images.Where(x => x.Width == 0 || x.Height == 0).ToList();
        if (pending.Count == 0)
        {
            progress?.Report(100);
            return 0;
        }

        int fallbackCount = 0;
        const int batchSize = 256;
        ParallelOptions options = new() { MaxDegreeOfParallelism = Math.Min(16, Environment.ProcessorCount * 4) };

        // Process in batches so progress and the (UI-affine) SetDimensions run on the caller's context
        // between awaits, while each batch's header reads overlap on background threads.
        for (int start = 0; start < pending.Count; start += batchSize)
        {
            int count = Math.Min(batchSize, pending.Count - start);
            List<GameImage> slice = pending.GetRange(start, count);
            (int Width, int Height)?[] dimensions = new (int, int)?[count];

            await Task.Run(() => Parallel.For(0, count, options, i =>
            {
                dimensions[i] = GetImageDimensionsFromHeader(slice[i].File);
            }));

            for (int i = 0; i < count; i++)
            {
                if (dimensions[i] is (int width, int height))
                {
                    slice[i].SetDimensions(width, height);
                }
                else if (!await GetImageDimensionsAsync(slice[i]))
                {
                    fallbackCount++;
                }
            }

            progress?.Report((start + count) * 100 / pending.Count);
        }

        // Se han leído dimensiones nuevas: avisa a las vistas que dependen de ellas (pastillas, CanExecute, etc.).
        ImageDimensionsChanged?.Invoke();

        return fallbackCount;
    }

    /// <summary>
    /// Reads a video's NATIVE resolution (not its thumbnail's) from the file's video properties. Runs off the
    /// UI thread (the Shell property system is slow) and returns <c>null</c> when it cannot be read.
    /// </summary>
    public static async Task<(int Width, int Height, TimeSpan Duration)?> TryReadVideoDimensionsAsync(string path)
    {
        try
        {
            return await Task.Run(async () =>
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path);
                VideoProperties props = await file.Properties.GetVideoPropertiesAsync();
                return props.Width > 0 && props.Height > 0
                    ? ((int)props.Width, (int)props.Height, props.Duration)
                    : ((int, int, TimeSpan)?)null;
            });
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadHeader(string filePath, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[]? sig = ReadAt(stream, 0, 4);
            if (sig == null) return false;

            // PNG: 89 50 4E 47 — IHDR width/height are big-endian at offsets 16 and 20.
            if (sig[0] == 0x89 && sig[1] == 0x50 && sig[2] == 0x4E && sig[3] == 0x47)
            {
                byte[]? ihdr = ReadAt(stream, 16, 8);
                if (ihdr == null) return false;
                width = ReadBE32(ihdr, 0);
                height = ReadBE32(ihdr, 4);
                return width > 0 && height > 0;
            }

            // GIF: "GIF8" — logical screen width/height are little-endian at offsets 6 and 8.
            if (sig[0] == 0x47 && sig[1] == 0x49 && sig[2] == 0x46 && sig[3] == 0x38)
            {
                byte[]? lsd = ReadAt(stream, 6, 4);
                if (lsd == null) return false;
                width = ReadLE16(lsd, 0);
                height = ReadLE16(lsd, 2);
                return width > 0 && height > 0;
            }

            // BMP: "BM" — dimensions depend on the DIB header variant.
            if (sig[0] == 0x42 && sig[1] == 0x4D)
                return TryReadBmp(stream, out width, out height);

            // WebP: "RIFF" container holding a "WEBP" payload.
            if (sig[0] == 0x52 && sig[1] == 0x49 && sig[2] == 0x46 && sig[3] == 0x46)
                return TryReadWebp(stream, out width, out height);

            // JPEG: FF D8 — dimensions live in the SOFn segment, which must be scanned for.
            if (sig[0] == 0xFF && sig[1] == 0xD8)
                return TryReadJpeg(stream, out width, out height);

            return false;
        }
        catch
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    private static bool TryReadBmp(FileStream stream, out int width, out int height)
    {
        width = 0;
        height = 0;

        // The DIB header size at offset 14 distinguishes the legacy BITMAPCOREHEADER (12 bytes,
        // 16-bit dimensions) from the modern headers (32-bit dimensions).
        byte[]? dibSize = ReadAt(stream, 14, 4);
        if (dibSize == null) return false;

        if (ReadLE32(dibSize, 0) == 12)
        {
            byte[]? dims = ReadAt(stream, 18, 4);
            if (dims == null) return false;
            width = ReadLE16(dims, 0);
            height = ReadLE16(dims, 2);
        }
        else
        {
            byte[]? dims = ReadAt(stream, 18, 8);
            if (dims == null) return false;
            width = ReadLE32(dims, 0);
            height = ReadLE32(dims, 4); // may be negative for top-down bitmaps
        }

        width = Math.Abs(width);
        height = Math.Abs(height);
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(FileStream stream, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Start scanning right after the SOI marker (FF D8).
        stream.Position = 2;

        int b;
        while ((b = stream.ReadByte()) != -1)
        {
            if (b != 0xFF) continue;

            // Skip any fill bytes (a run of 0xFF) before the marker code.
            int marker = stream.ReadByte();
            while (marker == 0xFF) marker = stream.ReadByte();
            if (marker == -1) return false;

            // Markers without a length payload: SOI/EOI, restart markers, TEM.
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
                continue;

            int hi = stream.ReadByte();
            int lo = stream.ReadByte();
            if (hi == -1 || lo == -1) return false;
            int len = (hi << 8) | lo; // segment length, including these 2 bytes

            // SOFn carries the frame dimensions (excluding DHT 0xC4, JPG 0xC8, DAC 0xCC).
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                byte[] frame = new byte[5];
                if (stream.Read(frame, 0, 5) < 5) return false;
                // frame[0] = sample precision; then height (BE) and width (BE).
                height = (frame[1] << 8) | frame[2];
                width = (frame[3] << 8) | frame[4];
                return width > 0 && height > 0;
            }

            if (len < 2) return false;
            stream.Position += len - 2; // skip the rest of this segment
        }

        return false;
    }

    private static bool TryReadWebp(FileStream stream, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Offsets 8-11 must spell "WEBP"; offsets 12-15 are the format chunk FourCC.
        byte[]? head = ReadAt(stream, 8, 8);
        if (head == null) return false;
        if (head[0] != 0x57 || head[1] != 0x45 || head[2] != 0x42 || head[3] != 0x50) return false;

        string fourCc = $"{(char)head[4]}{(char)head[5]}{(char)head[6]}{(char)head[7]}";
        switch (fourCc)
        {
            case "VP8X": // Extended: canvas width-1 / height-1 as 24-bit little-endian.
            {
                byte[]? d = ReadAt(stream, 24, 6);
                if (d == null) return false;
                width = ReadLE24(d, 0) + 1;
                height = ReadLE24(d, 3) + 1;
                return true;
            }
            case "VP8 ": // Lossy: 14-bit dimensions after the 0x9D 0x01 0x2A start code.
            {
                byte[]? d = ReadAt(stream, 26, 4);
                if (d == null) return false;
                width = ReadLE16(d, 0) & 0x3FFF;
                height = ReadLE16(d, 2) & 0x3FFF;
                return width > 0 && height > 0;
            }
            case "VP8L": // Lossless: 14-bit width-1 / height-1 packed after the 0x2F signature.
            {
                byte[]? d = ReadAt(stream, 21, 4);
                if (d == null) return false;
                int bits = d[0] | (d[1] << 8) | (d[2] << 16) | (d[3] << 24);
                width = (bits & 0x3FFF) + 1;
                height = ((bits >> 14) & 0x3FFF) + 1;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes at <paramref name="offset"/>, or returns <c>null</c>
    /// when the file is too short or the read is incomplete.
    /// </summary>
    private static byte[]? ReadAt(FileStream stream, long offset, int count)
    {
        if (offset + count > stream.Length) return null;

        stream.Position = offset;
        byte[] buffer = new byte[count];

        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) return null;
            read += n;
        }

        return buffer;
    }

    private static int ReadBE32(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
    private static int ReadLE32(byte[] b, int i) => b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24);
    private static int ReadLE24(byte[] b, int i) => b[i] | (b[i + 1] << 8) | (b[i + 2] << 16);
    private static int ReadLE16(byte[] b, int i) => b[i] | (b[i + 1] << 8);
    #endregion
}