using Microsoft.UI.Xaml.Controls;
using MM4LB.ViewModels;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Contenido de la ventana de configuración de la app, mostrado dentro de un <see cref="AppDialog"/> (que aporta la
/// cabecera con logo y los botones OK/Cancel sobre el overlay de la aplicación). Resuelve su
/// <see cref="SettingsDialogViewModel"/> de DI (staging) y expone <see cref="Apply"/>, que el diálogo invoca al
/// aceptar (OK). Ver <see cref="Services.DialogsService.ShowSettingsAsync"/>.
/// </summary>
public sealed partial class SettingsControl : UserControl
{
    private readonly SettingsDialogViewModel _viewModel = App.GetService<SettingsDialogViewModel>();

    public SettingsControl()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <summary>Aplica los cambios (staging → AppSettings, en caliente) y persiste. Lo llama el diálogo al aceptar.</summary>
    public void Apply() => _viewModel.Apply();
}
