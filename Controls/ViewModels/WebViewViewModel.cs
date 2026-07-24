using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MM4LB.Controls.ViewModels;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del WebViewControl.
/// 
/// Responsabilidades:
/// - Mantener el texto de búsqueda actual.
/// - Construir la URL de búsqueda según el motor seleccionado.
/// - Reaccionar al juego seleccionado.
/// - Emitir una petición de navegación cuando corresponda.
/// 
/// Este ViewModel no conoce WebView2, TextBox, KeyDown ni detalles visuales.
/// El control decide si navega o no según su estado visual, especialmente SlotIndex.
/// </summary>
public class WebViewViewModel : WidgetViewModelBase
{
    #region Fields
    private readonly ImageLoadingService _imageLoadingService;
    private readonly ExceptionService _exceptionService;

    private RelayCommand? _navigateToOriginalUrlCommand;
    private RelayCommand? _searchEngineChangedCommand;

    private string _searchString = string.Empty;
    private string _searchStringUrl = string.Empty;

    private bool _searchViaGoogle = true;
    private bool _searchViaSteamGridDb;
    private bool _isVideoSearch;
    private bool _isDownloadInProgress;
    #endregion

    #region Properties
    /// <summary>
    /// Texto base usado para construir la búsqueda.
    /// Normalmente se genera desde el juego, plataforma e image set seleccionados.
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

    /// <summary>
    /// Indica si el image type seleccionado es un vídeo. En ese caso la búsqueda se hace en YouTube y el toggle
    /// Google/SteamGridDB se sustituye por el icono (informativo) de YouTube.
    /// </summary>
    public bool IsVideoSearch
    {
        get => _isVideoSearch;
        private set
        {
            if (SetProperty(ref _isVideoSearch, value))
            {
                OnPropertyChanged(nameof(ShowGoogleToggle));
                OnPropertyChanged(nameof(ShowSteamDbToggle));
                OnPropertyChanged(nameof(ShowYoutubeIndicator));
            }
        }
    }

    /// <summary>Visibilidad del toggle de Google: solo cuando NO es búsqueda de vídeo y el motor activo es Google.</summary>
    public bool ShowGoogleToggle => !IsVideoSearch && SearchViaGoogle;

    /// <summary>Visibilidad del toggle de SteamGridDB: solo cuando NO es búsqueda de vídeo y el motor activo es SteamGridDB.</summary>
    public bool ShowSteamDbToggle => !IsVideoSearch && SearchViaSteamGridDb;

    /// <summary>Visibilidad del icono (informativo) de YouTube: solo en búsqueda de vídeo.</summary>
    public bool ShowYoutubeIndicator => IsVideoSearch;

