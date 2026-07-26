using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MM4LB.Models;

/// <summary>
/// Holds the collection of tracked <see cref="Product"/>s shown in the left-hand ListView of the main
/// window. Structural counterpart of the old PlatformSet: the app keeps a single ProductSet as the
/// source of the product list, and it is what gets persisted/restored with the configuration.
/// </summary>
public partial class ProductSet : ObservableObject
{
    /// <summary>The tracked products, in display order.</summary>
    public ObservableCollection<Product> Products { get; } = new();

    /// <summary>Number of tracked products (observable for UI binding).</summary>
    public int TotalProducts => Products.Count;

    public ProductSet()
    {
        Products.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalProducts));
    }
}
