using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Fila de la tabla de resultados de auditoría (formato "largo": una fila por celda juego×categoría).
/// </summary>
public sealed class AuditRow
{
    public string GameTitle { get; init; } = string.Empty;

    /// <summary>Juego de la colección al que pertenece la fila (para fijar el SelectedGame al clicarla). Null si
    /// el resultado no trae juego.</summary>
    public Game? Game { get; init; }

    /// <summary>Categoría (columna del Excel) de esta fila; incluye los <see cref="MediaType"/> que agrega, usados
    /// por el filtro por tipo seleccionado.</summary>
    public MediaAuditCategory Category { get; init; } = null!;

    /// <summary>Nombre de la categoría, para mostrar en la tabla.</summary>
    public string CategoryName => Category.ExcelColumn;

    public int ExcelCount { get; init; }
    public int Mm4lbCount { get; init; }
    public int Diff => Mm4lbCount - ExcelCount;
    public AuditStatus Status { get; init; }

    public string StatusText => Status switch
    {
        AuditStatus.Missing => MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.AuditPanel_StatusMissing_Label] ?? "Missing",
        AuditStatus.Extra => MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.AuditPanel_StatusExtra_Label] ?? "Extra",
        _ => MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.AuditPanel_StatusOk_Label] ?? "OK"
    };

    /// <summary>La categoría de esta fila cubre el tipo de media indicado (filtro por el tipo seleccionado).</summary>
    public bool CoversType(int mediaTypeKey) =>
        Category.IsVideo ? MediaType.IsVideo(mediaTypeKey) : Category.Types.Any(t => t.Key == mediaTypeKey);
}

/// <summary>
/// View model del control autónomo <see cref="Views.AuditPanelControl"/>: pide un Excel de auditoría de
/// LaunchBox, lanza el chequeo contra la plataforma seleccionada (<see cref="MediaAuditService"/>) y expone
/// los resultados como filas para la tabla + un resumen. Solo escucha eventos externos (cambio de tipo de
/// media) mientras el filtro por tipo está activo y el control cargado, así que no fuga como VM transitorio.
/// </summary>
public class AuditPanelViewModel : ObservableObject
{
    #region Attributes
    private readonly MediaAuditService _mediaAuditService;
    private readonly SharedDataService _sharedDataService;
    private readonly WindowService _windowService;
    private readonly DialogsService _dialogsService;
    private readonly ExceptionService _exceptionService;

    /// <summary>Todas las celdas del último chequeo; <see cref="Rows"/> es la vista filtrada y ordenada.</summary>
    private List<AuditRow> _allRows = new();

    private AsyncRelayCommand? _runCheckCommand;

    // Estado de ordenación de la tabla (null = sin ordenar).
    private string? _sortColumn;
    private bool _sortAscending = true;

    // Suscripción al tipo de media seleccionado: solo activa mientras el control está cargado Y el filtro por
    // tipo está activo, para no fugar el VM transitorio.
    private bool _attached;
    private bool _typeSubscribed;
    private bool _platformSubscribed;
    private bool _gameSubscribed;

    /// <summary>Ruta del último Excel auditado, para re-ejecutar el chequeo al cambiar de plataforma.</summary>
    private string? _lastExcelPath;
    #endregion

    #region Properties (observable)
    private ObservableCollection<AuditRow> _rows = new();
    /// <summary>Filas visibles en la tabla (según filtros y orden).</summary>
    public ObservableCollection<AuditRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>Filas mostradas en la tabla (tras filtros): el "X" del contador "Showing X of Y".</summary>
    public int ShownRowsCount => _rows.Count;

    /// <summary>Total de filas del último chequeo (sin filtrar): el "Y" del contador "Showing X of Y".</summary>
    public int TotalRowsCount => _allRows.Count;

    private bool _showOnlyDiscrepancies = true;
    /// <summary>Si la tabla oculta las celdas coincidentes (Match). Activo por defecto.</summary>
    public bool ShowOnlyDiscrepancies
    {
        get => _showOnlyDiscrepancies;
        set { if (SetProperty(ref _showOnlyDiscrepancies, value)) { ApplyFilter(); } }
    }

    private bool _filterBySelectedType;
    /// <summary>
    /// Si la tabla se filtra por el tipo de media seleccionado en la app. Mientras está activo, el VM escucha
    /// los cambios de tipo para re-filtrar; al desactivarlo (o descargar el control) deja de escuchar.
    /// </summary>
    public bool FilterBySelectedType
    {
        get => _filterBySelectedType;
        set { if (SetProperty(ref _filterBySelectedType, value)) { UpdateTypeSubscription(); ApplyFilter(); } }
    }

