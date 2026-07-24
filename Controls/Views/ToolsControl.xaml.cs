using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// Widget contenedor de herramientas del dashboard. Aloja los controles de herramienta (auditoría de media y
/// huérfanos). Su <see cref="ViewModel"/> lo asigna el host del widget (MainWindow) y ORQUESTA la persistencia
/// (herramienta abierta + ajustes de cada tool) vía <see cref="ToolsViewModel.LoadConfig"/>/<c>SaveConfig</c>.
/// </summary>
public sealed partial class ToolsControl : UserControl
{
    /// <summary>Carga la config del VM UNA sola vez por instancia (patrón de los demás widgets).</summary>
    private readonly ViewModelConfigGate<ToolsViewModel> _configGate = new();

    /// <summary>View model del widget (ciclo de vida: SlotIndex + persistencia). Lo asigna el host.</summary>
    public ToolsViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as ToolsViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ToolsViewModel), typeof(ToolsControl), new PropertyMetadata(null, OnViewModelChanged));

    public ToolsControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _configGate.Ensure(ViewModel);

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolsControl control)
            control._configGate.Ensure(control.ViewModel);
    }
}
