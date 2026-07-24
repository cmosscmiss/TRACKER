using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model de la herramienta "Orphan media" del widget Tools: escanea, para la plataforma seleccionada, todos
/// los medios (imagen y vídeo de JUEGO) que no emparejan con ningún juego, y los expone en dos vistas — tabla y
/// grid de miniaturas — sobre el mismo <see cref="ImageGridViewModel"/>. Mantiene el conjunto completo de huérfanos
/// (<c>_allOrphans</c>, base de las pastillas de estadísticas) y una vista MOSTRADA (posiblemente filtrada por el
/// tipo de medio seleccionado). El escaneo es read-only y se relanza al cambiar de plataforma; el filtro se reaplica
/// al cambiar el tipo. El control (<c>OrphanToolControl</c>) llama a <see cref="Attach"/>/<see cref="Detach"/> en su
/// ciclo de vida visual (patrón de AuditPanelControl).
/// </summary>
public sealed class OrphanToolViewModel : ObservableObject
{
    #region Attributes
    private readonly SharedDataService _sharedDataService;
    private readonly ImageMatchingService _imageMatchingService;
    private readonly ImageLoadingService _imageLoadingService;
    private readonly FileSystemService _fileSystemService;
    private readonly ProgressService _progressService;
    private readonly DialogsService _dialogsService;
    private readonly WindowService _windowService;
    private readonly ExceptionService _exceptionService;

    private List<GameImage> _allOrphans = new();
    private RelayCommand? _deleteAllOrphansCommand;
    private bool _platformSubscribed;
    private bool _imageSetSubscribed;
    private bool _isTableView = true;
    private bool _filterBySelectedType;
    private bool _hasRun;
    private int _totalMediaCount;
    private long _totalMediaSizeKb;
    private int _totalTypesCount;
    #endregion

    #region Constructor
    public OrphanToolViewModel(SharedDataService sharedDataService, ImageMatchingService imageMatchingService, ProgressService progressService, ImageLoadingService imageLoadingService, ImageBinaryLoadingService imageBinaryLoadingService, FileSystemService fileSystemService, DialogsService dialogsService, WindowService windowService, ExceptionService exceptionService, IOptions<AppSettings> appSettings)
    {
        _sharedDataService = sharedDataService;
        _imageMatchingService = imageMatchingService;
        _imageLoadingService = imageLoadingService;
        _fileSystemService = fileSystemService;
        _progressService = progressService;
        _dialogsService = dialogsService;
        _windowService = windowService;
        _exceptionService = exceptionService;

        // Grid de miniaturas reutilizando el ImageGridControl (decodifica cada binario al hacer scroll). Es el
        // ImageGridViewModel base (no reacciona al juego seleccionado): se alimenta explícitamente con los huérfanos.
        IgViewModel = new ImageGridViewModel(sharedDataService, progressService, imageLoadingService, imageBinaryLoadingService, dialogsService, windowService, appSettings)
        {
            LazyLoadBinariesOnScroll = true,
        };
    }
    #endregion

    #region Properties
    /// <summary>Grid de miniaturas de los huérfanos MOSTRADOS; su colección <c>Images</c> alimenta también la tabla.</summary>
    public ImageGridViewModel IgViewModel { get; }

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

    /// <summary>Si está activo, solo se muestran los huérfanos del tipo de medio seleccionado en la app.</summary>
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

    /// <summary>Nº de huérfanos MOSTRADOS (tras el filtro); el "X" del "Showing X of Y".</summary>
    public int ShownCount => IgViewModel.Images.Count;

    /// <summary>Nº TOTAL de huérfanos de la plataforma (sin filtrar); el "Y" del "Showing X of Y".</summary>
    public int TotalOrphanCount => _allOrphans.Count;

    /// <summary>True cuando ya se escaneó y no hay huérfanos en la plataforma (para el texto de "sin resultados").</summary>
    public bool ShowEmpty => HasRun && _allOrphans.Count == 0;

    /// <summary>True cuando hay huérfanos MOSTRADOS que borrar.</summary>
    public bool HasOrphans => IgViewModel.Images.Count > 0;

    /// <summary>Borra de disco los huérfanos MOSTRADOS (todos, o los del tipo filtrado), con confirmación, progreso y undo.</summary>
    public RelayCommand DeleteAllOrphansCommand => _deleteAllOrphansCommand ??= new RelayCommand(OnDeleteAllOrphansClicked, () => HasOrphans);

    /// <summary>Total de ficheros de medios (imagen + vídeo de juego) de la plataforma.</summary>
    public int TotalMediaCount
    {
        get => _totalMediaCount;
        private set => SetProperty(ref _totalMediaCount, value);
    }

    // --- Pastillas: valor "X/Y" + leyenda (estática en el XAML). Se basan en TODOS los huérfanos, no en el filtro. ---

    /// <summary>Pastilla "Orphans": huérfanos / total de ficheros de medios.</summary>
    public string OrphansRatio => $"{_allOrphans.Count}/{_totalMediaCount}";

