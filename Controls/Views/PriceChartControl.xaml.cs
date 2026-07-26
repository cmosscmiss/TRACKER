using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// Widget que muestra la evolución del precio del producto seleccionado, reutilizando
/// <see cref="ChartTypeSelectorControl"/> (con su toolbar de tipo de gráfica) alimentado por
/// <see cref="PriceChartViewModel"/>. El ViewModel se inyecta desde fuera vía <see cref="ViewModel"/>.
/// </summary>
public sealed partial class PriceChartControl : UserControl
{
    #region Dependency Properties
    public PriceChartViewModel? ViewModel
    {
        get => (PriceChartViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(PriceChartViewModel), typeof(PriceChartControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    public PriceChartControl()
    {
        InitializeComponent();
    }
    #endregion
}
