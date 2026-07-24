using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.Views;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Base view model for the ImageGrid control.
/// 
/// This class provides the common behavior required by image grid variants,
/// including image collection management, selected image handling, aspect ratio
/// and resolution selection, drag support, image loading commands, and integration
/// with the shared application data.
/// 
/// Derived classes can specialize the behavior for specific gallery modes,
/// such as game image galleries or folder-based image galleries.
/// </summary>
public class ImageGridViewModel : WidgetViewModelBase
{
    #region Attributes
    protected readonly ProgressService _progressService;
    protected readonly ImageLoadingService _imageLoadingService;
    protected readonly ImageBinaryLoadingService _imageBinaryLoadingService;
    protected readonly DialogsService _dialogsService;
    protected readonly WindowService _windowService;
    private AsyncRelayCommand? _deleteSelectedImageCommand;

    private bool _canDeleteImages;

    private int _height;
    private int _width;

    // Re-entrancy guard for ForceGridItemSizeRefreshAsync. The refresh temporarily mutates Width and restores
    // it after an await; on the first audit load several gallery refreshes overlap on the UI thread, so a
    // nested call must not capture the (already temporary) Width as the value to restore. The baseline is
    // captured only at the outermost entry and every call restores to it.
    private int _itemSizeRefreshDepth;
    private int _itemSizeRefreshBaseline;

    protected bool _isLoadingInProgress;
    protected GameImage? _selectedImage;

    // Game/platform comparison pills, only filled by the game-images gallery (ImageGridGameViewModel).
    // They are declared on the base because ImageGridControl exposes its ViewModel typed as this base
    // class, and the pill bindings use x:Bind (resolved against the control, not by namescope) so that
    // they keep working once the cards are re-parented into the WidgetStatCardControl content host. Other
    // gallery variants leave ImagePills null (x:Bind cuts the path) and keep the coverage default.
    private ImagePills? _imagePills;
    private string _coverageText = "0% / 0%";

    /// <summary>
    /// Images whose binary is currently being decoded on demand. Used to avoid issuing duplicate
    /// load requests while a container is realized/recycled several times during fast scrolling.
    /// </summary>
    private readonly HashSet<GameImage> _binariesBeingLoaded = new();
    #endregion

    #region Interface
    /// <summary>
    /// Indicates whether the gallery is being used as a folder image view.
    /// </summary>
    public virtual bool IsFolderView { get; set; }

    /// <summary>
    /// Gets the currently selected folder when the gallery is used in folder mode.
    /// </summary>
    public virtual string? SelectedFolder { get; protected set; }

    /// <summary>
    /// Command used by derived folder-based view models to import images.
    /// </summary>
    public virtual RelayCommand? ImportImagesCommand { get; protected set; }

    /// <summary>
    /// Command used by derived folder-based view models to match images.
    /// </summary>
    public virtual RelayCommand? MatchImagesCommand { get; protected set; }

    /// <summary>
    /// Command used by derived folder-based view models to select a folder.
    /// </summary>
    public virtual RelayCommand? SelectFolderCommand { get; protected set; }

    /// <summary>
    /// Indicates whether the gallery is being used as a game images view.
    /// </summary>
    public virtual bool IsGameImagesView { get; protected set; }

    /// <summary>
    /// When enabled, the gallery decodes each image binary lazily, as its container scrolls into view.
    /// Every gallery variant opts in: binaries are never bulk-loaded up front, only the images the user
    /// actually scrolls to are decoded.
    /// </summary>
    public bool LazyLoadBinariesOnScroll { get; set; }
    #endregion

    #region Properties Observable
    /// <summary>
    /// Gets or sets the height, in pixels, of each item displayed in the gallery.
    /// </summary>
    public int Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    /// <summary>
    /// Gets the collection of images currently displayed in the gallery.
    /// </summary>
    public ObservableRangeCollection<GameImage> Images { get; } = new();

