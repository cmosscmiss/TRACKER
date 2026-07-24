using System;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Enums;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Content for the platform-image drop dialog: a type selector (one of the platform image types) plus a
/// Keep/Discard choice. The primary button is gated on a type being selected (<see cref="IAppDialogPrimaryGate"/>).
/// </summary>
public sealed partial class PlatformImageDropDialog : Page, IAppDialogPrimaryGate
{
    #region Constructors
    public PlatformImageDropDialog()
    {
        InitializeComponent();

        cbType.ItemsSource = MediaType.PlatformImageTypes;
    }
    #endregion

    #region Properties
    /// <summary>The platform image type chosen by the user, or <c>null</c> if none is selected.</summary>
    public MediaType? SelectedType => cbType.SelectedItem as MediaType;

    /// <summary>True when the existing images of the type should be replaced (Discard); false to add (Keep).</summary>
    public bool Discard => rbExisting.SelectedIndex == 0;

    public bool IsPrimaryEnabled => SelectedType != null;
    #endregion

    #region Events
    public event EventHandler? PrimaryEnabledChanged;
    #endregion

    #region Subscribed events
    private void OnTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
        => PrimaryEnabledChanged?.Invoke(this, EventArgs.Empty);
    #endregion
}
