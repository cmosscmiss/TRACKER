using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Models;
using MM4LB.Contracts.Services;
using MM4LB.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace MM4LB.Controls.ViewModels;

public class GamesAuditViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly IStatisticsService _statisticsService;

    protected RelayCommand? _filterGamesCommand;

    protected string _columnLastOrderedBy;
    protected DataGridSortDirection? _columnLastOrderedDirection;
    protected bool _isGamesAuditView;
    protected bool _pendingRefresh;
    protected ObservableCollection<Game> _gamesCollectionFiltered = new();
    protected Game? _selectedGame;
    protected GameAuditStats? _statisticsGames;
    #endregion


    #region Properties (Observable)
    /// <summary>
    /// The source collection of games (including the ones from the LB database). These are different instances than the actual games in the collection.
    /// </summary>
    public List<Game> GamesCollection { get; protected set; } = new();

    /// <summary>
    /// The collection of games filtered.
    /// The whole collection instance is replaced on every refresh (instead of Clear()+Add per row)
    /// so the DataGrid only reacts once, even with thousands of games.
    /// </summary>
    public ObservableCollection<Game> GamesCollectionFiltered
    {
        get => _gamesCollectionFiltered;
        protected set => SetProperty(ref _gamesCollectionFiltered, value);
    }

    /// <summary>
    /// Whether the component is behaving as games audit view or is used within an ImageGridControl.
    /// </summary>
    public bool IsGamesAuditView
    {
        get => _isGamesAuditView;
        set => SetProperty(ref _isGamesAuditView, value);
    }

    /// <summary>
    /// Selected game in the collection (it is not the same as the one in the SharedDataService).
    /// </summary>
    public Game? SelectedGame
    {
        get => _selectedGame;
        set => SetProperty(ref _selectedGame, value);
    }

    /// <summary>
    /// Statistics of the games of the collection.
    /// </summary>
    public GameAuditStats? StatisticsGames
    {
        get => _statisticsGames;
        set => SetProperty(ref _statisticsGames, value);
    }
    #endregion


    #region Properties
    /// <summary>
    /// Active filters. Holds both the game-state filters (collection / LB database) used in the
    /// audit view and the image-count filters (Missing / 1 / >1) used in the image-match view.
    /// </summary>
    public Filters ActiveCountFilters { get; protected set; } = new();
    #endregion


    #region Published events
    /// <summary>
    /// Delegate event handler when the selected game changes.
    /// </summary>
    public delegate void GameSelectionChangedEventHandler(Game game);
    public event GameSelectionChangedEventHandler? GameSelectionChanged;
    protected virtual void OnGameSelectionChanged(Game game) => GameSelectionChanged?.Invoke(game);
    #endregion


    #region Commands
    /// <summary>
    /// Filtering the games.
    /// </summary>
    public RelayCommand FilterGamesCommand => _filterGamesCommand ??= new RelayCommand(OnFilterGames);

    protected virtual void OnFilterGames()
    {
        Game? selectedGame = SelectedGame;
        SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection);
        SelectedGame = selectedGame;
    }
    #endregion


    #region Constructors
    public GamesAuditViewModel(SharedDataService sharedDataService, IStatisticsService statisticsService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _statisticsService = statisticsService;
        _columnLastOrderedBy = "FileName";
        IsGamesAuditView = true;
        // Subscribing to events
        SharedDataService.PropertyChanged += SharedDataService_PropertyChanged;
        PropertyChanged += GamesAuditViewModel_PropertyChanged;

        // A platform may already be selected by the time the widget is created (typically on startup),
        // in which case no SelectedPlatform notification will arrive. Stage the games now so they show
        // as soon as the widget becomes active (SlotIndex >= 0).
        if (SharedDataService.SelectedPlatform != null)
        {
            SetGames(SharedDataService.SelectedPlatform.GamesInLauchboxDb);
        }
    }
    #endregion


    #region Subscribed events
    /// <summary>
    /// The selected game changes.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void GaControl_OnGameSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.Count > 0 && e.AddedItems.First() is Game game)
            {
                (sender as DataGrid)?.ScrollIntoView(game, null);

                // The audit list holds copies, not the collection instances. When the selected game is
                // in the user's collection, map it back to the real instance so the rest of the app
                // (image gallery, web view, etc.) reacts through SharedDataService.SelectedGame.
                if (game.InCollection)
                {
                    Game? collectionGame = SharedDataService.SelectedPlatform?.Games.Find(x => x.Equals(game));
                    if (collectionGame != null) { SharedDataService.SelectedGame = collectionGame; }
                }

                OnGameSelectionChanged(game);
                // The audit counts depend only on the platform's audit set, not on which game is selected,
                // so there is nothing to recompute here (StatisticsGames is set on platform change).
            }
        }
        catch
        {
            // Sometimes this function raises an exception when opening the games view for the first time.
        }
    }

    /// <summary>
    /// Reacts to the widget becoming active. While the widget is not assigned to a slot
    /// (SlotIndex &lt; 0) the games are never pushed to the DataGrid; the refresh is deferred until the
    /// widget becomes visible. Building the bound collection for thousands of games is expensive, so it
    /// only happens for an active widget.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void GamesAuditViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SlotIndex)) { return; }
        if (SlotIndex >= 0 && _pendingRefresh) { RefreshGames(); }
    }

    /// <summary>
    /// The SharedDataService selected game or platform changes. 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    protected virtual void SharedDataService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "SelectedGame":
                if (SharedDataService.SelectedGame != null) { SelectedGame = GamesCollection.Find(x => x.Equals(SharedDataService.SelectedGame)); }
                break;
            case "SelectedPlatform":
                SetGames(SharedDataService.SelectedPlatform?.GamesInLauchboxDb);
                break;
        }
    }
    #endregion


    #region Methods (private)
    /// <summary>
    /// Calculates the statistics related to the games of the selected platform.
    /// </summary>
    /// <returns>A Stats instance</returns>
    // Las estadísticas se calculan solo con una plataforma seleccionada (siempre la hay tras el arranque:
    // LaunchBoxService lanza si no existe ninguna), de ahí el '!'.
    protected GameAuditStats GetGameStatistics() => _statisticsService.GetGameCollectionStatistics(SharedDataService.SelectedPlatform!);

    /// <summary>
    /// Filters the collection of games based on the active filters.
    /// </summary>
    /// <param name="sourceList"></param>
    protected void SetCollection(List<Game> sourceList)
    {
        // Apply the game-state filters in both views; the image-count filters only apply to the
        // image-match view (the second DataGrid / CommandBar).
        IEnumerable<Game> filtered = ActiveCountFilters.ApplyGlobalFilters(sourceList);
        if (!IsGamesAuditView)
        {
            filtered = ActiveCountFilters.ApplyImageFilters(filtered);
        }

        // Replace the whole collection in one shot. Populating an ObservableCollection bound to the
        // DataGrid with a Clear()+Add loop raises one CollectionChanged per row, which freezes the UI
        // for thousands of games; a single assignment makes the DataGrid rebind just once.
        GamesCollectionFiltered = new ObservableCollection<Game>(filtered);
    }
    #endregion


    #region Methods public
    /// <summary>
    /// Sets the collection of games for the control (called normally when changing platform).
    /// </summary>
    /// <param name="games"></param>
    public virtual void SetGames(List<Game>? games)
    {
        GamesCollection.Clear();
        if (games != null) { GamesCollection.AddRange(games); }
        // GamesCollection is a plain List, so notify explicitly to refresh the "Showing X of Y" total.
        OnPropertyChanged(nameof(GamesCollection));
        StatisticsGames = GetGameStatistics();

        // Push to the DataGrid only when the widget is active; otherwise defer until SlotIndex >= 0
        // (see GamesAuditViewModel_PropertyChanged).
        _pendingRefresh = true;
        if (SlotIndex >= 0) { RefreshGames(); }
    }

    /// <summary>
    /// Pushes the staged games to the DataGrid using the current sort/filter state.
    /// </summary>
    protected void RefreshGames()
    {
        _pendingRefresh = false;
        SortCollection(_columnLastOrderedBy, _columnLastOrderedDirection);
    }

    /// <summary>
    /// Sorts the collection based on the column and order selected.
    /// </summary>
    /// <param name="columnTag"></param>
    /// <param name="sortDirection"></param>
    public void SortCollection(string columnTag, DataGridSortDirection? sortDirection)
    {
        List<Game> data = new();
        _columnLastOrderedBy = columnTag; _columnLastOrderedDirection = sortDirection;
        if (sortDirection != null)
        {
            if (columnTag == "Title")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<Game>(from item in GamesCollection
                                     orderby item.Title ascending
                                     select item)
                    : new List<Game>(from item in GamesCollection
                                     orderby item.Title descending
                                     select item);
            }
            if (columnTag == "DatabaseId")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<Game>(from item in GamesCollection
                                     orderby item.DatabaseId ascending
                                     select item)
                    : new List<Game>(from item in GamesCollection
                                     orderby item.DatabaseId descending
                                     select item);
            }
            if (columnTag == "RomFileName")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<Game>(from item in GamesCollection
                                     orderby item.RomFileName ascending
                                     select item)
                    : new List<Game>(from item in GamesCollection
                                     orderby item.RomFileName descending
                                     select item);
            }
            if (columnTag == "Version")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<Game>(from item in GamesCollection
                                     orderby item.Version ascending
                                     select item)
                    : new List<Game>(from item in GamesCollection
                                     orderby item.Version descending
                                     select item);
            }
            if (columnTag == "InCollection")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<Game>(from item in GamesCollection
                                     orderby item.InCollection ascending
                                     select item)
                    : new List<Game>(from item in GamesCollection
                                     orderby item.InCollection descending
                                     select item);
            }
            if (columnTag == "MatchedImages")
            {
                data = sortDirection == DataGridSortDirection.Ascending
                    ? new List<Game>(from item in GamesCollection
                                     orderby item.Images.Count ascending
                                     select item)
                    : new List<Game>(from item in GamesCollection
                                     orderby item.Images.Count descending
                                     select item);
            }
            SetCollection(data);
        }
        else
        {
            SetCollection(GamesCollection);
        }
    }

    /// <summary>
    /// Restores the persisted active game-state filters. Only applies to the games audit view; the
    /// in-gallery (image-match) instance keeps its own state. Called by the control once the application
    /// settings have been restored from disk.
    /// </summary>
    public override void LoadConfig()
    {
        if (!IsGamesAuditView) { return; }

        AppSettings.GamesAuditControlSettings config = _appSettings.GamesAuditControl;
        if (config == null) { return; }

        ActiveCountFilters.Game.InCollection = config.InCollection;
        ActiveCountFilters.Game.InLaunchboxDb = config.InLaunchboxDb;
        ActiveCountFilters.Game.InCollectionNotInLaunchboxDb = config.InCollectionNotInLaunchboxDb;

        OnFilterGames();
    }

    /// <summary>
    /// Saves the active game-state filters into the application settings.
    /// </summary>
    public override void SaveConfig()
    {
        if (!IsGamesAuditView) { return; }

        AppSettings.GamesAuditControlSettings config = _appSettings.GamesAuditControl;

        config.InCollection = ActiveCountFilters.Game.InCollection;
        config.InLaunchboxDb = ActiveCountFilters.Game.InLaunchboxDb;
        config.InCollectionNotInLaunchboxDb = ActiveCountFilters.Game.InCollectionNotInLaunchboxDb;
    }

    /// <summary>
    /// Releases resources associated with this view model.
    /// 
    /// The base implementation currently does not dispose managed resources.
    /// Derived classes can override this method to detach event handlers or release
    /// additional resources.
    /// </summary>
    public override void Dispose()
    {
        SharedDataService.PropertyChanged -= SharedDataService_PropertyChanged;
        PropertyChanged -= GamesAuditViewModel_PropertyChanged;
    }
    #endregion
}
