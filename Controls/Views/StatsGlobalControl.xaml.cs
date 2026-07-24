using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

public sealed partial class StatsGlobalControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// View model del control: series de las gráficas comparativas entre plataformas (juegos, imágenes, tamaño).
    /// </summary>
    public StatsGlobalViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as StatsGlobalViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(StatsGlobalViewModel), typeof(StatsGlobalControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion

    #region Attributes
    /// <summary>
    /// View model cuya configuración ya se ha cargado, para restaurar los ajustes una sola vez por instancia.
    /// </summary>
    private readonly ViewModelConfigGate<StatsGlobalViewModel> _configGate = new();
    #endregion

    #region Constructor
    public StatsGlobalControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    #endregion

    #region Configuration
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        StatsGlobalControl control = (StatsGlobalControl)d;
        control.EnsureConfigurationLoaded();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => EnsureConfigurationLoaded();

    /// <summary>
    /// Carga la configuración del view model una sola vez, tras cargarse el control y restaurarse los ajustes.
    /// </summary>
    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);
    #endregion
}
