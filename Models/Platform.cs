using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace MM4LB.Models;

/// <summary>
/// Represents a LaunchBox platform (e.g., "Nintendo NES", "Sony PlayStation").
///
/// This class is intentionally a clean DTO:
/// - It contains no async logic.
/// - It contains no filesystem access.
/// - It contains no matching logic.
/// - It contains no progress reporting.
///
/// Responsibilities:
/// - Hold platform metadata (file path).
/// - Hold the list of games.
/// </summary>
public class Platform : LocalFile
{
    #region Properties
    public List<Game> Games { get; } = new();
    public List<Game> GamesInLauchboxDb { get; } = new();

    /// <summary>
    /// Platform metadata read from the platform's &lt;Platform&gt; node in Platforms.xml (see
    /// <see cref="SetMetadata"/>), as an ordered list of label/value rows for the details sheet. Only
    /// fields with a non-empty value are included, in display order, so the view iterates them with a
    /// single template instead of one property + block per field.
    /// </summary>
    public List<PlatformMetadataField> Metadata { get; } = new();
    #endregion

    #region Constructor
    /// <summary>
    /// Creates a new Platform DTO.
    /// platformFile: absolute path to the platform XML file.
    /// </summary>
    public Platform(string platformFile)
        : base(platformFile)
    {
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Populates the list of games by parsing the platform XML.
    ///
    /// Responsibilities:
    /// - Extract DatabaseID, ROM path, Title, Version.
    /// - Create Game objects.
    /// - Sort games alphabetically.
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
    /// Construye el índice invertido "search string → juegos que la tienen". Usa
    /// <see cref="Game.SearchStringsSet"/> (deduplicado) para no añadir el mismo juego dos veces por un mismo
    /// string. Cada lista queda en el orden de <paramref name="games"/>. Cómputo puro (solo lectura).
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
    /// Mirrors <see cref="SetGames"/> (XML parsing lives here).
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
    /// <see cref="GamesInLauchboxDb"/>, which has already been filled with the games found in the
    /// LaunchBox database. Must be called once, after the database has been read.
    /// </summary>
    public void AddOrphanGames()
    {
        GamesInLauchboxDb.AddRange(Games.Where(x => !x.InLaunchboxDb));
        GamesInLauchboxDb.Sort((x, y) => x.Title.CompareTo(y.Title));
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
