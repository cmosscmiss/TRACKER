using System;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Implemented by <see cref="AppDialog"/> content that needs to gate the primary button: the dialog keeps
/// the primary button enabled only while <see cref="IsPrimaryEnabled"/> is true, re-evaluating it whenever
/// <see cref="PrimaryEnabledChanged"/> is raised (e.g. a required selection becoming valid).
/// </summary>
public interface IAppDialogPrimaryGate
{
    bool IsPrimaryEnabled { get; }

    event EventHandler PrimaryEnabledChanged;
}
