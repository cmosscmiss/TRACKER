using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Views;

/// <summary>
/// Página de la categoría "About" de la ventana de configuración: identidad de la app, descripción, detalles del
/// build/runtime y componentes de terceros con su licencia. Solo lectura; se enlaza al
/// <see cref="ViewModels.SettingsDialogViewModel"/> (DataContext heredado).
/// </summary>
public sealed partial class AboutSettingsControl : UserControl
{
    public AboutSettingsControl()
    {
        InitializeComponent();
    }
}
