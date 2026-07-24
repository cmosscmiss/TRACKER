using Microsoft.Extensions.Options;
using MM4LB.Contracts.Services;
using MM4LB.Controls.Templates;
using MM4LB.Controls.ViewModels;
using MM4LB.Controls.Views;
using MM4LB.Models;
using MM4LB.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static MM4LB.Services.WindowService;

namespace MM4LB.ViewModels;

public class MainWindowViewModel : WidgetViewModelBase
{
    #region Constants
    public const double PlatformDetailsMinWidth = 400;
    public const double PlatformDetailsMaxWidth = 600;
    #endregion

    #region Attributes
    private readonly ProgressService _progressService;
    private new readonly SharedDataService _sharedDataService;
    private readonly ImageBinariesCacheService _imageBinariesCacheService;
    private readonly YoutubeDownloadService _youtubeDownloadService;

    private bool _isAnimating;
    private bool _isGameListDockedAside = false;
    private bool _isPlatformDetailsVisible = true;
    private bool _isImageTypeBandVisible = true;
    private double _platformDetailsWidth = PlatformDetailsMinWidth;
    private IReadOnlyList<WidgetInfo> _widgets = Array.Empty<WidgetInfo>();
    #endregion

    #region Properties (Observable)
    public bool IsAnimating
    {
        get => _isAnimating;
        set
        {
            SetProperty(ref _isAnimating, value);
        }
    }

    /// <summary>
    /// Posición de la lista de juegos. <c>false</c> = bajo la lista de plataformas (posición "home");
    /// <c>true</c> = acoplada en su propia columna lateral a la derecha de plataformas.
    /// </summary>
    public bool IsGameListDockedAside
    {
        get => _isGameListDockedAside;
        set
        {
            if (SetProperty(ref _isGameListDockedAside, value))
            {
                GameListDockedAsideChanged?.Invoke(value);
            }
        }
    }

