namespace MM4LB.Enums;

/// <summary>
/// Which image types are plotted on the platform coverage-by-type chart. Selectable by the user; the default
/// is <see cref="Favourites"/>.
/// </summary>
public enum CoverageTypeScope
{
    /// <summary>Only the favourite image types (<c>AppSettings.ImageTypeControlSettings.FavouriteImageTypes</c>).</summary>
    Favourites,

    /// <summary>Every image type the selected platform actually has an image set for.</summary>
    Present,
}
