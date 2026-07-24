using System;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;

namespace MM4LB.Services;

public class ProgressService : ObservableObject
{
    private readonly SharedDataService _sharedDataService;

    private int _operationIdSequence;
    private double _progressValue;
    private string _progressMessage = string.Empty;
    private bool _progressIsIndeterminate;
    private Visibility _progressVisibility = Visibility.Collapsed;

    private readonly Stopwatch _stopWatchTimedOperation = new();

    private readonly Progress<ProgressNotifier> _progressNotifier;
    private readonly Progress<ProgressNotifier> _progressNotifierWithStats;

    // Carga lazy de imágenes: UNA entrada COMPARTIDA por plataforma. Varias galerías (instancias de ImageGrid)
    // decodifican a la vez; todas reportan aquí, así que solo hay una entrada por plataforma. Se inserta una vez
    // y se mantiene en su posición del log (no se recoloca al cargar imágenes), actualizando solo su mensaje.
    private ProgressNotifier? _lazyImageNotifier;
    private int _lazyImageCount;
    private string _lazyImagePlatform = string.Empty;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _lazyImageSettleTimer;

    public ConsoleViewModel ConsoleViewModel
    {
        get; set;
    }

    public long LastTimedOperationDuration
    {
        get; private set;
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        set => SetProperty(ref _progressMessage, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public Visibility ProgressVisibility
    {
        get => _progressVisibility;
        set => SetProperty(ref _progressVisibility, value);
    }

    public bool ProgressIsIndeterminate
    {
        get => _progressIsIndeterminate;
        set => SetProperty(ref _progressIsIndeterminate, value);
    }

    public IProgress<ProgressNotifier> ProgressNotifier => _progressNotifier;

    public IProgress<ProgressNotifier> ProgressNotifierWithStats => _progressNotifierWithStats;

    public ProgressService(ConsoleViewModel consoleViewModel, SharedDataService sharedDataService)
    {
        _sharedDataService = sharedDataService;
        ConsoleViewModel = consoleViewModel;

        _progressNotifier = new Progress<ProgressNotifier>(ReportProgress);
        _progressNotifierWithStats = new Progress<ProgressNotifier>(ReportProgress);
    }

    private void ReportProgress(ProgressNotifier progress)
    {
        ProgressValue = progress.Progress;
        ProgressMessage = progress.Message;
        ProgressIsIndeterminate = progress.IsIndeterminate;

        if (progress.IsOperationFinished)
            OnPropertyChanged(nameof(ConsoleViewModel.IsOperationInExecution));
    }

    public void FinishBlockingOperation()
    {
        _sharedDataService.IsUIEnabled = true;

        if (!ConsoleViewModel.IsOperationInExecution)
        {
            ProgressVisibility = Visibility.Collapsed;
            ProgressValue = 0;
            ProgressIsIndeterminate = false;
        }

        OnPropertyChanged(nameof(ConsoleViewModel.IsOperationInExecution));
    }

    public void FinishOperation()
    {
        if (!ConsoleViewModel.IsOperationInExecution)
        {
            ProgressVisibility = Visibility.Collapsed;
            ProgressValue = 0;
            ProgressIsIndeterminate = false;
        }

        OnPropertyChanged(nameof(ConsoleViewModel.IsOperationInExecution));
    }

    public long FinishTimedOperation()
    {
        _stopWatchTimedOperation.Stop();
        LastTimedOperationDuration = _stopWatchTimedOperation.ElapsedMilliseconds;
        return LastTimedOperationDuration;
    }

    public ProgressNotifier StartBlockingOperation(bool showProgress = true)
        => StartOperationInternal(showProgress, blockUI: true);

    public ProgressNotifier StartOperation(bool showProgress = true)
        => StartOperationInternal(showProgress, blockUI: false);

    /// <summary>
    /// Crea una operación "de fondo" cuyo notifier se inserta al PRINCIPIO del log (índice 0, arriba) y que NO
    /// toca la barra global. Para la carga lazy de imágenes: una entrada discreta en el ACTIVITY LOG,
    /// que se actualiza sola, sin mostrar barra.
    /// </summary>
    public ProgressNotifier StartBackgroundOperation()
    {
        var progressNotifier = new ProgressNotifier(startWatch: true)
        {
            OperationId = Interlocked.Increment(ref _operationIdSequence)
        };

        progressNotifier.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MM4LB.Models.ProgressNotifier.IsOperationFinished))
            {
                OnPropertyChanged(nameof(ConsoleViewModel.IsOperationInExecution));
            }
        };

