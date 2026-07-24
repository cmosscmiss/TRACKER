using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MM4LB.Services;

namespace MM4LB.ViewModels;

/// <summary>
/// ViewModel encargado de gestionar la lógica de la ventana de carga.
/// Ejecuta la inicialización de LaunchBox y notifica cuando el proceso ha finalizado.
/// </summary>
public class LoadingWindowViewModel : ObservableObject
{
    #region Attributes
    private readonly LaunchBoxService _launchBoxService;
    private readonly ProgressService _progressService;
    private readonly ThemeService _themeService;
    #endregion

    #region Properties
    public ProgressService ProgressService => _progressService;
    
    public Uri? ImageUri { get; }
    public bool UseTintedImage { get; }

    /// <summary>Si se muestra el marco de neón del splash (dibujado por código). Antes eran PNG con el borde quemado.</summary>
    public bool ShowFrameBorder { get; }
    #endregion

    #region Events
    public event EventHandler? LoadCompleted;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el ViewModel con los servicios necesarios para la carga de datos.
    /// </summary>
    /// <param name="launchBoxService">Servicio encargado de inicializar LaunchBox.</param>
    /// <param name="progressService">Servicio que gestiona el progreso mostrado en la UI.</param>
    public LoadingWindowViewModel(LaunchBoxService launchBoxService, ProgressService progressService, ThemeService themeService)
    {
        _launchBoxService = launchBoxService;
        _progressService = progressService;
        _themeService = themeService;

        // Inicializamos propiedades derivadas del tema
        UseTintedImage = themeService.BackgroundImageTinted;
        ShowFrameBorder = themeService.BackgroundImageFramed;   // el marco ahora se dibuja por código (ver LoadingWindow)

        ImageUri = _themeService.BackgroundImageUri;             // siempre la imagen SIN marco
    }
    #endregion    

    #region Methods (public)
    /// <summary>
    /// Ejecuta la inicialización de LaunchBox de forma asíncrona y notifica cuando ha concluido.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _launchBoxService.InitializeAsync();
        LoadCompleted?.Invoke(this, EventArgs.Empty);
    }
    #endregion
}