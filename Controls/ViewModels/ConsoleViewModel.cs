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
    private readonly DialogsService _dialogsService;
    private readonly WindowService _windowService;
    #endregion

    #region Properties
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
    #endregion

    #region Constructor
    public ConsoleViewModel(DialogsService dialogsService, WindowService windowService, SharedDataService sharedDataService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _dialogsService = dialogsService;
        _windowService = windowService;

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
    }

    public override void LoadConfig()
    {
    }

    public override void SaveConfig()
    {
    }
    #endregion
}
