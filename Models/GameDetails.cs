using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace MM4LB.Models;

/// <summary>
/// Full detail sheet of a game. The collection part is built from the game's &lt;Game&gt; node in the LaunchBox
/// collection XML (<c>Data\Platforms\{Platform}.xml</c>) — ALL fields, grouped into label/value rows (empty
/// string values are skipped, booleans are shown as Yes/No). The database part is built on demand from the
/// LaunchBox metadata database (see <see cref="DatabaseGroups"/>) for the selected game.
///
/// The collection groups are built once by <see cref="Platform.SetGames"/> and exposed via
/// <see cref="Game.Details"/>; the database groups are appended by the widget's view model once fetched.
/// </summary>
public sealed class GameDetails
{
    /// <summary>A single label/value row of a group.</summary>
    public sealed class Field
    {
        public string Label { get; }
        public string Value { get; }

        /// <summary>Draws the divider below this row. False on the last row of a group (no trailing divider).</summary>
        public bool ShowSeparator { get; set; }

        /// <summary>
        /// Si tiene valor (0–5), la fila se pinta además con un control de 5 estrellas relleno a esa nota (community
        /// rating). Si es <c>null</c>, la fila es texto normal. Ver <see cref="IsRating"/> / <see cref="RatingValue"/>.
        /// </summary>
        public double? Rating { get; set; }

        /// <summary>True si la fila debe mostrar el control de estrellas (dirige la plantilla del control).</summary>
        public bool IsRating => Rating.HasValue;

        /// <summary>Nota (0–5) para el control de estrellas; 0 si no es una fila de rating.</summary>
        public double RatingValue => Rating ?? 0;

