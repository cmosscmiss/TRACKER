using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MM4LB.Models;

/// <summary>
/// Estado de los filtros por variable de la lista de productos (equivalente al viejo <c>Filters</c> de la lista de
/// juegos). Cada dimensión es un booleano; se combinan con OR dentro del grupo, y <see cref="HasAny"/> indica si
/// hay alguno activo (para que la lista pueda saltarse el filtrado cuando no hay ninguno).
/// </summary>
public partial class ProductFilters : ObservableObject
{
    /// <summary>Solo productos marcados como favoritos.</summary>
    [ObservableProperty]
    private bool _favorites;

    /// <summary>Solo productos con algún aviso/problema (alguna tienda no disponible o sin precio).</summary>
    [ObservableProperty]
    private bool _withIssues;

    /// <summary>Solo productos cuyo mejor precio ha cambiado respecto a la lectura anterior (subió o bajó).</summary>
    [ObservableProperty]
    private bool _withPriceChange;

    /// <summary>Solo productos con un precio de alerta configurado.</summary>
    [ObservableProperty]
    private bool _withAlert;

    /// <summary>True si hay al menos un filtro por variable activo.</summary>
    public bool HasAny => Favorites || WithIssues || WithPriceChange || WithAlert;

    /// <summary>
    /// Aplica los filtros por variable activos (combinados con OR) a la secuencia dada. Si no hay ninguno activo,
    /// devuelve la secuencia sin tocar.
    /// </summary>
    public IEnumerable<Product> Apply(IEnumerable<Product> source)
    {
        if (!HasAny)
            return source;

        return source.Where(product =>
            (Favorites && product.IsFavorite) ||
            (WithIssues && product.HasIssues) ||
            (WithPriceChange && (product.Trend == PriceTrend.Up || product.Trend == PriceTrend.Down)) ||
            (WithAlert && product.HasAlert));
    }
}
