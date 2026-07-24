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
    #region Attributes
    private readonly ProgressService _progressService;
    private new readonly SharedDataService _sharedDataService;
    private readonly YoutubeDownloadService _youtubeDownloadService;

    private bool _isAnimating;
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

    public IReadOnlyList<WidgetInfo> Widgets
    {
        get => _widgets;
        set => SetProperty(ref _widgets, value);
    }
    #endregion

    #region Properties
    public ProgressService ProgressService => _progressService;
    public new SharedDataService SharedDataService => _sharedDataService;

    public ConsoleViewModel ConsoleViewModel
    {
        get; private set;
    }
    public StatsPlatformViewModel StatsPlatformViewModel
    {
        get; private set;
    }
    public GameDetailsViewModel GameDetailsViewModel
    {
        get; private set;
    }
    public ImageGridGameViewModel ImageGridGameViewModel
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
    #endregion

    #region Constructors
    public MainWindowViewModel(ProgressService progressService, SharedDataService sharedDataService, YoutubeDownloadService youtubeDownloadService, ConsoleViewModel consoleViewModel, StatsPlatformViewModel statsPlatformViewModel, GameDetailsViewModel gameDetailsViewModel, PlatformListViewModel platformListViewModel, LayoutSelectorViewModel layoutSelectorViewModel, WebViewViewModel webViewViewModel, ImageGridGameViewModel imageGridGameViewModel, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _progressService = progressService;
        _sharedDataService = sharedDataService;
        _youtubeDownloadService = youtubeDownloadService;

        ConsoleViewModel = consoleViewModel;
        StatsPlatformViewModel = statsPlatformViewModel;
        GameDetailsViewModel = gameDetailsViewModel;
        ImageGridGameViewModel = imageGridGameViewModel;
        PlatformListViewModel = platformListViewModel;
        LayoutSelectorViewModel = layoutSelectorViewModel;
        WebViewViewModel = webViewViewModel;

        LoadConfig();
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

    public override void LoadConfig()
    {
    }

    public override void SaveConfig()
    {
        SaveWidgetSlots();
    }

    public override void Dispose()
    {
        PlatformListViewModel.Dispose();
    }
    #endregion
}