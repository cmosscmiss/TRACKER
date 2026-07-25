using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Linq;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del WebViewControl.
///
/// Responsabilidades:
/// - Mantener el texto de búsqueda actual.
/// - Construir la URL de búsqueda según el motor seleccionado.
/// - Reaccionar al juego/plataforma seleccionados.
/// - Emitir una petición de navegación cuando corresponda.
///
/// Este ViewModel no conoce WebView2, TextBox, KeyDown ni detalles visuales.
/// El control decide si navega o no según su estado visual, especialmente SlotIndex.
/// </summary>
public class WebViewViewModel : WidgetViewModelBase
{
    #region Fields
    private RelayCommand? _navigateToOriginalUrlCommand;
    private RelayCommand? _searchEngineChangedCommand;

    private string _searchString = string.Empty;
    private string _searchStringUrl = string.Empty;

    private bool _searchViaGoogle = true;
    private bool _searchViaSteamGridDb;
    #endregion

    #region Properties
    /// <summary>
    /// Texto base usado para construir la búsqueda.
    /// Normalmente se genera desde el juego y la plataforma seleccionados.
    /// </summary>
    public string SearchString
    {
        get => _searchString;
        set => SetSearchString(value, requestNavigation: true);
    }

    /// <summary>
    /// URL final que debería abrir el WebView.
    ///
    /// El control leerá esta propiedad cuando el widget pase de SlotIndex == -1
    /// a SlotIndex != -1.
    /// </summary>
    public string SearchStringUrl
    {
        get => _searchStringUrl;
        private set => SetProperty(ref _searchStringUrl, value);
    }

    /// <summary>
    /// Indica si el motor activo es Google.
    /// </summary>
    public bool SearchViaGoogle
    {
        get => _searchViaGoogle;
        private set
        {
            if (SetProperty(ref _searchViaGoogle, value))
            {
                OnPropertyChanged(nameof(ShowGoogleToggle));
            }
        }
    }

    /// <summary>
    /// Indica si el motor activo es SteamGridDB.
    /// </summary>
    public bool SearchViaSteamGridDb
    {
        get => _searchViaSteamGridDb;
        private set
        {
            if (SetProperty(ref _searchViaSteamGridDb, value))
            {
                OnPropertyChanged(nameof(ShowSteamDbToggle));
            }
        }
    }

    /// <summary>Visibilidad del toggle de Google: cuando el motor activo es Google.</summary>
    public bool ShowGoogleToggle => SearchViaGoogle;

    /// <summary>Visibilidad del toggle de SteamGridDB: cuando el motor activo es SteamGridDB.</summary>
    public bool ShowSteamDbToggle => SearchViaSteamGridDb;
    #endregion

    #region Events
    /// <summary>
    /// Evento emitido cuando el ViewModel solicita que se navegue a una URL.
    ///
    /// Importante:
    /// El ViewModel no decide si el WebView debe navegar realmente.
    /// El control puede ignorar este evento si SlotIndex == -1.
    /// </summary>
    public event Action<string>? NavigationRequested;
    #endregion

    #region Commands
    /// <summary>
    /// Fuerza la navegación a la URL calculada actualmente.
    /// </summary>
    public RelayCommand NavigateToOriginalUrlCommand =>
        _navigateToOriginalUrlCommand ??= new RelayCommand(RequestCurrentNavigation);

    /// <summary>
    /// Solicita navegar de nuevo a la URL actual.
    /// </summary>
    private void RequestCurrentNavigation()
    {
        if (string.IsNullOrWhiteSpace(SearchStringUrl))
        {
            RefreshFromCurrentSelection(requestNavigation: false);
        }

        if (!string.IsNullOrWhiteSpace(SearchStringUrl))
        {
            NavigationRequested?.Invoke(SearchStringUrl);
        }
    }

    /// <summary>
    /// Cambia entre Google y SteamGridDB.
    /// </summary>
    public RelayCommand SearchEngineChangedCommand =>
        _searchEngineChangedCommand ??= new RelayCommand(OnSearchEngineChanged);

    /// <summary>
    /// Alterna entre Google y SteamGridDB y recalcula la URL.
    /// </summary>
    private void OnSearchEngineChanged()
    {
        SetSearchEngine(!SearchViaSteamGridDb, requestNavigation: true);
    }
    #endregion