    /// <summary>
    /// True mientras hay una descarga del navegador (imagen o vídeo) en curso. Sirve de cerrojo: bloquea lanzar
    /// otra descarga hasta que la actual termine, ya que las de vídeo pueden tardar y solaparlas confunde y
    /// multiplica la carga. Es de uso exclusivo del hilo de UI (se consulta y fija antes del primer await).
    /// </summary>
    public bool IsDownloadInProgress
    {
        get => _isDownloadInProgress;
        private set => SetProperty(ref _isDownloadInProgress, value);
    }
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
    public WebViewViewModel(SharedDataService sharedDataService, ImageLoadingService imageLoadingService, ExceptionService exceptionService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _imageLoadingService = imageLoadingService;
        _exceptionService = exceptionService;

        SearchViaGoogle = true;
        SearchViaSteamGridDb = false;

        SharedDataService.SelectedGameChanged += OnSelectedGameChanged;
        SharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
        SharedDataService.PropertyChanged += OnSharedDataServicePropertyChanged;

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

    /// <summary>
    /// Reacciona al cambio de image type: si pasa a/desde un tipo de vídeo, recalcula la URL (YouTube vs
    /// Google/SteamGridDB) y navega, además de actualizar qué icono se muestra en la toolbar.
    /// </summary>
    private void OnSelectedImageSetChanged(object? sender, ImageSetChangedEventArgs e)
    {
        RefreshFromCurrentSelection(requestNavigation: true);
    }

    /// <summary>
    /// Reacciona al cambio de la región activa (la fija el GameImagesRegionDashboard): recalcula la búsqueda para
    /// incluir/quitar la región como sufijo y navega.
    /// </summary>
    private void OnSharedDataServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.SelectedRegion))
        {
            RefreshFromCurrentSelection(requestNavigation: true);
        }
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Actualiza el texto de búsqueda desde el estado actual de la aplicación.
    /// </summary>
    private void RefreshFromCurrentSelection(bool requestNavigation)
    {
        UpdateVideoSearchMode();

        string searchString = BuildSearchStringFromCurrentSelection();

        if (string.IsNullOrWhiteSpace(searchString))
        {
            return;
        }

        SetSearchString(searchString, requestNavigation);
    }

    /// <summary>
    /// Determina si el image type seleccionado es un vídeo (Video Snap, Theme Video o vídeo de plataforma) y
    /// actualiza <see cref="IsVideoSearch"/>, que decide el motor (YouTube) y los iconos de la toolbar.
    /// </summary>
    private void UpdateVideoSearchMode()
    {
        MediaType? type = SharedDataService.SelectedImageSet?.Type;
        IsVideoSearch = type != null && (MediaType.IsVideo(type.Key) || MediaType.IsPlatformVideo(type.Key));
    }

    /// <summary>
    /// Construye el texto de búsqueda usando juego, plataforma e image set.
    /// </summary>
    private string BuildSearchStringFromCurrentSelection()
    {
        string? gameTitle = SharedDataService.SelectedGame?.Title;
        string? platformName = SharedDataService.SelectedPlatform?.Name;
        string? imageSetType = SharedDataService.SelectedPlatform?.SelectedImageSet?.Type.Value;

        // Región como sufijo adicional: solo tiene valor cuando el GameImagesRegionDashboard está activo y hay una
        // región favorita seleccionada (para "otras regiones"/"sin región" y con el dashboard normal es null).
        string? region = SharedDataService.SelectedRegion?.Value;

        return string.Join(" ", new[]
            {
                gameTitle,
                platformName,
                imageSetType,
                region
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));
    }

    /// <summary>
    /// Selecciona el motor de búsqueda activo y recalcula la URL.
    /// </summary>
    /// <param name="useSteamGridDb"></param>
    /// <param name="requestNavigation"></param>
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

        SearchStringUrl = IsVideoSearch
            ? Utilities.ConvertToYoutubeSearchString(SearchString)
            : Utilities.ConvertToSearchString(SearchString, SearchViaSteamGridDb);
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
    /// Añade al juego seleccionado una imagen elegida en el navegador (mediante el menú
    /// contextual, doble clic o Ctrl+clic) y la deja seleccionada.
    ///
    /// La imagen se descarga en la carpeta del image set seleccionado, por lo que persiste y
    /// vuelve a emparejarse con el juego al recargar la plataforma. El dashboard se actualiza
    /// automáticamente porque su lista y su imagen seleccionada están enlazadas a
    /// <see cref="SharedDataService.GameImages"/> y <see cref="SharedDataService.SelectedImage"/>.
    /// </summary>
    /// <param name="imageUrl">La URL absoluta de la imagen elegida en el navegador.</param>
    public async Task AddImageFromBrowserAsync(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        Game? game = SharedDataService.SelectedGame;
        PlatformImageSet? imageSet = SharedDataService.SelectedImageSet;

        if (game is null || imageSet is null || string.IsNullOrWhiteSpace(imageSet.FolderPath))
        {
            return;
        }

        // Cerrojo: si ya hay una descarga en curso, se ignora este gesto. El check y el set son síncronos
        // (sin await por medio), así que son atómicos en el hilo de UI y evitan la carrera del doble disparo.
        if (IsDownloadInProgress)
        {
            return;
        }

        IsDownloadInProgress = true;
        try
        {
            // Región de destino activa (fijada por el GameImagesRegionDashboard): si hay una región favorita
            // seleccionada, la imagen se descarga en su subcarpeta; si es null, en la raíz del set ("sin región").
            var region = SharedDataService.SelectedRegion;
            string? destinationFolder = region != null && !string.IsNullOrEmpty(region.Value)
                ? System.IO.Path.Combine(imageSet.FolderPath, region.Value)
                : null;

            GameImage image = await _imageLoadingService.AddImageFromUrlToGameAsync(imageUrl, game, imageSet, destinationFolder);

            if (!SharedDataService.GameImages.Contains(image))
            {
                SharedDataService.GameImages.Add(image);
            }

            SharedDataService.SelectedImage = image;
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, ex.Message);
        }
        finally
        {
            IsDownloadInProgress = false;
        }
    }

    /// <summary>
    /// Añade al juego seleccionado un vídeo elegido en el navegador de YouTube (menú contextual, doble clic o
    /// Ctrl+clic sobre un vídeo). El vídeo se descarga (YoutubeExplode) en la carpeta del image set de vídeo
    /// seleccionado y queda seleccionado, igual que una imagen añadida desde el navegador.
    /// </summary>
    /// <param name="videoUrl">URL del vídeo de YouTube elegido en el navegador.</param>
    public async Task AddVideoFromBrowserAsync(string videoUrl)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            return;
        }

        Game? game = SharedDataService.SelectedGame;
        PlatformImageSet? imageSet = SharedDataService.SelectedImageSet;

        if (game is null || imageSet is null || string.IsNullOrWhiteSpace(imageSet.FolderPath))
        {
            return;
        }

        // Cerrojo compartido con las descargas de imagen: una sola descarga del navegador a la vez.
        if (IsDownloadInProgress)
        {
            return;
        }

        IsDownloadInProgress = true;
        try
        {
            GameImage image = await _imageLoadingService.AddVideoFromYoutubeToGameAsync(videoUrl, game, imageSet);

            if (!SharedDataService.GameImages.Contains(image))
            {
                SharedDataService.GameImages.Add(image);
            }

            SharedDataService.SelectedImage = image;
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, ex.Message);
        }
        finally
        {
            IsDownloadInProgress = false;
        }
    }

    /// <summary>
    /// Limpia suscripciones globales.
    /// </summary>
    public override void Dispose()
    {
        SharedDataService.SelectedGameChanged -= OnSelectedGameChanged;
        SharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
        SharedDataService.PropertyChanged -= OnSharedDataServicePropertyChanged;
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