        public Field(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>A group of the sheet (header + rows), like the blocks of <c>SettingsControl</c>.</summary>
    public sealed class Group
    {
        public string Header { get; }
        public IReadOnlyList<Field> Fields { get; }

        public Group(string header, IReadOnlyList<Field> fields)
        {
            Header = header;
            Fields = fields;
        }
    }

    /// <summary>Non-empty groups of the collection (XML) part of the sheet, in display order.</summary>
    public IReadOnlyList<Group> Groups { get; }

    /// <summary>
    /// Metadata cached in the collection XML that normally comes from the metadata database (name, developer,
    /// release info, ratings, …). Shown by the widget ONLY when the game is NOT in the metadata database (see
    /// <see cref="Controls.ViewModels.GameDetailsViewModel"/>), as a fallback for the missing DATABASE groups.
    /// </summary>
    public IReadOnlyList<Group> FallbackGroups { get; }

    private GameDetails(IReadOnlyList<Group> groups, IReadOnlyList<Group> fallbackGroups)
    {
        Groups = groups;
        FallbackGroups = fallbackGroups;
    }

    /// <summary>Cabecera del grupo de identidad (colección). El VM lo fusiona con <see cref="DatabaseCatalogHeader"/>.</summary>
    public const string IdentityHeader = "IDENTITY";

    /// <summary>Cabecera del grupo de catálogo de la BBDD. El VM lo fusiona con <see cref="IdentityHeader"/>.</summary>
    public const string DatabaseCatalogHeader = "DATABASE — CATALOG";

    /// <summary>Cabecera del grupo de títulos alternativos de la BBDD. El VM lo fusiona en <see cref="IdentityHeader"/>.</summary>
    public const string DatabaseAlternateNamesHeader = "DATABASE — ALTERNATE NAMES";

    /// <summary>Cabecera del grupo de enlaces (rutas de colección y URLs de vídeo/Wikipedia).</summary>
    public const string LinksHeader = "LINKS";

    /// <summary>Cabecera del grupo de media faltante. El VM lo oculta en la interfaz (datos no fiables por ahora).</summary>
    public const string MissingMediaHeader = "MISSING MEDIA";

    /// <summary>Etiqueta del campo de valoración de la comunidad (se pinta con estrellas).</summary>
    public const string CommunityRatingLabel = "Community rating";

    /// <summary>Etiqueta del campo único de nombres alternativos (unidos con "  |  ").</summary>
    public const string AlternateNameLabel = "Alternate name(s)";

    /// <summary>
    /// Orden de los campos del grupo <c>IDENTITY</c> (fusiona identidad de colección + catálogo + nombre alternativo
    /// de la BBDD). El VM emite solo estos campos, en este orden, tomando cada valor de la fuente correspondiente.
    /// </summary>
    public static readonly string[] IdentityOrder =
    {
        "Name", "Compare name", AlternateNameLabel, "Database ID", "Genre",
        "Series", "Version", "Status", "Clone of",
        "Region", "Developer", "Publisher", "Release date",
        "Overview", CommunityRatingLabel,
    };

    /// <summary>
    /// The 12 "Missing*" flags LaunchBox stores on the game, with their display labels. Shown as Yes/No rows.
    /// </summary>
    private static readonly (string Element, string Label)[] MissingMedia =
    {
        ("MissingBoxFrontImage",   "Box Front"),
        ("MissingBox3dImage",      "Box 3D"),
        ("MissingCartImage",       "Cart"),
        ("MissingCart3dImage",     "Cart 3D"),
        ("MissingClearLogoImage",  "Clear Logo"),
        ("MissingMarqueeImage",    "Marquee"),
        ("MissingScreenshotImage", "Screenshot"),
        ("MissingBannerImage",     "Banner"),
        ("MissingBackgroundImage", "Background"),
        ("MissingVideo",           "Video"),
        ("MissingManual",          "Manual"),
        ("MissingMusic",           "Music"),
    };

    /// <summary>
    /// Columns of the metadata DB <c>Games</c> table (excluding <c>DatabaseID</c>, already known), in SELECT and
    /// display order, with their display labels. Shared by <see cref="Services.GameMetadataService"/> (which reads
    /// them in this order) and <see cref="DatabaseGroups"/> (which labels the values).
    /// </summary>
    public static readonly (string Column, string Label)[] DatabaseCatalogFields =
    {
        ("Name",                 "Name"),
        ("CompareName",          "Compare name"),
        ("ReleaseYear",          "Release year"),
        ("ReleaseDate",          "Release date"),
        ("Overview",             "Overview"),
        ("Genres",               "Genre"),
        ("Developer",            "Developer"),
        ("Publisher",            "Publisher"),
        ("CommunityRating",      CommunityRatingLabel),
        ("SteamAppId",           "Steam app ID"),
        ("DOS",                  "DOS"),
        ("StartupFile",          "Startup file"),
        ("StartupMD5",           "Startup MD5"),
        ("StartupParameters",    "Startup parameters"),
        ("SetupFile",            "Setup file"),
        ("SetupMD5",             "Setup MD5"),
        ("SetupParameters",      "Setup parameters"),
    };

    #region Collection (XML) projection
    /// <summary>
    /// Builds the collection part of the sheet from the &lt;Game&gt; node. Returns <c>null</c> for a null node.
    /// Groups with no non-empty field are dropped.
    /// </summary>
    public static GameDetails? FromXml(XmlNode? node)
    {
        if (node == null)
            return null;

        var groups = new List<Group>();

        string S(string element) => node[element]?.InnerText?.Trim() ?? "";
        bool BoolVal(string element) => string.Equals(S(element), "true", StringComparison.OrdinalIgnoreCase);
        string YesNo(string element) => BoolVal(element) ? "Yes" : "No";

        string DateVal(string element)
        {
            string raw = S(element);
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)
                ? dt.ToString("yyyy-MM-dd") : raw;
        }

        // Adds a group with the rows whose value is non-empty; sets the separator on all but the last.
        void Add(string header, params (string Label, string Value)[] rows)
        {
            var fields = rows.Where(r => !string.IsNullOrEmpty(r.Value))
                             .Select(r => new Field(r.Label, r.Value))
                             .ToList();
            if (fields.Count == 0)
                return;

            for (int i = 0; i < fields.Count; i++)
                fields[i].ShowSeparator = i < fields.Count - 1;

            groups.Add(new Group(header, fields));
        }

        // Solo campos de colección SIN equivalente en la BBDD de metadatos (los duplicados —Title, Developer,
        // Publisher, Release date/type, Genre, Max players, ESRB, Notes/Overview, Wikipedia/Video URL, Community
        // rating/votes— se muestran desde la BBDD, que es la fuente viva; ver DatabaseGroups).
        // Campos de identidad de la COLECCIÓN que van en IDENTITY (el orden final lo fija IdentityOrder en el VM,
        // que los combina con los del catálogo y el nombre alternativo de la BBDD).
        Add(IdentityHeader,
            ("Series", S("Series")),
            ("Version", S("Version")),
            ("Status", S("Status")),
            ("Clone of", S("CloneOf")),
            ("Region", S("Region")),
            ("Database ID", S("DatabaseID")));

        // Rutas de colección + URLs (del XML), en orden alfabético por etiqueta.
        Add(LinksHeader,
            ("Application path", S("ApplicationPath")),
            ("Configuration path", S("ConfigurationPath")),
            ("Manual path", S("ManualPath")),
            ("Music path", S("MusicPath")),
            ("Root folder", S("RootFolder")),
            ("Theme video path", S("ThemeVideoPath")),
            ("Video path", S("VideoPath")),
            ("Video URL", S("VideoUrl")),
            ("Wikipedia URL", S("WikipediaURL")));

        Add(MissingMediaHeader,
            MissingMedia.Select(m => (m.Label, YesNo(m.Element))).ToArray());

        // Fallback: los campos duplicados que normalmente vienen de la BBDD, tomados de la caché de colección.
        // Se muestran SOLO cuando el juego no está en la BBDD de metadatos (ver GameDetailsViewModel).
        var fallback = new List<Group>();
        {
            var rows = new (string Label, string Value)[]
            {
                ("Title", S("Title")),
                ("Developer", S("Developer")),
                ("Publisher", S("Publisher")),
                ("Release date", DateVal("ReleaseDate")),
                ("Genre", S("Genre")),
                ("Overview", S("Notes")),
                (CommunityRatingLabel, S("CommunityStarRating")),
            };
            var fields = rows.Where(r => !string.IsNullOrEmpty(r.Value))
                             .Select(r => new Field(r.Label, r.Value))
                             .ToList();
            if (fields.Count > 0)
            {
                SetRating(fields);
                for (int i = 0; i < fields.Count; i++)
                    fields[i].ShowSeparator = i < fields.Count - 1;
                fallback.Add(new Group("METADATA", fields));
            }
        }

        return new GameDetails(groups, fallback);
    }
    #endregion

