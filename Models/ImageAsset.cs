using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Enums;
using System;

namespace MM4LB.Models;

public class ImageAsset : LocalFile
{
    #region Attributes
    private BitmapImage? _binary;
    #endregion

    #region Properties
    public BitmapImage? Binary
    {
        get => _binary;
        set
        {
            if (SetProperty(ref _binary, value))
            {
                SetFileSize();
                // Only refresh the dimensions when a binary is actually decoded. When the binary is
                // cleared (value == null, e.g. cache eviction) keep the last known dimensions: they
                // describe the file, not the in-memory bitmap (and avoids dereferencing null).
                if (value != null)
                {
                    DecodedSizeMb = EstimateDecodedSizeMb(value);
                    // For plain images the decoded bitmap IS the asset, so it carries the real dimensions.
                    // For a video the binary is only a downscaled thumbnail frame (e.g. 320px wide): its size
                    // is NOT the video resolution, so those assets keep their dimensions (read from the file's
                    // video properties) and we must not overwrite them here. See BinaryReflectsDimensions.
                    if (BinaryReflectsDimensions)
                    {
                        Width = value.PixelWidth;
                        Height = value.PixelHeight;
                        OnPropertyChanged(nameof(Width));
                        OnPropertyChanged(nameof(Height));
                        OnPropertyChanged(nameof(Dimensions));
                        OnPropertyChanged(nameof(QualityText));
                    }
                }
                OnPropertyChanged(nameof(HasBinary));
            }
        }
    }

    public bool HasBinary => Binary != null;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Dimensions => $"{Width}x{Height}";

    /// <summary>
    /// Duración del clip para los assets de vídeo (cero para imágenes). Se lee de las propiedades del fichero
    /// a la vez que la resolución (ver <see cref="SetDuration"/>).
    /// </summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>Duración formateada (m:ss, o h:mm:ss si llega a la hora); cadena vacía si no hay duración.</summary>
    public string DurationText => Duration > TimeSpan.Zero
        ? (Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss") : Duration.ToString(@"m\:ss"))
        : string.Empty;

    /// <summary>
    /// Etiqueta de calidad de vídeo (p. ej. "1080p") derivada de la resolución vertical; cadena vacía si aún no se
    /// conoce la altura. Las dimensiones brutas (WxH) viven en <see cref="Dimensions"/>; esta es la clasificación
    /// 360p/720p/1080p habitual. Solo es significativa para vídeos.
    /// </summary>
    public string QualityText => Height > 0 ? $"{Height}p" : string.Empty;

    /// <summary>
    /// Whether the decoded <see cref="Binary"/> represents this asset's real pixel dimensions. True for plain
    /// images (the bitmap is the asset); overridden to false for assets whose binary is only a downscaled
    /// preview (e.g. a video's thumbnail frame), whose dimensions are resolved from the source file instead.
    /// </summary>
    protected virtual bool BinaryReflectsDimensions => true;

    /// <summary>
    /// Approximate RAM (MB) the decoded bitmap occupies at its current resolution, computed when the binary
    /// is decoded. Unlike <see cref="Width"/>/<see cref="Height"/> (the native file size) this reflects the
    /// down-scaled <c>DecodePixelWidth</c> the binary was actually decoded at, so the cache can charge a
    /// low-res image far less than a high-res one. Kept across <see cref="ClearBinary"/> so the cache can
    /// refund the exact amount it charged when the binary is evicted.
    /// </summary>
    public double DecodedSizeMb { get; private set; }
    public ImageResolutionSettings Resolution { get; set; } = ImageResolutionSettings.Low;

    /// <summary>
    /// True only when the image currently holds a decoded binary at the highest (native) resolution.
    /// Used to skip images that are already fully loaded and, conversely, to detect the ones that still
    /// need a high-res decode (including those whose binary was evicted from the cache, which have no
    /// binary even though their last <see cref="Resolution"/> was High).
    /// </summary>
    public bool HasHighResBinary => HasBinary && Resolution == ImageResolutionSettings.High;
    #endregion

    #region Constructors
    public ImageAsset() : base() { }

    public ImageAsset(string filePath) : base(filePath) { }
    #endregion

    #region Methods (public)
    public void ClearBinary()
    {
        Binary = null;
    }

    public void SetDimensions(int width, int height)
    {
        Width = width;
        Height = height;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(Dimensions));
        OnPropertyChanged(nameof(QualityText));
    }

    /// <summary>Asigna la duración del clip (vídeos); se lee de las propiedades del fichero con la resolución.</summary>
    public void SetDuration(TimeSpan duration)
    {
        Duration = duration;
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationText));
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Estimates the decoded bitmap's memory footprint (MB, 4 bytes/pixel BGRA). Only <c>DecodePixelWidth</c>
    /// is set when decoding, so the bitmap keeps its native aspect ratio and the decode never enlarges past
    /// the native width. <c>DecodePixelWidth == 0</c> means it was decoded at native size.
    /// </summary>
    private static double EstimateDecodedSizeMb(BitmapImage binary)
    {
        int nativeWidth = binary.PixelWidth;
        int nativeHeight = binary.PixelHeight;
        if (nativeWidth <= 0 || nativeHeight <= 0)
            return 0;

        int decodeWidth = binary.DecodePixelWidth > 0
            ? Math.Min(binary.DecodePixelWidth, nativeWidth)
            : nativeWidth;
        double decodeHeight = (double)decodeWidth * nativeHeight / nativeWidth;
        return decodeWidth * decodeHeight * 4 / (1024.0 * 1024.0);
    }
    #endregion
}