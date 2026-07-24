using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Enums;

namespace MM4LB.Models;

/// <summary>
/// Represents a LaunchBox platform (e.g., "Nintendo NES", "Sony PlayStation").
///
/// This class is intentionally a clean DTO:
/// - It contains no async logic.
/// - It contains no filesystem access.
/// - It contains no image loading logic.
/// - It contains no matching logic.
/// - It contains no progress reporting.
///
/// Responsibilities:
/// - Hold platform metadata (file path, LB folder).
/// - Hold the list of games.
/// - Hold the list of image folder definitions.
/// - Hold the PlatformImages container (DTO).
/// - Split the platform's declared folders into image vs video sets (ImageFolderStrings / VideoFolderStrings).
///
/// All heavy logic is handled by LaunchBoxService.
/// </summary>
public class Platform : LocalFile
{
    #region Attributes
    private string _launchBoxFolder;
    private PlatformImageSet? _selectedImageSet;
    private readonly List<PlatformImageFolder> _imageFolderStrings = new();
    private readonly List<PlatformImageFolder> _videoFolderStrings = new();
    #endregion

    #region Properties
    public List<Game> Games { get; } = new();
    public List<Game> GamesInLauchboxDb { get; } = new();
    public PlatformImages Images { get; }
    public PlatformImageSet? SelectedImageSet
    {
        get => _selectedImageSet;
        set => SetProperty(ref _selectedImageSet, value);
    }
    public ImageAsset? Icon { get; set; }
    public ImageAsset? Logo { get; set; }
    public ImageAsset? Fanart { get; set; }

    /// <summary>
    /// Platform metadata read from the platform's &lt;Platform&gt; node in Platforms.xml (see
    /// <see cref="SetMetadata"/>), as an ordered list of label/value rows for the details sheet. Only
    /// fields with a non-empty value are included, in display order, so the view iterates them with a
    /// single template instead of one property + block per field.
    /// </summary>
    public List<PlatformMetadataField> Metadata { get; } = new();

    /// <summary>
    /// The platform's OWN images (Banner, Default 3D Box, Device, Fanart, ...), one per
    /// platform-level <see cref="Enums.MediaType"/> that has an image on disk. Reuses
    /// <see cref="GameImage"/> (image + media type) so the views can render them with the same
    /// control as game images. Populated by <see cref="Services.ImageLoadingService.GetPlatformImageAssets"/>.
    /// </summary>
    public List<GameImage> OwnImages { get; } = new();

    /// List of the platform's IMAGE folder definitions, taken from its &lt;PlatformFolder&gt; nodes in
    /// Platforms.xml (BoxFront, ClearLogo, Screenshot, ...), sorted by media type.
    ///
    /// The setter is the single ingestion point for ALL the platform's declared folders and splits them:
    /// - It classifies each folder by its <see cref="MediaType"/> (never by a path prefix), so custom folder
    ///   locations are honored: images go here, videos go to <see cref="VideoFolderStrings"/>, and
    ///   manuals/music/unknown types are dropped (this app does not manage them).
    /// - It normalizes each path: relative LaunchBox paths (Images\..., Videos\...) become absolute against the
    ///   LaunchBox root, while already-rooted (custom/relocated) paths are left untouched.
    ///
    /// The loader later uses these folders to create the image <see cref="PlatformImageSet"/> objects.
    /// </summary>
    public List<PlatformImageFolder> ImageFolderStrings
    {
        get => _imageFolderStrings;
        set
        {
            if (value == null)
                return;

            foreach (PlatformImageFolder folder in value)
            {
                // A folder whose media type is not modeled by this app (e.g. a new LaunchBox type): ignore it
                // rather than crash, so unknown folders can't break loading.
                if (folder.ImageType == null)
                    continue;

                int key = folder.ImageType.Key;
                bool isImage = MediaType.IsImage(key);
                bool isVideo = MediaType.IsVideo(key);

                // Manuals, Music and anything else are intentionally not kept (and not resolved).
                if (!isImage && !isVideo)
                    continue;

                folder.FolderPath = ResolveFolderPath(folder, key);

                if (isImage)
                    _imageFolderStrings.Add(folder);
                else
                    _videoFolderStrings.Add(folder);
            }

            // Images are sorted by media type (BoxFront, ClearLogo, ...). Video folders keep their XML order
            // (Video before Theme Video), which the loader relies on for its root-vs-Theme scan.
            // Solo se añaden a _imageFolderStrings folders con ImageType no-null (los null se saltan arriba con
            // 'continue'), así que el '!' es seguro.
            _imageFolderStrings.Sort((x, y) => x.ImageType!.Value.CompareTo(y.ImageType!.Value));
        }
    }