    #region Constructors
    public WebViewViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        SearchViaGoogle = true;
        SearchViaSteamGridDb = false;

        SharedDataService.SelectedGameChanged += OnSelectedGameChanged;

        // Si el juego ya estaba seleccionado antes de crear este ViewModel,
        // el evento SelectedGameChanged ya no se recibirá. Por eso leemos
        // el estado actual en la construcción.
        RefreshFromCurrentSelection(requestNavigation: false);
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Actualiza el texto de búsqueda al cambiar el juego seleccionado.
    /// </summary>
    private void OnSelectedGameChanged(object? sender, GameChangedEventArgs e)
    {
        RefreshFromCurrentSelection(requestNavigation: true);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Actualiza el texto de búsqueda desde el estado actual de la aplicación.
    /// </summary>
    private void RefreshFromCurrentSelection(bool requestNavigation)
    {
        string searchString = BuildSearchStringFromCurrentSelection();

        if (string.IsNullOrWhiteSpace(searchString))
        {
            return;
        }

        SetSearchString(searchString, requestNavigation);
    }

    /// <summary>
    /// Construye el texto de búsqueda usando juego y plataforma.
    /// </summary>
    private string BuildSearchStringFromCurrentSelection()
    {
        string? gameTitle = SharedDataService.SelectedGame?.Title;
        string? platformName = SharedDataService.SelectedPlatform?.Name;

        return string.Join(" ", new[]
            {
                gameTitle,
                platformName
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));
    }

    /// <summary>
    /// Selecciona el motor de búsqueda activo y recalcula la URL.
    /// </summary>
    private void SetSearchEngine(bool useSteamGridDb, bool requestNavigation)
    {
        SearchViaSteamGridDb = useSteamGridDb;
        SearchViaGoogle = !useSteamGridDb;
        UpdateSearchStringUrl(requestNavigation);
    }

    /// <summary>
    /// Actualiza el SearchString y recalcula la URL asociada.
    /// </summary>
    private void SetSearchString(string? searchString, bool requestNavigation)
    {
        string normalizedSearchString = searchString?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSearchString))
        {
            return;
        }

        bool changed = SetProperty(ref _searchString, normalizedSearchString, nameof(SearchString));
        if (changed || string.IsNullOrWhiteSpace(SearchStringUrl))
        {
            UpdateSearchStringUrl(requestNavigation);
        }
    }

    /// <summary>
    /// Recalcula la URL de búsqueda a partir del SearchString y del motor activo.
    /// </summary>
    private void UpdateSearchStringUrl(bool requestNavigation)
    {
        if (string.IsNullOrWhiteSpace(SearchString))
        {
            return;
        }

        SearchStringUrl = Utilities.ConvertToSearchString(SearchString, SearchViaSteamGridDb);
        if (requestNavigation)
        {
            NavigationRequested?.Invoke(SearchStringUrl);
        }
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Solicita navegación manual a una URL escrita por el usuario.
    ///
    /// El control decidirá si ejecuta o no la navegación en función de SlotIndex.
    /// </summary>
    public void RequestNavigation(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        SearchStringUrl = url.Trim();
        NavigationRequested?.Invoke(SearchStringUrl);
    }

    /// <summary>
    /// Resincroniza el ViewModel con el juego actualmente seleccionado,
    /// sin emitir navegación.
    ///
    /// Es útil cuando el widget se activa después de haber estado oculto.
    /// </summary>
    public void RefreshCurrentSelectionWithoutNavigation()
    {
        RefreshFromCurrentSelection(requestNavigation: false);
    }

    /// <summary>
    /// Limpia suscripciones globales.
    /// </summary>
    public override void Dispose()
    {
        SharedDataService.SelectedGameChanged -= OnSelectedGameChanged;
    }

    /// <summary>
    /// Carga configuración persistida.
    /// </summary>
    public override void LoadConfig()
    {
        bool useSteamGridDb = _appSettings.WebViewControl.SearchViaSteamGridDB;

        SetSearchEngine(useSteamGridDb, requestNavigation: false);
    }

    /// <summary>
    /// Guarda configuración persistida.
    /// </summary>
    public override void SaveConfig()
    {
        _appSettings.WebViewControl.SearchViaGoogle = SearchViaGoogle;
        _appSettings.WebViewControl.SearchViaSteamGridDB = SearchViaSteamGridDb;
    }
    #endregion
}
