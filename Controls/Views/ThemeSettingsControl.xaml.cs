using Microsoft.UI.Xaml.Controls;

namespace Tracker.Controls.Views;

/// <summary>
/// Página de la categoría "Theme" de la ventana de configuración: selector del tema visual activo y parámetros del
/// tinte del overlay. Se enlaza al staging del <see cref="ViewModels.SettingsDialogViewModel"/> (DataContext
/// heredado) y el tema se aplica en caliente al aceptar el diálogo.
/// </summary>
public sealed partial class ThemeSettingsControl : UserControl
{
    public ThemeSettingsControl()
    {
        InitializeComponent();
    }
}
