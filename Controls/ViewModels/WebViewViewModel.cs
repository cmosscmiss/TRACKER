using Microsoft.Extensions.Options;
using Tracker.Models;
using Tracker.Services;
using System;
using System.Linq;
using static Tracker.Services.SharedDataService;

namespace Tracker.Controls.ViewModels;

/// <summary>
/// ViewModel del WebViewControl. Ya no es un buscador: es un navegador para inspeccionar la página del producto y
/// darlo de alta desde ahí. Al seleccionar un producto en la lista, navega a su primer enlace. El control gestiona
/// el selector de país (Amazon) y los botones de alta (necesitan el WebView2). Este ViewModel solo expone la petición
/// de navegación y la URL actual (para el título del widget).
/// </summary>
public class WebViewViewModel : WidgetViewModelBase
{
    #region Fields
    private string _currentUrl = string.Empty;
    #endregion

    #region Properties
    /// <summary>URL actualmente cargada (la refleja el control al terminar de navegar). Título del widget.</summary>
    public string CurrentUrl
    {
        get => _currentUrl;
        private set => SetProperty(ref _currentUrl, value);
    }
    #endregion

    #region Events
    /// <summary>Solicita al control que navegue a una URL (el control decide según su SlotIndex).</summary>
    public event Action<string>? NavigationRequested;
    #endregion

    #region Constructor
    public WebViewViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        SharedDataService.SelectedProductChanged += OnSelectedProductChanged;
    }
    #endregion

    #region Subscribed events
    /// <summary>Al seleccionar un producto, navega a la tienda con el precio más bajo (o a la primera si aún no hay precios).</summary>
    private void OnSelectedProductChanged(object? sender, ProductChangedEventArgs e)
    {
        Product? product = e.NewProduct;
        string? url = product?.BestStore?.Url ?? product?.Stores.FirstOrDefault()?.Url;
        if (!string.IsNullOrWhiteSpace(url))
            NavigationRequested?.Invoke(url!);
    }
    #endregion

    #region Methods (public)
    /// <summary>Solicita navegación manual (barra de direcciones o cambio de país).</summary>
    public void RequestNavigation(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        NavigationRequested?.Invoke(url.Trim());
    }

    /// <summary>El control informa de la URL cargada al terminar de navegar (para el título del widget).</summary>
    public void SetCurrentUrl(string url) => CurrentUrl = url ?? string.Empty;

    public override void Dispose()
    {
        SharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
    }

    public override void LoadConfig()
    {
    }

    public override void SaveConfig()
    {
    }
    #endregion
}
