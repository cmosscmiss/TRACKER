using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Controls.ViewModels;

/// <summary>Naturaleza de un <see cref="RegionBucket"/> del selector del GameImagesRegionDashboard.</summary>
public enum RegionBucketKind
{
    /// <summary>Una región favorita concreta (de la lista de appSettings, máx. 3).</summary>
    Favourite,

    /// <summary>Agrupa las imágenes que tienen región pero no es ninguna de las favoritas.</summary>
    OtherRegions,

    /// <summary>Agrupa las imágenes sin región (<see cref="ImageRegion.NoRegion"/>).</summary>
    NoRegion,
}

/// <summary>
/// Elemento fijo del selector de regiones del GameImagesRegionDashboard: una región favorita, el bucket "otras
/// regiones" o el bucket "sin región". Mantiene su etiqueta, el conjunto de imágenes del juego actual que caen en
/// él y su conteo (badge). <see cref="IsSelected"/> refleja si es el bucket activo.
/// </summary>
public sealed class RegionBucket : ObservableObject
{
    /// <summary>Etiqueta mostrada en el selector (nombre de la región, "Other regions" o "No region").</summary>
    public string Label { get; }

    /// <summary>Tipo de bucket.</summary>
    public RegionBucketKind Kind { get; }

    /// <summary>La región favorita de este bucket; solo para <see cref="RegionBucketKind.Favourite"/>.</summary>
    public ImageRegion? Region { get; }

    /// <summary>Imágenes del juego actual que caen en este bucket (se recalculan en cada refresco).</summary>
    public List<GameImage> Images { get; } = new();

    private int _count;
    /// <summary>Número de imágenes del bucket (badge del selector).</summary>
    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    private bool _isSelected;
    /// <summary>Si es el bucket activo (para resaltarlo en el selector).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public RegionBucket(string label, RegionBucketKind kind, ImageRegion? region = null)
    {
        Label = label;
        Kind = kind;
        Region = region;
    }
}
