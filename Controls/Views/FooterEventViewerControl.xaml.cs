using System;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;
using MM4LB.Services;
using Windows.Foundation;

namespace MM4LB.Controls.Views;

/// <summary>
/// Visor compacto del ACTIVITY LOG para el pie de la aplicación, como plan B cuando la consola no está como widget
/// visible. Muestra un evento (círculo de estado, mensaje y botón de cancelar/undo) dentro de un recuadro hundido y
/// permite navegar por el log con una mini barra (anterior / posterior / último). Por defecto sigue al evento más
/// reciente. Al cambiar de evento desliza el saliente y entra el nuevo (la dirección depende de hacia dónde se
/// navega). Si el mensaje no cabe, tras un segundo hace marquee (lo desplaza a la izquierda y al llegar al final
/// reinicia el ciclo).
///
/// El contenido visible se enlaza a <see cref="DisplayedEntry"/> (no directamente al ViewModel), para que la
/// transición tenga tiempo de mostrar el evento saliente.
/// </summary>
public sealed partial class FooterEventViewerControl : UserControl
{
    private const double TransitionMs = 160;

    // Marquee del mensaje: espera en cada extremo, velocidad del desplazamiento (lento), duración del rebobinado
    // (rápido, fija independientemente de la distancia) y refresco.
    private const double MarqueeDelayMs = 1000;
    private const double MarqueeSpeedPxPerSec = 35;
    private const double MarqueeRewindMs = 100;
    private const double MarqueeFrameMs = 16;

    private AnimationService.IAnimationHandle? _animation;

    // True mientras el visor "sigue" al evento más reciente: los eventos nuevos se muestran automáticamente. Pasa a
    // false al navegar a un evento concreto, y vuelve a true al llegar de nuevo al último (o pulsar el botón "último").
    private bool _followingLatest = true;

    // Estado del marquee del mensaje. El ciclo es: espera -> desplazamiento lento a la izquierda -> espera ->
    // rebobinado rápido a la derecha -> espera -> ... (cada espera "sabe" si la sigue un desplazamiento o un rebobinado).
    private enum MarqueePhase { Delay, ScrollLeft, RewindRight }

    private DispatcherTimer? _marqueeTimer;
    private double _marqueeDistance;
    private double _marqueePhaseElapsedMs;
    private MarqueePhase _marqueePhase;
    private bool _delayLeadsToScroll;

    private readonly ThemeService _themeService;

