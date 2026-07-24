using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Manages an exclusive group of toggle items.
/// 
/// This class guarantees that only one enabled item can be selected at a time.
/// It centralizes the common toggle-group behaviour so individual ViewModels do
/// not need to repeat the same activate/deactivate logic.
/// </summary>
/// <typeparam name="TValue">
/// Type of the logical value represented by the items in the group.
/// </typeparam>
public class ToggleGroup<TValue> : ObservableObject
{
    #region Attributes
    private readonly bool _autoSelectFirstEnabledItem;
    private readonly bool _requireSelection;

    private ToggleGroupItem<TValue>? _selectedItem;
    #endregion

    #region Properties
    /// <summary>
    /// Gets the collection of items that belong to this toggle group.
    /// 
    /// This collection can be bound directly to an ItemsControl.
    /// </summary>
    public ObservableCollection<ToggleGroupItem<TValue>> Items { get; } = new();

    /// <summary>
    /// Gets the currently selected item.
    /// 
    /// The value is controlled by the group and changes only through Select,
    /// SelectValue, SelectFirstEnabledItem or Clear.
    /// </summary>
    public ToggleGroupItem<TValue>? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(SelectedValue));
                OnPropertyChanged(nameof(HasSelection));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gets the logical value represented by the currently selected item.
    /// 
    /// Returns default when the group does not currently have a selected item.
    /// </summary>
    public TValue? SelectedValue => SelectedItem is null ? default : SelectedItem.Value;

    /// <summary>
    /// Gets whether the group currently has a selected item.
    /// </summary>
    public bool HasSelection => SelectedItem is not null;
    #endregion

    #region Events
    /// <summary>
    /// Event raised when the selected item changes.
    /// 
    /// Consumers can read SelectedItem or SelectedValue from the group when this
    /// event is raised.
    /// </summary>
    public event EventHandler? SelectionChanged;
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="ToggleGroup{TValue}"/> class.
    /// </summary>
    /// <param name="autoSelectFirstEnabledItem">
    /// If true, the first enabled item added to the group is selected automatically.
    /// </param>
    public ToggleGroup(bool requireSelection = true, bool autoSelectFirstEnabledItem = true)
    {
        _requireSelection = requireSelection;
        _autoSelectFirstEnabledItem = autoSelectFirstEnabledItem;
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles item property changes that can affect the validity of the current
    /// selection.
    /// 
    /// If the selected item becomes disabled, the group automatically selects
    /// the first available enabled item. If no enabled item exists, the selection
    /// is cleared.
    /// </summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToggleGroupItem<TValue>.IsEnabled))
            return;

        if (sender is not ToggleGroupItem<TValue> item)
            return;

        if (item != SelectedItem)
            return;

        if (item.IsEnabled)
            return;

        if (_requireSelection)
        {
            SelectFirstEnabledItem();
        }
        else
        {
            ClearSelection();
        }
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Clears only the current selection without removing the group items.
    /// </summary>
    public void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.SetSelectedFromGroup(false);
        }

        SelectedItem = null;
    }

    /// <summary>
    /// Creates and adds a new item to the group.
    /// </summary>
    /// <param name="value">Logical value represented by the item.</param>
    /// <param name="label">Display label associated with the item.</param>
    /// <param name="checkedIcon">Icon used when the item is selected.</param>
    /// <param name="uncheckedIcon">Icon used when the item is not selected.</param>
    /// <returns>The created toggle group item.</returns>
    public ToggleGroupItem<TValue> Add(TValue value, string label, ImageSource? checkedIcon = null, ImageSource? uncheckedIcon = null)
    {
        var item = new ToggleGroupItem<TValue>(value, label, checkedIcon, uncheckedIcon, item => Select(item));
        Add(item);

        return item;
    }

    /// <summary>
    /// Adds an existing item to the group.
    /// 
    /// Use this overload when the item needs to be created externally before
    /// being registered in the group.
    /// </summary>
    public void Add(ToggleGroupItem<TValue> item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (Items.Contains(item))
            return;

        Items.Add(item);
        item.PropertyChanged += OnItemPropertyChanged;

        if (_autoSelectFirstEnabledItem && SelectedItem is null && item.IsEnabled)
        {
            Select(item);
        }
    }

    /// <summary>
    /// Selects the provided item and deselects every other item in the group.
    /// </summary>
    /// <param name="selected">Item to select.</param>
    /// <returns>True when the selection was applied; otherwise false.</returns>
    public bool Select(ToggleGroupItem<TValue> selected)
    {
        if (selected is null)
            return false;

        if (!Items.Contains(selected))
            return false;

        if (!selected.IsEnabled)
            return false;

        foreach (var item in Items)
        {
            item.SetSelectedFromGroup(item == selected);
        }

        SelectedItem = selected;

        return true;
    }

    /// <summary>
    /// Selects the first item whose value matches the provided value.
    /// </summary>
    /// <param name="value">Value to select.</param>
    /// <returns>True when a matching enabled item was selected; otherwise false.</returns>
    public bool SelectValue(TValue value)
    {
        var item = Items.FirstOrDefault(i => Equals(i.Value, value));

        if (item is null)
            return false;

        return Select(item);
    }

    /// <summary>
    /// Selects the first enabled item in the group.
    /// </summary>
    /// <returns>True when an enabled item was selected; otherwise false.</returns>
    public bool SelectFirstEnabledItem()
    {
        var item = Items.FirstOrDefault(i => i.IsEnabled);

        if (item is null)
        {
            Clear();
            return false;
        }

        return Select(item);
    }

    /// <summary>
    /// Removes all items from the group and clears the current selection.
    /// </summary>
    public void Clear()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.SetSelectedFromGroup(false);
        }

        Items.Clear();
        SelectedItem = null;
    }
    #endregion
}