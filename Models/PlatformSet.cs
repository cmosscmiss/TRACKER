using System.Collections.Generic;

namespace MM4LB.Models;

/// <summary>
/// Class to hold the list of all platforms.
/// </summary>
public class PlatformSet : LocalFile
{
    private int _totalGames;

    public List<Platform> Platforms { get; set; } = new();

    public int TotalGames
    {
        get => _totalGames;
        internal set => SetProperty(ref _totalGames, value);
    }

    /// <summary>
    /// Default initialisation.
    /// </summary>
    /// <param name="progressService"></param>
    public PlatformSet(string platformsFile) : base(platformsFile)
    {
    }

    /// <summary>
    /// Iterates the platforms and adds the games in the collection that have not launchbox id to the list of all games (for the GamesAuditControl).
    /// </summary>
    internal void AddOrphanGames()
    {
        foreach (Platform platform in Platforms)
        {
            platform.AddOrphanGames();
        }
    }
}