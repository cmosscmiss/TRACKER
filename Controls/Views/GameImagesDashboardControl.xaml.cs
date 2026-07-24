using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Linq;

namespace MM4LB.Controls.Views;

/// <summary>
/// User control to visualise and select the images of a game.
/// </summary>
public sealed partial class GameImagesDashboardControl : UserControl
{
    #region Attributes
    private readonly ViewModelConfigGate<GameImagesDashboardViewModel> _configGate = new();
    private readonly ThemeService _themeService;
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Property to hold the view model for the control.
    /// </summary>
    public GameImagesDashboardViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as GameImagesDashboardViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(GameImagesDashboardViewModel), typeof(GameImagesDashboardControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion

    #region Constructors
    public GameImagesDashboardControl()
    {
        InitializeComponent();

        _themeService = App.GetService<ThemeService>();
        _themeService.ThemeChanged += OnThemeChanged;

        Loaded += OnLoaded;
    }
    #endregion

    /// <summary>
    /// Refresca los stops del gradiente de acento de la pastilla de proceso al cambiar de tema en caliente. Son recursos
    /// de tipo <see cref="Windows.UI.Color"/> (tipo por valor) que no se propagan solos como sí lo hacen los brushes.
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (ProcessPrevStopOuter is null) { return; }

        ProcessPrevStopOuter.Color = _themeService.AccentDarkColor;
        ProcessPrevStopInner.Color = _themeService.AccentColor;
        ProcessNextStopInner.Color = _themeService.AccentColor;
        ProcessNextStopOuter.Color = _themeService.AccentDarkColor;
    }

    #region Subscribed events
    /// <summary>
    /// Applies the configured state when the control has finished loading.
    /// </summary>
    /// <param name="sender">The loaded control.</param>
    /// <param name="e">The routed event arguments.</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        EnsureViewModelConfigurationLoaded();
        Bindings.Update();
        ApplyConfiguredThumbnailPanelSize();
    }

    /// <summary>
    /// Handles changes to the control ViewModel dependency property.
    /// </summary>
    /// <param name="dependencyObject">The control whose ViewModel has changed.</param>
    /// <param name="e">The dependency property change arguments.</param>
    private static void OnViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not GameImagesDashboardControl control)
        {
            return;
        }


        control.EnsureViewModelConfigurationLoaded();
        control.Bindings.Update();
        control.ApplyConfiguredThumbnailPanelSize();
    }

    /// <summary>
    /// Keeps the ViewModel synchronized with the current thumbnail panel size.
    /// </summary>
    /// <param name="sender">The resized thumbnail panel.</param>
    /// <param name="e">The size changed event arguments.</param>
    private void ThumbnailPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.IsHorizontalView && HorizontalThumbnailRow.ActualHeight > 0)
        {
            ViewModel.ThumbnailPanelHeight = HorizontalThumbnailRow.ActualHeight;
        }

        if (ViewModel.IsVerticalView && VerticalThumbnailColumn.ActualWidth > 0)
        {
            ViewModel.ThumbnailPanelWidth = VerticalThumbnailColumn.ActualWidth;
        }
    }

    /// <summary>
    /// Handles image selection changes in the thumbnails list.
    /// Keeps visual responsibilities, such as ScrollIntoView, in the view.
    /// </summary>
    /// <param name="sender">The ListView where the selection changed.</param>
    /// <param name="e">The selection changed event arguments.</param>
    private void GameImages_ImageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is not GameImage image)
        {
            return;
        }

        ViewModel?.SelectImage(image);

        if (sender is ListView listView)
        {
            listView.ScrollIntoView(image);
        }
    }

    #endregion

    #region Methods private
    /// <summary>
    /// Ensures that the ViewModel configuration is loaded once for the current ViewModel instance.
    /// </summary>
    private void EnsureViewModelConfigurationLoaded() => _configGate.Ensure(ViewModel);

    /// <summary>
    /// Applies the configured thumbnail panel width and height to the dashboard row and column definitions.
    /// </summary>
    private void ApplyConfiguredThumbnailPanelSize()
    {
        if (ViewModel is null)
        {
            return;
        }

        HorizontalThumbnailRow.Height = new GridLength(ViewModel.ThumbnailPanelHeight);
        VerticalThumbnailColumn.Width = new GridLength(ViewModel.ThumbnailPanelWidth);
    }
    #endregion
}