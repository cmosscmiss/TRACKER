namespace MM4LB.Enums;

/// <summary>
/// Enumeration for the different media types supported by LaunchBox.
/// </summary>
public class MediaType : Enumeration
{
    public static readonly MediaType AdvertisementFlyerBack = new(1, "Advertisement Flyer - Back");
    public static readonly MediaType AdvertisementFlyerFront = new(2, "Advertisement Flyer - Front");
    public static readonly MediaType AmazonBackground = new(3, "Amazon Background");
    public static readonly MediaType AmazonPoster = new(4, "Amazon Poster");
    public static readonly MediaType AmazonScreenshot = new(5, "Amazon Screenshot");
    public static readonly MediaType ArcadeCabinet = new(6, "Arcade - Cabinet");
    public static readonly MediaType ArcadeCircuitBoard = new(7, "Arcade - Circuit Board");
    public static readonly MediaType ArcadeControlPanel = new(8, "Arcade - Control Panel");
    public static readonly MediaType ArcadeControlsInformation = new(9, "Arcade - Controls Information");
    public static readonly MediaType ArcadeMarquee = new(10, "Arcade - Marquee");
    public static readonly MediaType Banner = new(11, "Banner");
    public static readonly MediaType Box3D = new(12, "Box - 3D");
    public static readonly MediaType BoxBack = new(13, "Box - Back");
    public static readonly MediaType BoxBackReconstructed = new(14, "Box - Back - Reconstructed");
    public static readonly MediaType BoxFront = new(15, "Box - Front");
    public static readonly MediaType BoxFrontReconstructed = new(16, "Box - Front - Reconstructed");
    public static readonly MediaType BoxFull = new(17, "Box - Full");
    public static readonly MediaType BoxSpine = new(18, "Box - Spine");
    public static readonly MediaType Cart3D = new(19, "Cart - 3D");
    public static readonly MediaType CartBack = new(20, "Cart - Back");
    public static readonly MediaType CartFront = new(21, "Cart - Front");
    public static readonly MediaType ClearLogo = new(22, "Clear Logo");
    public static readonly MediaType Disc = new(23, "Disc");
    public static readonly MediaType EpicGamesBackground = new(24, "Epic Games Background");
    public static readonly MediaType EpicGamesPoster = new(25, "Epic Games Poster");
    public static readonly MediaType EpicGamesScreenshot = new(26, "Epic Games Screenshot");
    public static readonly MediaType FanartBackground = new(27, "Fanart - Background");
    public static readonly MediaType FanartBoxBack = new(28, "Fanart - Box - Back");
    public static readonly MediaType FanartBoxFront = new(29, "Fanart - Box - Front");
    public static readonly MediaType FanartCartBack = new(30, "Fanart - Cart - Back");
    public static readonly MediaType FanartCartFront = new(31, "Fanart - Cart - Front");
    public static readonly MediaType FanartDisc = new(32, "Fanart - Disc");
    public static readonly MediaType GOGPoster = new(33, "GOG Poster");
    public static readonly MediaType GOGScreenshot = new(34, "GOG Screenshot");
    public static readonly MediaType Icon = new(35, "Icon");
    public static readonly MediaType OriginBackground = new(36, "Origin Background");
    public static readonly MediaType OriginPoster = new(37, "Origin Poster");
    public static readonly MediaType OriginScreenshot = new(38, "Origin Screenshot");
    public static readonly MediaType Poster = new(39, "Poster");
    public static readonly MediaType ScreenshotGameOver = new(40, "Screenshot - Game Over");
    public static readonly MediaType ScreenshotGameSelect = new(41, "Screenshot - Game Select");
    public static readonly MediaType ScreenshotGameTitle = new(42, "Screenshot - Game Title");
    public static readonly MediaType ScreenshotGameplay = new(43, "Screenshot - Gameplay");
    public static readonly MediaType ScreenshotHighScores = new(44, "Screenshot - High Scores");
    public static readonly MediaType Square = new(45, "Square");
    public static readonly MediaType SteamBanner = new(46, "Steam Banner");
    public static readonly MediaType SteamPoster = new(47, "Steam Poster");
    public static readonly MediaType SteamScreenshot = new(48, "Steam Screenshot");
    public static readonly MediaType UplayBackground = new(49, "Uplay Background");
    public static readonly MediaType UplayThumbnail = new(50, "Uplay Thumbnail");
    public static readonly MediaType Video = new(100, "Video");
    public static readonly MediaType ThemeVideo = new(101, "Theme Video");
    public static readonly MediaType Recordings = new(102, nameof(Recordings));
    public static readonly MediaType Trailer = new(103, nameof(Trailer));
    public static readonly MediaType Manual = new(110, "Manual");
    public static readonly MediaType Music = new(120, "Music");

    // Platform-level images: the platform's OWN artwork (not game images). Stored under
    // Images\Platforms\{Platform}\{Value}\, i.e. the Value is exactly the LaunchBox subfolder name.
    public static readonly MediaType PlatformBanner = new(150, "Banner");
    public static readonly MediaType PlatformBox3D = new(151, "Default 3D Box");
    public static readonly MediaType PlatformCart3D = new(152, "Default 3D Cart");
    public static readonly MediaType PlatformBox = new(153, "Default Box");
    public static readonly MediaType PlatformCart = new(154, "Default Cart");
    public static readonly MediaType PlatformDevice = new(155, "Device");
    public static readonly MediaType PlatformFanart = new(156, "Fanart");

    // Platform-level video: the platform's own video, stored under Videos\Platforms\{Platform}.<ext>.
    // Lives alongside the platform images (in Platform.OwnImages) but is played, not decoded as a bitmap.
    public static readonly MediaType PlatformVideo = new(157, "Platform Video");

    /// <summary>
    /// The platform-level image types, in display order. Their <see cref="Enumeration.Value"/> is the
    /// name of the subfolder under Images\Platforms\{Platform}\ where the image lives.
    /// </summary>
    public static readonly MediaType[] PlatformImageTypes =
        [PlatformBanner, PlatformBox3D, PlatformCart3D, PlatformBox, PlatformCart, PlatformDevice, PlatformFanart];

    public static bool IsImage(int key) => key < 100;

    public static bool IsManual(int key) => key >= 110 && key < 120;

    public static bool IsMusic(int key) => key >= 120 && key < 130;

    public static bool IsVideo(int key) => key >= 100 && key < 110;

    public static bool IsPlatformImage(int key) => key >= 150 && key < 157;

    public static bool IsPlatformVideo(int key) => key == PlatformVideo.Key;

    public MediaType()
    {
    }

    public MediaType(int id, string name) : base(id, name) { }
}