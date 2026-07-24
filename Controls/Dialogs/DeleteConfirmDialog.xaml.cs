using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Content for the media-deletion confirmation dialog: the message plus a "Ask for confirmation before
/// deleting" check. The check reflects (and lets the user change) the
/// <see cref="MM4LB.Models.AppSettings.GeneralSettings.PromptBeforeDeleteImage"/> setting: unchecking it and
/// confirming turns the confirmation off for next time. Shown inside an <see cref="AppDialog"/>.
/// </summary>
public sealed partial class DeleteConfirmDialog : Page
{
    #region Constructors
    public DeleteConfirmDialog(string message, bool askBefore)
    {
        InitializeComponent();

        MessageText.Text = message ?? string.Empty;
        cbAskBefore.IsChecked = askBefore;
    }
    #endregion

    #region Properties
    /// <summary>True if the confirmation should keep being asked next time (the check is ticked).</summary>
    public bool AskBeforeDeleting => cbAskBefore.IsChecked == true;
    #endregion
}
