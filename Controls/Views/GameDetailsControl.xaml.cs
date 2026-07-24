using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// Widget que muestra la ficha completa del juego seleccionado (todos los campos del &lt;Game&gt; del XML de
/// colección), agrupada en un ScrollViewer: cabecera de grupo estilo <c>SettingsControl</c> y filas etiqueta/valor
/// estilo <c>PlatformDetailsControl</c>. Los datos los proyecta <see cref="GameDetailsViewModel"/> desde
/// <see cref="MM4LB.Models.GameDetails"/>.
/// </summary>
public sealed partial class GameDetailsControl : UserControl
{
    #region Dependency Properties
    /// <summary>ViewModel del control.</summary>
    public GameDetailsViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as GameDetailsViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        "ViewModel", typeof(GameDetailsViewModel), typeof(GameDetailsControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion

    #region Attributes
    /// <summary>
    /// El ViewModel cuya configuración ya se ha cargado, para restaurar los ajustes persistidos una sola vez por
    /// instancia (aunque este widget no persista estado propio, se mantiene el patrón común de los widgets).
    /// </summary>
    private readonly ViewModelConfigGate<GameDetailsViewModel> _configGate = new();
    #endregion

    #region Constructor
    public GameDetailsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    #endregion

    #region Configuration
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((GameDetailsControl)d).EnsureConfigurationLoaded();

    private void OnLoaded(object sender, RoutedEventArgs e) => EnsureConfigurationLoaded();

    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);
    #endregion
}
