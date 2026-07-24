using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using System.Collections.Generic;

namespace MM4LB.Controls.ViewModels;

public class GamesAuditInGalleryViewModel : GamesAuditViewModel
{
    #region Attributes
    private readonly ProgressService _progressService;
    #endregion

    #region Properties
    /// <summary>
    /// Number of games with matched images.
    /// </summary>
    public int MatchedGamesCount { get; protected set; }

    /// <summary>
    /// Number of images matched.
    /// </summary>
    public int MatchedImagesCount { get; protected set; }
    #endregion


    #region Constructors
    public GamesAuditInGalleryViewModel(SharedDataService sharedDataService, ProgressService progressService, IStatisticsService statisticsService, IOptions<AppSettings> appSettings) : base(sharedDataService, statisticsService, appSettings)
    {
        _progressService = progressService;

        IsGamesAuditView = false;
    }
    #endregion


    #region Methods
    /// <summary>
    /// Cleaning the images of the games in the collection (when loading the images of a folder of changing platform)
    /// </summary>
    public void ClearGameImages()
    {
        foreach (Game game in GamesCollection)
        {
            game.Images.Clear();
        }

        // Reset outside the loop so the counters clear even when the collection is empty.
        MatchedGamesCount = 0;
        MatchedImagesCount = 0;
    }

    /// <summary>
    /// Matches the images loaded from the folder with the games in the collection.
    /// </summary>
    /// <param name="images"></param>
    /// <param name="folder"></param>
    /// <returns></returns>
    public void MatchImages(List<GameImage> images, string folder)
    {
        ProgressNotifier progressNotifier = _progressService.StartOperation();
        ClearGameImages();

        // Índice invertido search-string→juegos: por cada imagen se salta directo a sus juegos, en vez de
        // recorrer toda la colección. O(M + coincidencias) en vez de O(M·N). Sigue siendo image-outer, así que
        // cada juego recibe sus imágenes en el mismo orden que antes.
        Dictionary<string, List<Game>> gamesBySearchString = Platform.BuildSearchStringIndex(GamesCollection);
        foreach (GameImage image in images)
        {
            string imageGameString = Utilities.ImageFileNameToGameString(image.File);
            if (gamesBySearchString.TryGetValue(imageGameString, out List<Game>? matchedGames))
            {
                foreach (Game game in matchedGames)
                {
                    game.Images.Add(image);
                    MatchedImagesCount++;
                }
                MatchedGamesCount++;
            }
        }
        //SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection);
        string platformPrefix = string.IsNullOrWhiteSpace(SharedDataService.SelectedPlatform?.Name) ? string.Empty : $"{SharedDataService.SelectedPlatform.Name}  |  ";
        string? typeValue = SharedDataService.SelectedImageSet?.Type?.Value;
        progressNotifier.Message = $"{platformPrefix}{typeValue}  |  {folder}  |  Matched  {MatchedImagesCount}/{images.Count} media files with {MatchedGamesCount}/{GamesCollection.Count} games";
        progressNotifier.FinishOperation();
        _progressService.ProgressNotifier.Report(progressNotifier);
        _progressService.FinishOperation();
    }

    /// <summary>
    /// Sets the collection of games for the control (called normally when changing platform).
    /// </summary>
    /// <param name="games"></param>
    public override void SetGames(List<Game>? gamesInLaunchboxDb)
    {
        if (gamesInLaunchboxDb != null)
        {
            GamesCollection.Clear();
            GamesCollection.AddRange(gamesInLaunchboxDb.FindAll(x => x.InCollection));
            ClearGameImages();
            SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection);
        }
    }
    #endregion
}
