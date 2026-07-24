using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using System.ComponentModel;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control responsible for displaying a grid of game images.
/// 
/// The control receives an <see cref="ImageGridViewModel"/> through a dependency property
/// and uses an <see cref="ItemsWrapGrid"/> to control the size of each grid item.
/// 
/// The item size is applied directly to the panel through <see cref="ItemsWrapGrid.ItemWidth"/>
/// and <see cref="ItemsWrapGrid.ItemHeight"/>, which provides a more reliable layout behavior
/// than setting the width and height only on the generated <see cref="GridViewItem"/> containers.
/// </summary>
public sealed partial class ImageGridControl : UserControl
{
    #region Attributes
    /// <summary>
    /// Reference to the internal <see cref="ItemsWrapGrid"/> used by the <see cref="GridView"/>.
    /// 
    /// This reference is obtained when the panel is loaded and is used to apply the current
    /// item width and height from the view model.
    /// </summary>
    private ItemsWrapGrid? _itemsWrapGrid;

    /// <summary>
    /// The view model whose configuration has already been loaded, used so the persisted settings are
    /// restored only once per view model instance.
    /// </summary>
    private readonly ViewModelConfigGate<ImageGridViewModel> _configGate = new();
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Gets or sets the view model used by the control.
    /// 
    /// The view model provides the image collection, the selected image, the item dimensions,
    /// and the commands used by the toolbar and the gallery.
    /// </summary>
    public ImageGridViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as ImageGridViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Dependency property backing the <see cref="ViewModel"/> property.
    /// 
    /// When the view model changes, the control updates its x:Bind expressions,
    /// subscribes to the new view model property changes, and applies the current grid item size.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ImageGridViewModel), typeof(ImageGridControl), new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>
    /// Handles changes to the <see cref="ViewModel"/> dependency property.
    /// 
    /// The method removes the previous property change subscription, subscribes to the new
    /// view model, refreshes x:Bind bindings, and reapplies the current item size to the
    /// internal <see cref="ItemsWrapGrid"/>.
    /// </summary>
    /// <param name="d">The dependency object whose property changed.</param>
    /// <param name="e">The event data containing the old and new view model values.</param>
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ImageGridControl)d;

        if (e.OldValue is ImageGridViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= control.ViewModel_PropertyChanged;
            oldViewModel.BinariesInvalidated -= control.ViewModel_BinariesInvalidated;
        }

        if (e.NewValue is ImageGridViewModel newViewModel)
        {
            newViewModel.PropertyChanged += control.ViewModel_PropertyChanged;
            newViewModel.BinariesInvalidated += control.ViewModel_BinariesInvalidated;
        }

        control.Bindings.Update();
        control.ApplyGridItemSize();

        control.EnsureConfigurationLoaded();
    }
    #endregion

    #region Constructors
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageGridControl"/> class.
    /// 
    /// The constructor initializes the XAML components and subscribes to the unloaded event
    /// so that event handlers can be detached when the control leaves the visual tree.
    /// </summary>
    public ImageGridControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Configuration
    private void OnLoaded(object? sender, RoutedEventArgs e) => EnsureConfigurationLoaded();

    /// <summary>
    /// Restores the persisted configuration of the view model once, after the control is loaded and the
    /// application settings have been restored. For view models that do not persist configuration this is
    /// a no-op (the base <see cref="ImageGridViewModel.LoadConfig"/> does nothing).
    /// </summary>
    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles the loaded event of the internal <see cref="ItemsWrapGrid"/>.
    /// 
    /// Stores a reference to the panel and applies the current item size from the view model.
    /// This is necessary because the panel is created from the <see cref="ItemsPanelTemplate"/>
    /// and is not directly available when the control is constructed.
    /// </summary>
    /// <param name="sender">The loaded <see cref="ItemsWrapGrid"/>.</param>
    /// <param name="e">The routed event data.</param>
    private void ImagesItemsWrapGrid_Loaded(object? sender, RoutedEventArgs e)
    {
        _itemsWrapGrid = sender as ItemsWrapGrid;

        ApplyGridItemSize();
    }

    /// <summary>
    /// Handles container realization/recycling in the <see cref="GridView"/>.
    ///
    /// When the view model opts into lazy loading (<see cref="ImageGridViewModel.LazyLoadBinariesOnScroll"/>),
    /// each image's binary is decoded on demand as its container scrolls into view. This event fires both
    /// for the items initially visible and for items realized later while scrolling, so it covers the whole
    /// "load what is visible, load the rest as you scroll" behavior. Galleries that do not opt in are
    /// unaffected and keep loading binaries through their (re)load-images command.
    /// </summary>
    /// <param name="sender">The gallery raising the event.</param>
    /// <param name="args">The container/item being realized or recycled.</param>
    private void ImagesGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || ViewModel?.LazyLoadBinariesOnScroll != true)
        {
            return;
        }

        if (args.Item is Models.GameImage image)
        {
            // Fire and forget: the on-demand loader de-duplicates in-flight requests, so it is safe to
            // call repeatedly as the same container is recycled for different images during scrolling.
            _ = ViewModel.LoadImageBinaryOnDemandAsync(image);
        }
    }

    /// <summary>
    /// Re-decodes the items that currently have a realized container — those on screen plus the small
    /// virtualization buffer around them (<see cref="ItemsWrapGrid.FirstCacheIndex"/>..<see
    /// cref="ItemsWrapGrid.LastCacheIndex"/>) — after a resolution change released every binary. Off-screen
    /// items have no container and stay without a binary until scrolled into view, where
    /// <see cref="ImagesGridView_ContainerContentChanging"/> loads them lazily at the new resolution. The
    /// cache indices are -1 while nothing is realized yet (e.g. before the first layout).
    /// </summary>
    private void ViewModel_BinariesInvalidated()
    {
        if (_itemsWrapGrid == null || ViewModel == null)
        {
            return;
        }

        int first = _itemsWrapGrid.FirstCacheIndex;
        int last = _itemsWrapGrid.LastCacheIndex;

        if (first < 0 || last < 0)
        {
            return;
        }

        for (int i = first; i <= last && i < ViewModel.Images.Count; i++)
        {
            // Fire and forget: the on-demand loader de-duplicates in-flight requests and no-ops images that
            // already hold a binary, so re-decoding the realized set is safe and idempotent.
            _ = ViewModel.LoadImageBinaryOnDemandAsync(ViewModel.Images[i]);
        }
    }

    /// <summary>
    /// Handles property changes raised by the current view model.
    /// 
    /// When the image item width or height changes, the method reapplies the values
    /// to the internal <see cref="ItemsWrapGrid"/> so that the layout is recalculated correctly.
    /// </summary>
    /// <param name="sender">The view model that raised the change notification.</param>
    /// <param name="e">The property change event data.</param>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageGridViewModel.Width) ||
            e.PropertyName == nameof(ImageGridViewModel.Height))
        {
            ApplyGridItemSize();
        }
    }

    /// <summary>
    /// Handles the unloaded event of the control.
    /// 
    /// Detaches the property change subscription from the current view model and unsubscribes
    /// from the control unloaded event to avoid keeping unnecessary references alive.
    /// </summary>
    /// <param name="sender">The unloaded control.</param>
    /// <param name="e">The routed event data.</param>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is ImageGridViewModel viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            viewModel.BinariesInvalidated -= ViewModel_BinariesInvalidated;
        }

        Unloaded -= OnUnloaded;
    }
    #endregion

    #region Methods private
    /// <summary>
    /// Applies the current item width and height from the view model to the internal
    /// <see cref="ItemsWrapGrid"/>.
    /// 
    /// This ensures that the panel responsible for arranging the <see cref="GridView"/> items
    /// knows the expected item size before or during layout calculation.
    /// </summary>
    private void ApplyGridItemSize()
    {
        if (_itemsWrapGrid == null || ViewModel == null)
        {
            return;
        }

        _itemsWrapGrid.ItemWidth = ViewModel.Width;
        _itemsWrapGrid.ItemHeight = ViewModel.Height;

        ImagesGridView?.InvalidateMeasure();
    }
    #endregion
}