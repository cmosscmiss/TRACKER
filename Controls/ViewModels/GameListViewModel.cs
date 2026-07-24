using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del control GamesList.  
/// Gestiona filtros, sincronización con SharedDataService y selección de juegos.
/// </summary>
public class GameListViewModel : WidgetViewModelBase
{
    #region Attributes  
    private RelayCommand? _filtersChangedCommand;
    private bool _filtersEnabled;
    private string _filterBy = string.Empty;
    /// <summary>
    /// Último juego seleccionado no nulo. Sirve de red de seguridad al refiltrar: un refiltrado transitorio
    /// con la lista vacía (p. ej. justo antes de que termine el matching del nuevo image type) anularía la
    /// selección; conservarla aquí permite restaurarla en el refiltrado bueno posterior.
    /// </summary>
    private Game? _lastSelectedGame;
    #endregion

    #region Events
    /// <summary>
    /// Evento que solicita a la vista desplazar el ListView hacia un juego concreto.
    /// </summary>
    public event Action<Game>? RequestScrollIntoView;
    #endregion

    #region Properties
    /// <summary>
    /// Conjunto de filtros activos aplicados a la lista de juegos.
    /// </summary>
    public Filters ActiveFilters { get; protected set; } = new();

    /// <summary>
    /// Texto usado para filtrar juegos por título.
    /// </summary>
    public string FilterBy
    {
        get => _filterBy;
        set
        {
            SetProperty(ref _filterBy, value);
            SetGames();
        }
    }

    /// <summary>
    /// Indica si los filtros de imagen están activos.
    /// </summary>
    public bool FiltersEnabled
    {
        get => _filtersEnabled;
        set
        {
            SetProperty(ref _filtersEnabled, value);
            SetGames();
        }
    }
    #endregion

    #region Commands
    /// <summary>
    /// Command ejecutado cuando cambian los filtros de imagen.
    /// </summary>
    public RelayCommand FiltersChangedCommand =>
        _filtersChangedCommand ??= new RelayCommand(OnFiltersChanged);

    /// <summary>
    /// Maneja cambios en filtros de imagen.
    /// </summary>
    private void OnFiltersChanged()
    {
        SyncFiltersEnabledWithImageFilters();
        SetGames();
    }
    #endregion

    #region Constructors
    /// <summary>
    /// Constructor del ViewModel.  
    /// Se suscribe a eventos globales y solicita estado inicial.
    /// </summary>
    public GameListViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        _sharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
        _sharedDataService.SelectedGameImagesChanged += OnSelectedGameImagesChanged;
        _sharedDataService.SelectedGameChanged += OnSelectedGameChanged;
        _sharedDataService.NotifyInitialState();
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Se ejecuta cuando cambia el ImageSet global.
    ///
    /// Ya NO se refiltra aquí cuando hay un set nuevo: este evento se dispara ANTES del matching, con
    /// <c>game.Images</c> aún poblado con los conteos del tipo anterior. Con un filtro por nº de imágenes activo,
    /// refiltrar en este momento cambiaba la selección de forma espuria (el juego actual se caía del filtro por
    /// conteo obsoleto), lo que disparaba <c>SelectedGameChanged</c> y provocaba una SEGUNDA carga alta-res en el
    /// dashboard (doble "Loading high-resolution binaries completed"). El refiltrado real se hace en
    /// <see cref="OnSelectedGameImagesChanged"/> (post-matching), ya con los conteos correctos.
    /// </summary>
    private void OnSelectedImageSetChanged(object? sender, ImageSetChangedEventArgs e)
    {
        if (e.NewImageSet == null)
            SharedDataService.GamesFiltered.Clear();
    }

    /// <summary>
    /// Se ejecuta después de (re)emparejar las imágenes del set seleccionado con los juegos, es decir cuando
    /// <c>game.Images</c> ya está repoblado con el image type nuevo. Refiltra aquí porque
    /// <see cref="OnSelectedImageSetChanged"/> se dispara ANTES del matching: con un filtro por número de
    /// imágenes activo (p. ej. "&gt; 1"), filtrar en ese momento veía <c>game.Images.Count</c> a 0/obsoleto y
    /// dejaba la lista vacía al volver a un image type.
    /// </summary>
    private void OnSelectedGameImagesChanged(object? sender, GameImagesChangedEventArgs e)
    {
        SetGames();
    }