    /// <summary>Pastilla "Size": tamaño de los huérfanos / tamaño total en disco.</summary>
    public string SizeRatio => $"{FormatSizeKb(_allOrphans.Sum(i => i.FileSize))}/{FormatSizeKb(_totalMediaSizeKb)}";

    /// <summary>Pastilla "Types": tipos con huérfanos / total de tipos con ficheros.</summary>
    public string TypesRatio => $"{_allOrphans.Select(i => i.Type?.Key ?? -1).Distinct().Count()}/{_totalTypesCount}";

    /// <summary>Pastilla de tipo: nombre del tipo de medio más frecuente ENTRE los huérfanos (o "-").</summary>
    public string TopOrphanType => TopOrphanGroup?.Key is { Length: > 0 } name ? name : "-";

    /// <summary>Pastilla de tipo: huérfanos de ese tipo / total de huérfanos.</summary>
    public string TopTypeOrphanRatio => TopOrphanGroup is { } top ? $"{top.Count()}/{_allOrphans.Count}" : "-";

    private IGrouping<string, GameImage>? TopOrphanGroup =>
        _allOrphans.GroupBy(i => i.Type?.Value ?? string.Empty).OrderByDescending(g => g.Count()).FirstOrDefault();
    #endregion

    #region Methods (public)
    public void SelectTableView() => IsTableView = true;

    public void SelectGridView() => IsTableView = false;

    /// <summary>
    /// Reordena la vista MOSTRADA por la columna pulsada (Type/Name/Region). Con <paramref name="ascending"/> null
    /// (sin orden) se restaura el orden natural reaplicando el filtro.
    /// </summary>
    public void SortShown(string? tag, bool? ascending)
    {
        if (ascending == null || string.IsNullOrEmpty(tag))
        {
            ApplyFilter();
            return;
        }

        List<GameImage> items = IgViewModel.Images.ToList();
        IEnumerable<GameImage> ordered = tag switch
        {
            "Type" => ascending.Value ? items.OrderBy(i => i.Type?.Value) : items.OrderByDescending(i => i.Type?.Value),
            "Region" => ascending.Value ? items.OrderBy(i => i.Region.Value) : items.OrderByDescending(i => i.Region.Value),
            _ => ascending.Value ? items.OrderBy(i => i.Name) : items.OrderByDescending(i => i.Name),
        };

        IgViewModel.Images.ReplaceAll(ordered);
    }

