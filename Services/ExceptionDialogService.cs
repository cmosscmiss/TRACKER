using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Services;

/// <summary>
/// Servicio encargado de mostrar diálogos de error de forma global en la aplicación.
/// 
/// Responsabilidades:
/// - Escuchar los mensajes emitidos por <see cref="ExceptionService"/>.
/// - Mantener una cola de mensajes de error pendientes.
/// - Mostrar los errores mediante <see cref="ContentDialog"/> sobre la ventana activa.
/// - Garantizar que los diálogos se muestran en el hilo correcto de UI.
/// - Evitar que varios diálogos de error se abran simultáneamente.
/// 
/// Este servicio actúa como puente entre la gestión lógica de excepciones
/// y su presentación visual en la interfaz de usuario.
/// </summary>
public sealed class ExceptionDialogService : IDisposable
{
    #region Attributes
    /// <summary>
    /// Servicio centralizado que emite los mensajes de error generados por la aplicación.
    /// </summary>
    private readonly ExceptionService _exceptionService;

    /// <summary>
    /// Servicio encargado de conocer la ventana activa y proporcionar su <see cref="Microsoft.UI.Xaml.XamlRoot"/>.
    /// </summary>
    private readonly WindowService _windowService;

    /// <summary>
    /// Servicio de diálogos: muestra el diálogo de error con el estilo de la app.
    /// </summary>
    private readonly DialogsService _dialogsService;

    /// <summary>
    /// Cola de mensajes de error pendientes de mostrar.
    /// Se utiliza para evitar perder errores cuando llegan varios mensajes seguidos
    /// o cuando ya hay un diálogo mostrándose en pantalla.
    /// </summary>
    private readonly Queue<string> _pendingMessages = new();

    /// <summary>
    /// Indica si actualmente se está procesando la cola de mensajes.
    /// Evita que se ejecuten varios procesos simultáneos intentando mostrar
    /// diálogos al mismo tiempo.
    /// </summary>
    private bool _isProcessing;

    /// <summary>
    /// Indica si el servicio ya ha sido liberado.
    /// Se utiliza para evitar procesar nuevos mensajes o repetir la desuscripción
    /// de eventos después de llamar a <see cref="Dispose"/>.
    /// </summary>
    private bool _disposed;
    #endregion

    #region Constructor and Initialization
    /// <summary>
    /// Inicializa una nueva instancia de <see cref="ExceptionDialogService"/>.
    /// 
    /// Durante la construcción, el servicio se suscribe a:
    /// - <see cref="ExceptionService.ErrorMessageRaised"/>, para recibir mensajes de error.
    /// - <see cref="WindowService.ActiveWindowChanged"/>, para intentar mostrar errores pendientes
    ///   cuando exista una ventana activa disponible.
    /// </summary>
    /// <param name="exceptionService">
    /// Servicio que centraliza la gestión de excepciones y emite mensajes de error.
    /// </param>
    /// <param name="windowService">
    /// Servicio que mantiene la referencia a la ventana activa de la aplicación.
    /// </param>
    public ExceptionDialogService(ExceptionService exceptionService, WindowService windowService, DialogsService dialogsService)
    {
        _exceptionService = exceptionService;
        _windowService = windowService;
        _dialogsService = dialogsService;

        _exceptionService.ErrorMessageRaised += OnErrorMessageRaised;
        _windowService.ActiveWindowChanged += RequestProcessing;
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Gestiona un nuevo mensaje de error emitido por <see cref="ExceptionService"/>.
    /// El mensaje se añade a la cola de pendientes y se solicita el procesamiento
    /// de dicha cola.
    /// </summary>
    /// <param name="message">
    /// Mensaje de error que debe mostrarse al usuario.
    /// </param>
    private void OnErrorMessageRaised(string message)
    {
        lock (_pendingMessages)
        {
            _pendingMessages.Enqueue(message);
        }

        RequestProcessing();
    }

    /// <summary>
    /// Solicita el procesamiento de la cola de mensajes de error.
    /// 
    /// Si hay una ventana activa, el procesamiento se envía a su <c>DispatcherQueue</c>
    /// para garantizar que los diálogos se crean y muestran en el hilo de UI.
    /// </summary>
    private void RequestProcessing()
    {
        if (_disposed)
            return;

        var dispatcherQueue = _windowService.ActiveWindow?.DispatcherQueue;

        if (dispatcherQueue is null)
            return;

        dispatcherQueue.TryEnqueue(() =>
        {
            _ = ProcessQueueAsync();
        });
    }

    /// <summary>
    /// Procesa la cola de mensajes pendientes y muestra un diálogo por cada error.
    /// 
    /// Los mensajes se muestran secuencialmente para evitar abrir varios
    /// <see cref="ContentDialog"/> al mismo tiempo. Si no hay una ventana activa
    /// o no existe un <see cref="Microsoft.UI.Xaml.XamlRoot"/> disponible,
    /// el procesamiento se detiene hasta que pueda reintentarse.
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        if (_isProcessing)
            return;

        _isProcessing = true;

        try
        {
            while (true)
            {
                var xamlRoot = _windowService.ActiveXamlRoot;

                if (xamlRoot is null)
                    break;

                string message;
                lock (_pendingMessages)
                {
                    if (_pendingMessages.Count == 0)
                        break;

                    message = _pendingMessages.Dequeue();
                }

                try
                {
                    await _dialogsService.AlertAsync(xamlRoot, "Error", message, "Aceptar");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error showing dialog: {ex}");
                }
            }
        }
        finally
        {
            _isProcessing = false;
            bool hasPendingMessages;
            lock (_pendingMessages)
            {
                hasPendingMessages = _pendingMessages.Count > 0;
            }

            if (hasPendingMessages)
            {
                RequestProcessing();
            }
        }
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Libera los recursos usados por el servicio y cancela las suscripciones a eventos.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _exceptionService.ErrorMessageRaised -= OnErrorMessageRaised;
        _windowService.ActiveWindowChanged -= RequestProcessing;
    }
    #endregion
}