    private bool _isRunning;
    /// <summary>Chequeo en curso (activa el ProgressRing y desactiva el comando).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    private bool _hasRun;
    /// <summary>Ya se ejecutó al menos un chequeo (controla el estado vacío / muestra el resumen).</summary>
    public bool HasRun
    {
        get => _hasRun;
        private set => SetProperty(ref _hasRun, value);
    }

    private string _auditedFileName = string.Empty;
    /// <summary>Nombre del fichero Excel del último chequeo (p. ej. "audit.xlsx"), sin ruta.</summary>
    public string AuditedFileName
    {
        get => _auditedFileName;
        private set => SetProperty(ref _auditedFileName, value);
    }

    private int _gamesCompared;
    public int GamesCompared { get => _gamesCompared; private set => SetProperty(ref _gamesCompared, value); }

    private int _gamesWithDiscrepancies;
    public int GamesWithDiscrepancies { get => _gamesWithDiscrepancies; private set => SetProperty(ref _gamesWithDiscrepancies, value); }

    private int _rowsNotMatched;
    /// <summary>Filas del Excel sin juego cargado en la colección.</summary>
    public int RowsNotMatched { get => _rowsNotMatched; private set => SetProperty(ref _rowsNotMatched, value); }

    private int _gamesNotInExcel;
    /// <summary>Juegos de la colección sin fila en el Excel.</summary>
    public int GamesNotInExcel { get => _gamesNotInExcel; private set => SetProperty(ref _gamesNotInExcel, value); }

    private int _wrongMediaFiles;
    /// <summary>Ficheros de media descuadrados: suma de |Δ| (los que faltan más los que sobran) en todas las celdas.</summary>
    public int WrongMediaFiles { get => _wrongMediaFiles; private set => SetProperty(ref _wrongMediaFiles, value); }

    private int _totalMediaFiles;
    /// <summary>Total de ficheros de media considerados: suma por celda del mayor lado (LaunchBox o MM4LB), de modo
    /// que los descuadrados nunca superan el total.</summary>
    public int TotalMediaFiles { get => _totalMediaFiles; private set => SetProperty(ref _totalMediaFiles, value); }

    // Totales de cada pill (X/Y): el universo del que la métrica es una parte.
    private int TotalExcelGames => _gamesCompared + _rowsNotMatched;        // filas del Excel = comparados ∪ sin juego
    private int TotalCollectionGames => _gamesCompared + _gamesNotInExcel;  // colección = comparados ∪ sin fila

    /// <summary>Comparados / juegos del Excel.</summary>
    public string GamesComparedText => $"{_gamesCompared}/{TotalExcelGames}";
    /// <summary>Con discrepancia / comparados.</summary>
    public string GamesWithDiscrepanciesText => $"{_gamesWithDiscrepancies}/{_gamesCompared}";
    /// <summary>Filas del Excel sin juego / juegos del Excel.</summary>
    public string RowsNotMatchedText => $"{_rowsNotMatched}/{TotalExcelGames}";
    /// <summary>Juegos de la colección sin fila / juegos de la colección.</summary>
    public string GamesNotInExcelText => $"{_gamesNotInExcel}/{TotalCollectionGames}";
    /// <summary>Ficheros de media descuadrados / total de ficheros considerados.</summary>
    public string WrongMediaFilesText => $"{_wrongMediaFiles}/{_totalMediaFiles}";

    private string _warnings = string.Empty;
    /// <summary>Avisos no fatales del chequeo (uno por línea), mostrados en un InfoBar.</summary>
    public string Warnings
    {
        get => _warnings;
        private set { if (SetProperty(ref _warnings, value)) { OnPropertyChanged(nameof(HasWarnings)); } }
    }

    public bool HasWarnings => _warnings.Length > 0;
    #endregion

    #region Commands
    /// <summary>Abre el selector de Excel y ejecuta el chequeo contra la plataforma seleccionada.</summary>
    public AsyncRelayCommand RunCheckCommand => _runCheckCommand ??= new AsyncRelayCommand(RunCheckAsync);
    #endregion

    #region Constructor
    public AuditPanelViewModel(MediaAuditService mediaAuditService, SharedDataService sharedDataService,
        WindowService windowService, DialogsService dialogsService, ExceptionService exceptionService)
    {
        _mediaAuditService = mediaAuditService ?? throw new ArgumentNullException(nameof(mediaAuditService));
        _sharedDataService = sharedDataService ?? throw new ArgumentNullException(nameof(sharedDataService));
        _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        _dialogsService = dialogsService ?? throw new ArgumentNullException(nameof(dialogsService));
        _exceptionService = exceptionService ?? throw new ArgumentNullException(nameof(exceptionService));
    }
    #endregion

