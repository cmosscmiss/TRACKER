using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

public sealed partial class GameListControl : UserControl
{
    #region Constants
    private const int MaxScrollAttempts = 5;

    private const double SelectorFadeMs = 180;
    private const double SelectorResizeMs = 240;
    private const double SelectorFallbackHeight = 64;
    #endregion

    #region Attributes
    private Game? _pendingScrollGame;
    private int _scrollAttempts;
    private bool _isLoaded;

    // Animación del selector de tipo de medio integrado (fade + colapsar/expandir su fila para que la lista de
    // juegos tome el espacio). _selectorReady evita animar el estado inicial (que se aplica sin animación en Loaded).
    private double _selectorNaturalHeight;
    private bool _selectorReady;
    private bool _selectorAnimating;
    private AnimationService.IAnimationHandle? _selectorAnimation;
    #endregion

    #region Dependency Properties
    /// <summary>
    /// ViewModel asociado al control.
    /// Se expone como DependencyProperty para permitir binding desde XAML.
    /// </summary>
    public GameListViewModel? ViewModel
    {
        get => (GameListViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// DependencyProperty que almacena el ViewModel.
    /// Incluye un callback que permite suscribirse al evento RequestScrollIntoView.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(GameListViewModel), typeof(GameListControl), new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>
    /// Indica si el selector de tipo de medio integrado (sobre la lista) está visible. Al cambiar en caliente, anima
    /// la transición: al ocultar hace fade out y colapsa su fila (la lista crece para ocupar el hueco); al mostrar
    /// expande la fila y luego hace fade in. Por defecto true.
    /// </summary>
    public bool ShowImageTypeSelector
    {
        get => (bool)GetValue(ShowImageTypeSelectorProperty);
        set => SetValue(ShowImageTypeSelectorProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="ShowImageTypeSelector"/>.
    /// </summary>
    public static readonly DependencyProperty ShowImageTypeSelectorProperty = DependencyProperty.Register(nameof(ShowImageTypeSelector), typeof(bool), typeof(GameListControl), new PropertyMetadata(true, OnShowImageTypeSelectorChanged));

    /// <summary>
    /// Anima el selector al cambiar <see cref="ShowImageTypeSelector"/>, salvo en el arranque (antes del primer
    /// Loaded), donde el estado se aplica sin animación.
    /// </summary>
    private static void OnShowImageTypeSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (GameListControl)d;

        if (control._selectorReady)
            control.AnimateSelector((bool)e.NewValue);
    }
    #endregion

    #region Constructor
    /// <summary>
    /// Constructor del control.
    /// Inicializa el componente y registra los eventos de ciclo de vida.
    /// </summary>
    public GameListControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Se ejecuta cuando el control ya está cargado visualmente.
    /// Solicita un scroll inicial hacia el juego seleccionado, si existe.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        // Suscripción simétrica con Unloaded. El VM es Singleton, así que la DP no cambia en un remontaje: si solo
        // se atara en OnViewModelChanged, nunca se soltaría en Unloaded (fuga del control anterior enganchado al VM)
        // ni se re-ataría al remontar. El '-=' previo la hace idempotente.
        if (ViewModel != null)
        {
            ViewModel.RequestScrollIntoView -= OnRequestScrollIntoView;
            ViewModel.RequestScrollIntoView += OnRequestScrollIntoView;
        }

        // Estado inicial del selector según la propiedad (sin animación); a partir de aquí, los cambios animan.
        ApplySelectorStateImmediate(ShowImageTypeSelector);
        _selectorReady = true;

        if (ViewModel?.SharedDataService?.SelectedGame != null)
            OnRequestScrollIntoView(ViewModel.SharedDataService.SelectedGame);
    }

    /// <summary>
    /// Se ejecuta cuando el control se descarga.
    /// Evita que se sigan intentando scrolls sobre un control ya destruido o no visible.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _pendingScrollGame = null;
        _scrollAttempts = 0;

        // Suelta la suscripción para no dejar el control enganchado al VM Singleton tras desmontarse (fuga).
        if (ViewModel != null)
            ViewModel.RequestScrollIntoView -= OnRequestScrollIntoView;
    }

    /// <summary>
    /// Callback que se ejecuta cuando cambia el ViewModel asignado al control.
    /// Permite suscribirse o desuscribirse al evento RequestScrollIntoView del ViewModel.
    /// </summary>
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (GameListControl)d;

        if (e.OldValue is GameListViewModel oldVm)
            oldVm.RequestScrollIntoView -= control.OnRequestScrollIntoView;

        // Solo re-suscribe aquí si el control ya está cargado (cambio de VM en caliente); en el arranque el VM se
        // asigna antes del Loaded, y es OnLoaded quien suscribe, para no duplicar.
        if (e.NewValue is GameListViewModel newVm && control._isLoaded)
            newVm.RequestScrollIntoView += control.OnRequestScrollIntoView;
    }

