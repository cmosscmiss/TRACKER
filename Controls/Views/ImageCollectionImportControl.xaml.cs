using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

public sealed partial class ImageCollectionImportControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Gets or sets the view model used by the control.
    /// 
    /// The view model provides the image collection, the selected image, the item dimensions,
    /// and the commands used by the toolbar and the gallery.
    /// </summary>
    public ImageCollectionImportViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as ImageCollectionImportViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Dependency property backing the <see cref="ViewModel"/> property.
    /// 
    /// When the view model changes, the control updates its x:Bind expressions,
    /// subscribes to the new view model property changes, and applies the current grid item size.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ImageCollectionImportViewModel), typeof(ImageCollectionImportControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion

    #region Attributes
    /// <summary>
    /// The view model whose configuration has already been loaded, used so the persisted settings are
    /// restored only once per view model instance.
    /// </summary>
    private readonly ViewModelConfigGate<ImageCollectionImportViewModel> _configGate = new();
    #endregion

    #region Constructor
    public ImageCollectionImportControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    #endregion

    #region Configuration
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ImageCollectionImportControl control = (ImageCollectionImportControl)d;
        control.EnsureConfigurationLoaded();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => EnsureConfigurationLoaded();

    /// <summary>
    /// Restores the persisted configuration of the view model once, after the control is loaded and the
    /// application settings have been restored.
    /// </summary>
    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);
    #endregion

    #region Subscribed events
    /// <summary>
    /// Toggling a view (games audit and/or image grid).
    ///
    /// The two views are independent and can be shown at the same time. The only constraint is that at
    /// least one view must always remain visible, so unchecking the last visible view is reverted.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void AtbView_Click(object sender, RoutedEventArgs e)
    {
        if (atbImagesView.IsChecked != true && atbGamesView.IsChecked != true)
        {
            ((AppBarToggleButton)sender).IsChecked = true;
        }
    }
    #endregion
}