    #region Lifecycle (lo llama el control en Loaded/Unloaded)
    /// <summary>El control se cargó: escucha cambios de plataforma (para re-chequear), de juego (para reflejar la
    /// selección de la app en la tabla) y de tipo (si el filtro por tipo está activo).</summary>
    public void Attach()
    {
        _attached = true;
        if (!_platformSubscribed) { _sharedDataService.SelectedPlatformChanged += OnSelectedPlatformChanged; _platformSubscribed = true; }
        if (!_gameSubscribed) { _sharedDataService.SelectedGameChanged += OnSelectedGameChanged; _gameSubscribed = true; }
        UpdateTypeSubscription();
    }

    /// <summary>El control se descargó: suelta las escuchas (VM transitorio → sin fugas).</summary>
    public void Detach()
    {
        _attached = false;
        if (_platformSubscribed) { _sharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged; _platformSubscribed = false; }
        if (_gameSubscribed) { _sharedDataService.SelectedGameChanged -= OnSelectedGameChanged; _gameSubscribed = false; }
        UpdateTypeSubscription();
    }
    #endregion

    #region Sorting (lo llama el control desde el evento Sorting del DataGrid)
    /// <summary>Ordena por la columna indicada; <paramref name="ascending"/> null = sin ordenar.</summary>
    public void Sort(string? column, bool? ascending)
    {
        _sortColumn = ascending.HasValue ? column : null;
        _sortAscending = ascending ?? true;
        ApplyFilter();
    }
    #endregion

    #region Selección (lo llama el control desde el evento SelectionChanged del DataGrid)
    /// <summary>
    /// Pide al control seleccionar (y hacer scroll a) una fila de la tabla; <c>null</c> = limpiar la selección.
    /// Lo dispara el cambio de juego seleccionado en la app, para reflejarlo en la tabla.
    /// </summary>
    public event EventHandler<AuditRow?>? SelectRowRequested;

    /// <summary>Al seleccionar una fila, fija el juego seleccionado de la app (si la fila trae un juego).</summary>
    public void OnRowSelected(AuditRow row)
    {
        if (row?.Game != null)
        {
            _sharedDataService.SelectedGame = row.Game;
        }
    }
    #endregion

