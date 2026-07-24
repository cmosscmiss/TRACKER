using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model responsible for managing the image type selection widget.
///
/// This view model keeps the list of available image types synchronized with
/// the currently selected platform, the active image type filters, and the
/// globally selected image set stored in <see cref="SharedDataService"/>.
///
/// Los botones de acceso rápido a tipos favoritos están integrados en
/// <see cref="MM4LB.Controls.Views.ImageTypeControl"/> (modo Full).
/// </summary>
public class ImageTypeViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly ImageMatchingService _imageMatchingService;
    private readonly ExceptionService _exceptionService;
    private RelayCommand? _filtersChangedCommand;
    private bool _filtersEnabled;
    #endregion

    #region Properties
    /// <summary>
    /// Gets the active image type filters used by this view model.
    ///
    /// The filters are applied to the selected platform image sets when
    /// <see cref="FiltersEnabled"/> is set to <c>true</c>.
    /// </summary>
    public Filters ActiveFilters { get; } = new();

    /// <summary>
    /// Gets or sets whether image type filtering is enabled.
    ///
    /// When the value changes, the filtered image type collection is refreshed
    /// immediately so the UI and the shared selected image set remain consistent
    /// with the current filtering state.
    /// </summary>
    public bool FiltersEnabled
    {
        get => _filtersEnabled;
        set
        {
            if (SetProperty(ref _filtersEnabled, value))
            {
                OnFiltersChanged();
            }
        }
    }
    #endregion

    #region Commands
    /// <summary>
    /// Gets the command executed when the active filters change.
    ///
    /// The command refreshes the filtered image type collection and preserves
    /// the current selection when possible.
    /// </summary>
    public RelayCommand FiltersChangedCommand => _filtersChangedCommand ??= new RelayCommand(OnFiltersChanged);

    /// <summary>
    /// Handles changes in the active image type filters.
    ///
    /// The method refreshes the filtered image type collection and then attempts
    /// to preserve the current image set selection when it is still available
    /// after filtering. If the previous selection is no longer present, the first
    /// available filtered image set becomes the new shared selection.
    /// </summary>
    private void OnFiltersChanged()
    {
        var current = _sharedDataService.SelectedImageSet;

        SetImageTypes();

        if (current != null && SharedDataService.ImageTypesFiltered.Contains(current))
        {
            _sharedDataService.SelectedImageSet = current;
        }
        else
        {
            _sharedDataService.SelectedImageSet = SharedDataService.ImageTypesFiltered.FirstOrDefault();
        }
    }
    #endregion

    #region Constructors
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageTypeViewModel"/> class.
    ///
    /// The constructor stores the required services, subscribes to shared data
    /// events, requests the initial shared state, and initializes the widget slot
    /// used by the dashboard layout.
    /// </summary>
    public ImageTypeViewModel(SharedDataService sharedDataService, ImageMatchingService imageMatchingService, ExceptionService exceptionService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _imageMatchingService = imageMatchingService;
        _exceptionService = exceptionService;

        _sharedDataService.SelectedPlatformChanged += OnSelectedPlatformChanged;
        _sharedDataService.SelectedImageSetChanged += OnSelectedImageSetChangedHandler;

        _sharedDataService.NotifyInitialState();

        SlotIndex = 0;
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Bridges the synchronous shared data event with the asynchronous image set
    /// change workflow.
    /// </summary>
    private async void OnSelectedImageSetChangedHandler(object? s, ImageSetChangedEventArgs e)
    {
        try
        {
            await OnSelectedImageSetChanged(s, e);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ImageType_LoadImages_Error] ?? "Error loading images for the selected image type.");
        }
    }

    /// <summary>
    /// Handles changes to the globally selected image set: reloads the image matching information for the
    /// selected platform and notifies the shared data service that the selected game images have changed.
    /// </summary>
    private async Task OnSelectedImageSetChanged(object? sender, ImageSetChangedEventArgs e)
    {
        if (_sharedDataService.SelectedPlatform == null)
        {
            return;
        }

        await _imageMatchingService.MatchImagesWithGamesAsync(_sharedDataService.SelectedPlatform);

        _sharedDataService.NotifySelectedGameImagesChanged();
    }

    /// <summary>
    /// Handles changes to the globally selected platform: refreshes the available image types for the new
    /// platform and restores the previously selected image type by key, falling back to the first available.
    /// </summary>
    private void OnSelectedPlatformChanged(object? sender, PlatformChangedEventArgs e)
    {
        var previousTypeKey = _sharedDataService.SelectedImageSet?.Type.Key;

        SetImageTypes();

        _sharedDataService.SelectedImageSet = _sharedDataService.ImageTypesFiltered.FirstOrDefault(s => s.Type.Key == previousTypeKey) ?? _sharedDataService.ImageTypesFiltered.FirstOrDefault();
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Releases the resources used by the view model by unsubscribing from the shared data service events.
    /// </summary>
    public override void Dispose()
    {
        _sharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged;
        _sharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChangedHandler;
    }

    /// <summary>
    /// Loads persisted configuration for this widget. Intentionally empty: this view model does not restore
    /// configuration beyond the shared state and application settings provided during initialization.
    /// </summary>
    public override void LoadConfig()
    {
    }

    /// <summary>
    /// Saves the current widget configuration into the application settings (the currently selected image set type).
    /// </summary>
    public override void SaveConfig()
    {
        _appSettings.ImageTypeControl.SelectedImageSet = _sharedDataService.SelectedImageSet?.Type;
    }

    /// <summary>
    /// Refreshes the globally available filtered image type collection: starts from the image sets of the
    /// currently selected platform, applies the active filters when filtering is enabled, and updates the shared
    /// filtered image type collection (the favourite buttons react to that collection from their own control).
    /// </summary>
    public void SetImageTypes()
    {
        Platform? platform = SharedDataService.SelectedPlatform;
        if (platform == null) { return; }

        var source = platform.Images.ImageSets.AsEnumerable();

        if (FiltersEnabled)
        {
            var favouriteKeys = (_appSettings.ImageTypeControl.FavouriteImageTypes ?? Array.Empty<MediaType>())
                .Select(t => t.Key).ToHashSet();
            source = ActiveFilters.ApplyImageTypeFilters(source, favouriteKeys);
        }

        SharedDataService.ImageTypesFiltered.Clear();

        foreach (var set in source)
            SharedDataService.ImageTypesFiltered.Add(set);
    }
    #endregion
}
