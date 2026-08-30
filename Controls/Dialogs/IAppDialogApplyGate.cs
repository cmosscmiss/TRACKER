using System;

namespace Tracker.Controls.Dialogs;

/// <summary>
/// Implemented by <see cref="AppDialog"/> content that needs to gate the "Apply" button: the dialog keeps that
/// button enabled only while <see cref="IsApplyEnabled"/> is true, re-evaluating it whenever
/// <see cref="ApplyEnabledChanged"/> is raised. Es el equivalente de <see cref="IAppDialogPrimaryGate"/> para el
/// botón de aplicar; lo usa la ventana de configuración para tenerlo apagado mientras no haya cambios pendientes.
/// </summary>
public interface IAppDialogApplyGate
{
    bool IsApplyEnabled { get; }

    event EventHandler ApplyEnabledChanged;
}
