using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Linq;

namespace MM4LB.Controls.Views;

/// <summary>
/// UserControl del widget GameImagesRegionDashboard: gestiona las imágenes del juego seleccionado por región.
/// FASE C: selector de región + toolbar + preview + miniaturas de la región (drag&amp;drop y procesado por región
/// en fases posteriores).
/// </summary>
public sealed partial class GameImagesRegionDashboardControl : UserControl
{
    #region Attributes
    private readonly ViewModelConfigGate<GameImagesRegionDashboardViewModel> _configGate = new();
    private readonly ThemeService _themeService;
    #endregion

    #region Dependency Properties
    /// <summary>ViewModel del control.</summary>
    public GameImagesRegionDashboardViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as GameImagesRegionDashboardViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(GameImagesRegionDashboardViewModel), typeof(GameImagesRegionDashboardControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion

    #region Constructors
    public GameImagesRegionDashboardControl()
    {
        InitializeComponent();

        _themeService = App.GetService<ThemeService>();
        _themeService.ThemeChanged += OnThemeChanged;

        Loaded += OnLoaded;
    }
    #endregion

    #region Subscribed events
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        EnsureViewModelConfigurationLoaded();
        Bindings.Update();
        ApplyConfiguredThumbnailPanelSize();
    }

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

    private static void OnViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not GameImagesRegionDashboardControl control)
        {
            return;
        }

        control.EnsureViewModelConfigurationLoaded();
        control.Bindings.Update();
        control.ApplyConfiguredThumbnailPanelSize();
    }

    /// <summary>Mantiene el ViewModel sincronizado con el tamaño actual del panel de miniaturas.</summary>
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

    /// <summary>Clic en un item del selector de regiones: fija el bucket activo en el ViewModel.</summary>
    private void RegionBucket_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is RegionBucket bucket && ViewModel is not null)
        {
            ViewModel.SelectedBucket = bucket;
        }
    }

    /// <summary>Selección de miniatura: fija la imagen y hace ScrollIntoView (responsabilidad visual en la vista).</summary>
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
    private void EnsureViewModelConfigurationLoaded() => _configGate.Ensure(ViewModel);

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
