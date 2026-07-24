using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model de la herramienta "Shared media" del widget Tools: escanea, para la plataforma seleccionada, los
/// medios (imagen y vídeo de JUEGO) que emparejan con AL MENOS 2 juegos, y los expone en dos vistas — tabla (una
/// fila por pareja juego↔media) y grid de miniaturas (una por media distinta). Hermano de
/// <see cref="OrphanToolViewModel"/> (mismo ciclo de vida Attach/Detach, filtro por tipo, vistas y pastillas), pero
/// sin borrado y con sincronización de selección con la app: seleccionar un juego resalta su fila; seleccionar una
/// imagen la resalta en el grid; y al revés (click en fila → juego; click en imagen → primer juego que la tiene).
/// </summary>
public sealed class SharedMediaToolViewModel : ObservableObject
{
    #region Nested types
    /// <summary>Una fila de la tabla: una pareja juego ↔ media compartida. Un media de N juegos genera N filas.</summary>
    public sealed class SharedMediaRow
    {
        public Game Game { get; }
        public GameImage Image { get; }

        public SharedMediaRow(Game game, GameImage image)
        {
            Game = game;
            Image = image;
        }
    }
    #endregion

    #region Attributes
    private readonly SharedDataService _sharedDataService;
    private readonly ImageMatchingService _imageMatchingService;
    private readonly ExceptionService _exceptionService;

    private List<GameImage> _allShared = new();
    private bool _platformSubscribed;
    private bool _imageSetSubscribed;
    private bool _selectionSubscribed;
    private bool _isTableView = true;
    private bool _filterBySelectedType;
    private bool _hasRun;
    private int _totalMediaCount;
    private long _totalMediaSizeKb;
    private int _totalTypesCount;
    private int _totalGames;
    private SharedMediaRow? _selectedRow;

    /// <summary>Evita bucles de selección mientras se propaga una selección (click ↔ SharedDataService).</summary>
    private bool _applyingSelection;
    #endregion

    #region Constructor
    public SharedMediaToolViewModel(SharedDataService sharedDataService, ImageMatchingService imageMatchingService, ProgressService progressService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, DialogsService dialogsService, WindowService windowService, ExceptionService exceptionService, IOptions<AppSettings> appSettings)
    {
        _sharedDataService = sharedDataService;
        _imageMatchingService = imageMatchingService;
        _exceptionService = exceptionService;

        // Grid de miniaturas reutilizando el ImageGridControl (decodifica al hacer scroll). ImageGridViewModel base
        // (no reacciona al juego seleccionado): se alimenta explícitamente con las media compartidas mostradas.
        IgViewModel = new ImageGridViewModel(sharedDataService, progressService, imageLoadingService, imageBinaryLoadingService, dialogsService, windowService, appSettings)
        {
            LazyLoadBinariesOnScroll = true,
        };
    }
    #endregion

    #region Properties
    /// <summary>Grid de miniaturas de las media compartidas MOSTRADAS (una por media distinta).</summary>
    public ImageGridViewModel IgViewModel { get; }

    /// <summary>Filas de la tabla MOSTRADAS: una por pareja juego ↔ media compartida.</summary>
    public ObservableRangeCollection<SharedMediaRow> Rows { get; } = new();

    /// <summary>Fila seleccionada en la tabla (TwoWay con el DataGrid). Al cambiarla por click, selecciona su juego.</summary>
    public SharedMediaRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
                return;

            if (_applyingSelection || value == null)
                return;

