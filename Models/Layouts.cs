using System.Collections.Generic;
using System.Linq;

namespace MM4LB.Models;

/// <summary>
/// Representa la información de una celda dentro de un layout en cuadrícula.
/// Define su posición (fila y columna) y su tamaño (rowspan y colspan).
/// </summary>
public record SlotInfo(int Row, int Column, int RowSpan = 1, int ColSpan = 1);

/// <summary>
/// Define los márgenes aplicados a un slot dentro del layout.
/// Cada margen se expresa en unidades dobles.
/// </summary>
public record SlotMargin(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Contiene toda la información necesaria para describir un layout:
/// - Lista de slots
/// - Anchos de columnas
/// - Márgenes por slot
/// - Separación (gap) entre elementos
/// </summary>
public record LayoutInfo(int Index, IReadOnlyList<SlotInfo> Slots, IReadOnlyList<string> ColumnWidths, IReadOnlyList<SlotMargin> Margins, double Gap)
{
    /// <summary>
    /// Número de columnas realmente usadas por el layout (ancho distinto de "0").
    /// Determina cuántos splitters verticales hacen falta (usadas - 1).
    /// </summary>
    public int UsedColumnCount => ColumnWidths?.Count(width => !IsZeroWidth(width)) ?? 0;

    private static bool IsZeroWidth(string width) => string.IsNullOrWhiteSpace(width) || width.Trim() == "0";
}

/// <summary>
/// Proporciona una colección de layouts predefinidos para organizar elementos en cuadrícula.
/// Cada layout se identifica mediante una clave entera.
/// </summary>
public static class Layouts
{
    #region Attributes
    /// <summary>
    /// Espaciado base entre elementos del layout.
    /// </summary>
    private static double _gap = 8;

    /// <summary>
    /// Mitad del espaciado base, usado para generar márgenes uniformes.
    /// </summary>
    private static double _halfGap => _gap / 2.0;

    /// <summary>
    /// Diccionario que almacena todos los layouts disponibles indexados por clave.
    /// </summary>
    private static readonly Dictionary<int, LayoutInfo> _layouts = new();
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa los layouts predefinidos al cargar la clase.
    /// </summary>
    static Layouts()
    {
        CreateLayouts(_gap);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Layout de una sola columna que ocupa todo el ancho.
    /// </summary>
    private static LayoutInfo OneColumnGrid(int index)
    {
        var slots = new[] { new SlotInfo(0, 0, 2, 3) };
        var cols = new[] { "1*", "0", "0" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout de dos columnas con ancho 50/50.
    /// </summary>
    private static LayoutInfo TwoColumns50Grid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0, 2, 1),
            new SlotInfo(0, 1, 2, 1)
        };
        var cols = new[] { "*", "*", "0" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout donde la columna izquierda está dividida en dos filas y la derecha ocupa toda la altura.
    /// </summary>
    private static LayoutInfo LeftColumnSplitGrid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0),
            new SlotInfo(1, 0),
            new SlotInfo(0, 1, 2, 1)
        };
        var cols = new[] { "*", "*", "0" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout donde la columna derecha está dividida en dos filas y la izquierda ocupa toda la altura.
    /// </summary>
    private static LayoutInfo RightColumnSplitGrid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0, 2, 1),
            new SlotInfo(0, 1),
            new SlotInfo(1, 1)
        };
        var cols = new[] { "*", "*", "0" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout de cuadrícula 2x2 con cuatro slots iguales.
    /// </summary>
    private static LayoutInfo Grid2x2Grid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0),
            new SlotInfo(0, 1),
            new SlotInfo(1, 0),
            new SlotInfo(1, 1)
        };
        var cols = new[] { "*", "*", "0" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout de tres columnas con anchos iguales.
    /// </summary>
    private static LayoutInfo ThreeColumnsEqualGrid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0, 2, 1),
            new SlotInfo(0, 1, 2, 1),
            new SlotInfo(0, 2, 2, 1)
        };
        var cols = new[] { "*", "*", "*" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout de tres columnas: las laterales (25% cada una) divididas en dos filas y la central (50%) a
    /// toda altura.
    /// </summary>
    private static LayoutInfo WideMiddleSidesSplitGrid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0),
            new SlotInfo(1, 0),
            new SlotInfo(0, 1, 2, 1),
            new SlotInfo(0, 2),
            new SlotInfo(1, 2)
        };
        var cols = new[] { "*", "2*", "*" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout de tres columnas: la izquierda al 50% a toda altura; la central y la derecha (25% cada una)
    /// divididas en dos filas.
    /// </summary>
    private static LayoutInfo WideLeftBothColumnsSplitGrid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0, 2, 1),
            new SlotInfo(0, 1),
            new SlotInfo(1, 1),
            new SlotInfo(0, 2),
            new SlotInfo(1, 2)
        };
        var cols = new[] { "2*", "*", "*" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout de tres columnas: la izquierda al 50% y la central al 25%, ambas a toda altura; la derecha
    /// al 25% dividida en dos filas.
    /// </summary>
    private static LayoutInfo WideLeftRightColumnSplitGrid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0, 2, 1),
            new SlotInfo(0, 1, 2, 1),
            new SlotInfo(0, 2),
            new SlotInfo(1, 2)
        };
        var cols = new[] { "2*", "*", "*" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Layout en cuadrícula de dos filas por tres columnas con seis slots iguales.
    /// </summary>
    private static LayoutInfo TwoRowsThreeColumnsGrid(int index)
    {
        var slots = new[]
        {
            new SlotInfo(0, 0),
            new SlotInfo(0, 1),
            new SlotInfo(0, 2),
            new SlotInfo(1, 0),
            new SlotInfo(1, 1),
            new SlotInfo(1, 2)
        };
        var cols = new[] { "*", "*", "*" };
        return Create(index, slots, cols);
    }

    /// <summary>
    /// Crea un layout a partir de una lista de slots y columnas, generando márgenes uniformes.
    /// </summary>
    private static LayoutInfo Create(int index, IReadOnlyList<SlotInfo> slots, IReadOnlyList<string> cols)
    {
        var margins = GenerateMargins(slots);
        return new LayoutInfo(index, slots, cols, margins, _gap);
    }

    /// <summary>
    /// Genera márgenes uniformes para cada slot del layout.
    /// </summary>
    private static IReadOnlyList<SlotMargin> GenerateMargins(IReadOnlyList<SlotInfo> slots)
    {
        var list = new List<SlotMargin>(slots.Count);
        for (int i = 0; i < slots.Count; i++)
            list.Add(new SlotMargin(_halfGap, _halfGap, _halfGap, _halfGap));

        return list;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Crea o actualiza los layouts predefinidos con un nuevo valor de separación (gap).
    /// </summary>
    /// <param name="gap">Nuevo valor de separación entre elementos del layout.</param>
    public static void CreateLayouts(double gap)
    {
        _gap = gap;
        _layouts.Clear();
        _layouts.Add(0, OneColumnGrid(0));
        _layouts.Add(1, TwoColumns50Grid(1));
        _layouts.Add(2, RightColumnSplitGrid(2));
        _layouts.Add(3, LeftColumnSplitGrid(3));
        _layouts.Add(4, ThreeColumnsEqualGrid(4));
        _layouts.Add(5, Grid2x2Grid(5));
        _layouts.Add(6, WideLeftRightColumnSplitGrid(6));
        _layouts.Add(7, WideLeftBothColumnsSplitGrid(7));
        _layouts.Add(8, WideMiddleSidesSplitGrid(8));
        _layouts.Add(9, TwoRowsThreeColumnsGrid(9));
    }

    /// <summary>
    /// Obtiene un layout por su clave. Si no existe, devuelve el layout por defecto (clave 0).
    /// </summary>
    public static LayoutInfo Get(int key)
    {
        if (_layouts.TryGetValue(key, out var info))
            return info;

        return _layouts[0];
    }
    #endregion
}