    /// <summary>
    /// Gets or sets the currently selected image in the gallery.
    /// </summary>
    public GameImage? SelectedImage
    {
        get => _selectedImage;
        set
        {
            if (SetProperty(ref _selectedImage, value))
            {
                _deleteSelectedImageCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Indica si esta galería permite borrar el medio seleccionado (habilita el comando y el botón de borrar de
    /// la barra). Por defecto false; la activan la galería del juego y el audit. El import la deja en false.
    /// </summary>
    public bool CanDeleteImages
    {
        get => _canDeleteImages;
        set
        {
            if (SetProperty(ref _canDeleteImages, value))
            {
                _deleteSelectedImageCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the width, in pixels, of each item displayed in the gallery.
    /// 
    /// When the width changes, the height is recalculated using the currently selected
    /// aspect ratio.
    /// </summary>
    public int Width
    {
        get => _width;
        set
        {
            // Guard against out-of-range writes. The size Slider binds its Value TwoWay to this property, and
            // during the control's start-up race (the view model can be null when the x:Bind expressions are
            // first evaluated) the Slider keeps its default Value (0) and writes it back here when it lays out,
            // collapsing the thumbnail size — a 0 that then gets persisted via SaveConfig (config.ItemSize =
            // Width) and reloaded on the next run. Clamping to the slider range neutralises any such stray
            // value regardless of binding/coercion ordering.
            int clamped = Math.Clamp(value, MinimumSize, MaximumSize);

            SetProperty(ref _width, clamped);
            Height = Convert.ToInt32(clamped * SelectedAspectRatio.Value);
        }
    }

    /// <summary>
    /// Game-vs-platform image pills (images / image types / size) produced by the service: each carries its
    /// label, description and formatted value. Only the game-images gallery fills it; other variants leave it null.
    /// </summary>
    public ImagePills? ImagePills
    {
        get => _imagePills;
        protected set => SetProperty(ref _imagePills, value);
    }

    /// <summary>Pill "Coverage": favourite coverage of the game / platform average ("X% / Y%").</summary>
    public string CoverageText
    {
        get => _coverageText;
        protected set => SetProperty(ref _coverageText, value);
    }
    #endregion

    #region Properties
    /// <summary>
    /// Gets the list of aspect ratio settings available in the gallery toolbar.
    /// </summary>
    public List<SelectableOption> AspectRatioSettings { get; protected set; }

    /// <summary>
    /// Gets the list of image resolution settings available in the gallery toolbar.
    /// </summary>
    public List<SelectableOption> ImageResolutionSettings { get; protected set; }

    /// <summary>
    /// Gets or sets the maximum image item size allowed by the gallery size slider.
    /// </summary>
    public int MaximumSize { get; set; }

    /// <summary>
    /// Gets or sets the minimum image item size allowed by the gallery size slider.
    /// </summary>
    public int MinimumSize { get; set; }

    /// <summary>
    /// Gets the currently selected aspect ratio setting.
    /// </summary>
    public SelectableOption SelectedAspectRatio { get; protected set; }

    /// <summary>
    /// Gets the currently selected image resolution setting.
    /// </summary>
    public SelectableOption SelectedImageResolution { get; protected set; }

    /// <summary>Opciones de aspect ratio para el <c>ExclusiveOptionsControl</c> (Label = Value = Name).</summary>
    public List<ExclusiveOption> AspectRatioOptions { get; }

    /// <summary>Opciones de resolución para el <c>ExclusiveOptionsControl</c> (Label = Value = Name).</summary>
    public List<ExclusiveOption> ImageResolutionOptions { get; }

    /// <summary>Aspect ratio activo como cadena (su <see cref="SelectableOption.Name"/>), TwoWay con el control.</summary>
    public string SelectedAspectRatioValue
    {
        get => SelectedAspectRatio.Name;
        set
        {
            if (value == SelectedAspectRatio.Name) { return; }
            ApplyAspectRatio(value);
        }
    }

    /// <summary>Resolución activa como cadena (su <see cref="SelectableOption.Name"/>), TwoWay con el control.</summary>
    public string SelectedImageResolutionValue
    {
        get => SelectedImageResolution.Name;
        set
        {
            if (value == SelectedImageResolution.Name) { return; }
            ApplyResolution(value);
            InvalidateDecodedBinaries();
        }
    }
    #endregion

    #region Published events
    /// <summary>
    /// Delegate used when the associated control is closed.
    /// </summary>
    public delegate void ControlClosedEventHandler();

    /// <summary>
    /// Event raised when the associated control is closed.
    /// </summary>
    public event ControlClosedEventHandler? ControlClosed;

    /// <summary>
    /// Raises the <see cref="ControlClosed"/> event.
    /// </summary>
    protected virtual void OnControlClosed() => ControlClosed?.Invoke();

    /// <summary>
    /// Delegate used when the selected image changes.
    /// </summary>
    /// <param name="image">The newly selected image.</param>
    public delegate void ImageSelectionChangedEventHandler(GameImage image);

    /// <summary>
    /// Event raised when the selected image changes.
    /// </summary>
    public event ImageSelectionChangedEventHandler? ImageSelectionChanged;

    /// <summary>
    /// Raises the <see cref="ImageSelectionChanged"/> event.
    /// </summary>
    /// <param name="image">The newly selected image.</param>
    protected virtual void OnImageSelectionChanged(GameImage image) => ImageSelectionChanged?.Invoke(image);

    /// <summary>
    /// Delegate used when an image binary is decoded on demand.
    /// </summary>
    /// <param name="image">The image whose binary (and, with it, its dimensions) was just loaded.</param>
    public delegate void ImageBinaryLoadedEventHandler(GameImage image);

    /// <summary>
    /// Event raised after an image binary is decoded on demand while lazily loading the gallery
    /// (see <see cref="LoadImageBinaryOnDemandAsync"/>). Because decoding the binary also fills the
    /// image dimensions, consumers can use this to refresh dimension-based UI/statistics as the
    /// gallery scrolls binaries into view.
    /// </summary>
    public event ImageBinaryLoadedEventHandler? ImageBinaryLoaded;

    /// <summary>
    /// Raises the <see cref="ImageBinaryLoaded"/> event.
    /// </summary>
    /// <param name="image">The image whose binary was just loaded.</param>
    protected virtual void OnImageBinaryLoaded(GameImage image) => ImageBinaryLoaded?.Invoke(image);

    /// <summary>
    /// Event raised after a resolution change has released every decoded binary (see
    /// <see cref="InvalidateDecodedBinaries"/>). The view handles it by re-decoding only the items that are
    /// currently realized (on screen) at the new resolution; off-screen images reload lazily on scroll.
    /// It lives on the view model because deciding which items are realized is view state.
    /// </summary>
    public event Action? BinariesInvalidated;
    #endregion

    #region Commands
    /// <summary>
    /// Gets the command that deletes the currently selected media (with confirmation and undo). Only enabled when
    /// <see cref="CanDeleteImages"/> is set and there is a selection.
    /// </summary>
    public AsyncRelayCommand DeleteSelectedImageCommand =>
        _deleteSelectedImageCommand ??= new AsyncRelayCommand(DeleteSelectedImageAsync, CanDeleteSelectedImage);

    /// <summary>True when the delete command can run: the gallery allows deletion and a media is selected.</summary>
    private bool CanDeleteSelectedImage() => CanDeleteImages && SelectedImage != null;

    /// <summary>
    /// Confirms (honoring the PromptBeforeDeleteImage setting) and deletes the selected media from disk via
    /// <see cref="ImageLoadingService.DeleteImageAsync"/>. The deletion is undoable from the activity log and the
    /// galleries refresh through the service events.
    /// </summary>
    protected virtual async Task DeleteSelectedImageAsync()
    {
        GameImage? image = SelectedImage;
        if (image is null)
        {
            return;
        }

        string message = $"\"{image.Name}{image.FileExtension}\" will be deleted from disk. You can undo this from the activity log. Do you want to continue?";
        bool confirmed = await _dialogsService.ConfirmImageDeletionAsync(_windowService.ActiveXamlRoot!, message);
        if (!confirmed)
        {
            return;
        }

        await _imageLoadingService.DeleteImageAsync(image);
    }
    #endregion

    #region Constructors
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageGridViewModel"/> class.
    /// 
    /// The constructor initializes the gallery settings, default item size, and
    /// subscribes to shared data changes.
    /// </summary>
    public ImageGridViewModel(SharedDataService sharedDataService, ProgressService progressService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, DialogsService dialogsService, WindowService windowService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _progressService = progressService;
        _imageLoadingService = imageLoadingService;
        _imageBinaryLoadingService = imageBinaryLoadingService;
        _dialogsService = dialogsService;
        _windowService = windowService;

        AspectRatioSettings = new();
        AspectRatioSettings.Add(new SelectableOption() { Name = Enums.AspectRatioSettings.AR11.Value, Value = 1, IsChecked = true });
        AspectRatioSettings.Add(new SelectableOption() { Name = Enums.AspectRatioSettings.AR916.Value, Value = 16 / (double)9, IsChecked = false });
        AspectRatioSettings.Add(new SelectableOption() { Name = Enums.AspectRatioSettings.AR34.Value, Value = 4 / (double)3, IsChecked = false });
        AspectRatioSettings.Add(new SelectableOption() { Name = Enums.AspectRatioSettings.AR169.Value, Value = 9 / (double)16, IsChecked = false });
        AspectRatioSettings.Add(new SelectableOption() { Name = Enums.AspectRatioSettings.AR43.Value, Value = 3 / (double)4, IsChecked = false });
        SelectedAspectRatio = AspectRatioSettings[0];

        ImageResolutionSettings = new();
        ImageResolutionSettings.Add(new SelectableOption() { Name = Enums.ImageResolutionSettings.Low.Value, Value = 1, IsChecked = false });
        ImageResolutionSettings.Add(new SelectableOption() { Name = Enums.ImageResolutionSettings.Medium.Value, Value = 2, IsChecked = false });
        ImageResolutionSettings.Add(new SelectableOption() { Name = Enums.ImageResolutionSettings.High.Value, Value = 3, IsChecked = true });
        SelectedImageResolution = ImageResolutionSettings[2];

        // Opciones equivalentes para el ExclusiveOptionsControl (mismo glyph que los AppBarToggleButton originales).
        AspectRatioOptions = AspectRatioSettings.Select(o => new ExclusiveOption { Label = o.Name, Value = o.Name, Glyph = "" }).ToList();
        ImageResolutionOptions = ImageResolutionSettings.Select(o => new ExclusiveOption { Label = o.Name, Value = o.Name, Glyph = "" }).ToList();

        MinimumSize = 200;
        MaximumSize = 500;
        Width = 250;
        Height = 250;

        SharedDataService.PropertyChanged += SharedDataService_PropertyChanged;
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles the start of a drag operation from the image gallery.
    /// 
    /// The method exports the selected image file paths as text and requests a copy
    /// drag operation.
    /// </summary>
    /// <param name="sender">The control that started the drag operation.</param>
    /// <param name="e">The drag operation event data.</param>
    public void IgControl_OnDragItemsStarting(object? sender, DragItemsStartingEventArgs e)
    {
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetText(string.Join(',', e.Items.Cast<GameImage>().Select(x => x.File)));
    }

    /// <summary>
    /// Handles selection changes in the image gallery.
    /// 
    /// The selected image is scrolled into view. In generic gallery mode, the method
    /// also raises the image selection changed event.
    /// </summary>
    /// <param name="sender">The gallery control that raised the selection event.</param>
    /// <param name="e">The selection change event data.</param>
    public virtual void IgControl_OnImageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems.First() is GameImage image)
        {
            (sender as GridView)?.ScrollIntoView(image);

            if (!IsFolderView && !IsGameImagesView)
            {
                OnImageSelectionChanged(image);
            }
        }
    }

    /// <summary>
    /// Handles changes in the shared application data.
    /// 
    /// The base implementation refreshes command availability whenever the shared
    /// selection context changes.
    /// </summary>
    /// <param name="sender">The shared data service that raised the event.</param>
    /// <param name="e">The property change event data.</param>
    protected virtual void SharedDataService_PropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        RaiseCanExecuteCommands(false);
    #endregion

    #region Methods
    /// <summary>
    /// Adds an image to the gallery collection.
    /// </summary>
    /// <param name="image">The image to add.</param>
    public void AddImage(GameImage image) => Images.Add(image);

    /// <summary>
    /// Inserts an image into the gallery collection at the given position.
    /// </summary>
    /// <param name="index">The position to insert the image at.</param>
    /// <param name="image">The image to insert.</param>
    public void InsertImage(int index, GameImage image) => Images.Insert(index, image);

    /// <summary>
    /// Removes an image from the gallery collection.
    /// </summary>
    /// <param name="image">The image to remove.</param>
    public void RemoveImage(GameImage image) => _ = Images.Remove(image);

    /// <summary>
    /// Replaces the current gallery content with the provided image collection.
    /// 
    /// After updating the collection, the method forces the gallery item size to refresh
    /// so that the visual containers apply the expected dimensions.
    /// </summary>
    /// <param name="images">The images to display in the gallery.</param>
    /// <returns>A task representing the asynchronous gallery update operation.</returns>
    public async Task SetGalleryAsync(IEnumerable<GameImage> images)
    {
        if (images == null)
        {
            Images.ReplaceAll(null!);
            SelectedImage = null;
            await ForceGridItemSizeRefreshAsync();
            return;
        }

        // Replace the whole gallery in a single Reset notification: adding thousands of items one by
        // one makes the bound GridView re-run layout on every add (O(N) layout passes), which becomes
        // pathological when the visual tree is heavy.
        Images.ReplaceAll(images);

        await ForceGridItemSizeRefreshAsync();
    }

    /// <summary>
    /// Forces the gallery item containers to refresh their size.
    /// 
    /// The method temporarily changes the item width and restores it after a short delay.
    /// This forces the UI layer to re-evaluate item dimensions after the gallery collection
    /// has changed.
    /// </summary>
    /// <returns>A task representing the asynchronous size refresh operation.</returns>
    private async Task ForceGridItemSizeRefreshAsync()
    {
        // Capture the real width only at the outermost call. Overlapping refreshes (common on the first audit
        // load) reuse this baseline, so a nested call never restores the temporary width left by an outer one,
        // which is what collapsed the gallery to the minimum size.
        if (_itemSizeRefreshDepth == 0)
        {
            _itemSizeRefreshBaseline = Width;
        }

        _itemSizeRefreshDepth++;

        try
        {
            // Use an in-range temporary that always differs from the baseline so the change is real (the Width
            // setter clamps to [MinimumSize, MaximumSize], so an out-of-range nudge could collapse to the
            // current value and skip the refresh).
            Width = _itemSizeRefreshBaseline == MinimumSize ? MaximumSize : MinimumSize;

            await Task.Delay(5);

            Width = _itemSizeRefreshBaseline;
        }
        finally
        {
            _itemSizeRefreshDepth--;
        }
    }

    /// <summary>
    /// Decodes the binary of a single image on demand, at the currently selected resolution.
    ///
    /// The gallery calls it as each item scrolls into view (see <see cref="LazyLoadBinariesOnScroll"/>)
    /// so only the images the user actually looks at are decoded. It no-ops when the image already has a binary or when a
    /// decode for it is already in flight, so it is safe to call repeatedly during fast scrolling.
    /// </summary>
    /// <param name="image">The image whose binary should be loaded.</param>
    /// <returns>A task representing the asynchronous decode operation.</returns>
    public async Task LoadImageBinaryOnDemandAsync(GameImage image)
    {
        if (image == null || image.HasBinary || !_binariesBeingLoaded.Add(image))
        {
            return;
        }

        try
        {
            await _imageBinaryLoadingService.LoadGameImageBinaryAsync(
                image, Enumeration.FromValue<ImageResolutionSettings>(SelectedImageResolution.Name) ?? Enums.ImageResolutionSettings.High);

            // Decoding the binary also filled the image dimensions; let consumers refresh any
            // dimension-based UI/statistics now that this image has them.
            OnImageBinaryLoaded(image);

            // Progreso lazy COMPARTIDO: una sola entrada por plataforma en el log, sumando todas las galerías.
            _progressService.ReportLazyImageLoaded(SharedDataService.SelectedPlatform?.Name ?? string.Empty);
        }
        catch
        {
            // A single corrupt/locked image failing to decode must not break scrolling or the gallery.
        }
        finally
        {
            _binariesBeingLoaded.Remove(image);
        }
    }

    /// <summary>
    /// Applies a resolution change. Every decoded binary is released — freed AND dropped from the cache, so
    /// the reported usage reflects only what stays in memory — and the view is asked, through
    /// <see cref="BinariesInvalidated"/>, to re-decode just the items currently realized (on screen) at the
    /// new resolution. Off-screen images are intentionally left without a binary: re-decoding the whole
    /// (potentially huge) cached working set on every resolution change would be wasteful, so they lazily
    /// reload at the new resolution when scrolled into view.
    ///
    /// The images of the currently selected game are skipped: they are the same shared <see cref="GameImage"/>
    /// instances the dashboard (<see cref="SharedDataService.GameImages"/>) displays, and it does not lazily
    /// reload, so releasing their binary here would blank it. They keep their current binary and the grid
    /// shows them as-is.
    /// </summary>
    private void InvalidateDecodedBinaries()
    {
        foreach (GameImage image in Images.Where(x => x.HasBinary).ToList())
        {
            if (SharedDataService.GameImages.Contains(image))
            {
                continue;
            }

            _imageBinaryLoadingService.ReleaseBinary(image);
        }

        BinariesInvalidated?.Invoke();
    }
    #endregion

    #region Methods private
    /// <summary>
    /// Updates the loading state and raises command availability notifications.
    /// </summary>
    /// <param name="isLoadingInProgress">
    /// Indicates whether an image loading operation is currently running.
    /// </param>
    protected virtual void RaiseCanExecuteCommands(bool isLoadingInProgress)
    {
        _isLoadingInProgress = isLoadingInProgress;
    }
    #endregion

    #region Methods public
    /// <summary>
    /// Applies the aspect ratio identified by its display name (e.g. when restoring persisted
    /// configuration), updating the toolbar toggle state and recomputing the item height.
    /// </summary>
    /// <param name="name">The <see cref="SelectableOption.Name"/> of the aspect ratio to select.</param>
    public void ApplyAspectRatio(string? name)
    {
        SelectableOption? match = AspectRatioSettings.Find(x => x.Name == name);
        if (match == null) { return; }

        foreach (SelectableOption item in AspectRatioSettings)
        {
            item.IsChecked = item == match;
        }

        SelectedAspectRatio = match;
        Height = Convert.ToInt32(Width * SelectedAspectRatio.Value);
        OnPropertyChanged(nameof(SelectedAspectRatioValue));
    }

    /// <summary>
    /// Applies the image resolution identified by its display name (e.g. when restoring persisted
    /// configuration), updating the toolbar toggle state.
    /// </summary>
    /// <param name="name">The <see cref="SelectableOption.Name"/> of the resolution to select.</param>
    public void ApplyResolution(string? name)
    {
        SelectableOption? match = ImageResolutionSettings.Find(x => x.Name == name);
        if (match == null) { return; }

        foreach (SelectableOption item in ImageResolutionSettings)
        {
            item.IsChecked = item == match;
        }

        SelectedImageResolution = match;
        OnPropertyChanged(nameof(SelectedImageResolutionValue));
    }

    /// <summary>
    /// Loads the persisted configuration for this view model.
    /// 
    /// The base image grid view model does not currently persist any configuration.
    /// Derived classes can override this method to restore their own settings.
    /// </summary>
    public override void LoadConfig()
    {
    }

    /// <summary>
    /// Saves the current configuration for this view model.
    /// 
    /// The base image grid view model does not currently persist any configuration.
    /// Derived classes can override this method to store their own settings.
    /// </summary>
    public override void SaveConfig()
    {
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
    }
    #endregion
}