            _applyingSelection = true;
            try { _sharedDataService.SelectedGame = value.Game; }
            finally { _applyingSelection = false; }
        }
    }

    /// <summary>Vista activa: true = tabla, false = grid de miniaturas.</summary>
    public bool IsTableView
    {
        get => _isTableView;
        set
        {
            if (SetProperty(ref _isTableView, value))
            {
                OnPropertyChanged(nameof(IsGridView));
                OnPropertyChanged(nameof(SelectedViewValue));
            }
        }
    }

    public bool IsGridView => !_isTableView;

    /// <summary>Vista activa como cadena, para el <c>ExclusiveOptionsControl</c> (TwoWay).</summary>
    public string SelectedViewValue
    {
        get => _isTableView ? "Table" : "Grid";
        set => IsTableView = value != "Grid";
    }

    /// <summary>Si está activo, solo se muestran las media compartidas del tipo de medio seleccionado en la app.</summary>
    public bool FilterBySelectedType
    {
        get => _filterBySelectedType;
        set
        {
            if (SetProperty(ref _filterBySelectedType, value))
                ApplyFilter();
        }
    }

    /// <summary>True tras el primer escaneo (para mostrar la tabla/grid en vez del texto de vacío).</summary>
    public bool HasRun
    {
        get => _hasRun;
        private set => SetProperty(ref _hasRun, value);
    }

    /// <summary>Nº de media compartidas MOSTRADAS (tras el filtro); el "X" del "Showing X of Y".</summary>
    public int ShownCount => IgViewModel.Images.Count;

    /// <summary>Nº TOTAL de media compartidas de la plataforma (sin filtrar); el "Y" del "Showing X of Y".</summary>
    public int TotalSharedCount => _allShared.Count;

    /// <summary>True cuando ya se escaneó y no hay media compartida en la plataforma (para el texto de "sin resultados").</summary>
    public bool ShowEmpty => HasRun && _allShared.Count == 0;

    // --- Pastillas: valor "X/Y" + leyenda (estática en el XAML). Se basan en TODAS las compartidas, no en el filtro. ---

    /// <summary>Pastilla "Shared": compartidas / total de ficheros de medios.</summary>
    public string SharedRatio => $"{_allShared.Count}/{_totalMediaCount}";

    /// <summary>Pastilla "Size": tamaño de las compartidas / tamaño total en disco.</summary>
    public string SizeRatio => $"{FormatSizeKb(_allShared.Sum(i => i.FileSize))}/{FormatSizeKb(_totalMediaSizeKb)}";

    /// <summary>Pastilla "Types": tipos con compartidas / total de tipos con ficheros.</summary>
    public string TypesRatio => $"{_allShared.Select(i => i.Type?.Key ?? -1).Distinct().Count()}/{_totalTypesCount}";

    /// <summary>Pastilla "Games": juegos afectados (con alguna media compartida) / total de juegos de la plataforma.</summary>
    public string GamesRatio => $"{_allShared.SelectMany(i => i.LinkedGames).Distinct().Count()}/{_totalGames}";
    #endregion

    #region Methods (public)
    public void SelectTableView() => IsTableView = true;

    public void SelectGridView() => IsTableView = false;

    /// <summary>
    /// Reordena la tabla por la columna pulsada (Game/Type/File/Region). Con <paramref name="ascending"/> null (sin
    /// orden) se restaura el orden natural reaplicando el filtro.
    /// </summary>
    public void SortRows(string? tag, bool? ascending)
    {
        if (ascending == null || string.IsNullOrEmpty(tag))
        {
            ApplyFilter();
            return;
        }

        List<SharedMediaRow> items = Rows.ToList();
        IEnumerable<SharedMediaRow> ordered = tag switch
        {
            "Game" => ascending.Value ? items.OrderBy(r => r.Game.Title) : items.OrderByDescending(r => r.Game.Title),
            "Type" => ascending.Value ? items.OrderBy(r => r.Image.Type?.Value) : items.OrderByDescending(r => r.Image.Type?.Value),
            "Region" => ascending.Value ? items.OrderBy(r => r.Image.Region.Value) : items.OrderByDescending(r => r.Image.Region.Value),
            _ => ascending.Value ? items.OrderBy(r => r.Image.Name) : items.OrderByDescending(r => r.Image.Name),
        };

        Rows.ReplaceAll(ordered);
    }

    /// <summary>Empieza a escuchar cambios de plataforma, tipo y selección (juego/imagen), y lanza un primer escaneo. Idempotente.</summary>
    public void Attach()
    {
        if (!_platformSubscribed)
        {
            _sharedDataService.SelectedPlatformChanged += OnSelectedPlatformChanged;
            _platformSubscribed = true;
        }
        if (!_imageSetSubscribed)
        {
            _sharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
            _imageSetSubscribed = true;
        }
        if (!_selectionSubscribed)
        {
            _sharedDataService.SelectedGameChanged += OnSelectedGameChanged;
            _sharedDataService.SelectedImageChanged += OnSelectedImageChanged;
            IgViewModel.PropertyChanged += OnIgViewModelPropertyChanged;
            _selectionSubscribed = true;
        }

        _ = ScanAsync();
    }

    /// <summary>Deja de escuchar (el control se descargó del árbol visual).</summary>
    public void Detach()
    {
        if (_platformSubscribed)
        {
            _sharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged;
            _platformSubscribed = false;
        }
        if (_imageSetSubscribed)
        {
            _sharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
            _imageSetSubscribed = false;
        }
        if (_selectionSubscribed)
        {
            _sharedDataService.SelectedGameChanged -= OnSelectedGameChanged;
            _sharedDataService.SelectedImageChanged -= OnSelectedImageChanged;
            IgViewModel.PropertyChanged -= OnIgViewModelPropertyChanged;
            _selectionSubscribed = false;
        }
    }
    #endregion

    #region Methods (private)
    private async void OnSelectedPlatformChanged(object? sender, SharedDataService.PlatformChangedEventArgs e)
    {
        try
        {
            await ScanAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.SharedMediaTool_Scan_Error] ?? "Error scanning shared media.");
        }
    }

    /// <summary>Al cambiar el tipo de medio seleccionado, reaplica el filtro (solo tiene efecto si está activo).</summary>
    private void OnSelectedImageSetChanged(object? sender, SharedDataService.ImageSetChangedEventArgs e)
    {
        if (_filterBySelectedType)
            ApplyFilter();
    }

    /// <summary>Cambio EXTERNO de juego: si está en la tabla, selecciona su primera fila.</summary>
    private void OnSelectedGameChanged(object? sender, SharedDataService.GameChangedEventArgs e)
    {
        if (_applyingSelection)
            return;

        Game? game = _sharedDataService.SelectedGame;
        SharedMediaRow? row = game == null ? null : Rows.FirstOrDefault(r => r.Game.Equals(game));

        _applyingSelection = true;
        try { SelectedRow = row; }
        finally { _applyingSelection = false; }
    }

    /// <summary>Cambio EXTERNO de imagen: si está en el grid (por ruta), la selecciona.</summary>
    private void OnSelectedImageChanged(object? sender, SharedDataService.GameImageChangedEventArgs e)
    {
        if (_applyingSelection)
            return;

        GameImage? target = _sharedDataService.SelectedImage;
        GameImage? match = target == null
            ? null
            : IgViewModel.Images.FirstOrDefault(i => string.Equals(i.File, target.File, StringComparison.OrdinalIgnoreCase));

        _applyingSelection = true;
        try { IgViewModel.SelectedImage = match; }
        finally { _applyingSelection = false; }
    }

    /// <summary>
    /// Click en una imagen del grid → selecciona el PRIMER juego que la tiene. La imagen solo se fija como imagen
    /// GLOBAL seleccionada si su tipo coincide con el media type seleccionado en la app (si no, la selección global
    /// de imagen quedaría fuera del set activo).
    /// </summary>
    private void OnIgViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImageGridViewModel.SelectedImage) || _applyingSelection)
            return;

        GameImage? image = IgViewModel.SelectedImage;
        Game? first = image?.LinkedGames.FirstOrDefault();
        if (image == null || first == null)
            return;

        _applyingSelection = true;
        try
        {
            _sharedDataService.SelectedGame = first;

            int? selectedTypeKey = _sharedDataService.SelectedImageSet?.Type?.Key;
            if (selectedTypeKey.HasValue && image.Type?.Key == selectedTypeKey.Value)
                _sharedDataService.SelectedImage = image;
        }
        finally { _applyingSelection = false; }
    }

    /// <summary>
    /// Recalcula todas las media compartidas de la plataforma seleccionada (read-only, en hilo de fondo), guarda el
    /// conjunto completo y sus totales, y refresca las vistas según el filtro.
    /// </summary>
    private async Task ScanAsync()
    {
        Platform? platform = _sharedDataService.SelectedPlatform;

        SharedMediaScan scan = platform == null
            ? new SharedMediaScan(new List<GameImage>(), 0, 0, 0, 0)
            : await Task.Run(() => _imageMatchingService.ScanPlatformSharedMedia(platform));

        _allShared = scan.Shared;
        _totalMediaCount = scan.TotalMediaCount;
        _totalMediaSizeKb = scan.TotalMediaSizeKb;
        _totalTypesCount = scan.TypesWithMedia;
        _totalGames = scan.TotalGames;
        HasRun = true;

        ApplyFilter();
    }

    /// <summary>
    /// Vuelca en las vistas mostradas todas las media compartidas (o solo las del tipo seleccionado si el filtro
    /// está activo): el grid con las media distintas y la tabla con una fila por pareja juego ↔ media.
    /// </summary>
    private void ApplyFilter()
    {
        IEnumerable<GameImage> shown = _allShared;

        if (_filterBySelectedType)
        {
            int? key = _sharedDataService.SelectedImageSet?.Type?.Key;
            shown = key.HasValue ? _allShared.Where(m => m.Type?.Key == key.Value) : Enumerable.Empty<GameImage>();
        }

        List<GameImage> shownMedia = shown.ToList();

        IgViewModel.Images.ReplaceAll(shownMedia);
        Rows.ReplaceAll(shownMedia.SelectMany(m => m.LinkedGames.Select(g => new SharedMediaRow(g, m))));

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(ShownCount));
        OnPropertyChanged(nameof(TotalSharedCount));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(SharedRatio));
        OnPropertyChanged(nameof(SizeRatio));
        OnPropertyChanged(nameof(TypesRatio));
        OnPropertyChanged(nameof(GamesRatio));
    }

    /// <summary>Formatea un tamaño en KB (KB/MB/GB, con un decimal).</summary>
    private static string FormatSizeKb(long kb)
    {
        if (kb >= 1_000_000)
            return $"{kb / 1_000_000.0:0.0} GB";
        if (kb >= 1_000)
            return $"{kb / 1_000.0:0.0} MB";
        return $"{kb} KB";
    }
    #endregion
}
