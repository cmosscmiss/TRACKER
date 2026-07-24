using Microsoft.UI.Xaml;

namespace Tracker;

/// <summary>
/// Ventana principal. Escaparate mínimo que ejercita los estilos portados (tipografía + botones).
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Tracker";
    }
}
