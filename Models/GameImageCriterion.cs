using MM4LB.Enums;
using Newtonsoft.Json;

namespace MM4LB.Models;

/// <summary>
/// A configurable criterion used to pre-select or to process the images of a game. Each criterion points, by
/// <see cref="ID"/>, to an entry of the enum catalog identified by <see cref="Type"/>.
/// </summary>
public class GameImageCriterion
{
    /// <summary>
    /// ID of the selected criteria in the dictionary.
    /// </summary>
    public int ID { get; set; } = -1;

    /// <summary>
    /// Specifies whether the criterion is active (to be applied when selecting or processing the game images) or not.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Type of criterion.
    /// </summary>
    public SettingsType Type { get; set; } = null!;

    /// <summary>
    /// Text describing the criteria.
    /// </summary>
    public string CriteriaName { get; set; } = string.Empty;

    /// <summary>
    /// Text describing the selected criteria type. Computed from <see cref="Type"/> + <see cref="ID"/>;
    /// no se persiste (se recalcula al restaurar Type/ID).
    /// </summary>
    [JsonIgnore]
    public string Name
    {
        get
        {
            if (ID != -1)
            {
                if (Type == SettingsType.Image) { return Enumeration.FromKey<ImageSettings>(ID)?.Value ?? string.Empty; }
                if (Type == SettingsType.Region) { return Enumeration.FromKey<RegionSettings>(ID)?.Value ?? string.Empty; }
                if (Type == SettingsType.FileNameSuffix) { return Enumeration.FromKey<FileNameSuffixSettings>(ID)?.Value ?? string.Empty; }
                if (Type == SettingsType.FileName) { return Enumeration.FromKey<FileNameSettings>(ID)?.Value ?? string.Empty; }
            }
            return string.Empty;
        }
    }
}
