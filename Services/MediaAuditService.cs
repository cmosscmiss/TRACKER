using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Construye el mapa de la auditoría de media de LaunchBox: relaciona cada columna del Excel de auditoría
/// (export de LaunchBox) con los <see cref="MediaType"/> de la app cuyos conteos suma esa columna.
///
/// La agrupación NO se hardcodea: se lee de los elementos <c>*ImageTypePriorities</c> de
/// <c>{LaunchBox}\Data\Settings.xml</c> (la misma configuración que usa LaunchBox para decidir qué tipos
/// caen bajo cada categoría). Lo único fijo son las cabeceras del export y a qué grupo/tipo apunta cada
/// una (<see cref="_columnSpecs"/>), porque son constantes del propio LaunchBox.
///
/// Fase A del "chequeo de auditoría": este servicio solo construye el mapeo. El parseo del Excel y la
/// comparación de conteos con lo que empareja la app se añaden en fases posteriores.
/// </summary>
public sealed class MediaAuditService
{
    #region Attributes
    private readonly AppSettings _appSettings;
    private readonly FileSystemService _fileSystemService;
    private readonly ImageMatchingService _imageMatchingService;

    /// <summary>
    /// Solo tipos imagen (<see cref="MediaType.IsImage"/>, Key &lt; 100). Se excluyen a propósito los de
    /// vídeo/plataforma/etc.: varios comparten <see cref="Enumeration.Value"/> con un tipo imagen (p. ej.
    /// "Banner" existe como imagen Key=11 y como imagen de plataforma Key=150), y las columnas de este
    /// mapeo siempre se refieren al tipo imagen.
    /// </summary>
    private static readonly IReadOnlyList<MediaType> _imageTypes =
        Enumeration.GetAll<MediaType>().Where(t => MediaType.IsImage(t.Key)).ToList();

    /// <summary>Extrae el entero inicial de las celdas de conteo ("3 Images", "1 Video", "0 Images").</summary>
    private static readonly Regex _leadingIntRegex = new(@"^\s*(\d+)", RegexOptions.Compiled);

    /// <summary>Extrae los dígitos del identificador ("LaunchBox DB ID #128018" → "128018").</summary>
    private static readonly Regex _dbIdRegex = new(@"(\d+)", RegexOptions.Compiled);
    #endregion

    #region Fixed table (Excel column -> source)
    private enum ColumnSource
    {
        /// <summary>La columna suma un grupo de tipos definido en Settings.xml (<see cref="AuditColumnSpec.Key"/> = nombre del elemento).</summary>
        PriorityGroup,
        /// <summary>La columna mapea 1:1 a un único tipo (<see cref="AuditColumnSpec.Key"/> = <see cref="Enumeration.Value"/> del tipo).</summary>
        SingleType,
        /// <summary>La columna cuenta vídeos (por rango, no por lista de tipos).</summary>
        Videos
    }

    private sealed record AuditColumnSpec(string ExcelColumn, ColumnSource Source, string Key);

    /// <summary>
    /// Las 16 columnas de media del Excel de auditoría, EN SU ORDEN, y a qué apunta cada una. La columna
    /// "Arcade Controls Information" del export se omite a propósito: es un campo de texto, no media.
    /// La expansión de los grupos de prioridad se resuelve en tiempo de ejecución desde Settings.xml.
    /// </summary>
    private static readonly IReadOnlyList<AuditColumnSpec> _columnSpecs = new List<AuditColumnSpec>
    {
        new("Arcade Cabinet Images",       ColumnSource.SingleType,    "Arcade - Cabinet"),
        new("Arcade Circuit Board Images", ColumnSource.SingleType,    "Arcade - Circuit Board"),
        new("Arcade Control Panel Images", ColumnSource.SingleType,    "Arcade - Control Panel"),
        new("Banner Images",               ColumnSource.SingleType,    "Banner"),
        new("Background Images",           ColumnSource.PriorityGroup, "BackgroundImageTypePriorities"),
        new("Box Back Images",             ColumnSource.PriorityGroup, "BackImageTypePriorities"),
        new("Box Front Images",            ColumnSource.PriorityGroup, "FrontImageTypePriorities"),
        new("Box Spine Images",            ColumnSource.SingleType,    "Box - Spine"),
        new("3D Box Images",               ColumnSource.PriorityGroup, "Box3dImageTypePriorities"),
        new("Cart Front Images",           ColumnSource.PriorityGroup, "CartFrontImageTypePriorities"),
        new("Cart Back Images",            ColumnSource.PriorityGroup, "CartBackImageTypePriorities"),
        new("3D Cart Images",              ColumnSource.PriorityGroup, "Cart3dImageTypePriorities"),
        new("Clear Logo Images",           ColumnSource.SingleType,    "Clear Logo"),
        new("Marquee Images",              ColumnSource.PriorityGroup, "MarqueeImageTypePriorities"),
        new("Screenshot Images",           ColumnSource.PriorityGroup, "ScreenshotsImageTypePriorities"),
        new("Videos",                      ColumnSource.Videos,        ""),
    };
    #endregion

