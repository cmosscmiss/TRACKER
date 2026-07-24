using CommunityToolkit.Mvvm.ComponentModel;

namespace MM4LB.Models;

/// <summary>
/// Helper class for a selectable, labelled option with an optional numeric value and a checked state
/// (used by toggle groups such as the aspect-ratio / resolution toolbars).
/// </summary>
public class SelectableOption : ObservableObject
{
    #region Attributes
    private bool _isChecked;
    #endregion


    #region Properties
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public string Name { get; set; } = string.Empty;

    public double Value { get; set; }
    #endregion
}