    /// <summary>
    /// The platform's VIDEO folder definitions (Video, Theme Video), taken from its &lt;PlatformFolder&gt; nodes
    /// in Platforms.xml and normalized to absolute paths. Populated alongside <see cref="ImageFolderStrings"/>
    /// by the same setter, in XML order. The loader turns these into the video <see cref="PlatformImageSet"/>s.
    /// </summary>
    public IReadOnlyList<PlatformImageFolder> VideoFolderStrings => _videoFolderStrings;
    #endregion

    #region Methods (private)
    /// <summary>
    /// Resolves a folder definition to an absolute path. A non-empty &lt;FolderPath&gt; is used verbatim when it
    /// is already rooted (a custom/relocated folder) or made absolute against the LaunchBox root when it is a
    /// relative LaunchBox path. An empty &lt;FolderPath /&gt; means the folder sits at LaunchBox's DEFAULT
    /// location: LaunchBox only writes an explicit path when the user relocates a folder, so empty must resolve
    /// to that default (Images\{Platform}\{Type} for images, Videos\{Platform}[\Theme] for video), never to the
    /// LaunchBox root — otherwise a recursive scan would pull EVERY file on disk into the set.
    /// </summary>
    private string ResolveFolderPath(PlatformImageFolder folder, int mediaTypeKey)
    {
        if (!string.IsNullOrWhiteSpace(folder.FolderPath))
        {
            return Path.IsPathRooted(folder.FolderPath)
                ? folder.FolderPath
                : Path.Combine(_launchBoxFolder, folder.FolderPath);
        }

        if (MediaType.IsVideo(mediaTypeKey))
        {
            // Video snap: Videos\{Platform}. Theme Video: Videos\{Platform}\Theme.
            string videoRoot = Path.Combine(_launchBoxFolder, "Videos", folder.Platform);
            return mediaTypeKey == MediaType.ThemeVideo.Key ? Path.Combine(videoRoot, "Theme") : videoRoot;
        }

        // Image: Images\{Platform}\{MediaType name} (the LaunchBox subfolder is the media type's display value).
        // ResolveFolderPath solo se invoca para folders con ImageType no-null (ver el guard en ImageFolderStrings).
        return Path.Combine(_launchBoxFolder, "Images", folder.Platform, folder.ImageType!.Value);
    }
    #endregion

