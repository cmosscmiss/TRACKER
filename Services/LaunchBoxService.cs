using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// High-level coordinator for all LaunchBox-related operations.
///
/// This service does NOT contain heavy logic.
/// Instead, it delegates responsibilities to:
/// - IPlatformLoadingService  → loading platforms, games, folder definitions
/// - IImageLoadingService     → loading image paths, matching, binaries, metadata
///
/// Responsibilities:
/// - Provide a single entry point for initializing the application
/// - Coordinate platform loading and image loading
/// - Expose simple methods for the UI/ViewModels to trigger image operations
/// - Hold shared state (PlatformSet) via SharedDataService
/// - Forward progress notifications to the console
///
/// This class intentionally remains small and easy to read.
/// </summary>
public sealed class LaunchBoxService
{
    #region Attributes
    private readonly PlatformLoadingService _platformLoadingService;
    private readonly ImageLoadingService _imageLoadingService;
    private readonly SharedDataService _sharedDataService;
    private readonly AppSettings _appSettings;
    #endregion

    #region Constructor
    public LaunchBoxService(PlatformLoadingService platformLoadingService, ImageLoadingService imageLoadingService, SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
    {
        _platformLoadingService = platformLoadingService;
        _imageLoadingService = imageLoadingService;
        _sharedDataService = sharedDataService;
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Initializes the entire LaunchBox data model.
    ///
    /// Steps:
    /// 1) Load PlatformSet (platforms, games, folder definitions)
    ///    → handled by PlatformLoadingService
    ///
    /// 2) Load image file paths for each platform
    ///    → handled by ImageLoadingService
    ///
    /// This method is the ONLY place where the full initialization pipeline runs.
    /// After this, the UI has access to a fully populated PlatformSet.
    /// </summary>
    public async Task InitializeAsync()
    {
        var platformSet = await _platformLoadingService.LoadPlatformSetAsync();
        _sharedDataService.PlatformSet = platformSet;

        // Enrich the platforms with the games found in the LaunchBox metadata database.
        await _platformLoadingService.LoadGamesLbDatabaseAsync(platformSet);

        var selectedPlatformName = !string.IsNullOrWhiteSpace(_appSettings.PlatformListControl.SelectedPlatform) ? _appSettings.PlatformListControl.SelectedPlatform : platformSet.Platforms.FirstOrDefault()?.Name;
        var platform = platformSet.Platforms.FirstOrDefault(p => p.Name == selectedPlatformName);
        _sharedDataService.SelectedPlatform = platform;

        // Instalación de LaunchBox sin plataformas (o ninguna que case con la selección guardada). Decisión de
        // producto: mostrar un mensaje y cerrar la app. Lanzamos para enrutar el fallo al único manejador de
        // error de arranque (LoadingWindow_Activated), que muestra el mensaje y cierra. Sin esto, desreferenciar
        // un platform null hacía crashear el primer arranque de un usuario nuevo (pasa el gate de App.OnLaunched
        // porque Launchbox.exe sí existe).
        if (platform is null)
            throw new InvalidOperationException(
                MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.LaunchBox_NoPlatforms_Error] ?? "No platforms found in LaunchBox. Add at least one platform in LaunchBox and restart MM4LB.");

        platform.SetSelectedImageSet(_appSettings.ImageTypeControl.SelectedImageSet?.Value);
        _sharedDataService.SelectedImageSet = platform.SelectedImageSet;
        var selectedGameTitle = _appSettings.GameListControl.SelectedGame;
        _sharedDataService.SelectedGame = platform.Games.FirstOrDefault(g => g.Title == selectedGameTitle);
    }
    #endregion
}