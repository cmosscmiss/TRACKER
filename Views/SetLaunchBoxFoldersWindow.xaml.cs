using System;
using Microsoft.UI.Xaml;
using MM4LB.ViewModels;
using MM4LB.Services;

namespace MM4LB.Views;

/// <summary>
/// Ventana dedicada a la configuración de carpetas utilizadas por LaunchBox.
/// Se inicializa con un ViewModel específico y un servicio de gestión de ventanas.
/// </summary>
public sealed partial class SetLaunchBoxFoldersWindow : Window
{
    #region Attributes
    private readonly SetLaunchBoxFoldersViewModel _viewModel;
    private readonly WindowService _windowService;
    
    private bool _initialized;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa una nueva instancia de la ventana para configurar las carpetas de LaunchBox.
    /// Asigna el ViewModel, establece el DataContext y suscribe los eventos necesarios.
    /// </summary>
    /// <param name="viewModel">ViewModel que gestiona la lógica de la ventana.</param>
    /// <param name="windowService">Servicio encargado de gestionar el comportamiento de la ventana.</param>
    public SetLaunchBoxFoldersWindow(SetLaunchBoxFoldersViewModel viewModel, WindowService windowService)
    {
        _viewModel = viewModel;
        _viewModel.Window = this;
        _windowService = windowService;

        this.InitializeComponent();

        if (this.Content is FrameworkElement root)
        {
            root.DataContext = _viewModel;
        }

        // Suscribirse a eventos
        Activated += SetLaunchBoxFoldersWindow_Activated;
        _viewModel.ShowLoadingRequested += ViewModel_ShowLoadingRequested;

        // Desuscribirse de eventos al cerrar la ventana para evitar fugas de memoria
        this.Closed += (_, __) =>
        {
            Activated -= SetLaunchBoxFoldersWindow_Activated;
            _viewModel.ShowLoadingRequested -= ViewModel_ShowLoadingRequested;
        };
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Evento ejecutado cuando la ventana se activa por primera vez.
    /// Se utiliza para aplicar configuración inicial como tamaño y disposición.
    /// </summary>
    private async void SetLaunchBoxFoldersWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;
        _windowService.DialogWindow(this, 900, 600);
    }

    /// <summary>
    /// Evento lanzado por el ViewModel cuando se solicita mostrar la ventana de carga.
    /// </summary>
    private void ViewModel_ShowLoadingRequested(object? sender, EventArgs e)
    {
        try
        {
            var loadingWindow = App.GetService<LoadingWindow>();
            loadingWindow.PrepareAndShowWhenReady();
        }
        catch
        {
        }
    }
    #endregion
}