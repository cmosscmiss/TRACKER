using MM4LB.Enums;

/// <summary>
/// Representa un tipo de layout usando el patrón "Enumeration"
/// </summary>
public class LayoutType : Enumeration
{
    public static readonly LayoutType OneColumn = new(0, nameof(OneColumn));
    public static readonly LayoutType TwoColumns50 = new(1, nameof(TwoColumns50));
    public static readonly LayoutType RightColumnSplit = new(2, nameof(RightColumnSplit));
    public static readonly LayoutType LeftColumnSplit = new(3, nameof(LeftColumnSplit));
    public static readonly LayoutType ThreeColumnsEqualGrid = new(4, nameof(ThreeColumnsEqualGrid));
    public static readonly LayoutType Grid2x2 = new(5, nameof(Grid2x2));
    public static readonly LayoutType WideLeftRightColumnSplit = new(6, nameof(WideLeftRightColumnSplit));

    public LayoutType()
    {
    }

    private LayoutType(int id, string name) : base(id, name)
    {
    }
}