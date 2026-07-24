using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Tracker.Services;

/// <summary>
/// Servicio centralizado para la creación y ejecución de animaciones en WinUI 3.
/// 
/// Encapsula animaciones basadas tanto en DispatcherTimer como en Composition API,
/// proporcionando una API común mediante manejadores cancelables.
/// 
/// El objetivo principal de este servicio es evitar que cada control gestione sus
/// propias animaciones de forma aislada, reduciendo duplicación y permitiendo
/// cancelar animaciones anteriores cuando una nueva interacción visual debe tomar
/// el control.
/// </summary>
public static class AnimationService
{
    #region Interfaces and nested classes
    /// <summary>
    /// Define el contrato común que deben cumplir todas las animaciones creadas
    /// por el servicio.
    /// 
    /// Permite iniciar una animación, cancelarla, consultar si sigue en ejecución
    /// y reaccionar cuando finaliza de forma natural.
    /// </summary>
    public interface IAnimationHandle
    {
        /// <summary>
        /// Evento que se lanza cuando la animación finaliza de forma natural.
        /// 
        /// No se lanza cuando la animación se cancela explícitamente.
        /// </summary>
        event Action? Completed;

        /// <summary>
        /// Indica si la animación está actualmente en ejecución.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Inicia la animación.
        /// 
        /// Si la animación ya está en ejecución, no realiza ninguna acción.
        /// </summary>
        void Start();

        /// <summary>
        /// Cancela la animación en curso.
        /// 
        /// La cancelación detiene la animación sin lanzar el evento Completed.
        /// </summary>
        void Cancel();
    }

    /// <summary>
    /// Implementación de animación basada en DispatcherTimer.
    /// 
    /// Se utiliza para animaciones frame-a-frame sobre propiedades XAML simples,
    /// como Width, Height, Opacity, ScaleTransform o TranslateTransform.
    /// </summary>
    private class TimerAnimationHandle : IAnimationHandle
    {
        private readonly double _durationMs;
        private readonly Action<double> _onFrame;
        private readonly Action? _onCompleted;

        private DispatcherTimer? _timer;
        private DateTime _start;
        private DateTime _end;
        private bool _isCancelled;
        private bool _hasCompleted;

        public event Action? Completed;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Crea una nueva animación basada en temporizador.
        /// </summary>
        /// <param name="durationMs">Duración total de la animación, en milisegundos.</param>
        /// <param name="onFrame">Acción invocada en cada frame con el progreso normalizado entre 0 y 1.</param>
        /// <param name="onCompleted">Acción opcional invocada al finalizar la animación de forma natural.</param>
        public TimerAnimationHandle(double durationMs, Action<double> onFrame, Action? onCompleted = null)
        {
            _durationMs = durationMs;
            _onFrame = onFrame;
            _onCompleted = onCompleted;
        }

        /// <summary>
        /// Inicia la animación y configura el DispatcherTimer que generará los frames.
        /// 
        /// Si la duración es cero o negativa, la animación se completa inmediatamente.
        /// </summary>
        public void Start()
        {
            if (IsRunning)
                return;

            _isCancelled = false;
            _hasCompleted = false;

            if (_durationMs <= 0)
            {
                Complete();
                return;
            }

            _start = DateTime.Now;
            _end = _start.AddMilliseconds(_durationMs);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            _timer.Tick += OnTick;

            IsRunning = true;
            _timer.Start();
        }

        /// <summary>
        /// Cancela la animación y detiene el temporizador asociado.
        /// 
        /// No invoca la acción de finalización ni el evento Completed.
        /// </summary>
        public void Cancel()
        {
            if (_isCancelled)
                return;

            _isCancelled = true;

            StopTimer();
        }

        /// <summary>
        /// Gestiona cada tick del temporizador, calcula el progreso temporal
        /// y ejecuta el callback de frame.
        /// </summary>
        /// <param name="sender">Objeto que ha generado el evento.</param>
        /// <param name="e">Argumentos del evento.</param>
        private void OnTick(object? sender, object e)
        {
            if (_isCancelled)
                return;

            var now = DateTime.Now;
            if (now >= _end)
            {
                Complete();
                return;
            }

            double progress = (now - _start).TotalMilliseconds / _durationMs;
            progress = Math.Max(0, Math.Min(1, progress));

            _onFrame(progress);
        }

