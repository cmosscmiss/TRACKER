using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MM4LB.Models;

/// <summary>
/// Severidad visual de una entrada del ACTIVITY LOG, que decide el color del mensaje y de la duración: en curso
/// (texto normal), finalizada (texto secundario), warning (p. ej. cancelada) o error.
/// </summary>
public enum LogEntrySeverity
{
    Running,
    Finished,
    Warning,
    Error
}

/// <summary>
/// Helper class to report the progress to the UI thread.
/// </summary>
public class ProgressNotifier : ObservableObject
{
    #region Attributes
    private readonly Stopwatch _watch = new();
    private long _duration;
    private bool _isException;
    private bool _isWarning;
    private bool _isIndeterminate;
    private bool _isOperationFinished;
    private string _message = string.Empty;
    private int _progress;
    private long _operationId;
    private bool _isUndone;
    private Func<Task>? _undoAction;
    private IAsyncRelayCommand? _undoCommand;
    private bool _isCancelled;
    private Action? _cancelAction;
    private IRelayCommand? _cancelCommand;
    #endregion

    #region Properties (observable)
    public long Duration
    {
        get => _duration;
        private set
        {
            if (SetProperty(ref _duration, value))
                OnPropertyChanged(nameof(DurationText));
        }
    }

    public bool IsException
    {
        get => _isException;
        set
        {
            if (SetProperty(ref _isException, value))
                OnPropertyChanged(nameof(Severity));
        }
    }

    /// <summary>
    /// True cuando la entry es una advertencia (p. ej. una descarga cancelada por el usuario): se muestra con el
    /// color de warning del tema, distinto del rojo de error, pero igual de destacado que un error.
    /// </summary>
    public bool IsWarning
    {
        get => _isWarning;
        set
        {
            if (SetProperty(ref _isWarning, value))
                OnPropertyChanged(nameof(Severity));
        }
    }

    /// <summary>
    /// Severidad visual de la entry (error &gt; warning &gt; finalizada &gt; en curso). El color del mensaje y el de
    /// la duración se enlazan a esto, de modo que comparten color en error y warning.
    /// </summary>
    public LogEntrySeverity Severity
    {
        get
        {
            if (IsException) return LogEntrySeverity.Error;
            if (IsWarning) return LogEntrySeverity.Warning;
            return IsOperationFinished ? LogEntrySeverity.Finished : LogEntrySeverity.Running;
        }
    }

    /// <summary>
    /// True para barras de progreso indeterminadas (operaciones sin porcentaje conocido, p. ej. descargas
    /// que bajan el buffer completo). El ProgressService lo propaga a la ProgressBar global.
    /// </summary>
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    public bool IsOperationFinished
    {
        get => _isOperationFinished;
        private set
        {
            // Solo refresca el estado de undo/cancel si la entry tiene esa acción. Para operaciones que no la
            // tienen (p. ej. la carga lazy) CanUndo/CanCancel es false pase lo que pase, así que notificarlo es
            // inútil y, en la ventana frágil del arranque, dispara el x:Bind del botón del ConsoleControl (lookup
            // del convertidor sobre el control aún sin conectar) provocando un fail-fast 0xc000027b.
            if (SetProperty(ref _isOperationFinished, value))
            {
                OnPropertyChanged(nameof(Severity));
                if (_undoAction != null)
                    RaiseUndoState();
                if (_cancelAction != null)
                    RaiseCancelState();
            }
        }
    }

    /// <summary>
    /// Acción opaca para deshacer la operación de esta entry. La rellena la operación reversible (captura
    /// sus servicios y lo que hizo); si es null, la entry no se puede deshacer.
    /// </summary>
    public Func<Task>? UndoAction
    {
        get => _undoAction;
        set
        {
            if (SetProperty(ref _undoAction, value))
                RaiseUndoState();
        }
    }

    /// <summary>True una vez que la operación de esta entry se ha deshecho desde el log.</summary>
    public bool IsUndone
    {
        get => _isUndone;
        private set
        {
            if (SetProperty(ref _isUndone, value))
                RaiseUndoState();
        }
    }

    /// <summary>La entry es reversible: tiene acción de undo, su operación ya terminó y no se ha deshecho aún.</summary>
    public bool CanUndo => _undoAction != null && IsOperationFinished && !IsUndone;

