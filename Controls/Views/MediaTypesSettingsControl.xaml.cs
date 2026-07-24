using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Views;

/// <summary>
/// Página de la categoría "Media types" de la ventana de configuración: lista de todos los tipos de imagen de juego
/// con un check por cada uno (máximo 10, los favoritos de la banda de tipos). Se enlaza al staging del
/// <see cref="ViewModels.SettingsDialogViewModel"/> (DataContext heredado).
/// </summary>
public sealed partial class MediaTypesSettingsControl : UserControl
{
    public MediaTypesSettingsControl()
    {
        InitializeComponent();
    }
}
