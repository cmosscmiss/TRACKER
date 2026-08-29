using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tracker.Models;

/// <summary>
/// Holds the collection of tracked <see cref="Product"/>s shown in the left-hand ListView of the main
/// window. Structural counterpart of the old PlatformSet: the app keeps a single ProductSet as the
/// source of the product list, and it is what gets persisted/restored with the configuration.
///
/// The collection is kept sorted ALPHABETICALLY by product name: new products are inserted in order
/// (<see cref="AddSorted"/>) and repositioned automatically when their name changes (e.g. after the first parse
/// turns "Amazon &lt;ASIN&gt;" into the real title).
/// </summary>
public partial class ProductSet : ObservableObject
{
    /// <summary>The tracked products, kept sorted alphabetically by name.</summary>
    public ObservableCollection<Product> Products { get; } = new();

    /// <summary>Number of tracked products (observable for UI binding).</summary>
    public int TotalProducts => Products.Count;

    public ProductSet()
    {
        Products.CollectionChanged += OnProductsChanged;
    }

    #region Sorting
    /// <summary>Inserta un producto en su posición alfabética (por nombre, ignorando mayúsculas).</summary>
    public void AddSorted(Product product)
    {
        int index = 0;
        while (index < Products.Count && Compare(Products[index], product) < 0)
            index++;
        Products.Insert(index, product);
    }

    /// <summary>Reordena toda la colección alfabéticamente por nombre (p. ej. tras cargar de la BD).</summary>
    public void SortByName()
    {
        var sorted = Products.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (int target = 0; target < sorted.Count; target++)
        {
            int current = Products.IndexOf(sorted[target]);
            if (current != target)
                Products.Move(current, target);
        }
    }

    private static int Compare(Product a, Product b)
        => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Recoloca un producto en su sitio alfabético tras cambiar su nombre.</summary>
    private void Reposition(Product product)
    {
        int current = Products.IndexOf(product);
        if (current < 0)
            return;

        // Índice destino = nº de OTROS productos que van antes alfabéticamente (el resto ya está ordenado).
        int target = 0;
        for (int i = 0; i < Products.Count; i++)
            if (i != current && Compare(Products[i], product) < 0)
                target++;

        if (target != current)
            Products.Move(current, target);
    }
    #endregion

    #region Subscribed events
    private void OnProductsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TotalProducts));

        if (e.OldItems is not null)
            foreach (Product product in e.OldItems)
                product.PropertyChanged -= OnProductPropertyChanged;

        if (e.NewItems is not null)
            foreach (Product product in e.NewItems)
                product.PropertyChanged += OnProductPropertyChanged;
    }

    private void OnProductPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Product.Name) && sender is Product product)
            Reposition(product);
    }
    #endregion
}
