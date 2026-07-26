using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control de usuario que muestra y gestiona la selección de productos rastreados como una lista
/// (<see cref="ListView"/>) en la columna izquierda de la ventana principal. Expone su
/// <see cref="ProductListViewModel"/> mediante una <see cref="DependencyProperty"/> para que la ventana
/// principal pueda inyectar el ViewModel desde el exterior.
/// </summary>
public sealed partial class ProductListControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// ViewModel asociado al control: da acceso a la colección de productos y al producto seleccionado.
    /// </summary>
    public ProductListViewModel? ViewModel
    {
        get => (ProductListViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ProductListViewModel), typeof(ProductListControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    public ProductListControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Al cargarse en el árbol visual, restaura el estado guardado del control vía su ViewModel.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (ViewModel is null)
            return;

        ViewModel.LoadConfig();
    }
    #endregion
}
