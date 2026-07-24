using MM4LB.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MM4LB.Models;

/// <summary>
/// Represents all image sets belonging to a platform.
/// 
/// This class is intentionally lightweight and acts as a pure data container:
/// - It does NOT load image binaries.
/// - It does NOT perform matching between games and images.
/// - It does NOT report progress.
/// - It does NOT interact with UI.
/// - It does NOT access the filesystem directly.
/// 
/// Responsibilities:
/// - Hold the list of PlatformImageSet objects.
/// - Hold a collection of GameImage objects (used by UI when needed).
/// - Expose the total number of discovered image files.
/// 
/// All heavy logic (filesystem access, matching, loading binaries, dimensions, progress)
/// is handled by LaunchBoxService.
/// </summary>
public class PlatformImages
{
    /// <summary>
    /// Total number of image files across all image sets.
    /// This is updated by LaunchBoxService.
    /// </summary>
    private int _totalImages;

    /// <summary>
    /// Total number of image files across all image sets.
    /// Exposed as read-only.
    /// </summary>
    public int TotalImages => _totalImages;

    /// <summary>
    /// List of image sets (one per image type).
    /// These are created and populated by LaunchBoxService.
    /// </summary>
    public List<PlatformImageSet> ImageSets { get; private set; } = new();

    /// <summary>
    /// Collection of loaded GameImage objects (used by UI).
    /// LaunchBoxService is responsible for populating this when needed.
    /// </summary>
    public ObservableCollection<GameImage> Images { get; private set; } = new();

    /// <summary>
    /// Creates a new PlatformImages container.
    /// </summary>
    public PlatformImages()
    {
    }

    // ---------------------------------------------------------------------
    // Internal helpers used by LaunchBoxService
    // ---------------------------------------------------------------------

    /// <summary>
    /// Clears all image sets and resets counters.
    /// Called by LaunchBoxService before loading new image sets.
    /// </summary>
    internal void Reset()
    {
        ImageSets.Clear();
        _totalImages = 0;
        Images.Clear();
    }

    /// <summary>
    /// Adds a new image set to the platform.
    /// Called by LaunchBoxService.
    /// </summary>
    internal void AddImageSet(PlatformImageSet set)
    {
        ImageSets.Add(set);
    }

    /// <summary>
    /// Increments the total number of images.
    /// Called by LaunchBoxService after loading file paths.
    /// </summary>
    internal void AddToTotalImages(int count)
    {
        _totalImages += count;
    }
}