        /// <summary>
        /// Finaliza la animación de forma natural.
        /// 
        /// Detiene el temporizador, ejecuta el callback de finalización y lanza
        /// el evento Completed.
        /// </summary>
        private void Complete()
        {
            if (_hasCompleted || _isCancelled)
                return;

            _hasCompleted = true;

            StopTimer();

            _onCompleted?.Invoke();
            Completed?.Invoke();
        }

        /// <summary>
        /// Detiene y libera el DispatcherTimer utilizado por la animación.
        /// </summary>
        private void StopTimer()
        {
            if (_timer is not null)
            {
                _timer.Tick -= OnTick;
                _timer.Stop();
                _timer = null;
            }

            IsRunning = false;
        }
    }

    /// <summary>
    /// Manejador de animación compuesto que ejecuta varias animaciones en paralelo.
    /// 
    /// El manejador se considera completado únicamente cuando todas las animaciones
    /// hijas han finalizado de forma natural.
    /// </summary>
    private class ParallelAnimationHandle : IAnimationHandle
    {
        private readonly List<IAnimationHandle> _animations;
        private readonly Action? _onCompleted;

        private bool _isCancelled;
        private int _pending;

        public event Action? Completed;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Crea un manejador para ejecutar varias animaciones al mismo tiempo.
        /// </summary>
        /// <param name="animations">Colección de animaciones que se ejecutarán en paralelo.</param>
        /// <param name="onCompleted">Acción opcional invocada cuando todas las animaciones finalicen.</param>
        public ParallelAnimationHandle(
            IEnumerable<IAnimationHandle> animations,
            Action? onCompleted = null)
        {
            _animations = animations.ToList();
            _onCompleted = onCompleted;
        }

        /// <summary>
        /// Inicia todas las animaciones hijas y se suscribe a sus eventos de finalización.
        /// 
        /// Si no hay animaciones hijas, el manejador se completa inmediatamente.
        /// </summary>
        public void Start()
        {
            if (IsRunning)
                return;

            _isCancelled = false;
            _pending = _animations.Count;

            if (_pending == 0)
            {
                Complete();
                return;
            }

            IsRunning = true;

            foreach (var animation in _animations)
            {
                animation.Completed += OnChildCompleted;
                animation.Start();
            }
        }

        /// <summary>
        /// Cancela todas las animaciones hijas y elimina las suscripciones a sus eventos.
        /// </summary>
        public void Cancel()
        {
            if (_isCancelled)
                return;

            _isCancelled = true;

            foreach (var animation in _animations)
            {
                animation.Completed -= OnChildCompleted;
                animation.Cancel();
            }

            IsRunning = false;
        }

        /// <summary>
        /// Gestiona la finalización individual de cada animación hija.
        /// 
        /// Cuando todas las animaciones han terminado, completa el manejador compuesto.
        /// </summary>
        private void OnChildCompleted()
        {
            if (_isCancelled)
                return;

            _pending--;

            if (_pending <= 0)
            {
                Complete();
            }
        }

