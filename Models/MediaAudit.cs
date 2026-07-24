using MM4LB.Enums;
using System.Collections.Generic;
using System.Linq;

namespace MM4LB.Models;

/// <summary>
/// Una categoría de la auditoría de LaunchBox: una columna del Excel de auditoría y el conjunto de
/// <see cref="MediaType"/> de la app cuyos conteos suma esa columna.
///
/// LaunchBox agrupa varios tipos "fuente" bajo una misma columna del export. Por ejemplo, la columna
/// "Box Front Images" agrega Box - Front, GOG Poster, Steam Poster, Poster, Square, Steam Banner... Esa
/// agrupación se lee de los elementos <c>*ImageTypePriorities</c> de <c>Data\Settings.xml</c> (ver
/// <see cref="Services.MediaAuditService"/>). Las columnas 1:1 (Banner, Box Spine, Clear Logo, Arcade - *)
/// mapean a un único tipo. La columna "Videos" no lista tipos: cuenta por rango (<see cref="IsVideo"/>).
/// </summary>
public sealed class MediaAuditCategory
{
    /// <summary>Cabecera exacta de la columna en el Excel de auditoría (p. ej. "Box Front Images").</summary>
    public string ExcelColumn { get; }

    /// <summary>
    /// Tipos de media de la app cuyos conteos suma esta columna. Para las columnas 1:1 tiene un único
    /// elemento; para "Videos" está vacío y <see cref="IsVideo"/> es <c>true</c>.
    /// </summary>
    public IReadOnlyList<MediaType> Types { get; }

    /// <summary>
    /// La columna cuenta vídeos (todos los <see cref="MediaType"/> con <see cref="MediaType.IsVideo"/>),
    /// no un conjunto de tipos imagen.
    /// </summary>
    public bool IsVideo { get; }

    public MediaAuditCategory(string excelColumn, IReadOnlyList<MediaType> types, bool isVideo = false)
    {
        ExcelColumn = excelColumn;
        Types = types;
        IsVideo = isVideo;
    }
}

/// <summary>
/// Mapa completo de la auditoría: las columnas del Excel de LaunchBox, en su orden, cada una con el
/// conjunto de <see cref="MediaType"/> que agrega, más los avisos no fatales encontrados al construirlo
/// (nombres de tipo sin resolver por una versión distinta de LaunchBox, o ausencia de Settings.xml).
/// </summary>
public sealed class MediaAuditMap
{
    /// <summary>Las categorías (columnas), en el orden en que aparecen en el Excel de auditoría.</summary>
    public IReadOnlyList<MediaAuditCategory> Categories { get; }

    /// <summary>Avisos no fatales: nombres de tipo sin resolver, grupo ausente o Settings.xml no encontrado.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public MediaAuditMap(IReadOnlyList<MediaAuditCategory> categories, IReadOnlyList<string> warnings)
    {
        Categories = categories;
        Warnings = warnings;
    }
}

/// <summary>
/// Una fila del Excel de auditoría de LaunchBox ya parseada: un juego con sus conteos por columna.
/// </summary>
public sealed class AuditExcelRow
{
    /// <summary>Título del juego tal cual en el Excel.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Ruta completa de la ROM (columna "Application Path").</summary>
    public string ApplicationPath { get; init; } = string.Empty;

    /// <summary>Nombre de fichero de la ROM (con extensión); clave primaria de emparejado con <see cref="Game.RomFileName"/>.</summary>
    public string ApplicationFileName { get; init; } = string.Empty;

    /// <summary>ID de la BBDD online de LaunchBox (los dígitos de "LaunchBox DB ID #NNNNN").</summary>
    public string DatabaseId { get; init; } = string.Empty;

    /// <summary>Conteo por columna de media (clave = cabecera exacta del Excel).</summary>
    public IReadOnlyDictionary<string, int> CountsByColumn { get; init; } = new Dictionary<string, int>();

    /// <summary>Número de fila en la hoja (para diagnósticos).</summary>
    public int RowNumber { get; init; }
}

/// <summary>Resultado de comparar una categoría para un juego: conteo del Excel vs conteo que empareja MM4LB.</summary>
public enum AuditStatus
{
    /// <summary>MM4LB empareja el mismo número que declara el Excel.</summary>
    Match,
    /// <summary>MM4LB empareja MENOS que el Excel (LaunchBox lo tiene pero la app no lo recupera / no está en disco).</summary>
    Missing,
    /// <summary>MM4LB empareja MÁS que el Excel (sobre-emparejado, o export del Excel obsoleto).</summary>
    Extra
}

/// <summary>Comparación de una categoría (columna) para un juego concreto.</summary>
public sealed class AuditCellResult
{
    public MediaAuditCategory Category { get; }
    public int ExcelCount { get; }
    public int Mm4lbCount { get; }

    public AuditStatus Status =>
        Mm4lbCount == ExcelCount ? AuditStatus.Match :
        Mm4lbCount < ExcelCount ? AuditStatus.Missing : AuditStatus.Extra;

    public AuditCellResult(MediaAuditCategory category, int excelCount, int mm4lbCount)
    {
        Category = category;
        ExcelCount = excelCount;
        Mm4lbCount = mm4lbCount;
    }
}

/// <summary>Resultado de la auditoría para un juego: sus celdas (una por categoría) y si hay alguna discrepancia.</summary>
public sealed class AuditGameResult
{
    /// <summary>Juego de la colección emparejado con la fila del Excel.</summary>
    public Game Game { get; }

    /// <summary>Título tal cual en el Excel (por si difiere del de la app).</summary>
    public string ExcelTitle { get; }

    public IReadOnlyList<AuditCellResult> Cells { get; }

    public bool HasDiscrepancy => Cells.Any(c => c.Status != AuditStatus.Match);

    public AuditGameResult(Game game, string excelTitle, IReadOnlyList<AuditCellResult> cells)
    {
        Game = game;
        ExcelTitle = excelTitle;
        Cells = cells;
    }
}

/// <summary>
/// Resultado completo de la auditoría: los juegos comparados, los huérfanos por ambos lados (filas del Excel
/// sin juego cargado y juegos cargados sin fila), el mapa usado y los avisos no fatales.
/// </summary>
public sealed class MediaAuditResult
{
    public IReadOnlyList<AuditGameResult> Games { get; }
    public IReadOnlyList<AuditExcelRow> RowsNotMatched { get; }
    public IReadOnlyList<Game> GamesNotInExcel { get; }
    public MediaAuditMap Map { get; }
    public IReadOnlyList<string> Warnings { get; }

    public MediaAuditResult(IReadOnlyList<AuditGameResult> games, IReadOnlyList<AuditExcelRow> rowsNotMatched,
        IReadOnlyList<Game> gamesNotInExcel, MediaAuditMap map, IReadOnlyList<string> warnings)
    {
        Games = games;
        RowsNotMatched = rowsNotMatched;
        GamesNotInExcel = gamesNotInExcel;
        Map = map;
        Warnings = warnings;
    }
}