    #region Constructor
    public MediaAuditService(IOptions<AppSettings> appSettings, FileSystemService fileSystemService, ImageMatchingService imageMatchingService)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _imageMatchingService = imageMatchingService ?? throw new ArgumentNullException(nameof(imageMatchingService));
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Construye el mapa columna-de-auditoría → <see cref="MediaType"/> leyendo los grupos de prioridad de
    /// <c>Settings.xml</c>. No lanza: los problemas (Settings.xml ausente, grupo vacío, tipo desconocido)
    /// se acumulan en <see cref="MediaAuditMap.Warnings"/> y la columna afectada queda con los tipos que sí
    /// se pudieron resolver. Un fallo de lectura del XML se registra en el log (fondo, Fase A).
    /// </summary>
    public async Task<MediaAuditMap> BuildMappingAsync()
    {
        var warnings = new List<string>();
        Dictionary<string, string[]> groups = await LoadPriorityGroupsAsync(warnings);

        var categories = new List<MediaAuditCategory>(_columnSpecs.Count);
        foreach (AuditColumnSpec spec in _columnSpecs)
        {
            switch (spec.Source)
            {
                case ColumnSource.Videos:
                    categories.Add(new MediaAuditCategory(spec.ExcelColumn, Array.Empty<MediaType>(), isVideo: true));
                    break;

                case ColumnSource.SingleType:
                {
                    MediaType? type = ResolveType(spec.Key, spec.ExcelColumn, warnings);
                    MediaType[] types = type != null ? new[] { type } : Array.Empty<MediaType>();
                    categories.Add(new MediaAuditCategory(spec.ExcelColumn, types));
                    break;
                }

                case ColumnSource.PriorityGroup:
                {
                    var types = new List<MediaType>();
                    if (groups.TryGetValue(spec.Key, out string[]? names))
                    {
                        foreach (string name in names)
                        {
                            MediaType? type = ResolveType(name, spec.ExcelColumn, warnings);
                            if (type != null) { types.Add(type); }
                        }
                    }
                    else
                    {
                        warnings.Add($"El grupo '{spec.Key}' no está en Settings.xml; la columna '{spec.ExcelColumn}' queda sin tipos.");
                    }
                    categories.Add(new MediaAuditCategory(spec.ExcelColumn, types));
                    break;
                }
            }
        }

        return new MediaAuditMap(categories, warnings);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Lee de <c>Settings.xml</c> los elementos <c>*ImageTypePriorities</c> referenciados por la tabla fija
    /// y los devuelve como (nombre de elemento → lista de nombres de tipo). Reutiliza el mismo patrón de
    /// lectura que <c>PlatformLoadingService.LoadPlatformPacksAsync</c> (LoadXmlDocument respeta el encoding).
    /// </summary>
    private async Task<Dictionary<string, string[]>> LoadPriorityGroupsAsync(List<string> warnings)
    {
        var result = new Dictionary<string, string[]>();
        string? settingsFile = _appSettings.LaunchBox?.LaunchboxSettingsXmlFile;

        if (string.IsNullOrEmpty(settingsFile) || !File.Exists(settingsFile))
        {
            warnings.Add($"Settings.xml de LaunchBox no encontrado ({settingsFile ?? "(ruta no configurada)"}); las columnas agregadas quedarán vacías.");
            return result;
        }

        try
        {
            XmlDocument doc = await _fileSystemService.LoadXmlDocument(settingsFile);
            XmlNode? settings = doc.SelectSingleNode("/LaunchBox/Settings");
            if (settings == null)
            {
                warnings.Add("No se encontró el nodo /LaunchBox/Settings en Settings.xml.");
                return result;
            }

            // Solo los elementos que la tabla fija necesita (los 9 grupos de prioridad).
            foreach (string element in _columnSpecs.Where(s => s.Source == ColumnSource.PriorityGroup)
                                                   .Select(s => s.Key)
                                                   .Distinct())
            {
                string? raw = settings[element]?.InnerText;
                if (string.IsNullOrWhiteSpace(raw)) { continue; }

                result[element] = raw.Split(',')
                                     .Select(s => s.Trim())
                                     .Where(s => s.Length > 0)
                                     .ToArray();
            }
        }
        catch (Exception ex)
        {
            // Fase A = plumbing de fondo (no lo dispara el usuario): solo log. En la fase con UI, el comando
            // de la toolbar envolverá la llamada con ExceptionService.Handle (diálogo propio de la app).
            ExceptionService.LogToFile(ex, "Error leyendo los grupos de prioridad de imagen de Settings.xml.");
            warnings.Add("Error leyendo Settings.xml (ver MM4LB.log).");
        }

        return result;
    }

    /// <summary>
    /// Resuelve un nombre de tipo de LaunchBox (p. ej. "Box - Front") al <see cref="MediaType"/> imagen de
    /// la app. Devuelve <c>null</c> y añade un aviso si no existe (versión de LaunchBox con tipos nuevos).
    /// </summary>
    private static MediaType? ResolveType(string value, string column, List<string> warnings)
    {
        MediaType? type = _imageTypes.FirstOrDefault(t => t.Value == value);
        if (type == null)
        {
            warnings.Add(LocalizationService.Instance is LocalizationService loc
                ? loc.Format(MM4LB.Helpers.LocKeys.MediaAuditService_UnknownType_Warning, value, column)
                : $"Type '{value}' (column '{column}') does not exist in MediaType; ignored.");
        }
        return type;
    }
    #endregion

    #region Methods (auditoría: parseo Excel + comparación)
    // Cabeceras (no de media) del Excel de auditoría que se usan para identificar el juego.
    private const string ColTitle = "Title";
    private const string ColApplicationPath = "Application Path";
    private const string ColDatabaseId = "LaunchBox Games Database ID";

    /// <summary>
    /// Ejecuta la auditoría: construye el mapa (Fase A), parsea el Excel de LaunchBox, empareja cada fila con
    /// un juego de la colección y compara, por categoría, el conteo declarado en el Excel con el que la app
    /// emparejaría. Solo lectura del modelo. Lanza si el Excel no se puede abrir (el comando de la UI lo
    /// envuelve con <c>ExceptionService.Handle</c>); los problemas por fila/tipo van a
    /// <see cref="MediaAuditResult.Warnings"/>.
    /// </summary>
    public async Task<MediaAuditResult> RunAuditAsync(Platform platform, string excelFilePath)
    {
        if (platform == null) { throw new ArgumentNullException(nameof(platform)); }

        var warnings = new List<string>();

        // Fase A: mapa columna → tipos. Sus avisos se arrastran al resultado.
        MediaAuditMap map = await BuildMappingAsync();
        warnings.AddRange(map.Warnings);

        // Parseo + emparejado + comparación en segundo plano (solo lectura del modelo → seguro fuera de la UI).
        return await Task.Run(() =>
        {
            List<AuditExcelRow> excelRows = ParseExcel(excelFilePath, warnings);

            // Índices de emparejado sobre la colección del usuario (referencias reales de platform.Games).
            var byRomFile = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            var byDbId = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            var byTitle = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            foreach (Game g in platform.Games)
            {
                if (!string.IsNullOrEmpty(g.RomFileName)) { byRomFile.TryAdd(g.RomFileName, g); }
                if (!string.IsNullOrEmpty(g.DatabaseId)) { byDbId.TryAdd(g.DatabaseId, g); }
                if (!string.IsNullOrEmpty(g.Title)) { byTitle.TryAdd(g.Title, g); }
            }

            var results = new List<AuditGameResult>();
            var rowsNotMatched = new List<AuditExcelRow>();
            // Reference equality: Game sobreescribe Equals SIN GetHashCode, así que un HashSet por valor sería
            // incoherente; y las referencias emparejadas provienen de platform.Games.
            var matchedGames = new HashSet<Game>(ReferenceEqualityComparer.Instance);

            foreach (AuditExcelRow row in excelRows)
            {
                Game? game = MatchGame(row, byRomFile, byDbId, byTitle);
                if (game == null) { rowsNotMatched.Add(row); continue; }
                matchedGames.Add(game);

                Dictionary<int, int> counts = _imageMatchingService.CountMatchedImagesByType(platform, game);

                var cells = new List<AuditCellResult>(map.Categories.Count);
                foreach (MediaAuditCategory category in map.Categories)
                {
                    int excelCount = row.CountsByColumn.TryGetValue(category.ExcelColumn, out int ec) ? ec : 0;
                    int mm4lbCount = category.IsVideo
                        ? counts.Where(kv => MediaType.IsVideo(kv.Key)).Sum(kv => kv.Value)
                        : category.Types.Sum(t => counts.TryGetValue(t.Key, out int c) ? c : 0);
                    cells.Add(new AuditCellResult(category, excelCount, mm4lbCount));
                }

                results.Add(new AuditGameResult(game, row.Title, cells));
            }

            List<Game> gamesNotInExcel = platform.Games.Where(g => !matchedGames.Contains(g)).ToList();

            return new MediaAuditResult(results, rowsNotMatched, gamesNotInExcel, map, warnings);
        });
    }

    /// <summary>
    /// Parsea el Excel de auditoría con ClosedXML: cabeceras por NOMBRE (robusto a reordenación) y una
    /// <see cref="AuditExcelRow"/> por juego, extrayendo el entero inicial de cada celda de conteo. Una fila
    /// ilegible se registra y se omite (aviso), sin abortar el resto. NO captura el fallo de apertura del
    /// libro: si el fichero no es un xlsx válido, la excepción sube al comando de la UI (diálogo).
    /// </summary>
    private List<AuditExcelRow> ParseExcel(string excelFilePath, List<string> warnings)
    {
        var rows = new List<AuditExcelRow>();

        using XLWorkbook workbook = new(excelFilePath);
        IXLWorksheet ws = workbook.Worksheets.First();

        // Cabecera (fila 1): nombre de columna → número de columna.
        IXLRow headerRow = ws.Row(1);
        int lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        var columnByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int col = 1; col <= lastCol; col++)
        {
            string header = headerRow.Cell(col).GetString().Trim();
            if (header.Length > 0) { columnByHeader.TryAdd(header, col); }
        }

        int titleCol = columnByHeader.GetValueOrDefault(ColTitle);
        int appPathCol = columnByHeader.GetValueOrDefault(ColApplicationPath);
        int dbIdCol = columnByHeader.GetValueOrDefault(ColDatabaseId);

        // Columnas de media conocidas (de la tabla fija) presentes en este Excel.
        var mediaColumns = _columnSpecs
            .Where(s => columnByHeader.ContainsKey(s.ExcelColumn))
            .ToDictionary(s => s.ExcelColumn, s => columnByHeader[s.ExcelColumn]);

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= lastRow; r++)
        {
            try
            {
                IXLRow row = ws.Row(r);
                string appPath = appPathCol > 0 ? row.Cell(appPathCol).GetString().Trim() : "";
                string title = titleCol > 0 ? row.Cell(titleCol).GetString().Trim() : "";

                // Fila vacía: se ignora en silencio.
                if (appPath.Length == 0 && title.Length == 0) { continue; }

                var counts = new Dictionary<string, int>(mediaColumns.Count);
                foreach (KeyValuePair<string, int> mc in mediaColumns)
                {
                    counts[mc.Key] = ExtractLeadingInt(row.Cell(mc.Value).GetString());
                }

                rows.Add(new AuditExcelRow
                {
                    Title = title,
                    ApplicationPath = appPath,
                    ApplicationFileName = appPath.Length == 0 ? "" : Path.GetFileName(appPath),
                    DatabaseId = dbIdCol > 0 ? ExtractDbId(row.Cell(dbIdCol).GetString()) : "",
                    CountsByColumn = counts,
                    RowNumber = r
                });
            }
            catch (Exception ex)
            {
                ExceptionService.LogToFile(ex, $"Error parseando la fila {r} del Excel de auditoría.");
                warnings.Add($"Fila {r} del Excel ilegible; se omite.");
            }
        }

        return rows;
    }

    /// <summary>Empareja una fila del Excel con un juego: por nombre de ROM (exacto), luego DatabaseId, luego título.</summary>
    private static Game? MatchGame(AuditExcelRow row, Dictionary<string, Game> byRomFile,
        Dictionary<string, Game> byDbId, Dictionary<string, Game> byTitle)
    {
        if (!string.IsNullOrEmpty(row.ApplicationFileName) && byRomFile.TryGetValue(row.ApplicationFileName, out Game? g)) { return g; }
        if (!string.IsNullOrEmpty(row.DatabaseId) && byDbId.TryGetValue(row.DatabaseId, out g)) { return g; }
        if (!string.IsNullOrEmpty(row.Title) && byTitle.TryGetValue(row.Title, out g)) { return g; }
        return null;
    }

    private static int ExtractLeadingInt(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) { return 0; }
        Match m = _leadingIntRegex.Match(cell);
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
    }

    private static string ExtractDbId(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) { return ""; }
        Match m = _dbIdRegex.Match(cell);
        return m.Success ? m.Groups[1].Value : cell.Trim();
    }
    #endregion
}
