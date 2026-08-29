// ------------------------------------------------------------------------------------------------------------
// Settings.cs
// Enumerations for the different types of settings that the application supports.
// ------------------------------------------------------------------------------------------------------------

namespace Tracker.Enums;

public class ImageSettings : Enumeration
{
    public static readonly ImageSettings FileDimensions = new(1, "Dimensions");
    public static readonly ImageSettings FileSize = new(2, "Size");
    public static readonly ImageSettings FileFormatBMP = new(3, "BMP");
    public static readonly ImageSettings FileFormatGIF = new(4, "GIF");
    public static readonly ImageSettings FileFormatJPEG = new(5, "JPEG");
    public static readonly ImageSettings FileFormatJPG = new(6, "JPG");
    public static readonly ImageSettings FileFormatPNG = new(7, "PNG");
    public static readonly ImageSettings FileFormatWMF = new(8, "WMF");

    public ImageSettings()
    {
    }

    private ImageSettings(int id, string name) : base(id, name) { }
}

public class ImageResolutionSettings : Enumeration
{
    public static readonly ImageResolutionSettings Low = new(1, "Low");
    public static readonly ImageResolutionSettings Medium = new(2, "Medium");
    public static readonly ImageResolutionSettings High = new(3, "High");

    public ImageResolutionSettings()
    {
    }

    private ImageResolutionSettings(int id, string name) : base(id, name) { }
}

public class VideoDownloadQualitySettings : Enumeration
{
    // El Key es la altura objetivo en píxeles, así el descargador lo usa directamente para resolver la calidad.
    /// <summary>240p (LD).</summary>
    public static readonly VideoDownloadQualitySettings P240 = new(240, "240p");
    /// <summary>360p (SD). Si hay stream combinado, se usa directo; si no, solo-vídeo+audio remuxeado.</summary>
    public static readonly VideoDownloadQualitySettings P360 = new(360, "360p");
    /// <summary>480p (ED).</summary>
    public static readonly VideoDownloadQualitySettings P480 = new(480, "480p");
    /// <summary>720p (HD).</summary>
    public static readonly VideoDownloadQualitySettings P720 = new(720, "720p");
    /// <summary>1080p (Full HD).</summary>
    public static readonly VideoDownloadQualitySettings P1080 = new(1080, "1080p");

    public VideoDownloadQualitySettings()
    {
    }

    private VideoDownloadQualitySettings(int id, string name) : base(id, name) { }
}

public class RegionSettings : Enumeration
{
    public static readonly RegionSettings RegionKeep = new(1, "Keep");
    public static readonly RegionSettings RegionDiscard = new(2, "Discard");

    public RegionSettings()
    {
    }

    private RegionSettings(int id, string name) : base(id, name) { }
}

public class FileNameSuffixSettings : Enumeration
{
    public static readonly FileNameSuffixSettings Suffix = new(1, "Suffix");
    public static readonly FileNameSuffixSettings NoSuffix = new(2, "No suffix");

    public FileNameSuffixSettings()
    {
    }

    private FileNameSuffixSettings(int id, string name) : base(id, name) { }
}

public class FileNameSettings : Enumeration
{
    public static readonly FileNameSettings DatabaseId = new(1, "LaunchBox ID");
    public static readonly FileNameSettings Rom = new(2, "Rom");
    public static readonly FileNameSettings RomSimplified = new(3, "Rom simple");
    public static readonly FileNameSettings Title = new(4, "Game title");

    public FileNameSettings()
    {
    }

    private FileNameSettings(int id, string name) : base(id, name) { }
}

public class AspectRatioSettings : Enumeration
{
    public static readonly AspectRatioSettings AR11 = new(1, "1:1");
    public static readonly AspectRatioSettings AR916 = new(2, "9:16");
    public static readonly AspectRatioSettings AR34 = new(3, "3:4");
    public static readonly AspectRatioSettings AR169 = new(4, "16:9");
    public static readonly AspectRatioSettings AR43 = new(5, "4:3");

    public AspectRatioSettings()
    {
    }

    private AspectRatioSettings(int id, string name) : base(id, name) { }
}

public class SettingsType : Enumeration
{
    public static readonly SettingsType Image = new(1, nameof(Image));
    public static readonly SettingsType Region = new(2, nameof(Region));
    public static readonly SettingsType FileNameSuffix = new(3, nameof(FileNameSuffix));
    public static readonly SettingsType FileName = new(4, nameof(FileName));

    // Ctor sin parámetros requerido por EnumerationJsonConverter<SettingsType> (restricción new()).
    public SettingsType() { }

    private SettingsType(int id, string name) : base(id, name) { }
}