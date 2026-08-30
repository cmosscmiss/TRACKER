using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Tracker.ViewModels;

namespace Tracker.Controls.Dialogs;

/// <summary>
/// Contenido de la ventana de configuración de la app, mostrado dentro de un <see cref="AppDialog"/> (que aporta la
/// cabecera con logo y los botones OK/Apply/Cancel sobre el overlay de la aplicación). Resuelve su
/// <see cref="SettingsDialogViewModel"/> de DI (staging) y expone <see cref="Apply"/>, que el diálogo invoca al
/// aceptar (OK) o al pulsar Apply. Ver <see cref="Services.DialogsService.ShowSettingsAsync"/>.
///
/// Implementa <see cref="IAppDialogApplyGate"/> para que el botón "Apply" solo esté activo mientras haya cambios sin
/// aplicar (<see cref="SettingsDialogViewModel.IsDirty"/>): arranca apagado, se enciende al tocar cualquier ajuste y
/// vuelve a apagarse al aplicar.
/// </summary>
public sealed partial class SettingsControl : UserControl, IAppDialogApplyGate
{
    private readonly SettingsDialogViewModel _viewModel = App.GetService<SettingsDialogViewModel>();

    /// <summary>ViewModel (staging) de este contenido. Lo usa <see cref="Services.DialogsService"/> para orquestar el editor de colores.</summary>
    public SettingsDialogViewModel ViewModel => _viewModel;

    public SettingsControl()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Hay cambios pendientes de aplicar (gate del botón "Apply" del diálogo).</summary>
    public bool IsApplyEnabled => _viewModel.IsDirty;

    /// <inheritdoc/>
    public event EventHandler? ApplyEnabledChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsDialogViewModel.IsDirty))
            ApplyEnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Aplica los cambios (staging → AppSettings, en caliente) y persiste. Lo llama el diálogo al aceptar/Apply.</summary>
    public void Apply() => _viewModel.Apply();

    /// <summary>Descarta el staging (deshace la vista previa en caliente). Lo llama el diálogo al cancelar/cerrar.</summary>
    public void Cancel() => _viewModel.Cancel();
}