    public FooterEventViewerControl()
    {
        InitializeComponent();

        _themeService = App.GetService<ThemeService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _themeService.ThemeChanged += OnThemeChanged;
        UpdateBorderGradient();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateBorderGradient();

    /// <summary>
    /// Refresca los colores del gradiente del borde con el tema actual. El gradiente usa recursos de tipo
    /// <see cref="Windows.UI.Color"/> (tipo por valor), que no se propagan solos al cambiar de tema en caliente como sí
    /// lo hacen los brushes; por eso se reasignan aquí (variante con alpha 0.8, igual que los recursos <c>...Opacity80</c>).
    /// </summary>
    private void UpdateBorderGradient()
    {
        if (BorderStopDark is null) { return; }

        BorderStopDark.Color = WithOpacity80(_themeService.AccentDarkColor);
        BorderStopMid.Color = WithOpacity80(_themeService.AccentColor);
        BorderStopLight.Color = WithOpacity80(_themeService.AccentLightColor);
    }

    private static Windows.UI.Color WithOpacity80(Windows.UI.Color color)
        => Windows.UI.Color.FromArgb(204, color.R, color.G, color.B);

    /// <summary>ViewModel de la consola: de él se observan los <see cref="ConsoleViewModel.LogEntries"/>.</summary>
    public ConsoleViewModel? ViewModel
    {
        get => (ConsoleViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(ConsoleViewModel), typeof(FooterEventViewerControl), new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>Evento que se está mostrando (puede ir por detrás del último durante la transición o al navegar).</summary>
    public ProgressNotifier? DisplayedEntry
    {
        get => (ProgressNotifier?)GetValue(DisplayedEntryProperty);
        set => SetValue(DisplayedEntryProperty, value);
    }

    public static readonly DependencyProperty DisplayedEntryProperty =
        DependencyProperty.Register(nameof(DisplayedEntry), typeof(ProgressNotifier), typeof(FooterEventViewerControl), new PropertyMetadata(null, OnDisplayedEntryChanged));

    /// <summary>Hay un evento más antiguo al que navegar (flecha izquierda).</summary>
    public bool CanGoOlder
    {
        get => (bool)GetValue(CanGoOlderProperty);
        set => SetValue(CanGoOlderProperty, value);
    }

    public static readonly DependencyProperty CanGoOlderProperty =
        DependencyProperty.Register(nameof(CanGoOlder), typeof(bool), typeof(FooterEventViewerControl), new PropertyMetadata(false));

    /// <summary>Hay un evento más reciente al que navegar (flecha derecha / botón "último").</summary>
    public bool CanGoNewer
    {
        get => (bool)GetValue(CanGoNewerProperty);
        set => SetValue(CanGoNewerProperty, value);
    }

    public static readonly DependencyProperty CanGoNewerProperty =
        DependencyProperty.Register(nameof(CanGoNewer), typeof(bool), typeof(FooterEventViewerControl), new PropertyMetadata(false));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FooterEventViewerControl)d;

        if (e.OldValue is ConsoleViewModel oldVm)
        {
            oldVm.LogEntries.CollectionChanged -= control.OnLogEntriesChanged;
        }

        if (e.NewValue is ConsoleViewModel newVm)
        {
            newVm.LogEntries.CollectionChanged += control.OnLogEntriesChanged;
            control._followingLatest = true;
            control.DisplayedEntry = newVm.LatestEntry; // estado inicial, sin animar
        }
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Un evento nuevo entra por arriba del log. Si estamos siguiendo el último, lo mostramos deslizándolo desde
        // la derecha; si el usuario está navegando un evento concreto, lo dejamos donde está (solo se recalcula la
        // navegación, porque los índices han cambiado).
        if (_followingLatest && ViewModel?.LatestEntry is { } latest && latest != DisplayedEntry)
        {
            AnimateTo(latest, fromRight: true);
        }

        UpdateNavState();
    }

    private static void OnDisplayedEntryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FooterEventViewerControl)d).UpdateNavState();

    private void OlderButton_Click(object? sender, RoutedEventArgs e) => Navigate(+1);

    private void NewerButton_Click(object? sender, RoutedEventArgs e) => Navigate(-1);

    private void LatestButton_Click(object? sender, RoutedEventArgs e)
    {
        _followingLatest = true;
        if (ViewModel?.LatestEntry is { } latest)
        {
            AnimateTo(latest, fromRight: true);
        }
    }

    /// <summary>
    /// Navega <paramref name="delta"/> posiciones en el log desde el evento mostrado: +1 = más antiguo (flecha
    /// izquierda), -1 = más reciente (flecha derecha). Más reciente entra desde la derecha; más antiguo, desde la
    /// izquierda.
    /// </summary>
    private void Navigate(int delta)
    {
        if (ViewModel?.LogEntries is not { } entries || DisplayedEntry == null)
        {
            return;
        }

        int index = entries.IndexOf(DisplayedEntry);
        if (index < 0)
        {
            return;
        }

        int target = index + delta;
        if (target < 0 || target >= entries.Count)
        {
            return;
        }

        _followingLatest = target == 0;
        AnimateTo(entries[target], fromRight: delta < 0);
    }

    /// <summary>Recalcula si hay eventos más antiguos / más recientes que el mostrado, para habilitar las flechas.</summary>
    private void UpdateNavState()
    {
        var entries = ViewModel?.LogEntries;
        int count = entries?.Count ?? 0;
        int index = (entries != null && DisplayedEntry != null) ? entries.IndexOf(DisplayedEntry) : -1;

        CanGoOlder = index >= 0 && index < count - 1;
        CanGoNewer = index > 0;
    }

    /// <summary>
    /// Transición entre el evento mostrado y <paramref name="newEntry"/>: el actual sale por un lado y, al terminar,
    /// el nuevo entra desde el otro. Con <paramref name="fromRight"/> el nuevo entra desde la derecha (y el actual
    /// sale por la izquierda); en caso contrario, al revés. Si aún no hay ancho medido o no había evento previo, hace
    /// el cambio directo.
    /// </summary>
    private void AnimateTo(ProgressNotifier newEntry, bool fromRight)
    {
        if (newEntry == DisplayedEntry)
        {
            return;
        }

        _animation?.Cancel();

        double width = ContentClip.ActualWidth;
        if (width <= 0 || DisplayedEntry == null)
        {
            DisplayedEntry = newEntry;
            ContentTransform.X = 0;
            ContentHost.Opacity = 1;
            return;
        }

        double direction = fromRight ? 1 : -1;
        double exitX = -direction * width;
        double enterX = direction * width;

        // Fase 1: el evento actual sale.
        _animation = AnimationService.CreateParallelAnimation(
            new[]
            {
                AnimationService.CreateTranslateAnimation(ContentTransform, 0, exitX, 0, 0, TransitionMs),
                AnimationService.CreateOpacityAnimation(ContentHost, 1, 0, TransitionMs),
            },
            onAllCompleted: () =>
            {
                // Cambia al nuevo evento y lo coloca fuera de vista en el lado contrario...
                DisplayedEntry = newEntry;
                ContentTransform.X = enterX;

                // ...y lo hace entrar deslizándose.
                _animation = AnimationService.RunAnimations(new[]
                {
                    AnimationService.CreateTranslateAnimation(ContentTransform, enterX, 0, 0, 0, TransitionMs),
                    AnimationService.CreateOpacityAnimation(ContentHost, 0, 1, TransitionMs),
                });
            });

        _animation.Start();
    }

    private void ContentClip_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Recorta el contenido al recuadro para que las capas que entran/salen no se vean fuera de él.
        ContentClip.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }

    private void MessageViewport_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Recorta el texto al viewport del mensaje (para que el marquee no se salga hacia el progreso o la barra).
        MessageViewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        RestartMarquee();
    }

    private void MessageText_SizeChanged(object? sender, SizeChangedEventArgs e) => RestartMarquee();

    /// <summary>
    /// (Re)inicia el marquee del mensaje: si el texto no cabe en el viewport, arranca un temporizador que, tras una
    /// espera inicial, lo desplaza linealmente a la izquierda y, al llegar al final, repite el ciclo. Si cabe (o el
    /// viewport aún no está medido / visible), no hace nada.
    /// </summary>
    private void RestartMarquee()
    {
        StopMarquee();

        if (MessageViewport is null || MessageText is null)
        {
            return;
        }

        double viewport = MessageViewport.ActualWidth;
        double overflow = MessageText.ActualWidth - viewport;
        if (viewport <= 0 || overflow <= 1)
        {
            return;
        }

        _marqueeDistance = overflow;
        _marqueePhaseElapsedMs = 0;
        _marqueePhase = MarqueePhase.Delay;
        _delayLeadsToScroll = true;

        _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MarqueeFrameMs) };
        _marqueeTimer.Tick += OnMarqueeTick;
        _marqueeTimer.Start();
    }

    private void OnMarqueeTick(object? sender, object e)
    {
        _marqueePhaseElapsedMs += MarqueeFrameMs;

        switch (_marqueePhase)
        {
            case MarqueePhase.Delay:
                if (_marqueePhaseElapsedMs >= MarqueeDelayMs)
                {
                    _marqueePhase = _delayLeadsToScroll ? MarqueePhase.ScrollLeft : MarqueePhase.RewindRight;
                    _marqueePhaseElapsedMs = 0;
                }
                break;

            case MarqueePhase.ScrollLeft:
                double traveled = MarqueeSpeedPxPerSec * (_marqueePhaseElapsedMs / 1000.0);
                if (traveled >= _marqueeDistance)
                {
                    // Final del texto: fija la posición, espera y luego rebobina rápido.
                    MessageTransform.X = -_marqueeDistance;
                    _marqueePhase = MarqueePhase.Delay;
                    _delayLeadsToScroll = false;
                    _marqueePhaseElapsedMs = 0;
                }
                else
                {
                    MessageTransform.X = -traveled;
                }
                break;

            case MarqueePhase.RewindRight:
                double t = _marqueePhaseElapsedMs / MarqueeRewindMs;
                if (t >= 1)
                {
                    // Inicio del texto: fija la posición, espera y vuelve a desplazar lento.
                    MessageTransform.X = 0;
                    _marqueePhase = MarqueePhase.Delay;
                    _delayLeadsToScroll = true;
                    _marqueePhaseElapsedMs = 0;
                }
                else
                {
                    MessageTransform.X = -_marqueeDistance * (1 - t);
                }
                break;
        }
    }

    private void StopMarquee()
    {
        if (_marqueeTimer is not null)
        {
            _marqueeTimer.Stop();
            _marqueeTimer.Tick -= OnMarqueeTick;
            _marqueeTimer = null;
        }

        if (MessageTransform is not null)
        {
            MessageTransform.X = 0;
        }
    }
}
