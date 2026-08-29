using Microsoft.UI.Xaml.Controls;
using Tracker.ViewModels;

namespace Tracker.Controls.Dialogs;

/// <summary>
/// Contenido de la ventana de configuración de la app, mostrado dentro de un <see cref="AppDialog"/> (que aporta la
/// cabecera con logo y los botones OK/Apply/Cancel sobre el overlay de la aplicación). Resuelve su
/// <see cref="SettingsDialogViewModel"/> de DI (staging) y expone <see cref="Apply"/>, que el diálogo invoca al
/// aceptar (OK) o al pulsar Apply. Ver <see cref="Services.DialogsService.ShowSettingsAsync"/>.
/// </summary>
public sealed partial class SettingsControl : UserControl
{
    private readonly SettingsDialogViewModel _viewModel = App.GetService<SettingsDialogViewModel>();

    /// <summary>ViewModel (staging) de este contenido. Lo usa <see cref="Services.DialogsService"/> para orquestar el editor de colores.</summary>
    public SettingsDialogViewModel ViewModel => _viewModel;

    public SettingsControl()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <summary>Aplica los cambios (staging → AppSettings, en caliente) y persiste. Lo llama el diálogo al aceptar/Apply.</summary>
    public void Apply() => _viewModel.Apply();

    /// <summary>Descarta el staging (deshace la vista previa en caliente). Lo llama el diálogo al cancelar/cerrar.</summary>
    public void Cancel() => _viewModel.Cancel();
}