    #region Database projection
    /// <summary>
    /// Builds the database part of the sheet (catalog row, known-image counts by type, alternate titles) from the
    /// raw data read on demand from the LaunchBox metadata database for the selected game (see
    /// <see cref="Services.GameMetadataService"/>). Empty catalog values are skipped. Returns an empty list when
    /// there is no catalog row.
    /// </summary>
    /// <param name="catalogValues">Values of the <c>Games</c> row, aligned by index with <see cref="DatabaseCatalogFields"/>.</param>
    /// <param name="alternateTitles">Alternate/regional titles (key = region, value = name).</param>
    public static IReadOnlyList<Group> DatabaseGroups(
        IReadOnlyList<string>? catalogValues,
        IReadOnlyList<KeyValuePair<string, string>> alternateTitles)
    {
        if (catalogValues == null || catalogValues.Count == 0)
            return Array.Empty<Group>();

        var groups = new List<Group>();

        void AddGroup(string header, IEnumerable<(string Label, string Value)> rows)
        {
            var fields = rows.Where(r => !string.IsNullOrEmpty(r.Value))
                             .Select(r => new Field(r.Label, r.Value))
                             .ToList();
            if (fields.Count == 0)
                return;

            for (int i = 0; i < fields.Count; i++)
                fields[i].ShowSeparator = i < fields.Count - 1;

            groups.Add(new Group(header, fields));
        }

        // DOS 0/1 → No/Yes; release date sin hora; community rating a 2 decimales; el resto tal cual.
        static string CatalogValue(string column, string value)
        {
            if (column == "DOS")
                return value switch { "1" => "Yes", "0" => "No", _ => value };

            if (column == "ReleaseDate" &&
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime releaseDate))
                return releaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (column == "CommunityRating" &&
                (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double rating) ||
                 double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out rating)))
                return rating.ToString("0.##", CultureInfo.InvariantCulture);

            return value;
        }

        // Catalog row: label each value by its column, in the shared field order.
        var catalogRows = new List<(string, string)>();
        for (int i = 0; i < DatabaseCatalogFields.Length && i < catalogValues.Count; i++)
        {
            (string column, string label) = DatabaseCatalogFields[i];
            catalogRows.Add((label, CatalogValue(column, catalogValues[i])));
        }
        AddGroup(DatabaseCatalogHeader, catalogRows);

        // Community rating: se marca con su nota numérica para pintarlo con estrellas.
        Group? catalogGroup = groups.FirstOrDefault(g => g.Header == DatabaseCatalogHeader);
        if (catalogGroup != null)
            SetRating(catalogGroup.Fields);

        // Nombres alternativos: un único campo con todos los nombres unidos por " | ".
        if (alternateTitles.Count > 0)
        {
            string joined = string.Join("  |  ", alternateTitles.Select(kv => kv.Value));
            AddGroup(DatabaseAlternateNamesHeader, new[] { (AlternateNameLabel, joined) });
        }

        return groups;
    }
    #endregion

    /// <summary>Marca el campo de community rating de la lista con su nota (0–5) para pintarlo con estrellas.</summary>
    private static void SetRating(IReadOnlyList<Field> fields)
    {
        Field? ratingField = fields.FirstOrDefault(f => f.Label == CommunityRatingLabel);
        if (ratingField != null &&
            double.TryParse(ratingField.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double rating))
            ratingField.Rating = rating;
    }
}
