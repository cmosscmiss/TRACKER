using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model for the search strings control.
/// 
/// This view model is responsible for preparing the search strings displayed by
/// <see cref="SearchStringsControl"/>. It can display either the search strings
/// associated with the selected game or the search strings associated with the
/// selected game image, depending on the configured <see cref="Source"/>.
/// 
/// No fallback is applied between sources: when the selected source has no search
/// strings, the control exposes an empty state instead of displaying search strings
/// from another object.
/// </summary>
public class SearchStringsViewModel : WidgetViewModelBase
{
    #region Nested classes
    /// <summary>
    /// Defines the available sources from which search strings can be displayed.
    /// </summary>
    public enum SearchStringsSource
    {
        Game,
        GameImage
    }

    /// <summary>
    /// Lightweight display model used by the control to render a search string.
    /// 
    /// The item normalizes the different source structures into a common shape that
    /// can be consumed by the XAML view.
    /// </summary>
    public class SearchStringDisplayItem
    {
        public string Name { get; set; } = string.Empty;

        public bool IsMatched { get; set; }
    }
    #endregion

    #region Attributes
    private SearchStringsSource _source = SearchStringsSource.Game;
    private string _title = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.SearchStrings_DefaultTitle] ?? "Search strings:";
    private bool _hasSearchStrings;
    private bool _disposed;

    private INotifyCollectionChanged? _observedSearchStringsCollection;
    #endregion

    #region Properties
    /// <summary>
    /// Determines whether the control displays search strings from the selected game
    /// or from the selected game image.
    /// </summary>
    public SearchStringsSource Source
    {
        get => _source;
        set
        {
            if (SetProperty(ref _source, value))
                Refresh();
        }
    }

    /// <summary>
    /// Title displayed above the list.
    /// </summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>
    /// Indicates whether the selected source has search strings to display.
    /// </summary>
    public bool HasSearchStrings
    {
        get => _hasSearchStrings;
        private set => SetProperty(ref _hasSearchStrings, value);
    }

    /// <summary>
    /// Message displayed when the selected source has no search strings.
    /// </summary>
    public string EmptyMessage => MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.SearchStrings_Empty] ?? "No search strings.";

    /// <summary>
    /// Normalized list rendered by the control.
    /// </summary>
    public ObservableCollection<SearchStringDisplayItem> SearchStrings { get; } = new();
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchStringsViewModel"/> class.
    /// 
    /// The view model subscribes to shared data changes so the visible search strings
    /// can be refreshed whenever the selected game or selected image changes.
    /// </summary>
    /// <param name="sharedDataService">
    /// Shared application data service containing the currently selected game and image.
    /// </param>
    /// <param name="appSettings">
    /// Application settings injected through the options pattern.
    /// </param>
    public SearchStringsViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _sharedDataService.PropertyChanged += SharedDataService_PropertyChanged;
        Refresh();
    }
    #endregion

    #region Methods public
    /// <summary>
    /// Releases resources owned by the view model.
    /// 
    /// This method detaches all event handlers and clears the displayed collection.
    /// It is idempotent, so calling it multiple times does not attempt to unsubscribe
    /// more than once.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        DetachObservedSearchStringsCollection();

        _sharedDataService.PropertyChanged -= SharedDataService_PropertyChanged;

        SearchStrings.Clear();

        _disposed = true;
    }

    /// <summary>
    /// Loads persisted configuration for the search strings control.
    /// 
    /// The control currently does not expose persisted configuration, so no values
    /// need to be loaded.
    /// </summary>
    public override void LoadConfig()
    {
    }

    /// <summary>
    /// Saves configuration for the search strings control.
    /// 
    /// The control currently does not expose persisted configuration, so no values
    /// need to be saved.
    /// </summary>
    public override void SaveConfig()
    {
    }
    #endregion

    #region Methods private
    /// <summary>
    /// Rebuilds the visible search string list according to the selected source.
    /// 
    /// The method clears the current display collection, detaches any previously
    /// observed source collection, reloads data from the configured source, and updates
    /// the empty-state flag.
    /// </summary>
    private void Refresh()
    {
        DetachObservedSearchStringsCollection();

        SearchStrings.Clear();
        switch (Source)
        {
            case SearchStringsSource.Game:
                RefreshFromSelectedGame();
                break;
            case SearchStringsSource.GameImage:
                RefreshFromSelectedGameImage();
                break;
        }

        HasSearchStrings = SearchStrings.Count > 0;
    }

    /// <summary>
    /// Loads only the search strings from the selected game.
    /// 
    /// No fallback is applied. If the selected game has no search strings, the
    /// resulting display list remains empty.
    /// </summary>
    private void RefreshFromSelectedGame()
    {
        Title = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.SearchStrings_GameTitle] ?? "Game search strings";

        var searchStrings = _sharedDataService.SelectedGame?.SearchStrings;

        if (searchStrings == null)
            return;

        AttachObservedSearchStringsCollection(searchStrings as INotifyCollectionChanged);

        foreach (var searchString in searchStrings)
        {
            SearchStrings.Add(new SearchStringDisplayItem
            {
                Name = searchString,
                IsMatched = false
            });
        }
    }

    /// <summary>
    /// Loads only the search strings from the selected game image.
    /// 
    /// No fallback is applied. If the selected image has no search strings, the
    /// resulting display list remains empty.
    /// </summary>
    private void RefreshFromSelectedGameImage()
    {
        Title = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.SearchStrings_GameImageTitle] ?? "Game image search strings";

        var searchStrings = _sharedDataService.SelectedImage?.SearchStrings;

        if (searchStrings == null)
            return;

        AttachObservedSearchStringsCollection(searchStrings as INotifyCollectionChanged);

        foreach (var searchString in searchStrings)
        {
            SearchStrings.Add(new SearchStringDisplayItem
            {
                Name = searchString.Name,
                IsMatched = searchString.IsChecked
            });
        }
    }

    /// <summary>
    /// Handles changes in the shared data service.
    /// 
    /// The visible search strings are refreshed when the selected game or selected
    /// image changes.
    /// </summary>
    /// <param name="sender">
    /// Object that raised the event.
    /// </param>
    /// <param name="e">
    /// Event data containing the name of the changed property.
    /// </param>
    private void SharedDataService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.SelectedGame) ||
            e.PropertyName == nameof(SharedDataService.SelectedImage))
        {
            Refresh();
        }
    }

    /// <summary>
    /// Subscribes to collection changes in the currently displayed search string source.
    /// 
    /// This allows the control to refresh automatically when the underlying search
    /// string collection changes without requiring a selected object change.
    /// </summary>
    /// <param name="collection">
    /// Source collection to observe, when it supports collection change notifications.
    /// </param>
    private void AttachObservedSearchStringsCollection(INotifyCollectionChanged? collection)
    {
        if (collection == null)
            return;

        _observedSearchStringsCollection = collection;
        _observedSearchStringsCollection.CollectionChanged += SearchStringsCollection_CollectionChanged;
    }

    /// <summary>
    /// Unsubscribes from the currently observed search string collection.
    /// 
    /// This prevents duplicated subscriptions when the source is refreshed and avoids
    /// keeping old collections alive unnecessarily.
    /// </summary>
    private void DetachObservedSearchStringsCollection()
    {
        if (_observedSearchStringsCollection == null)
            return;

        _observedSearchStringsCollection.CollectionChanged -= SearchStringsCollection_CollectionChanged;
        _observedSearchStringsCollection = null;
    }

    /// <summary>
    /// Handles changes in the observed search string collection.
    /// 
    /// Whenever the source collection changes, the normalized display list is rebuilt
    /// from the currently selected source.
    /// </summary>
    /// <param name="sender">
    /// Collection that raised the event.
    /// </param>
    /// <param name="e">
    /// Event data describing the collection change.
    /// </param>
    private void SearchStringsCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Refresh();
    }
    #endregion
}