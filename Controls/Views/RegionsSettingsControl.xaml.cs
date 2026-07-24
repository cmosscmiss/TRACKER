using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Views;

/// <summary>
/// Página de la categoría "Regions" de la ventana de configuración: lista de todas las regiones con un check por
/// cada una (máximo 3, las favoritas del dashboard de regiones). Se enlaza al staging del
/// <see cref="ViewModels.SettingsDialogViewModel"/> (DataContext heredado).
/// </summary>
public sealed partial class RegionsSettingsControl : UserControl
{
    public RegionsSettingsControl()
    {
        InitializeComponent();
    }
}
