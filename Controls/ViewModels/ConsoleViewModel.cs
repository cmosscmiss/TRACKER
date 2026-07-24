using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model for the logging console control.
///
/// Exposes the log entries displayed by the console, the image binaries cache (for the cache pills) and
/// the backup folder stats (pill + "empty backup" action).
/// </summary>
public class ConsoleViewModel : WidgetViewModelBase
{
    #region Attributes
    private readonly ImageBinariesCacheService _imageBinariesCacheService;
    private readonly BackupService _backupService;
    private readonly DialogsService _dialogsService;
    private readonly WindowService _windowService;

    private IAsyncRelayCommand? _clearBackupCommand;
    #endregion

    #region Properties
    public ImageBinariesCacheService ImageBinariesCacheService => _imageBinariesCacheService;

    /// <summary>Estadísticas de la carpeta de backup (nº de imágenes y tamaño) para la pastilla del log.</summary>
    public BackupService BackupService => _backupService;

    public ObservableCollection<ProgressNotifier> LogEntries { get; private set; } = new();

    /// <summary>
    /// Máximo de entradas retenidas en el ACTIVITY LOG. Evita el crecimiento ilimitado en sesiones largas y
    /// acota el coste O(n) de <see cref="IsOperationInExecution"/> (que recorre la colección).
    /// </summary>
    private const int MaxLogEntries = 500;

    public bool IsOperationInExecution => LogEntries.Any(x => !x.IsOperationFinished);

    /// <summary>
    /// Evento más reciente del log (el de arriba, en ejecución o el último terminado). Lo usa el visor del pie
    /// de la aplicación cuando la consola no está como widget visible (<see cref="IsHidden"/>).
    /// </summary>
    public ProgressNotifier? LatestEntry => LogEntries.FirstOrDefault();

    /// <summary>True cuando la consola no está colocada en ningún slot del WidgetPanel (SlotIndex == -1).</summary>
    public bool IsHidden => SlotIndex == -1;

    /// <summary>
    /// Si el visor de eventos del pie debe mostrarse: siempre (cuando lo fuerza el setting, por defecto) o solo
    /// cuando la consola no está como widget visible.
    /// </summary>
    public bool IsFooterViewerVisible => _appSettings.General.FooterEventViewerAlwaysVisible || IsHidden;

    /// <summary>Vacía la carpeta de backup tras confirmación. El botón se habilita si hay backups.</summary>
    public IAsyncRelayCommand ClearBackupCommand => _clearBackupCommand ??= new AsyncRelayCommand(ClearBackupAsync);
    #endregion

    #region Constructor
    public ConsoleViewModel(ImageBinariesCacheService imageBinariesCacheService, BackupService backupService, DialogsService dialogsService, WindowService windowService, SharedDataService sharedDataService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _imageBinariesCacheService = imageBinariesCacheService;
        _backupService = backupService;
        _dialogsService = dialogsService;
        _windowService = windowService;

        _backupService.Cleared += OnBackupCleared;

        // El visor del pie sigue al evento de arriba: re-notifica LatestEntry cuando entra/sale un evento, e
        // IsHidden cuando la consola se añade/quita del WidgetPanel (SlotIndex).
        LogEntries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LatestEntry));
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SlotIndex))
            {
                OnPropertyChanged(nameof(IsHidden));
                OnPropertyChanged(nameof(IsFooterViewerVisible));
            }
        };

        // Al cargar un template, el ajuste FooterEventViewerAlwaysVisible puede cambiar: re-evalúa la visibilidad.
        SharedDataService.SettingsReloaded += (_, _) => OnPropertyChanged(nameof(IsFooterViewerVisible));
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Re-evalúa la visibilidad del visor de eventos del pie tras cambiar en caliente el ajuste
    /// <see cref="AppSettings.GeneralSettings.FooterEventViewerAlwaysVisible"/> (lo llama la ventana de configuración
    /// al aceptar). El valor se lee de AppSettings; aquí solo se notifica para refrescar el binding.
    /// </summary>
    public void NotifyFooterViewerVisibilityChanged() => OnPropertyChanged(nameof(IsFooterViewerVisible));

    /// <summary>
    /// Añade una entrada al principio del log (la más reciente arriba) y descarta las más antiguas por encima
    /// de <see cref="MaxLogEntries"/> (buffer circular). Punto único de alta, para mantener la colección acotada.
    /// </summary>
    public void AddLogEntry(ProgressNotifier entry)
    {
        LogEntries.Insert(0, entry);
        while (LogEntries.Count > MaxLogEntries)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    public override void Dispose()
    {
        _backupService.Cleared -= OnBackupCleared;
    }

    public override void LoadConfig()
    {
    }

    public override void SaveConfig()
    {
    }
    #endregion

    #region Methods (private)
    private async Task ClearBackupAsync()
    {
        if (!_backupService.HasBackups)
        {
            return;
        }

        MM4LB.Services.LocalizationService? loc = MM4LB.Services.LocalizationService.Instance;
        string emptyTitle = loc?[MM4LB.Helpers.LocKeys.ConsoleViewModel_EmptyBackup_Title] ?? "Empty backup folder";
        string emptyContent = loc is not null
            ? loc.Format(MM4LB.Helpers.LocKeys.ConsoleViewModel_EmptyBackup_Content, _backupService.ImagesCount, _backupService.SizeMb)
            : $"Do you want to delete the {_backupService.ImagesCount} backed-up media file(s) ({_backupService.SizeMb} MB)? Operations that relied on these backups will no longer be undoable.";
        bool confirmed = await _dialogsService.ConfirmAsync(_windowService.ActiveXamlRoot!, emptyTitle, emptyContent, loc?[MM4LB.Helpers.LocKeys.Common_Empty_Label] ?? "Empty", loc?[MM4LB.Helpers.LocKeys.Common_Cancel_Label] ?? "Cancel");

        if (!confirmed)
        {
            return;
        }

        // Reporta la limpieza al ACTIVITY LOG (no-blocking: no afecta al dataset activo). ProgressService se
        // resuelve por App.GetService para evitar el ciclo de DI (ProgressService → ConsoleViewModel → este VM).
        int count = _backupService.ImagesCount;
        double mb = _backupService.SizeMb;

        ProgressService progress = App.GetService<ProgressService>();
        ProgressNotifier notifier = progress.StartOperation();
        notifier.IsIndeterminate = true;
        notifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.ConsoleViewModel_Emptying_Progress] ?? "Emptying backup folder...";
        progress.ProgressNotifier.Report(notifier);

        await _backupService.ClearAsync();

        notifier.IsIndeterminate = false;
        notifier.Message = MM4LB.Services.LocalizationService.Instance is MM4LB.Services.LocalizationService emptiedLoc
            ? emptiedLoc.Format(MM4LB.Helpers.LocKeys.ConsoleViewModel_Emptied_Progress, count, mb)
            : $"Backup folder emptied  |  {count} media files ({mb} MB) deleted";
        notifier.FinishOperation();
        progress.ProgressNotifier.Report(notifier);
        progress.FinishOperation();
    }

    /// <summary>
    /// Al vaciar el backup, las operaciones cuyo undo restaura desde backup ya no pueden deshacerse: se les
    /// quita la acción de undo (el botón desaparece). El undo de "añadir imagen" no usa backup y se conserva.
    /// </summary>
    private void OnBackupCleared()
    {
        foreach (ProgressNotifier entry in LogEntries)
        {
            if (entry.UndoNeedsBackup && entry.CanUndo)
            {
                entry.DisableUndo();
            }
        }
    }
    #endregion
}
