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
    private bool _showChartAxisLabels;
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
    /// Ajuste global (persistido en el .ini) de si las gráficas del widget de productos muestran las etiquetas del eje
    /// X (fechas de actualización). Observable: el toggle del pie lo cambia y todas las gráficas lo aplican en caliente.
    /// Por defecto false (leyenda oculta).
    /// </summary>
    public bool ShowChartAxisLabels
    {
        get => _showChartAxisLabels;
        set => SetProperty(ref _showChartAxisLabels, value);
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

    /// <summary>Se dispara cuando cambia el conjunto de productos favoritos (para el widget de favoritos y el toggle).</summary>
    public event EventHandler? FavoritesChanged;

    /// <summary>
    /// Se dispara ANTES de grabar un template: pide a los ViewModels que vuelquen su estado en vivo a
    /// <see cref="Models.AppSettings"/> (lo que normalmente solo se hace al cerrar), para que el template capture el
    /// estado ACTUAL y no el de arranque.
    /// </summary>
    public event EventHandler? SaveConfigRequested;

    /// <summary>
    /// Se dispara al CARGAR un template: pide a los ViewModels que recarguen su configuración desde
    /// <see cref="Models.AppSettings"/> y se re-aplique el layout, para reflejar el template EN CALIENTE (sin reiniciar).
    /// </summary>
    public event EventHandler? SettingsReloaded;
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

    /// <summary>Notifica que cambió el conjunto de favoritos (para reconstruir el widget de favoritos y el estado del toggle).</summary>
    public void NotifyFavoritesChanged()
    {
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pide volcar el estado en vivo de los ViewModels a AppSettings (antes de grabar un template).</summary>
    public void NotifySaveConfigRequested()
    {
        SaveConfigRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pide recargar la configuración en los ViewModels y re-aplicar el layout (al cargar un template).</summary>
    public void NotifySettingsReloaded()
    {
        SettingsReloaded?.Invoke(this, EventArgs.Empty);
    }
    #endregion
}
