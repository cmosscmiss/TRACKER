namespace MM4LB.Enums;

/// <summary>
/// Enumeration for the different regions supported by LaunchBox.
/// </summary>
public class ImageRegion : Enumeration
{
    public static readonly ImageRegion NoRegion = new(1, "");
    public static readonly ImageRegion Asia = new(2, nameof(Asia));
    public static readonly ImageRegion Australia = new(3, nameof(Australia));
    public static readonly ImageRegion Brazil = new(4, nameof(Brazil));
    public static readonly ImageRegion Canada = new(5, nameof(Canada));
    public static readonly ImageRegion China = new(6, nameof(China));
    public static readonly ImageRegion Europe = new(7, nameof(Europe));
    public static readonly ImageRegion Finland = new(8, nameof(Finland));
    public static readonly ImageRegion France = new(9, nameof(France));
    public static readonly ImageRegion Germany = new(10, nameof(Germany));
    public static readonly ImageRegion Greece = new(11, nameof(Greece));
    public static readonly ImageRegion Holland = new(12, nameof(Holland));
    public static readonly ImageRegion HongKong = new(13, "Hong Kong");
    public static readonly ImageRegion Italy = new(14, nameof(Italy));
    public static readonly ImageRegion Japan = new(15, nameof(Japan));
    public static readonly ImageRegion Korea = new(16, nameof(Korea));
    public static readonly ImageRegion NorthAmerica = new(17, "North America");
    public static readonly ImageRegion Norway = new(18, nameof(Norway));
    public static readonly ImageRegion Oceania = new(19, nameof(Oceania));
    public static readonly ImageRegion Russia = new(20, nameof(Russia));
    public static readonly ImageRegion SouthAmerica = new(21, "South America");
    public static readonly ImageRegion Spain = new(22, nameof(Spain));
    public static readonly ImageRegion Sweden = new(23, nameof(Sweden));
    public static readonly ImageRegion Thailand = new(24, nameof(Thailand));
    public static readonly ImageRegion TheNetherlands = new(25, "The Netherlands");
    public static readonly ImageRegion UnitedKingdom = new(26, "United Kingdom");
    public static readonly ImageRegion UnitedStates = new(27, "United States");
    public static readonly ImageRegion World = new(28, nameof(World));

    public ImageRegion()
    {
    }

    private ImageRegion(int id, string name) : base(id, name) { }
}