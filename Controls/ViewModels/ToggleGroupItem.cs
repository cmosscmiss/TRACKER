using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Represents one selectable item inside an exclusive toggle group.
/// 
/// This class is designed to be bound to visual controls such as ToolbarButtonIcon.
/// The item exposes its selected state, enabled state, icons and selection command,
/// but it does not decide group exclusivity by itself.
/// 
/// Selection is delegated to the parent ToggleGroup through a callback.
/// </summary>
/// <typeparam name="TValue">
/// Type of the logical value represented by the toggle item.
/// </typeparam>
public class ToggleGroupItem<TValue> : ObservableObject
{
    #region Attributes
    private readonly Action<ToggleGroupItem<TValue>> _onSelectionRequested;

    private bool _isSelected;
    private bool _isEnabled = true;
    private ImageSource? _checkedIcon;
    private ImageSource? _uncheckedIcon;
    private RelayCommand? _selectCommand;
    #endregion

    #region Properties
    /// <summary>
    /// Gets the logical value represented by this toggle item.
    /// 
    /// Examples:
    /// - Horizontal / Vertical orientation.
    /// - Grid / List / Details view mode.
    /// - A specific enum value.
    /// - A model instance associated with the button.
    /// </summary>
    public TValue Value { get; }

    /// <summary>
    /// Gets the display label associated with this toggle item.
    /// 
    /// This value can be used for tooltips, accessibility names, labels or debug output.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets or sets the icon displayed when the item is selected.
    /// 
    /// This property is intended to be bound to ToolbarButtonIcon.CheckedIcon.
    /// </summary>
    public ImageSource? CheckedIcon
    {
        get => _checkedIcon;
        set => SetProperty(ref _checkedIcon, value);
    }

    /// <summary>
    /// Gets or sets the icon displayed when the item is not selected.
    /// 
    /// This property is intended to be bound to ToolbarButtonIcon.UncheckedIcon.
    /// </summary>
    public ImageSource? UncheckedIcon
    {
        get => _uncheckedIcon;
        set => SetProperty(ref _uncheckedIcon, value);
    }

    /// <summary>
    /// Gets or sets whether this item is currently selected.
    /// 
    /// When the UI attempts to set this property to true, the item requests
    /// selection from its parent group.
    /// 
    /// When the UI attempts to set this property to false, the change is rejected.
    /// This prevents the active item from being manually deselected and guarantees
    /// that the group remains responsible for deciding which item is active.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected)
                return;

            if (value)
            {
                RequestSelection();
                return;
            }

            OnPropertyChanged(nameof(IsSelected));
        }
    }

    /// <summary>
    /// Gets or sets whether this item can be selected by the user.
    /// 
    /// When disabled, the item remains visible but its SelectCommand will not
    /// request selection.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _selectCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets the command used by the UI to select this item.
    /// 
    /// This command is intended to be bound to ToolbarButtonIcon.Command.
    /// </summary>
    public ICommand SelectCommand => _selectCommand ??= new RelayCommand(RequestSelection, CanRequestSelection);
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="ToggleGroupItem{TValue}"/> class.
    /// </summary>
    /// <param name="value">Logical value represented by the item.</param>
    /// <param name="label">Display label associated with the item.</param>
    /// <param name="checkedIcon">Icon used when the item is selected.</param>
    /// <param name="uncheckedIcon">Icon used when the item is not selected.</param>
    /// <param name="onSelectionRequested">Callback used to request selection from the parent group.</param>
    public ToggleGroupItem(TValue value, string label, ImageSource? checkedIcon, ImageSource? uncheckedIcon, Action<ToggleGroupItem<TValue>> onSelectionRequested)
    {
        Value = value;
        Label = label;
        CheckedIcon = checkedIcon;
        UncheckedIcon = uncheckedIcon;
        _onSelectionRequested = onSelectionRequested ?? throw new ArgumentNullException(nameof(onSelectionRequested));
    }
    #endregion

    #region Methods (internal)
    /// <summary>
    /// Updates the selected state from the parent group.
    /// 
    /// This method bypasses the public IsSelected setter so the group can update
    /// item states without triggering a new selection request.
    /// </summary>
    internal void SetSelectedFromGroup(bool value)
    {
        SetProperty(ref _isSelected, value, nameof(IsSelected));
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Determines whether this item can request selection.
    /// </summary>
    private bool CanRequestSelection()
    {
        return IsEnabled;
    }

    /// <summary>
    /// Requests selection from the parent group.
    /// 
    /// The item itself does not deselect other items. That responsibility belongs
    /// to ToggleGroup.
    /// </summary>
    private void RequestSelection()
    {
        if (!IsEnabled)
        {
            OnPropertyChanged(nameof(IsSelected));
            return;
        }

        _onSelectionRequested(this);
    }
    #endregion
}