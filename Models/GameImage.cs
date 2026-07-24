using MM4LB.Enums;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace MM4LB.Models;

public class GameImage : ImageAsset
{
    // TODO: Move default image to a global configuration setting.

    #region Attributes
    private ImageRegion _region = ImageRegion.NoRegion;
    #endregion

    #region Properties (Observable)
    public ImageRegion Region
    {
        get => _region;
        set => SetProperty(ref _region, value);
    }
    #endregion

    #region Properties
    /// <summary>
    /// Indicates whether the image has search strings assigned.
    /// Used mainly for UI components.
    /// </summary>
    public bool HasSearchStrings => SearchStrings.Count > 0;

    /// <summary>
    /// List of games that match this image.
    /// </summary>
    public List<Game> LinkedGames { get; protected set; } = new();

    /// <summary>
    /// String representation of linked games (used for UI display).
    /// </summary>
    public string LinkedGamesToString => string.Join(" || ", LinkedGames.Select(x => x.Title));

    /// <summary>
    /// Search strings used to match this image against games.
    /// </summary>
    public List<SelectableOption> SearchStrings { get; protected set; } = new();

    /// <summary>
    /// Media type of the image (e.g. Box Front, Screenshot, etc.). Puede ser null: el constructor sin tipo
    /// (<c>new GameImage(file)</c>) lo deja sin asignar.
    /// </summary>
    public MediaType? Type { get; protected set; }
    #endregion

    #region Properties (overrides)
    /// <summary>
    /// A video's binary is a thumbnail frame (downscaled), not the video itself, so it must not drive the
    /// reported dimensions: those are the video's native resolution, read from its file properties.
    /// </summary>
    protected override bool BinaryReflectsDimensions
        => Type == null || (!MediaType.IsVideo(Type.Key) && !MediaType.IsPlatformVideo(Type.Key));
    #endregion

    #region Constructors
    public GameImage() : base()
    {
        
    }

    /// <summary>
    /// Creates a GameImage from a file path and assigns its media type.
    /// Also determines the region based on the folder structure.
    /// </summary>
    public GameImage(string imageFile, MediaType? imageType = null) : base(imageFile)
    {
        Type = imageType;

        // Determine region based on the leaf folder name.
        ImageRegion? region = Enumeration.GetAll<ImageRegion>().FirstOrDefault(x => x.Value == FileLeafFolder);

        // If the folder does not correspond to a region, clear it.
        if (region == null)
        {
            FileLeafFolder = string.Empty;
            Region = ImageRegion.NoRegion;
        }
        else
        {
            Region = region;
        }
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Updates the file name and optionally clears the region if the setting requires it.
    /// </summary>
    public void SetFileName(string fileName, GameImageCriterion? criterion)
    {
        if (criterion?.IsActive == true && criterion.Name == RegionSettings.RegionDiscard.Value)
        {
            FileLeafFolder = string.Empty;
            Region = ImageRegion.NoRegion;
        }

        File = fileName;
        Name = Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>
    /// Reverts a previous <see cref="SetFileName"/>: restores the original file path, name, region and leaf folder.
    /// Used to undo the rename/move applied when processing a game.
    /// </summary>
    public void RestoreFileName(string file, string name, ImageRegion region, string fileLeafFolder)
    {
        File = file;
        Name = name;
        Region = region;
        FileLeafFolder = fileLeafFolder;
    }

    /// <summary>
    /// Assigns search strings for UI display based on the first linked game.
    /// </summary>
    public void SetSearchStrings(string imageFileName)
    {
        if (LinkedGames.Count == 0)
            return;

        foreach (string searchString in LinkedGames.First().SearchStrings)
        {
            SearchStrings.Add(new()
            {
                Name = searchString,
                IsChecked = searchString == imageFileName
            });
        }
    }
    #endregion
}