    /// <summary>Empieza a escuchar los cambios de plataforma y de tipo, y lanza un primer escaneo. Idempotente.</summary>
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
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.OrphanTool_Scan_Error] ?? "Error scanning orphan media.");
        }
    }

    /// <summary>Al cambiar el tipo de medio seleccionado, reaplica el filtro (solo tiene efecto si está activo).</summary>
    private void OnSelectedImageSetChanged(object? sender, SharedDataService.ImageSetChangedEventArgs e)
    {
        if (_filterBySelectedType)
            ApplyFilter();
    }

    /// <summary>
    /// Recalcula todos los huérfanos de la plataforma seleccionada (read-only, en hilo de fondo), guarda el conjunto
    /// completo y sus totales, y refresca la vista mostrada según el filtro.
    /// </summary>
    private async Task ScanAsync()
    {
        Platform? platform = _sharedDataService.SelectedPlatform;

        OrphanScan scan = platform == null
            ? new OrphanScan(new List<GameImage>(), 0, 0, 0)
            : await Task.Run(() => _imageMatchingService.ScanPlatformOrphans(platform));

        _allOrphans = scan.Orphans;
        TotalMediaCount = scan.TotalMediaCount;
        _totalMediaSizeKb = scan.TotalMediaSizeKb;
        _totalTypesCount = scan.TypesWithMedia;
        HasRun = true;

        ApplyFilter();
    }

    /// <summary>Vuelca en la vista mostrada todos los huérfanos, o solo los del tipo seleccionado si el filtro está activo.</summary>
    private void ApplyFilter()
    {
        IEnumerable<GameImage> shown = _allOrphans;

        if (_filterBySelectedType)
        {
            int? key = _sharedDataService.SelectedImageSet?.Type?.Key;
            shown = key.HasValue ? _allOrphans.Where(o => o.Type?.Key == key.Value) : Enumerable.Empty<GameImage>();
        }

        IgViewModel.Images.ReplaceAll(shown);
        RaiseOrphanStateChanged();
    }

    private void RaiseOrphanStateChanged()
    {
        OnPropertyChanged(nameof(ShownCount));
        OnPropertyChanged(nameof(TotalOrphanCount));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(HasOrphans));
        OnPropertyChanged(nameof(OrphansRatio));
        OnPropertyChanged(nameof(SizeRatio));
        OnPropertyChanged(nameof(TypesRatio));
        OnPropertyChanged(nameof(TopOrphanType));
        OnPropertyChanged(nameof(TopTypeOrphanRatio));
        DeleteAllOrphansCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Nº de tipos de medio de imagen/vídeo de juego que tienen al menos un fichero (denominador de "Types").</summary>
    private static int GetTotalTypesCount(Platform platform)
    {
        if (platform.Images?.ImageSets == null)
            return 0;

        return platform.Images.ImageSets
            .Count(s => s.Type != null && (MediaType.IsImage(s.Type.Key) || MediaType.IsVideo(s.Type.Key)) && s.ImageFilesLowerCase.Count > 0);
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

    private async void OnDeleteAllOrphansClicked()
    {
        try
        {
            await DeleteAllOrphansCoreAsync();
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.OrphanTool_Delete_Error] ?? "Error deleting orphan media.");
        }
    }

    /// <summary>
    /// Borra de disco los huérfanos MOSTRADOS (todos, o los del tipo filtrado) con backup, tras confirmación,
    /// reportando progreso y dejando la operación deshacible desde el activity log. Reconcilia el modelo (quita cada
    /// fichero de su set por ruta) y las vistas.
    /// </summary>
    private async Task DeleteAllOrphansCoreAsync()
    {
        Platform? platform = _sharedDataService.SelectedPlatform;
        if (platform == null)
            return;

        List<GameImage> shown = IgViewModel.Images.ToList();
        if (shown.Count == 0)
            return;

        // Confirmación: crea backups y es deshacible, pero borra ficheros de disco → confirmación explícita.
        string message = $"{shown.Count} orphan media file(s) of \"{platform.Name}\" will be deleted from disk. You can undo this from the activity log. Do you want to continue?";
        bool confirmed = await _dialogsService.ConfirmAsync(_windowService.ActiveXamlRoot!, "Delete orphan media", message, "Delete", "Cancel");
        if (!confirmed)
            return;

        ProgressNotifier notifier = _progressService.StartBlockingOperation(false);

        // Registro para el undo: imagen + su backup + su set (solo las que realmente se borraron).
        var deleted = new List<(GameImage image, string backupPath, PlatformImageSet set)>();

        int lastProgress = -1;
        for (int i = 0; i < shown.Count; i++)
        {
            GameImage orphan = shown[i];
            PlatformImageSet? set = platform.Images?.ImageSets.FirstOrDefault(s => s.Type?.Key == orphan.Type?.Key);

            set?.RemoveImageByFile(orphan);   // reconcilia el modelo por ruta (sirva o no cargado el set)
            _allOrphans.Remove(orphan);
            IgViewModel.Images.Remove(orphan);

            string? backupPath = await _fileSystemService.DeleteImageFileAsync(orphan);
            if (backupPath != null && set != null)
                deleted.Add((orphan, backupPath, set));

            int progress = (i + 1) * 100 / shown.Count;
            if (progress != lastProgress)
            {
                lastProgress = progress;
                notifier.Progress = progress;
                notifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.OrphanTool_DeletingMedia_Progress] ?? "{0}  |  Deleting orphan media ({1}/{2})", platform.Name, i + 1, shown.Count);
                _progressService.ProgressNotifier.Report(notifier);
            }
        }

        // Ajusta los totales por lo borrado (los ficheros salieron de los sets) para que las pastillas "X/Y"
        // reflejen el nuevo total SIN re-escanear (que releería el tamaño de todo el disco).
        TotalMediaCount = Math.Max(0, TotalMediaCount - shown.Count);
        _totalMediaSizeKb = Math.Max(0, _totalMediaSizeKb - shown.Sum(o => o.FileSize));
        _totalTypesCount = GetTotalTypesCount(platform);

        _imageLoadingService.NotifyPlatformImagesChanged();
        // Refresca también el Media Audit, el único control aparte de este tool que muestra huérfanos: enseña las
        // imágenes crudas del set seleccionado, así que se le hace re-leer el set (ya reconciliado por ruta).
        _sharedDataService.NotifySelectedGameImagesChanged();
        notifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.OrphanTool_MediaDeleted_Progress] ?? "{0}  |  {1} orphan media files deleted", platform.Name, shown.Count);

        // Undo: restaura cada fichero desde su backup y lo vuelve a registrar en su set y en el conjunto de huérfanos.
        if (deleted.Count > 0)
        {
            notifier.UndoNeedsBackup = true;
            notifier.UndoAction = async () =>
            {
                foreach (var (image, backupPath, set) in deleted)
                {
                    await _fileSystemService.RestoreImageFileAsync(backupPath, image.File);
                    set.AddImage(image);
                    _allOrphans.Add(image);
                }

                TotalMediaCount += deleted.Count;
                _totalMediaSizeKb += deleted.Sum(d => d.image.FileSize);
                _totalTypesCount = GetTotalTypesCount(platform);

                _imageLoadingService.NotifyPlatformImagesChanged();
                _sharedDataService.NotifySelectedGameImagesChanged();
                ApplyFilter();   // reconstruye la vista mostrada (respetando el filtro) + refresca pastillas
            };
        }

        notifier.FinishOperation();
        _progressService.ProgressNotifier.Report(notifier);
        _progressService.FinishBlockingOperation();

        RaiseOrphanStateChanged();
    }
    #endregion
}
