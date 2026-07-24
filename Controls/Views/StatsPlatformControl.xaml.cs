using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

public sealed partial class StatsPlatformControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// View model del control: estadísticas y series de gráficas de la plataforma seleccionada.
    /// </summary>
    public StatsPlatformViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as StatsPlatformViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(StatsPlatformViewModel), typeof(StatsPlatformControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion

    #region Attributes
    /// <summary>
    /// View model cuya configuración ya se ha cargado, para restaurar los ajustes una sola vez por instancia.
    /// </summary>
    private readonly ViewModelConfigGate<StatsPlatformViewModel> _configGate = new();

    /// <summary>Glue del chart de cobertura (volcado de Sections + velocidad de animación); compartido con PlatformDetailsControl.</summary>
    private readonly CoverageChartGlue _coverageGlue = new();
    #endregion

    #region Constructor
    public StatsPlatformControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Configuration
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        StatsPlatformControl control = (StatsPlatformControl)d;

        // La propiedad Sections del CartesianChart no admite x:Bind (su tipo genérico rompe la binding
        // compilada), así que la sincronizamos a mano desde el view model.
        if (e.OldValue is StatsPlatformViewModel oldViewModel)
            oldViewModel.PropertyChanged -= control.OnViewModelPropertyChanged;
        if (e.NewValue is StatsPlatformViewModel newViewModel)
            newViewModel.PropertyChanged += control.OnViewModelPropertyChanged;
        control.UpdateCoverageSections();

        control.EnsureConfigurationLoaded();
    }

    /// <summary>Refleja en la gráfica la línea punteada y la velocidad de animación cuando el view model lo recalcula.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatsPlatformViewModel.CoverageByGameSections))
            UpdateCoverageSections();
        // Se aplica ANTES de que lleguen las series/secciones nuevas (el VM fija AnimateCoverageByGameChart primero).
        else if (e.PropertyName == nameof(StatsPlatformViewModel.AnimateCoverageByGameChart))
            ApplyCoverageChartAnimation();
    }

    /// <summary>
    /// Ajusta la velocidad de animación del chart de cobertura: cero al cambiar de juego (cache hit, solo se mueve
    /// el resaltado → instantáneo) y la velocidad por defecto cuando hay recálculo real.
    /// </summary>
    private void ApplyCoverageChartAnimation()
    {
        if (ViewModel is null)
            return;

        _coverageGlue.ApplyAnimation(CoverageByGameChart, ViewModel.AnimateCoverageByGameChart);
    }

    /// <summary>Vuelca <see cref="StatsPlatformViewModel.CoverageByGameSections"/> en la propiedad Sections del chart.</summary>
    private void UpdateCoverageSections()
        => _coverageGlue.UpdateSections(CoverageByGameChart, ViewModel?.CoverageByGameSections);

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureConfigurationLoaded();
        UpdateCoverageSections();
        ApplyCoverageChartAnimation();
    }

    /// <summary>
    /// El ViewModel es Singleton y el control se descarta al reconfigurar el panel; sin desuscribir aquí, el VM
    /// mantendría vivo cada control anterior (fuga). Patrón simétrico al de ImageGridControl.
    /// </summary>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is StatsPlatformViewModel viewModel)
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        Unloaded -= OnUnloaded;
    }

    /// <summary>Abre el TeachingTip de ayuda de la gráfica de cobertura por juego.</summary>
    private void CoverageByGameHelp_Click(object? sender, RoutedEventArgs e) => CoverageByGameHelpTip.IsOpen = true;

    /// <summary>
    /// Carga la configuración del view model una sola vez, tras cargarse el control y restaurarse los ajustes.
    /// </summary>
    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);
    #endregion
}