        ConsoleViewModel.AddLogEntry(progressNotifier);
        OnPropertyChanged(nameof(ConsoleViewModel.IsOperationInExecution));

        return progressNotifier;
    }

    /// <summary>
    /// Reporta que una imagen se ha decodificado de forma lazy (al hacer scroll) para la plataforma dada.
    /// Mantiene UNA entrada compartida por plataforma (todas las galerías reportan aquí), que conserva su
    /// posición en el log y solo actualiza su mensaje, y la asienta por debounce cuando dejan de llegar.
    /// </summary>
    public void ReportLazyImageLoaded(string platform)
    {
        platform ??= string.Empty;

        _lazyImageSettleTimer ??= CreateLazyImageSettleTimer();
        _lazyImageSettleTimer.Stop();

        // Cambio de plataforma: cierra la entrada anterior (queda como registro) y crea otra.
        if (_lazyImageNotifier != null && _lazyImagePlatform != platform)
        {
            FinalizeLazyImageLoad();
        }

        if (_lazyImageNotifier == null)
        {
            _lazyImagePlatform = platform;
            _lazyImageCount = 0;
            _lazyImageNotifier = StartBackgroundOperation();
        }
        else if (_lazyImageNotifier.IsOperationFinished)
        {
            // Misma plataforma, nueva ráfaga tras asentarse: reactiva la entrada SIN resetear el
            // cronómetro, de modo que el tiempo se acumula entre ráfagas en vez de empezar de cero.
            _lazyImageNotifier.Resume();
        }

        _lazyImageCount++;
        _lazyImageNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Progress_LazyLoading_Progress] ?? "{0}Loading {1} media files", LazyImagePrefix(), _lazyImageCount);
        _lazyImageSettleTimer.Start();
    }

    private void OnLazyImageSettleTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        _lazyImageSettleTimer?.Stop();

        if (_lazyImageNotifier == null || _lazyImageNotifier.IsOperationFinished)
        {
            return;
        }

        _lazyImageNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Progress_LazyLoaded_Progress] ?? "{0}{1} media files loaded", LazyImagePrefix(), _lazyImageCount);
        _lazyImageNotifier.FinishOperation();
    }

    private void FinalizeLazyImageLoad()
    {
        if (_lazyImageNotifier == null)
        {
            return;
        }

        if (!_lazyImageNotifier.IsOperationFinished)
        {
            _lazyImageNotifier.Message = string.Format(MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Progress_LazyLoaded_Progress] ?? "{0}{1} media files loaded", LazyImagePrefix(), _lazyImageCount);
            _lazyImageNotifier.FinishOperation();
        }

        _lazyImageNotifier = null;
    }

    private string LazyImagePrefix() => string.IsNullOrWhiteSpace(_lazyImagePlatform) ? string.Empty : $"{_lazyImagePlatform}  |  ";

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateLazyImageSettleTimer()
    {
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += OnLazyImageSettleTick;
        return timer;
    }

    private ProgressNotifier StartOperationInternal(bool showProgress, bool blockUI)
    {
        ProgressValue = 0;
        ProgressIsIndeterminate = false;
        ProgressVisibility = showProgress ? Visibility.Visible : Visibility.Collapsed;

        if (blockUI)
            _sharedDataService.IsUIEnabled = false;

        var progressNotifier = new ProgressNotifier(startWatch: true)
        {
            OperationId = Interlocked.Increment(ref _operationIdSequence)
        };

        // Sin ambigüedad: tipo totalmente calificado
        progressNotifier.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MM4LB.Models.ProgressNotifier.IsOperationFinished))
            {
                OnPropertyChanged(nameof(ConsoleViewModel.IsOperationInExecution));
            }
        };

        ConsoleViewModel.AddLogEntry(progressNotifier);
        OnPropertyChanged(nameof(ConsoleViewModel.IsOperationInExecution));

        return progressNotifier;
    }

    public void StartTimedOperation()
    {
        _stopWatchTimedOperation.Reset();
        _stopWatchTimedOperation.Start();
    }
}