    /// <summary>
    /// Recibe una petición de scroll desde el ViewModel.
    /// El scroll no se ejecuta inmediatamente: se guarda como pendiente y se difiere
    /// para dar tiempo al ListView a generar sus elementos visuales.
    /// </summary>
    private void OnRequestScrollIntoView(Game game)
    {
        if (game == null)
            return;

        _pendingScrollGame = game;
        _scrollAttempts = 0;

        ScrollPendingGameIntoView();
    }
    #endregion

    #region Methods (private) - Image type selector
    /// <summary>
    /// Mientras el selector está desplegado y no hay animación en curso, recuerda su alto natural (destino de la
    /// animación de expansión/colapso). Se ignora durante la animación y cuando está oculto.
    /// </summary>
    private void OnSelectorHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_selectorAnimating && ShowImageTypeSelector && e.NewSize.Height > 0)
            _selectorNaturalHeight = e.NewSize.Height;
    }

    /// <summary>
    /// Aplica el estado del selector sin animación (arranque): fila a Auto/0 y contenido visible/colapsado.
    /// </summary>
    private void ApplySelectorStateImmediate(bool visible)
    {
        _selectorAnimation?.Cancel();
        _selectorAnimation = null;
        _selectorAnimating = false;

        SelectorRow.Height = visible ? GridLength.Auto : new GridLength(0);
        SelectorHost.Opacity = visible ? 1 : 0;
        SelectorHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Anima mostrar/ocultar el selector de forma secuencial (sin aplastamiento visible):
    /// - Ocultar: fade out del contenido y, al terminar, colapso de su fila de su alto a 0; la lista crece hacia arriba.
    /// - Mostrar: expansión de la fila de 0 a su alto natural (la lista cede el hueco) y, al terminar, fade in.
    /// </summary>
    private void AnimateSelector(bool visible)
    {
        _selectorAnimation?.Cancel();
        _selectorAnimation = null;

        if (!visible)
        {
            double from = SelectorHost.ActualHeight > 0 ? SelectorHost.ActualHeight : _selectorNaturalHeight;
            if (from > 0)
                _selectorNaturalHeight = from;

            _selectorAnimating = true;

            var fade = AnimationService.CreateOpacityAnimation(SelectorHost, SelectorHost.Opacity, 0, SelectorFadeMs);
            fade.Completed += () =>
            {
                double collapseFrom = _selectorNaturalHeight > 0 ? _selectorNaturalHeight : SelectorHost.ActualHeight;
                SelectorRow.Height = new GridLength(collapseFrom);

                var collapse = AnimationService.CreateDoubleAnimation(v => SelectorRow.Height = new GridLength(v), collapseFrom, 0, SelectorResizeMs);
                collapse.Completed += () =>
                {
                    SelectorHost.Visibility = Visibility.Collapsed;
                    _selectorAnimating = false;
                    _selectorAnimation = null;
                };
                _selectorAnimation = collapse;
                collapse.Start();
            };
            _selectorAnimation = fade;
            fade.Start();
        }
        else
        {
            double target = _selectorNaturalHeight > 0 ? _selectorNaturalHeight : MeasureSelectorHeight();
            if (target <= 0)
                target = SelectorFallbackHeight;

            _selectorAnimating = true;

            SelectorHost.Visibility = Visibility.Visible;
            SelectorHost.Opacity = 0;
            SelectorRow.Height = new GridLength(0);

            var expand = AnimationService.CreateDoubleAnimation(v => SelectorRow.Height = new GridLength(v), 0, target, SelectorResizeMs);
            expand.Completed += () =>
            {
                SelectorRow.Height = GridLength.Auto;

                var fade = AnimationService.CreateOpacityAnimation(SelectorHost, 0, 1, SelectorFadeMs);
                fade.Completed += () =>
                {
                    _selectorAnimating = false;
                    _selectorAnimation = null;
                };
                _selectorAnimation = fade;
                fade.Start();
            };
            _selectorAnimation = expand;
            expand.Start();
        }
    }

    /// <summary>
    /// Mide el alto natural del selector cuando aún no se conoce (p. ej. arrancó oculto), forzando un Measure del
    /// contenido con el ancho disponible del control.
    /// </summary>
    private double MeasureSelectorHeight()
    {
        SelectorHost.Visibility = Visibility.Visible;

        double width = ActualWidth;
        SelectorHost.Measure(new Windows.Foundation.Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));

        return SelectorHost.DesiredSize.Height;
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Intenta hacer scroll hacia el juego pendiente.
    /// Si el contenedor visual todavía no existe, reintenta unas pocas veces.
    /// </summary>
    private void ScrollPendingGameIntoView()
    {
        if (!_isLoaded || _pendingScrollGame == null)
            return;

        lvGamesList.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_isLoaded || _pendingScrollGame == null)
                return;

            var game = _pendingScrollGame;

            lvGamesList.UpdateLayout();
            lvGamesList.ScrollIntoView(game);
            lvGamesList.UpdateLayout();

            var container = lvGamesList.ContainerFromItem(game);

            if (container == null && _scrollAttempts < MaxScrollAttempts)
            {
                _scrollAttempts++;
                ScrollPendingGameIntoView();
                return;
            }

            _pendingScrollGame = null;
            _scrollAttempts = 0;
        });
    }
    #endregion
}