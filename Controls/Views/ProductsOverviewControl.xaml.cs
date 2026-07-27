using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;

namespace MM4LB.Controls.Views;

/// <summary>
/// Widget de resumen: una gráfica de columnas (producto por columna, mejor precio en Y) alimentada por
/// <see cref="ProductsOverviewViewModel"/>, reutilizando <see cref="ChartTypeSelectorControl"/>. Al pulsar una
/// columna, selecciona ese producto (que a su vez actualiza el resto de la app).
/// </summary>
public sealed partial class ProductsOverviewControl : UserControl
{
    #region Dependency Properties
    public ProductsOverviewViewModel? ViewModel
    {
        get => (ProductsOverviewViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ProductsOverviewViewModel), typeof(ProductsOverviewControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    public ProductsOverviewControl()
    {
        InitializeComponent();
        Chart.CategoryClicked += OnCategoryClicked;
    }
    #endregion

    #region Methods (private)
    /// <summary>Clic en una columna: selecciona el producto correspondiente.</summary>
    private void OnCategoryClicked(int index)
    {
        Product? product = ViewModel?.ProductAt(index);
        if (product is not null && ViewModel is not null)
            ViewModel.SharedDataService.SelectedProduct = product;
    }
    #endregion
}