    /// <summary>
    /// Se ejecuta cuando cambia el juego seleccionado, también desde fuera del control (p. ej. al
    /// seleccionar en Images Audit una imagen de un juego distinto). Solicita desplazar la lista hasta
    /// el nuevo juego, que <see cref="SetGames"/> no cubre porque solo se invoca al refiltrar.
    /// </summary>
    private void OnSelectedGameChanged(object? sender, GameChangedEventArgs e)
    {
        if (e.NewGame != null)
            RequestScrollIntoView?.Invoke(e.NewGame);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Sincroniza FiltersEnabled con el estado real de los filtros de imagen.
    /// </summary>
    private void SyncFiltersEnabledWithImageFilters()
    {
        bool anyImageFilterActive = ActiveFilters.Images.HasAny;

        if (!anyImageFilterActive && FiltersEnabled)
            FiltersEnabled = false;
        else if (anyImageFilterActive && !FiltersEnabled)
            FiltersEnabled = true;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Limpia suscripciones globales al destruir el ViewModel.
    /// </summary>
    public override void Dispose()
    {
        _sharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
        _sharedDataService.SelectedGameImagesChanged -= OnSelectedGameImagesChanged;
        _sharedDataService.SelectedGameChanged -= OnSelectedGameChanged;
    }

    /// <summary>
    /// Carga configuración persistida (no implementado).
    /// </summary>
    public override void LoadConfig()
    {
    }

    /// <summary>
    /// Guarda configuración persistida del control.
    /// </summary>
    public override void SaveConfig()
    {
        _appSettings.GameListControl.SelectedGame = _sharedDataService.SelectedGame?.Title ?? string.Empty;
    }

    /// <summary>
    /// Recalcula la lista filtrada de juegos y restaura la selección.
    /// </summary>
    public void SetGames()
    {
        var previousSelection = SharedDataService.SelectedGame ?? _lastSelectedGame;

        Platform? platform = SharedDataService.SelectedPlatform;
        if (platform == null) { return; }

        var source = platform.Games.AsEnumerable();
        source = ActiveFilters.ApplyGlobalFilters(source);

        if (FiltersEnabled)
            source = ActiveFilters.ApplyImageFilters(source);

        if (!string.IsNullOrWhiteSpace(FilterBy))
        {
            source = source.Where(g =>
                g.Title.Contains(FilterBy, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = source.ToList();

        // Reconciliar en sitio en vez de Clear()+Add(): la ListView del GameList enlaza SelectedItem a SelectedGame
        // en TwoWay, así que un Clear() total deja la lista sin selección y ESCRIBE SelectedGame = null por el
        // binding (aunque el juego siga en la nueva lista). Ese blip null→juego dispara SelectedGameChanged y, con
        // él, una SEGUNDA carga alta-res en el dashboard (doble "Loading high-resolution binaries completed"), más
        // un parpadeo. Sincronizando en sitio, el juego seleccionado nunca sale de la colección si sigue presente,
        // así que la ListView no lo deselecciona y no hay blip.
        SyncGamesFiltered(filtered);

        var restored = filtered.FirstOrDefault(g => g == previousSelection);
        SharedDataService.SelectedGame = restored ?? filtered.FirstOrDefault();

        if (SharedDataService.SelectedGame != null)
        {
            _lastSelectedGame = SharedDataService.SelectedGame;
            RequestScrollIntoView?.Invoke(SharedDataService.SelectedGame);
        }
    }

    /// <summary>
    /// Sincroniza <see cref="SharedDataService.GamesFiltered"/> con <paramref name="desired"/> mutándola EN SITIO
    /// (quita lo que sobra, inserta/reordena lo que falta) en vez de Clear()+Add(). Así el elemento seleccionado
    /// nunca desaparece de la colección si sigue en la nueva lista, evitando que la ListView (SelectedItem TwoWay)
    /// escriba SelectedGame = null. Si el orden apenas cambia (p. ej. cambio de tipo de imagen sin cambiar
    /// filtros), el coste es O(n) y no genera ningún cambio de colección.
    /// </summary>
    private void SyncGamesFiltered(List<Game> desired)
    {
        var current = SharedDataService.GamesFiltered;
        var desiredSet = new HashSet<Game>(desired);

        // 1) Quitar (de atrás hacia delante, índices estables) lo que ya no está.
        for (int i = current.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(current[i]))
                current.RemoveAt(i);
        }

        // 2) Insertar/reordenar para casar el orden de 'desired'.
        for (int i = 0; i < desired.Count; i++)
        {
            Game game = desired[i];

            if (i < current.Count && ReferenceEquals(current[i], game))
                continue;

            int existing = -1;
            for (int j = i + 1; j < current.Count; j++)
            {
                if (ReferenceEquals(current[j], game)) { existing = j; break; }
            }

            if (existing >= 0)
                current.Move(existing, i);
            else
                current.Insert(i, game);
        }
    }
    #endregion
}