    /// <summary>Comando que deshace la operación de esta entry (habilitado solo si <see cref="CanUndo"/>).</summary>
    public IAsyncRelayCommand UndoCommand => _undoCommand ??= new AsyncRelayCommand(ExecuteUndoAsync, () => CanUndo);

    /// <summary>
    /// Acción que cancela la operación EN CURSO de esta entry (típicamente dispara su CancellationTokenSource).
    /// La rellena la operación cancelable (p. ej. una descarga); si es null, la operación no se puede cancelar.
    /// </summary>
    public Action? CancelAction
    {
        get => _cancelAction;
        set
        {
            if (SetProperty(ref _cancelAction, value))
                RaiseCancelState();
        }
    }

    /// <summary>True una vez que la operación de esta entry se ha cancelado desde el log.</summary>
    public bool IsCancelled
    {
        get => _isCancelled;
        private set
        {
            if (SetProperty(ref _isCancelled, value))
                RaiseCancelState();
        }
    }

    /// <summary>
    /// La entry se puede cancelar: tiene acción de cancelación, su operación sigue en curso y no se ha cancelado
    /// aún. El botón de cancelar del log se enlaza a esto, así que desaparece al finalizar la operación.
    /// </summary>
    public bool CanCancel => _cancelAction != null && !IsOperationFinished && !IsCancelled;

    /// <summary>Comando que cancela la operación de esta entry (habilitado solo si <see cref="CanCancel"/>).</summary>
    public IRelayCommand CancelCommand => _cancelCommand ??= new RelayCommand(ExecuteCancel, () => CanCancel);

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public int Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }
    public long OperationId
    {
        get => _operationId;
        set
        {
            if (SetProperty(ref _operationId, value))
                OnPropertyChanged(nameof(OperationIdText));
        }
    }

    public TimeSpan Elapsed => _watch.Elapsed;
    #endregion

    #region Computed properties
    public string OperationIdText => $"#{OperationId}";
    public string ProgressText => $"[{Progress}%]";
    public string DurationText => $"[{Duration} ms]";
    #endregion

    #region Constructors
    public ProgressNotifier(bool startWatch = true)
    {
        if (startWatch) _watch.Start();
    }
    #endregion

    #region Methods
    public void StartOperation()
    {
        if (!_watch.IsRunning)
            _watch.Start();
    }

    public void FinishOperation()
    {
        if (_watch.IsRunning)
            _watch.Stop();

        Duration = _watch.ElapsedMilliseconds;
        IsOperationFinished = true;
    }

    public void Reset()
    {
        _watch.Reset();
        Duration = 0;
        Progress = 0;
        IsOperationFinished = false;
        IsException = false;
        IsIndeterminate = false;
    }

    /// <summary>
    /// Reactiva una operación ya finalizada SIN perder el tiempo acumulado: reanuda el cronómetro desde
    /// donde quedó (no lo pone a cero, y el hueco parado no cuenta) y la vuelve a marcar como en ejecución.
    /// </summary>
    public void Resume()
    {
        IsOperationFinished = false;
        IsException = false;

        if (!_watch.IsRunning)
            _watch.Start();
    }

    private async Task ExecuteUndoAsync()
    {
        if (!CanUndo)
            return;

        try
        {
            await _undoAction!();
            IsUndone = true;
        }
        catch
        {
            // Un undo fallido marca la entry como error pero no la da por deshecha (sigue reversible).
            IsException = true;
        }
    }

    /// <summary>Invalida el undo de esta entry (deja de ser reversible) sin marcarla como deshecha.</summary>
    public void DisableUndo() => UndoAction = null;

    private void RaiseUndoState()
    {
        OnPropertyChanged(nameof(CanUndo));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteCancel()
    {
        if (!CanCancel)
            return;

        // Marca la entry como cancelada de inmediato (oculta el botón) y dispara la cancelación real. El
        // servicio que ejecuta la operación pondrá el mensaje final y la marcará como error al abortar.
        IsCancelled = true;
        _cancelAction?.Invoke();
    }

    private void RaiseCancelState()
    {
        OnPropertyChanged(nameof(CanCancel));
        CancelCommand.NotifyCanExecuteChanged();
    }
    #endregion
}
