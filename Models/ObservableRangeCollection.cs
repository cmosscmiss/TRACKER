using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Tracker.Models;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can replace its whole content in a single
/// notification.
///
/// Mutating an <see cref="ObservableCollection{T}"/> item by item raises one
/// <see cref="NotifyCollectionChangedAction"/> per element, and a bound ListView/GridView/DataGrid
/// re-runs layout on every one of them. For large collections that is O(N) layout passes; this type
/// replaces the content with a single <see cref="NotifyCollectionChangedAction.Reset"/>, so the bound
/// control re-reads the collection once.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public ObservableRangeCollection()
    {
    }

    public ObservableRangeCollection(IEnumerable<T> collection) : base(collection)
    {
    }

    /// <summary>
    /// Replaces every item with the provided ones, raising a single <see cref="NotifyCollectionChangedAction.Reset"/>
    /// notification (one layout pass) instead of one notification per item.
    /// </summary>
    /// <param name="items">The new content; <c>null</c> clears the collection.</param>
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();

        if (items != null)
        {
            foreach (T item in items)
            {
                Items.Add(item);
            }
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
