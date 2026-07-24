using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Enums;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Contenido del diálogo para elegir la REGIÓN de destino al importar imágenes con el GameImagesRegionDashboard
/// activo: una lista de regiones (favoritas configuradas + "No region"). La primera queda preseleccionada, así que
/// el botón primario está siempre disponible.
/// </summary>
public sealed partial class SelectRegionDialog : Page
{
    /// <summary>Elemento del combo: la región y su etiqueta ("No region" para la región vacía).</summary>
    private sealed record RegionOption(ImageRegion Region, string Label);

    #region Constructors
    public SelectRegionDialog(IEnumerable<ImageRegion> regions)
    {
        InitializeComponent();

        string noRegionLabel = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Common_NoRegion_Label] ?? "No region";
        List<RegionOption> options = regions
            .Select(r => new RegionOption(r, string.IsNullOrEmpty(r.Value) ? noRegionLabel : r.Value))
            .ToList();

        cbRegion.ItemsSource = options;
        if (options.Count > 0)
        {
            cbRegion.SelectedIndex = 0;
        }
    }
    #endregion

    #region Properties
    /// <summary>Región elegida por el usuario (o <c>null</c> si no hay selección).</summary>
    public ImageRegion? SelectedRegion => (cbRegion.SelectedItem as RegionOption)?.Region;
    #endregion
}
