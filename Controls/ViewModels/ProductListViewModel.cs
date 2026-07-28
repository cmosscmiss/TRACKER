using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel asociado a <see cref="Views.ProductListControl"/>.
///
/// Expone (vía <see cref="SharedDataService"/>) la colección de productos rastreados y el producto
/// seleccionado, que se muestran en el ListView de la columna izquierda de la ventana principal.
/// </summary>
public partial class ProductListViewModel : WidgetViewModelBase
{
    #region Constructor
    public ProductListViewModel(
        SharedDataService sharedDataService,
        IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
    }
    #endregion

    #region Methods public
    /// <summary>
    /// Libera los recursos asociados al ViewModel.
    /// </summary>
    public override void Dispose()
    {
    }

    /// <summary>
    /// Carga desde la configuración el estado visual guardado del control.
    /// </summary>
    public override void LoadConfig()
    {
    }

    /// <summary>
    /// Guarda en la configuración el Id del producto seleccionado, para re-seleccionarlo al arrancar.
    /// </summary>
    public override void SaveConfig()
    {
        _appSettings.ProductListControl.SelectedProductId = SharedDataService.SelectedProduct?.Id ?? 0;
    }
    #endregion
}
