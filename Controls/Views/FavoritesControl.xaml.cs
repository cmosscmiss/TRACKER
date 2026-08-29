using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tracker.Controls.ViewModels;

namespace Tracker.Controls.Views;

/// <summary>
/// Widget de favoritos: un FlipView que muestra, por cada producto favorito, la misma vista de producto que el
/// widget del seleccionado (<see cref="PriceChartControl"/>), alimentada por <see cref="FavoritesViewModel"/>.
/// </summary>
public sealed partial class FavoritesControl : UserControl
{
    #region Dependency Properties
    public FavoritesViewModel? ViewModel
    {
        get => (FavoritesViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(FavoritesViewModel), typeof(FavoritesControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    public FavoritesControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }
    #endregion

    #region Subscribed Events
    /// <summary>Al cargarse (con las vistas ya construidas), restaura la página activa del FlipView vía el ViewModel.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel?.LoadConfig();
    }
    #endregion
}
