using MM4LB.Enums;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MM4LB.Models;

/// <summary>
/// Represents a single image set for a platform (e.g., Box - Front, Clear Logo, Screenshot).
/// 
/// This class is intentionally a pure DTO:
/// - It contains no async logic.
/// - It contains no ProgressService or UI reporting.
/// - It contains no matching logic.
/// - It contains no image loading logic.
/// 
/// All heavy operations (matching, loading binaries, loading dimensions, progress reporting)
/// are handled by LaunchBoxService.
/// 
/// PlatformImageSet only stores:
/// - The folder where images are located.
/// - The list of image file paths.
/// - The list of GameImage objects (created but not loaded).
/// - Lowercase versions of filenames for fast matching.
/// - Metadata such as MediaType and PlatformName.
/// </summary>
public class PlatformImageSet : ObservableObject
{
    #region Attributes
    private int _imagesCount;
    private List<string> _imageFiles = new();

    /// <summary>
    /// Folder metadata for this image set (type + folder path).
    /// </summary>
    private readonly PlatformImageFolder _imageFolder;
    #endregion

    // ---------------------------------------------------------------------
    // Observable properties
    // ---------------------------------------------------------------------

    /// <summary>
    /// Total number of images in this set.
    /// Observable for UI binding.
    /// </summary>
    public int ImagesCount
    {
        get => _imagesCount;
        private set => SetProperty(ref _imagesCount, value);
    }

    /// <summary>
    /// Total size on disk of this set's image files, in KB (each file's length / 1000, matching
    /// <see cref="LocalFile.FileSize"/>). Seeded by the initial folder scan and kept in sync incrementally on
    /// <see cref="AddImage"/>/<see cref="RemoveImage"/>, so the platform-wide size never needs a rescan.
    /// </summary>
    public long SizeOnDiskKb { get; set; }

    // ---------------------------------------------------------------------
    // Public properties
    // ---------------------------------------------------------------------

    /// <summary>
    /// Lowercase normalized versions of the filenames for fast matching.
    /// These are generated automatically when ImageFiles is set.
    /// </summary>
    public List<string> ImageFilesLowerCase = new();

    /// <summary>
    /// Absolute folder path where the images of this set are stored.
    /// </summary>
    public string FolderPath => _imageFolder.FolderPath;

    /// <summary>
    /// The media type of this image set (e.g., BoxFront, ClearLogo). Un image set solo se crea a partir de un
    /// folder con ImageType no-null (Platform.ImageFolderStrings filtra los de tipo desconocido), de ahí el '!'.
    /// </summary>
    public MediaType Type => _imageFolder.ImageType!;

    /// <summary>
    /// Name of the platform this image set belongs to.
    /// </summary>
    public string PlatformName { get; }

    /// <summary>
    /// Indicates whether the binary image data has been loaded.
    /// LaunchBoxService is responsible for setting this.
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// List of GameImage objects for this set.
    /// These objects are created without loading the actual image binary.
    /// </summary>
    public List<GameImage> Images { get; private set; } = new();

    /// <summary>
    /// Absolute file paths of all images in this set.
    /// Setting this property automatically:
    /// - Stores the list
    /// - Generates lowercase normalized filenames
    /// - Updates ImagesCount
    /// </summary>
    public List<string> ImageFiles
    {
        get => _imageFiles;
        set
        {
            _imageFiles = value ?? new();
            ImageFilesLowerCase = _imageFiles
                .Select(x => Utilities.ImageFileNameToGameString(x))
                .ToList();

            ImagesCount = _imageFiles.Count;
        }
    }

    // ---------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates a new image set for a platform.
    /// </summary>
    public PlatformImageSet(PlatformImageFolder imageFolder, string platformName)
    {
        _imageFolder = imageFolder;
        PlatformName = platformName;
        ImageFiles = new();
    }

    // ---------------------------------------------------------------------
    // Internal helpers (used by LaunchBoxService)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates GameImage objects for each file in ImageFiles.
    /// This does NOT load the image binary.
    /// </summary>
    public void CreateImages()
    {
        Images = _imageFiles
            .Select(f => new GameImage(f, Type))
            .OrderBy(img => img.Name)
            .ToList();
    }

    /// <summary>
    /// Returns the lowercase normalized filenames for fast matching.
    /// </summary>
    internal IReadOnlyList<string> GetLowercaseFiles() => ImageFilesLowerCase;

    // ---------------------------------------------------------------------
    // Public maintenance methods
    // ---------------------------------------------------------------------

    /// <summary>
    /// Adds a new image to the set (e.g., drag & drop).
    /// </summary>
    public void AddImage(GameImage image)
    {
        Images.Add(image);
        _imageFiles.Add(image.File);
        ImageFilesLowerCase.Add(Utilities.ImageFileNameToGameString(image.File));
        SizeOnDiskKb += image.FileSize;
        ImagesCount = Images.Count;
    }

    /// <summary>
    /// Removes an image from the set.
    /// </summary>
    public void RemoveImage(GameImage image)
    {
        if (Images.Remove(image))
        {
            // Locate by exact file path (unique), NOT by normalized name: two variants of the same
            // image (e.g. "…-01"/"…-02") normalize to the same string, so an IndexOf on the normalized
            // list could remove the wrong entry. _imageFiles and ImageFilesLowerCase are parallel.
            int index = _imageFiles.IndexOf(image.File);
            if (index != -1)
            {
                _imageFiles.RemoveAt(index);
                ImageFilesLowerCase.RemoveAt(index);
            }
            SizeOnDiskKb -= image.FileSize;
            if (SizeOnDiskKb < 0)
                SizeOnDiskKb = 0;
            ImagesCount = Images.Count;
        }
    }

    /// <summary>
    /// Removes an image from the set matching BY FILE PATH (not by reference), so it works with a
    /// <see cref="GameImage"/> that is not the exact instance held in <see cref="Images"/> — e.g. the read-only
    /// orphan scan builds fresh instances. Keeps the parallel <c>_imageFiles</c>/<c>ImageFilesLowerCase</c> lists,
    /// <see cref="Images"/> and the counters consistent whether the set was loaded (Images populated) or not.
    /// </summary>
    public void RemoveImageByFile(GameImage image)
    {
        int index = _imageFiles.IndexOf(image.File);
        if (index == -1)
            return;

        _imageFiles.RemoveAt(index);
        ImageFilesLowerCase.RemoveAt(index);

        GameImage? existing = Images.FirstOrDefault(x => x.File == image.File);
        if (existing != null)
            Images.Remove(existing);

        SizeOnDiskKb -= image.FileSize;
        if (SizeOnDiskKb < 0)
            SizeOnDiskKb = 0;

        ImagesCount = _imageFiles.Count;
    }
}