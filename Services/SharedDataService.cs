using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Service to keep a snapshot of the data structures and status being used by the different components.
/// </summary>
public class SharedDataService : ObservableObject
{
    #region Subclasses
    public class ProductChangedEventArgs : EventArgs
    {
        public Product? OldProduct { get; }
        public Product? NewProduct { get; }
        public ProductChangedEventArgs(Product? oldProduct, Product? newProduct)
        {
            OldProduct = oldProduct;
            NewProduct = newProduct;
        }
    }
    #endregion

    #region Attributes
    private bool _isUiEnabled;
    private Product? _selectedProduct;
    #endregion

    #region Properties
    /// <summary>The set of tracked products shown in the left-hand list of the main window.</summary>
    public ProductSet ProductSet { get; } = new();

    public bool IsUIEnabled
    {
        get => _isUiEnabled;
        set => SetProperty(ref _isUiEnabled, value);
    }

    /// <summary>
    /// Currently selected product in the left-hand list.
    /// </summary>
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (!ReferenceEquals(_selectedProduct, value))
            {
                var oldValue = _selectedProduct;
                var newValue = value;
                SetProperty(ref _selectedProduct, newValue);
                SelectedProductChanged?.Invoke(this, new ProductChangedEventArgs(oldValue, newValue));
            }
        }
    }
    #endregion

    #region Constructors
    public SharedDataService()
    {
    }
    #endregion

    #region
    public void NotifyInitialState()
    {
        SelectedProductChanged?.Invoke(this, new ProductChangedEventArgs(null, SelectedProduct));
    }
    #endregion

    #region Events
    public event EventHandler<ProductChangedEventArgs>? SelectedProductChanged;

    /// <summary>
    /// Se dispara cuando cambia el ajuste global de visibilidad de la cabecera de los widgets
    /// (<see cref="Models.AppSettings.GeneralSettings.ShowWidgetHeader"/>), para que los widgets lo apliquen en
    /// caliente. Lo emite la ventana de configuración al aceptar; lo escuchan los <c>WidgetBaseControl</c>.
    /// </summary>
    public event EventHandler? WidgetHeaderVisibilityChanged;

    /// <summary>
    /// Se dispara cuando cambia el modo de grupos de las toolbars
    /// (<see cref="Models.AppSettings.GeneralSettings.ToolbarGroupsDisplayMode"/>), para que las toolbars con grupos
    /// excluyentes se reconstruyan en caliente. Lo emite la ventana de configuración al aceptar.
    /// </summary>
    public event EventHandler? ToolbarGroupsDisplayModeChanged;
    #endregion

    #region Methods (public)
    /// <summary>Notifica que la visibilidad de la cabecera de los widgets cambió, para aplicarla en caliente.</summary>
    public void NotifyWidgetHeaderVisibilityChanged()
    {
        WidgetHeaderVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifica que el modo de grupos de las toolbars cambió, para reconstruir las toolbars con grupos excluyentes.</summary>
    public void NotifyToolbarGroupsDisplayModeChanged()
    {
        ToolbarGroupsDisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }
    #endregion
}
