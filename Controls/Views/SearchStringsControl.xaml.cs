using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// User control responsible for displaying search strings associated with either
/// the selected game or the selected game image.
/// 
/// The control delegates the actual data preparation to <see cref="SearchStringsViewModel"/>
/// and exposes <see cref="SearchStringsSource"/> as a dependency property so that the
/// consuming XAML can decide which source should be rendered.
/// </summary>
public sealed partial class SearchStringsControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Gets or sets the source used to determine which search strings are displayed.
    /// 
    /// When set to <see cref="SearchStringsViewModel.SearchStringsSource.Game"/>,
    /// the control displays the search strings of the selected game.
    /// 
    /// When set to <see cref="SearchStringsViewModel.SearchStringsSource.GameImage"/>,
    /// the control displays the search strings of the selected game image.
    /// </summary>
    public SearchStringsViewModel.SearchStringsSource SearchStringsSource
    {
        get => (SearchStringsViewModel.SearchStringsSource)GetValue(SearchStringsSourceProperty);
        set => SetValue(SearchStringsSourceProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="SearchStringsSource"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SearchStringsSourceProperty = DependencyProperty.Register(nameof(SearchStringsSource), typeof(SearchStringsViewModel.SearchStringsSource), typeof(SearchStringsControl), new PropertyMetadata(SearchStringsViewModel.SearchStringsSource.Game, OnSearchStringsSourceChanged));
    #endregion

    #region Properties
    /// <summary>
    /// View model used by the control to prepare and expose the search strings
    /// displayed by the XAML view.
    /// </summary>
    public SearchStringsViewModel ViewModel { get; } = App.GetService<SearchStringsViewModel>();
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchStringsControl"/> class.
    /// 
    /// The control subscribes to its loading lifecycle in order to synchronize the
    /// dependency property value with the internal view model and to release resources
    /// when the control is unloaded.
    /// </summary>
    public SearchStringsControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Event handlers
    /// <summary>
    /// Handles changes to the <see cref="SearchStringsSource"/> dependency property.
    /// 
    /// When the source changes, the internal view model is updated so that it refreshes
    /// the visible list according to the requested source.
    /// </summary>
    /// <param name="dependencyObject">
    /// The dependency object whose property changed.
    /// </param>
    /// <param name="e">
    /// Event data containing the old and new property values.
    /// </param>
    private static void OnSearchStringsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (SearchStringsControl)dependencyObject;
        if (e.NewValue is SearchStringsViewModel.SearchStringsSource source)
            control.ViewModel.Source = source;
    }

    /// <summary>
    /// Synchronizes the configured search string source with the view model when the
    /// control is loaded.
    /// </summary>
    /// <param name="sender">
    /// The control that raised the event.
    /// </param>
    /// <param name="e">
    /// Event data for the loaded event.
    /// </param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Source = SearchStringsSource;
    }

    /// <summary>
    /// Releases resources held by the control when it is unloaded.
    /// 
    /// This disposes the internal view model and detaches lifecycle event handlers
    /// to avoid keeping the control alive longer than necessary.
    /// </summary>
    /// <param name="sender">
    /// The control that raised the event.
    /// </param>
    /// <param name="e">
    /// Event data for the unloaded event.
    /// </param>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
    #endregion
}