    #region Constructor
    /// <summary>
    /// Creates a new Platform DTO.
    /// platformFile: absolute path to the platform XML file.
    /// launchBoxFolder: root LaunchBox folder (used to normalize image paths).
    /// </summary>
    public Platform(string platformFile, string launchBoxFolder)
        : base(platformFile)
    {
        _launchBoxFolder = launchBoxFolder;
        Images = new PlatformImages();
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Populates the list of games by parsing the platform XML.
    /// This method is called exclusively by LaunchBoxService.
    ///
    /// Responsibilities:
    /// - Extract DatabaseID, ROM path, Title, Version.
    /// - Create Game objects.
    /// - Sort games alphabetically.
    ///
    /// This method does NOT:
    /// - Load images
    /// - Match images
    /// - Access filesystem
    /// </summary>
    public void SetGames(XmlNodeList listOfGames)
    {
        Games.Clear();

        foreach (XmlNode game in listOfGames)
        {
            string databaseId = game["DatabaseID"]?.InnerText ?? "";
            string rom = game["ApplicationPath"]?.InnerText ?? "";
            string title = game["Title"]?.InnerText ?? "";
            string version = game["Version"]?.InnerText ?? "";

            // Ficha completa (todos los campos del <Game>) para el widget de detalles. Lectura barata sobre el
            // nodo ya en memoria; ver GameDetails.
            GameDetails? details = GameDetails.FromXml(game);

            Games.Add(new Game(databaseId, rom, title, version, details: details));
        }

        Games.Sort((x, y) => x.Title.CompareTo(y.Title));
    }

    /// <summary>
    /// Construye el índice invertido "search string → juegos que la tienen" para emparejar imágenes con juegos
    /// en O(1) por fichero (en vez de recorrer todos los juegos por cada fichero). Usa
    /// <see cref="Game.SearchStringsSet"/> (deduplicado) para no añadir el mismo juego dos veces por un mismo
    /// string. Cada lista queda en el orden de <paramref name="games"/> (importa: preserva el orden con que se
    /// asignan las imágenes). Cómputo puro (solo lectura) → seguro en un hilo de fondo.
    /// </summary>
    public static Dictionary<string, List<Game>> BuildSearchStringIndex(IEnumerable<Game> games)
    {
        var index = new Dictionary<string, List<Game>>();
        foreach (Game game in games)
        {
            foreach (string searchString in game.SearchStringsSet)
            {
                if (!index.TryGetValue(searchString, out List<Game>? matched))
                {
                    matched = new List<Game>();
                    index[searchString] = matched;
                }
                matched.Add(game);
            }
        }
        return index;
    }

    /// <summary>
    /// Reads the platform metadata (developer, manufacturer, hardware specs and notes) from this platform's
    /// &lt;Platform&gt; node in Platforms.xml into <see cref="Metadata"/>. Only elements with a non-empty value
    /// are added, in the display order below (so empty fields are simply absent rather than hidden in the view).
    /// Called by the loader once, right after the platform is created. Mirrors <see cref="SetGames"/> (XML
    /// parsing lives here).
    /// </summary>
    public void SetMetadata(XmlNode platformNode)
    {
        Metadata.Clear();

        if (platformNode == null)
            return;

        // (display label, XML element) pairs in the order they should appear in the sheet.
        (string Label, string Element)[] fields =
        {
            ("Developer",    "Developer"),
            ("Manufacturer", "Manufacturer"),
            ("CPU",          "Cpu"),
            ("Memory",       "Memory"),
            ("Graphics",     "Graphics"),
            ("Sound",        "Sound"),
            ("Display",      "Display"),
            ("Media",        "Media"),
            ("Notes",        "Notes"),
        };

        List<(string Label, string Value)> present = fields
            .Select(f => (f.Label, Value: platformNode[f.Element]?.InnerText ?? ""))
            .Where(f => !string.IsNullOrEmpty(f.Value))
            .ToList();

        for (int i = 0; i < present.Count; i++)
            Metadata.Add(new PlatformMetadataField(present[i].Label, present[i].Value, showSeparator: i < present.Count - 1));
    }

    /// <summary>
    /// Appends the collection games that are NOT present in the LaunchBox DB (orphans) to
    /// <see cref="GamesInLauchboxDb"/>, which the loader has already filled with the games found in the
    /// LaunchBox database. The result is the full audit set (DB games + collection-only games) used by
    /// the GamesAuditControl. Must be called once, after the database has been read.
    /// </summary>
    public void AddOrphanGames()
    {
        GamesInLauchboxDb.AddRange(Games.Where(x => !x.InLaunchboxDb));
        GamesInLauchboxDb.Sort((x, y) => x.Title.CompareTo(y.Title));
    }

    /// <summary>
    /// Sets the selected image set to the first one with images, if existing. Otherwise, selects the first image set available.
    /// </summary>
    public void SetSelectedImageSet()
    {
        SelectedImageSet = Images.ImageSets.Find(x => x.ImageFiles.Count > 0) ?? Images.ImageSets.FirstOrDefault();
    }

    /// <summary>
    /// Sets the selected image set based on the image folder passed as parameter.
    /// </summary>
    /// <param name="imageType"></param>
    public void SetSelectedImageSet(string? imageType)
    {
        if (imageType != null)
        {
            SelectedImageSet = Images.ImageSets.Find(x => x.Type.Value == imageType);
        }
        else
        {
            SelectedImageSet = null;
        }
    }
    #endregion

    #region Nested types
    /// <summary>
    /// A single label/value row of the platform metadata sheet (e.g. "CPU" → "Ricoh 2A03"), in display
    /// order. Built by <see cref="SetMetadata"/>, which only adds rows with a non-empty value, so the
    /// details view iterates them with one template instead of a property + block per field. Nested in
    /// <see cref="Platform"/> because it exists only to shape that sheet.
    /// </summary>
    public class PlatformMetadataField
    {
        public string Label { get; }
        public string Value { get; }

        /// <summary>
        /// Whether to draw the divider below this row. False for the last row, so the sheet shows dividers
        /// between rows but none trailing at the bottom (matches the original per-field layout).
        /// </summary>
        public bool ShowSeparator { get; }

        public PlatformMetadataField(string label, string value, bool showSeparator)
        {
            Label = label;
            Value = value;
            ShowSeparator = showSeparator;
        }
    }
    #endregion
}