        /// <summary>
        /// Finaliza el conjunto de animaciones en paralelo.
        /// 
        /// Limpia las suscripciones, marca el manejador como detenido y lanza
        /// los callbacks de finalización.
        /// </summary>
        private void Complete()
        {
            if (_isCancelled)
                return;

            foreach (var animation in _animations)
            {
                animation.Completed -= OnChildCompleted;
            }

            IsRunning = false;

            _onCompleted?.Invoke();
            Completed?.Invoke();
        }
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Aplica una función de easing cúbica de entrada y salida.
    /// 
    /// Esta curva suaviza el inicio y el final de la animación, evitando cambios
    /// demasiado bruscos en las propiedades visuales.
    /// </summary>
    /// <param name="t">Progreso normalizado de la animación, entre 0 y 1.</param>
    /// <returns>Valor transformado por la curva de easing.</returns>
    private static double EaseInOutCubic(double t)
    {
        return t < 0.5
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Crea una animación genérica para interpolar un valor double.
    /// 
    /// La animación calcula el valor intermedio entre un valor inicial y uno final,
    /// aplicando easing cúbico antes de invocar el setter indicado.
    /// </summary>
    /// <param name="setter">Acción que aplica el valor calculado sobre la propiedad correspondiente.</param>
    /// <param name="from">Valor inicial de la animación.</param>
    /// <param name="to">Valor final de la animación.</param>
    /// <param name="durationMs">Duración de la animación, en milisegundos.</param>
    /// <returns>Manejador cancelable de la animación.</returns>
    public static IAnimationHandle CreateDoubleAnimation(Action<double> setter, double from, double to, double durationMs = 250)
    {
        return new TimerAnimationHandle(
            durationMs,
            progress =>
            {
                double eased = EaseInOutCubic(progress);
                setter(from + (to - from) * eased);
            },
            onCompleted: () => setter(to));
    }

    /// <summary>
    /// Crea una animación genérica para interpolar dos valores double simultáneamente.
    /// 
    /// Resulta útil para propiedades que trabajan con coordenadas X/Y, como
    /// TranslateTransform.
    /// </summary>
    /// <param name="setter">Acción que aplica los valores X e Y calculados.</param>
    /// <param name="fromX">Valor inicial del eje X.</param>
    /// <param name="toX">Valor final del eje X.</param>
    /// <param name="fromY">Valor inicial del eje Y.</param>
    /// <param name="toY">Valor final del eje Y.</param>
    /// <param name="durationMs">Duración de la animación, en milisegundos.</param>
    /// <returns>Manejador cancelable de la animación.</returns>
    public static IAnimationHandle CreateVector2Animation(Action<double, double> setter, double fromX, double toX, double fromY, double toY, double durationMs = 250)
    {
        return new TimerAnimationHandle(
            durationMs,
            progress =>
            {
                double eased = EaseInOutCubic(progress);

                setter(
                    fromX + (toX - fromX) * eased,
                    fromY + (toY - fromY) * eased);
            },
            onCompleted: () => setter(toX, toY));
    }

    /// <summary>
    /// Crea un manejador compuesto para ejecutar varias animaciones en paralelo.
    /// </summary>
    /// <param name="animations">Animaciones que deben ejecutarse simultáneamente.</param>
    /// <param name="onAllCompleted">Acción opcional invocada cuando todas las animaciones finalizan.</param>
    /// <returns>Manejador cancelable del conjunto de animaciones.</returns>
    public static IAnimationHandle CreateParallelAnimation(IEnumerable<IAnimationHandle> animations, Action? onAllCompleted = null)
    {
        return new ParallelAnimationHandle(animations, onAllCompleted);
    }

    /// <summary>
    /// Crea e inicia varias animaciones en paralelo.
    /// 
    /// Devuelve el manejador compuesto para que el llamador pueda cancelar
    /// posteriormente el conjunto de animaciones si fuera necesario.
    /// </summary>
    /// <param name="animations">Animaciones que deben ejecutarse simultáneamente.</param>
    /// <param name="onAllCompleted">Acción opcional invocada cuando todas las animaciones finalizan.</param>
    /// <returns>Manejador cancelable del conjunto de animaciones ya iniciado.</returns>
    public static IAnimationHandle RunAnimations(IEnumerable<IAnimationHandle> animations, Action? onAllCompleted = null)
    {
        var handle = CreateParallelAnimation(animations, onAllCompleted);
        handle.Start();

        return handle;
    }

    /// <summary>
    /// Inicia una animación y devuelve una tarea que se completa cuando la animación
    /// finaliza de forma natural.
    /// 
    /// Esta extensión permite usar animaciones con async/await.
    /// </summary>
    /// <param name="handle">Manejador de animación que se quiere iniciar.</param>
    /// <returns>Task que se completa cuando se lanza el evento Completed.</returns>
    public static Task StartAsync(this IAnimationHandle handle)
    {
        var tcs = new TaskCompletionSource();

        void OnCompleted()
        {
            handle.Completed -= OnCompleted;
            tcs.SetResult();
        }

        handle.Completed += OnCompleted;
        handle.Start();

        return tcs.Task;
    }
    #endregion

    #region Helper methods for common animations
    /// <summary>
    /// Crea una animación para modificar progresivamente la propiedad Width
    /// de un FrameworkElement.
    /// </summary>
    /// <param name="element">Elemento cuya anchura se va a animar.</param>
    /// <param name="from">Anchura inicial.</param>
    /// <param name="to">Anchura final.</param>
    /// <param name="durationMs">Duración de la animación, en milisegundos.</param>
    /// <returns>Manejador cancelable de la animación.</returns>
    public static IAnimationHandle CreateWidthAnimation(FrameworkElement element, double from, double to, double durationMs = 250)
    {
        return CreateDoubleAnimation(v => element.Width = v, from, to, durationMs);
    }

    /// <summary>
    /// Crea una animación para modificar progresivamente la propiedad Height
    /// de un FrameworkElement.
    /// </summary>
    /// <param name="element">Elemento cuya altura se va a animar.</param>
    /// <param name="from">Altura inicial.</param>
    /// <param name="to">Altura final.</param>
    /// <param name="durationMs">Duración de la animación, en milisegundos.</param>
    /// <returns>Manejador cancelable de la animación.</returns>
    public static IAnimationHandle CreateHeightAnimation(FrameworkElement element, double from, double to, double durationMs = 250)
    {
        return CreateDoubleAnimation(v => element.Height = v, from, to, durationMs);
    }

    /// <summary>
    /// Crea una animación para modificar progresivamente la opacidad de un UIElement.
    /// </summary>
    /// <param name="element">Elemento cuya opacidad se va a animar.</param>
    /// <param name="from">Opacidad inicial.</param>
    /// <param name="to">Opacidad final.</param>
    /// <param name="durationMs">Duración de la animación, en milisegundos.</param>
    /// <returns>Manejador cancelable de la animación.</returns>
    public static IAnimationHandle CreateOpacityAnimation(UIElement element, double from, double to, double durationMs = 250)
    {
        return CreateDoubleAnimation(v => element.Opacity = v, from, to, durationMs);
    }

    /// <summary>
    /// Crea una animación de escala uniforme para un ScaleTransform.
    /// 
    /// El mismo valor interpolado se aplica tanto a ScaleX como a ScaleY.
    /// </summary>
    /// <param name="transform">Transformación de escala que se va a animar.</param>
    /// <param name="from">Escala inicial.</param>
    /// <param name="to">Escala final.</param>
    /// <param name="durationMs">Duración de la animación, en milisegundos.</param>
    /// <returns>Manejador cancelable de la animación.</returns>
    public static IAnimationHandle CreateScaleAnimation(ScaleTransform transform, double from, double to, double durationMs = 250)
    {
        return CreateDoubleAnimation(
            v =>
            {
                transform.ScaleX = v;
                transform.ScaleY = v;
            },
            from,
            to,
            durationMs);
    }

    /// <summary>
    /// Crea una animación compuesta que modifica escala y opacidad en paralelo.
    /// 
    /// Es útil para efectos visuales de entrada, salida, hover o selección.
    /// </summary>
    /// <param name="transform">ScaleTransform sobre el que se aplicará la escala.</param>
    /// <param name="opacityElement">Elemento cuya opacidad se animará.</param>
    /// <param name="targetScale">Escala final deseada.</param>
    /// <param name="targetOpacity">Opacidad final deseada.</param>
    /// <param name="durationMs">Duración de ambas animaciones, en milisegundos.</param>
    /// <param name="onCompleted">Acción opcional invocada cuando ambas animaciones finalizan.</param>
    /// <returns>Manejador cancelable de la animación compuesta.</returns>
    public static IAnimationHandle CreateScaleAndOpacityAnimation(ScaleTransform transform, UIElement opacityElement, double targetScale, double targetOpacity, double durationMs = 250, Action? onCompleted = null)
    {
        return CreateParallelAnimation(
            new[]
            {
                CreateScaleAnimation(transform, transform.ScaleX, targetScale, durationMs),
                CreateOpacityAnimation(opacityElement, opacityElement.Opacity, targetOpacity, durationMs)
            },
            onCompleted);
    }

    /// <summary>
    /// Crea una animación para modificar progresivamente las propiedades X e Y
    /// de un TranslateTransform.
    /// </summary>
    /// <param name="transform">TranslateTransform que se va a animar.</param>
    /// <param name="fromX">Valor inicial de X.</param>
    /// <param name="toX">Valor final de X.</param>
    /// <param name="fromY">Valor inicial de Y.</param>
    /// <param name="toY">Valor final de Y.</param>
    /// <param name="durationMs">Duración de la animación, en milisegundos.</param>
    /// <returns>Manejador cancelable de la animación.</returns>
    public static IAnimationHandle CreateTranslateAnimation(TranslateTransform transform, double fromX, double toX, double fromY, double toY, double durationMs = 250)
    {
        return CreateVector2Animation(
            (x, y) =>
            {
                transform.X = x;
                transform.Y = y;
            },
            fromX,
            toX,
            fromY,
            toY,
            durationMs);
    }
    #endregion
}