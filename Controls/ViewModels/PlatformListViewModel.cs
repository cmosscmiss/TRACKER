using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel asociado a <see cref="Views.PlatformListControl"/>.
/// 
/// Expone los datos necesarios para mostrar y seleccionar plataformas,
/// utilizando <see cref="SharedDataService"/> como fuente compartida de estado.
/// 
/// También mantiene el estado visual propio del control, indicando si debe
/// mostrarse como una lista completa o como un selector compacto mediante ComboBox.
/// </summary>
public partial class PlatformListViewModel : WidgetViewModelBase
{
    #region Constructor
    /// <summary>
    /// Inicializa una nueva instancia de <see cref="PlatformListViewModel"/>.
    /// </summary>
    /// <param name="sharedDataService"> Servicio compartido que contiene la colección de plataformas y la plataforma seleccionada. </param>
    /// <param name="appSettings"> Configuración de la aplicación utilizada para cargar y guardar el estado del control. </param>
    public PlatformListViewModel(
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
    /// Guarda en la configuración la plataforma seleccionada.
    /// </summary>
    public override void SaveConfig()
    {
        _appSettings.PlatformListControl.SelectedPlatform = SharedDataService.SelectedPlatform?.Name ?? string.Empty;
    }

    #endregion
}