    public bool IsPlatformDetailsVisible
    {
        get => _isPlatformDetailsVisible;
        set
        {
            if (SetProperty(ref _isPlatformDetailsVisible, value))
            {
                PlatformDetailsVisibilityChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Visibilidad de la banda fija del selector de tipo de medio (parte alta del WidgetPanel). Al cambiar, dispara
    /// <see cref="ImageTypeBandVisibilityChanged"/> para que la ventana anime la banda y el reflujo del panel.
    /// </summary>
    public bool IsImageTypeBandVisible
    {
        get => _isImageTypeBandVisible;
        set
        {
            if (SetProperty(ref _isImageTypeBandVisible, value))
            {
                OnPropertyChanged(nameof(IsLeftImageTypeSelectorVisible));
                ImageTypeBandVisibilityChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Visibilidad del selector de tipo de medio integrado en la columna izquierda (sobre la lista de juegos). Es el
    /// inverso de <see cref="IsImageTypeBandVisible"/>: solo se muestra uno de los dos selectores a la vez (si la banda
    /// del panel está visible, el de la izquierda se oculta, y viceversa).
    /// </summary>
    public bool IsLeftImageTypeSelectorVisible => !IsImageTypeBandVisible;

    public double PlatformDetailsWidth
    {
        get => _platformDetailsWidth;
        set => SetProperty(ref _platformDetailsWidth, value);
    }

    public IReadOnlyList<WidgetInfo> Widgets
    {
        get => _widgets;
        set => SetProperty(ref _widgets, value);
    }
    #endregion

    #region Events
    public event Action<bool>? GameListDockedAsideChanged;
    public event Action<bool>? PlatformDetailsVisibilityChanged;
    public event Action<bool>? ImageTypeBandVisibilityChanged;
    #endregion

    #region Properties
    public ProgressService ProgressService => _progressService;
    public new SharedDataService SharedDataService => _sharedDataService;
    public ImageBinariesCacheService ImageBinariesCacheService => _imageBinariesCacheService;

    public ConsoleViewModel ConsoleViewModel
    {
        get; private set;
    }
    public GameImagesDashboardViewModel GameImagesDashboardViewModel
    {
        get; private set;
    }
    public GameImagesRegionDashboardViewModel GameImagesRegionDashboardViewModel
    {
        get; private set;
    }
    public StatsPlatformViewModel StatsPlatformViewModel
    {
        get; private set;
    }
    public StatsGlobalViewModel StatsGlobalViewModel
    {
        get; private set;
    }
    public GameListViewModel GameListViewModel
    {
        get; private set;
    }
    public GamesAuditViewModel GamesAuditViewModel
    {
        get; private set;
    }
    public GameDetailsViewModel GameDetailsViewModel
    {
        get; private set;
    }
    public ImageAuditViewModel ImageAuditViewModel
    {
        get; private set;
    }
    public ImageCollectionImportViewModel ImageCollectionImportViewModel
    {
        get; private set;
    }
    public ImageGridGameViewModel ImageGridGameViewModel
    {
        get; private set;
    }
    public ImageTypeViewModel ImageTypeViewModel
    {
        get; private set;
    }
    public PlatformDetailsViewModel PlatformDetailsViewModel
    {
        get; private set;
    }
    public PlatformListViewModel PlatformListViewModel
    {
        get; private set;
    }
    public LayoutSelectorViewModel LayoutSelectorViewModel
    {
        get; private set;
    }
    public WebViewViewModel WebViewViewModel
    {
        get; private set;
    }
    public ToolsViewModel ToolsViewModel
    {
        get; private set;
    }
    #endregion

    #region Constructors
    public MainWindowViewModel(ProgressService progressService, SharedDataService sharedDataService, ImageBinariesCacheService imageBinariesCacheService, YoutubeDownloadService youtubeDownloadService, ConsoleViewModel consoleViewModel, GameImagesDashboardViewModel gameImagesDashboardViewModel, GameImagesRegionDashboardViewModel gameImagesRegionDashboardViewModel, StatsPlatformViewModel statsPlatformViewModel, StatsGlobalViewModel statsGlobalViewModel, GameListViewModel gameListViewModel, GamesAuditViewModel gamesAuditViewModel, GameDetailsViewModel gameDetailsViewModel, ImageAuditViewModel imageAuditViewModel, ImageCollectionImportViewModel imageCollectionImportViewModel, ImageTypeViewModel imageTypeViewModel, PlatformDetailsViewModel platformDetailsViewModel, PlatformListViewModel platformListViewModel, LayoutSelectorViewModel layoutSelectorViewModel, WebViewViewModel webViewViewModel, ImageGridGameViewModel imageGridGameViewModel, ToolsViewModel toolsViewModel, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _progressService = progressService;
        _sharedDataService = sharedDataService;
        _imageBinariesCacheService = imageBinariesCacheService;
        _youtubeDownloadService = youtubeDownloadService;

        ConsoleViewModel = consoleViewModel;
        GameImagesDashboardViewModel = gameImagesDashboardViewModel;
        GameImagesRegionDashboardViewModel = gameImagesRegionDashboardViewModel;
        StatsPlatformViewModel = statsPlatformViewModel;
        StatsGlobalViewModel = statsGlobalViewModel;
        ToolsViewModel = toolsViewModel;
        GameListViewModel = gameListViewModel;
        GamesAuditViewModel = gamesAuditViewModel;
        GameDetailsViewModel = gameDetailsViewModel;
        ImageAuditViewModel = imageAuditViewModel;
        ImageCollectionImportViewModel = imageCollectionImportViewModel;
        ImageTypeViewModel = imageTypeViewModel;
        ImageGridGameViewModel = imageGridGameViewModel;
        PlatformDetailsViewModel = platformDetailsViewModel;
        PlatformListViewModel = platformListViewModel;
        LayoutSelectorViewModel = layoutSelectorViewModel;
        WebViewViewModel = webViewViewModel;

        // Coordinación de exclusividad: los dos dashboards (estándar y regiones) no pueden estar visibles a la vez.
        GameImagesDashboardViewModel.PropertyChanged += OnDashboardSlotIndexChanged;
        GameImagesRegionDashboardViewModel.PropertyChanged += OnDashboardSlotIndexChanged;

        // Templates: al GRABAR, volcar el estado en vivo a AppSettings; al CARGAR, re-aplicar todo desde AppSettings.
        _sharedDataService.SaveConfigRequested += OnSaveConfigRequested;
        _sharedDataService.SettingsReloaded += OnSettingsReloaded;

        LoadConfig();
    }

    /// <summary>
    /// Antes de GRABAR un template: vuelca a AppSettings el estado en vivo de todos los ViewModels (que normalmente
    /// solo se persiste al cerrar la app), para que el template capture el estado ACTUAL y no el de arranque. Igual
    /// que el shutdown (SaveConfig en todos los IWidgetViewModelBase) + este VM (toggles + slots de widgets).
    /// </summary>
    private void OnSaveConfigRequested(object? sender, EventArgs e)
    {
        foreach (IWidgetViewModelBase vm in App.GetService<IEnumerable<IWidgetViewModelBase>>())
            vm.SaveConfig();
        SaveConfig();   // este VM: toggles + SaveWidgetSlots
    }

    /// <summary>
    /// Al CARGAR un template, re-aplica en vivo TODA la configuración recargada (el tema se excluye del template):
    /// recarga cada ViewModel desde AppSettings (layout, toggles de la toolbar que viven en distintos VMs, etc.) y el
    /// layout de widgets por slot.
    /// </summary>
    private void OnSettingsReloaded(object? sender, EventArgs e)
    {
        foreach (IWidgetViewModelBase vm in App.GetService<IEnumerable<IWidgetViewModelBase>>())
            vm.LoadConfig();
        LoadConfig();            // este VM: toggles (detalles de plataforma, game list aparte, banda de tipos) + anchura
        RestoreWidgetSlots();    // widgets visibles por slot -> el panel se re-organiza
    }
    #endregion

    #region Dashboard exclusivity
    // Evita reentrada al ajustar el slot del otro dashboard dentro del propio handler.
    private bool _coordinatingDashboards;

    /// <summary>
    /// Cuando uno de los dashboards pasa a estar colocado (SlotIndex &gt;= 0), saca al otro del panel (SlotIndex =
    /// -1): "el último que se coloca gana". Luego publica el dashboard activo. El arranque usa su propia regla
    /// (conservar el estándar) en <see cref="RestoreWidgetSlots"/>, con el guard activo.
    /// </summary>
    private void OnDashboardSlotIndexChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WidgetViewModelBase.SlotIndex) || _coordinatingDashboards)
        {
            return;
        }

        _coordinatingDashboards = true;
        try
        {
            if (sender is WidgetViewModelBase changed && changed.SlotIndex >= 0)
            {
                WidgetViewModelBase other = ReferenceEquals(changed, GameImagesDashboardViewModel)
                    ? GameImagesRegionDashboardViewModel
                    : GameImagesDashboardViewModel;

                if (other.SlotIndex >= 0)
                {
                    other.SlotIndex = -1;
                }
            }

            PublishActiveDashboardMode();
        }
        finally
        {
            _coordinatingDashboards = false;
        }
    }

    /// <summary>Publica en <see cref="SharedDataService.ActiveDashboardMode"/> qué dashboard está visible.</summary>
    private void PublishActiveDashboardMode()
    {
        _sharedDataService.ActiveDashboardMode =
            GameImagesRegionDashboardViewModel.SlotIndex >= 0 ? DashboardMode.Region
            : GameImagesDashboardViewModel.SlotIndex >= 0 ? DashboardMode.Standard
            : DashboardMode.None;
    }
    #endregion

    #region Subscribed events

    #endregion

    #region Methods (private)

    #endregion

    #region Methods (public)
    /// <summary>
    /// Comprueba al mostrarse la ventana principal si ffmpeg está disponible y, si no, lo descarga en segundo plano
    /// (build estática de BtbN) y lo cachea en %LocalAppData%\MM4LB\Tools\ffmpeg, de modo que las descargas de vídeo
    /// en HD funcionen luego sin esperas. No bloquea la UI ni propaga excepciones: la descarga se muestra en la
    /// consola (con botón de cancelar) y, si falla o se cancela, queda como warning y se reintentará en el próximo
    /// arranque o al primer uso HD.
    /// </summary>
    public async Task EnsureFfmpegReadyAsync()
    {
        if (_youtubeDownloadService.IsFfmpegAvailable)
            return;

        ProgressNotifier notifier = _progressService.StartOperation();
        using var cts = new CancellationTokenSource();
        notifier.CancelAction = () => cts.Cancel();

        notifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.MainWindow_FfmpegPreparing_Progress] ?? "Preparing video tools: downloading ffmpeg (one-time setup)...";
        _progressService.ProgressNotifier.Report(notifier);

        try
        {
            var progress = new Progress<double>(fraction =>
            {
                notifier.Progress = (int)(fraction * 100);
                _progressService.ProgressNotifier.Report(notifier);
            });

            var statusProgress = new Progress<string>(message =>
            {
                notifier.Message = message;
                _progressService.ProgressNotifier.Report(notifier);
            });

            await _youtubeDownloadService.EnsureFfmpegAvailableAsync(progress, statusProgress, cts.Token);
            notifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.MainWindow_FfmpegReady_Progress] ?? "Video tools ready (ffmpeg installed)";
        }
        catch (OperationCanceledException)
        {
            notifier.IsWarning = true;
            notifier.Message = MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.MainWindow_FfmpegCancelled_Progress] ?? "ffmpeg download cancelled (HD video downloads will retry it)";
        }
        catch (Exception ex)
        {
            // No es fatal: la app funciona con normalidad salvo las descargas de vídeo en HD.
            notifier.IsWarning = true;
            notifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.MainWindow_FfmpegPrepare_Error] ?? "Could not prepare ffmpeg: {0}", ex.Message);
        }
        finally
        {
            notifier.CancelAction = null; // la operación terminó: ya no es cancelable
            notifier.FinishOperation();
            _progressService.ProgressNotifier.Report(notifier);
            _progressService.FinishOperation();
        }
    }

    /// <summary>
    /// Actualiza en el ViewModel el estado real del panel de detalles antes de guardar.
    /// 
    /// Este método debe llamarse desde la ventana, porque la ventana es quien conoce
    /// el ancho real de la columna después de usar el GridSplitter.
    /// </summary>
    public void CapturePlatformDetailsLayout(double visibleWidth, double previousVisibleWidth)
    {
        if (IsPlatformDetailsVisible && visibleWidth > 0)
        {
            PlatformDetailsWidth = Math.Clamp(visibleWidth, PlatformDetailsMinWidth, PlatformDetailsMaxWidth);
        }
        else if (previousVisibleWidth > 0)
        {
            PlatformDetailsWidth = Math.Clamp(previousVisibleWidth, PlatformDetailsMinWidth, PlatformDetailsMaxWidth);
        }
    }

    /// <summary>
    /// Devuelve la colocación de ventana guardada en configuración.
    /// </summary>
    public WindowPlacement? GetSavedWindowPlacement()
    {
        var settings = _appSettings.Window;

        if (!settings.HasSavedPlacement)
            return null;

        return new WindowPlacement
        {
            X = settings.X,
            Y = settings.Y,
            Width = settings.Width,
            Height = settings.Height,
            IsMaximized = settings.IsMaximized
        };
    }

    /// <summary>
    /// Guarda en AppSettings la posición, tamaño y estado actual de la ventana principal.
    /// </summary>
    /// <param name="placement">
    /// Estado de ventana capturado desde WindowService.
    /// </param>
    public void SaveWindowPlacement(WindowPlacement placement)
    {
        _appSettings.Window.X = placement.X;
        _appSettings.Window.Y = placement.Y;
        _appSettings.Window.Width = placement.Width;
        _appSettings.Window.Height = placement.Height;
        _appSettings.Window.IsMaximized = placement.IsMaximized;
        _appSettings.Window.HasSavedPlacement = true;
    }

    public void SetWidgets(IEnumerable<WidgetInfo> widgets)
    {
        Widgets = widgets.ToList();

        RestoreWidgetSlots();
    }

    public void RestoreWidgetSlots()
    {
        if (Widgets.Count == 0)
            return;

        var savedSlots = _appSettings.LayoutSelectorControl.WidgetSlots;

        // Guard activo durante el restore: se aplican los slots guardados y, al final, la regla de arranque
        // (conservar el ESTÁNDAR si ambos dashboards quedaron visibles), sin que el coordinador "último gana"
        // interfiera mientras se asignan los slots uno a uno.
        _coordinatingDashboards = true;
        try
        {
            foreach (var widget in Widgets)
            {
                if (savedSlots.TryGetValue(widget.IconName, out int savedSlotIndex))
                {
                    widget.ViewModel.SlotIndex = savedSlotIndex;
                }
                else
                {
                    widget.ViewModel.SlotIndex = -1;
                }
            }

            // Exclusividad en arranque: si ambos dashboards quedaron colocados, se conserva el estándar.
            if (GameImagesDashboardViewModel.SlotIndex >= 0 && GameImagesRegionDashboardViewModel.SlotIndex >= 0)
            {
                GameImagesRegionDashboardViewModel.SlotIndex = -1;
            }

            PublishActiveDashboardMode();
        }
        finally
        {
            _coordinatingDashboards = false;
        }
    }

    public void SaveWidgetSlots()
    {
        _appSettings.LayoutSelectorControl.WidgetSlots.Clear();

        foreach (var widget in Widgets)
        {
            _appSettings.LayoutSelectorControl.WidgetSlots[widget.IconName] =
                widget.ViewModel.SlotIndex;
        }
    }

    /// <summary>
    /// Carga desde AppSettings el estado del panel de detalles de plataforma.
    /// </summary>
    public override void LoadConfig()
    {
        IsGameListDockedAside = _appSettings.GameListControl.IsDockedAside;
        PlatformDetailsWidth = Math.Clamp(_appSettings.PlatformDetailsControl.Width, PlatformDetailsMinWidth, PlatformDetailsMaxWidth);
        IsPlatformDetailsVisible = _appSettings.PlatformDetailsControl.IsVisible;
        IsImageTypeBandVisible = _appSettings.ImageTypeControl.IsBandVisible;
    }

    public override void SaveConfig()
    {
        _appSettings.GameListControl.IsDockedAside = IsGameListDockedAside;
        _appSettings.PlatformDetailsControl.Width = PlatformDetailsWidth;
        _appSettings.PlatformDetailsControl.IsVisible = IsPlatformDetailsVisible;
        _appSettings.ImageTypeControl.IsBandVisible = IsImageTypeBandVisible;

        SaveWidgetSlots();
    }

    public override void Dispose()
    {
        GameImagesDashboardViewModel.PropertyChanged -= OnDashboardSlotIndexChanged;
        GameImagesRegionDashboardViewModel.PropertyChanged -= OnDashboardSlotIndexChanged;

        PlatformListViewModel.Dispose();
        GameListViewModel.Dispose();
        ImageTypeViewModel.Dispose();
        PlatformDetailsViewModel.Dispose();
    }
    #endregion
}