using Microsoft.UI.Xaml;

namespace Tracker;

/// <summary>
/// Punto de entrada de la aplicación. Esqueleto mínimo (sin host de DI): solo crea y activa la ventana
/// principal. Si más adelante necesitas inyección de dependencias, servicios o temas en caliente, aquí es
/// donde se monta el host (ver cómo lo hace MM4LB).
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