    #region Methods (private)
    private async Task RunCheckAsync()
    {
        if (_sharedDataService.SelectedPlatform == null)
        {
            await _dialogsService.AlertAsync(_windowService.ActiveXamlRoot!, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.AuditPanel_Dialog_Title] ?? "Media audit", MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.AuditPanel_SelectPlatform_Text] ?? "Select a platform first.", MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Common_OK_Label] ?? "OK");
            return;
        }

        // App unpackaged: el picker necesita inicializarse con el handle de la ventana activa.
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".xlsx");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_windowService.ActiveWindow));

        StorageFile file = await picker.PickSingleFileAsync();
        if (file == null) { return; }   // cancelado

        _lastExcelPath = file.Path;
        await RunCheckForFileAsync(file.Path);
    }

    /// <summary>
    /// Ejecuta el chequeo con el Excel indicado contra la plataforma seleccionada. Lo comparten el comando (tras
    /// elegir fichero) y el re-chequeo al cambiar de plataforma. Guarda silenciosa: sin plataforma o con otro
    /// chequeo en curso, no hace nada.
    /// </summary>
    private async Task RunCheckForFileAsync(string excelPath)
    {
        Platform? platform = _sharedDataService.SelectedPlatform;
        if (platform == null || IsRunning) { return; }

        try
        {
            IsRunning = true;
            MediaAuditResult result = await _mediaAuditService.RunAuditAsync(platform, excelPath);
            Populate(result, platform, System.IO.Path.GetFileName(excelPath));
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.AuditPanel_ExcelError_Text] ?? "The audit Excel could not be processed.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Aplana el resultado en filas de tabla y actualiza el resumen.</summary>
    private void Populate(MediaAuditResult result, Platform platform, string fileName)
    {
        _allRows = result.Games
            .SelectMany(g => g.Cells.Select(c => new AuditRow
            {
                GameTitle = g.Game?.Title ?? g.ExcelTitle,
                Game = g.Game,
                Category = c.Category,
                ExcelCount = c.ExcelCount,
                Mm4lbCount = c.Mm4lbCount,
                Status = c.Status
            }))
            .ToList();

        GamesCompared = result.Games.Count;
        GamesWithDiscrepancies = result.Games.Count(g => g.HasDiscrepancy);
        RowsNotMatched = result.RowsNotMatched.Count;
        GamesNotInExcel = result.GamesNotInExcel.Count;

        // Ficheros de media descuadrados sobre el total considerado (a nivel de fichero, no de juego). "Mal" = suma
        // de |Δ| por celda (faltan + sobran); el total suma el mayor lado de cada celda, así los descuadrados ≤ total.
        WrongMediaFiles = _allRows.Sum(r => Math.Abs(r.Diff));
        TotalMediaFiles = _allRows.Sum(r => Math.Max(r.ExcelCount, r.Mm4lbCount));

        // Las pills muestran "X/Y" (getters computados): notificar tras fijar los conteos.
        OnPropertyChanged(nameof(GamesComparedText));
        OnPropertyChanged(nameof(GamesWithDiscrepanciesText));
        OnPropertyChanged(nameof(RowsNotMatchedText));
        OnPropertyChanged(nameof(GamesNotInExcelText));
        OnPropertyChanged(nameof(WrongMediaFilesText));

        AuditedFileName = fileName;
        Warnings = result.Warnings.Count == 0 ? string.Empty : string.Join(Environment.NewLine, result.Warnings);
        HasRun = true;

        ApplyFilter();
    }

    /// <summary>
    /// Reconstruye <see cref="Rows"/> desde <see cref="_allRows"/> aplicando los filtros (discrepancias y tipo
    /// seleccionado) y el orden activo, en una sola notificación.
    /// </summary>
    private void ApplyFilter()
    {
        IEnumerable<AuditRow> view = _allRows;

        if (_showOnlyDiscrepancies)
        {
            view = view.Where(r => r.Status != AuditStatus.Match);
        }

        if (_filterBySelectedType)
        {
            int? key = _sharedDataService.SelectedImageSet?.Type?.Key;
            if (key.HasValue)
            {
                view = view.Where(r => r.CoversType(key.Value));
            }
        }

        view = ApplySort(view);

        Rows = new ObservableCollection<AuditRow>(view);

        // Contador "Showing X of Y" (filas mostradas / total del chequeo).
        OnPropertyChanged(nameof(ShownRowsCount));
        OnPropertyChanged(nameof(TotalRowsCount));
    }

    /// <summary>Ordena la vista por la columna activa (o la deja igual si no hay orden), con clave tipada por columna.</summary>
    private IEnumerable<AuditRow> ApplySort(IEnumerable<AuditRow> rows)
    {
        switch (_sortColumn)
        {
            case "GameTitle": return _sortAscending ? rows.OrderBy(r => r.GameTitle) : rows.OrderByDescending(r => r.GameTitle);
            case "CategoryName": return _sortAscending ? rows.OrderBy(r => r.CategoryName) : rows.OrderByDescending(r => r.CategoryName);
            case "ExcelCount": return _sortAscending ? rows.OrderBy(r => r.ExcelCount) : rows.OrderByDescending(r => r.ExcelCount);
            case "Mm4lbCount": return _sortAscending ? rows.OrderBy(r => r.Mm4lbCount) : rows.OrderByDescending(r => r.Mm4lbCount);
            case "Diff": return _sortAscending ? rows.OrderBy(r => r.Diff) : rows.OrderByDescending(r => r.Diff);
            case "Status": return _sortAscending ? rows.OrderBy(r => r.Status) : rows.OrderByDescending(r => r.Status);
            default: return rows;
        }
    }

    /// <summary>Cambió el tipo de media seleccionado en la app: re-filtra (solo se escucha con el filtro activo).</summary>
    private void OnSelectedImageSetChanged(object? sender, ImageSetChangedEventArgs e) => ApplyFilter();

    /// <summary>
    /// Cambió la plataforma seleccionada: si ya se corrió un chequeo y el Excel sigue existiendo, se re-ejecuta
    /// contra la nueva plataforma con el mismo fichero (mantiene la tabla en sync con la plataforma activa).
    /// </summary>
    private async void OnSelectedPlatformChanged(object? sender, PlatformChangedEventArgs e)
    {
        if (!HasRun || string.IsNullOrEmpty(_lastExcelPath) || !System.IO.File.Exists(_lastExcelPath)) { return; }
        await RunCheckForFileAsync(_lastExcelPath);
    }

    /// <summary>
    /// Cambió el juego seleccionado en la app: selecciona su fila en la tabla (la primera visible del juego) o
    /// limpia la selección si no tiene fila visible. La selección resultante vuelve a fijar el mismo juego vía
    /// <see cref="OnRowSelected"/>, pero el <c>ReferenceEquals</c> de SharedDataService no re-emite → sin bucle.
    /// </summary>
    private void OnSelectedGameChanged(object? sender, GameChangedEventArgs e)
    {
        AuditRow? row = e.NewGame == null ? null : Rows.FirstOrDefault(r => ReferenceEquals(r.Game, e.NewGame));
        SelectRowRequested?.Invoke(this, row);
    }

    /// <summary>Suscribe/desuscribe la escucha del tipo según (control cargado ∧ filtro por tipo activo).</summary>
    private void UpdateTypeSubscription()
    {
        bool shouldSubscribe = _attached && _filterBySelectedType;
        if (shouldSubscribe && !_typeSubscribed)
        {
            _sharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
            _typeSubscribed = true;
        }
        else if (!shouldSubscribe && _typeSubscribed)
        {
            _sharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
            _typeSubscribed = false;
        }
    }
    #endregion
}
