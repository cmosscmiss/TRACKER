using System;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Tracker.Models;
using Tracker.Services;

namespace Tracker.Controls.ViewModels;

/// <summary>
/// ViewModel para el selector de layouts.
/// 
/// Este ViewModel:
/// - Expone el layout seleccionado.
/// - Expone el gap aplicado al panel de widgets.
/// - Calcula el margen externo del panel de widgets.
/// - Carga y guarda el layout seleccionado y el gap desde <see cref="AppSettings"/>.
/// - Persiste únicamente el índice del layout, no el objeto completo.
/// </summary>
public class LayoutSelectorViewModel : WidgetViewModelBase
{
    #region Constants
    private const double DefaultWidgetGap = 16;
    private const double MinWidgetGap = 0;
    private const double MaxWidgetGap = 24;

    private const double DefaultWidgetCornerRadius = 18;
    private const double MinWidgetCornerRadius = 0;
    private const double MaxWidgetCornerRadius = 32;

    private const double DefaultWidgetPanelMargin = 8;
    private const double MinWidgetPanelMargin = 0;
    private const double MaxWidgetPanelMargin = 48;

    private const double WidgetPanelTopMargin = 64;
    #endregion

    #region Attributes
    private LayoutInfo _selectedLayout;
    private double _widgetGap = DefaultWidgetGap;
    private double _widgetCornerRadius = DefaultWidgetCornerRadius;
    private double _widgetPanelMargin = DefaultWidgetPanelMargin;
    private bool _splittersEnabled;
    private bool _splittersVisible;

    /// <summary>
    /// Gap del usuario previo a activar los splitters, al que se vuelve al desactivarlos. Es también el
    /// valor que se persiste (no el inflado durante la activación de los splitters).
    /// </summary>
    private double _previousWidgetGap = DefaultWidgetGap;

    private AnimationService.IAnimationHandle? _gapAnimation;
    #endregion

    #region Properties (observable)
    /// <summary>
    /// Gap entre widgets dentro del layout.
    /// Cuando cambia, se recrean los layouts manteniendo la selección actual.
    /// </summary>
    public double WidgetGap
    {
        get => _widgetGap;
        set
        {
            var normalizedValue = NormalizeWidgetGap(value);

            if (SetProperty(ref _widgetGap, normalizedValue))
            {
                RecreateLayoutsKeepingSelection();
                OnPropertyChanged(nameof(WidgetBandMargin));
            }
        }
    }

    /// <summary>
    /// Radio de esquina aplicado visualmente a todos los widgets del panel.
    /// No requiere recrear layouts porque no cambia la distribución, solo la forma visual.
    /// </summary>
    public double WidgetCornerRadius
    {
        get => _widgetCornerRadius;
        set
        {
            var normalizedValue = NormalizeWidgetCornerRadius(value);
            SetProperty(ref _widgetCornerRadius, normalizedValue);
        }
    }

    /// <summary>
    /// Margen exterior del panel de widgets en izquierda, derecha y abajo (el superior lo gestiona la barra de
    /// herramientas flotante). Independiente del gap entre widgets. Al cambiar, notifica <see cref="WidgetPanelMargin"/>.
    /// </summary>
    public double WidgetPanelOuterMargin
    {
        get => _widgetPanelMargin;
        set
        {
            var normalizedValue = NormalizeWidgetPanelMargin(value);

            if (SetProperty(ref _widgetPanelMargin, normalizedValue))
                OnPropertyChanged(nameof(WidgetPanelMargin));
        }
    }

    /// <summary>
    /// Layout seleccionado actualmente.
    /// Esta propiedad se usa tanto por el selector visual como por el panel de widgets.
    /// </summary>
    public LayoutInfo SelectedLayout
    {
        get => _selectedLayout;
        set
        {
            value ??= Layouts.Get(LayoutType.TwoColumns50.Key);

            // Cambio real de layout (no un refresco del mismo índice, como los que provoca la animación del
            // gap al recrear los layouts): si los splitters están activos, desactivarlos primero. Esto los
            // oculta y, en el panel, captura y persiste los tamaños del layout que se abandona antes de que
            // el binding aplique el nuevo (el panel aún apunta al layout viejo en este instante).
            if (SplittersEnabled && (_selectedLayout == null || _selectedLayout.Index != value.Index))
                SplittersEnabled = false;

            SetProperty(ref _selectedLayout, value);
        }
    }

    /// <summary>
    /// Margen aplicado al panel de widgets. Los lados e inferior usan <see cref="WidgetPanelOuterMargin"/>; el
    /// superior es fijo (<see cref="WidgetPanelTopMargin"/>) para dejar hueco a la barra de herramientas flotante.
    /// </summary>
    public Thickness WidgetPanelMargin => new Thickness(WidgetPanelOuterMargin, WidgetPanelTopMargin, WidgetPanelOuterMargin, WidgetPanelOuterMargin);

    /// <summary>
    /// Margen de la banda fija superior del panel (fuera del sistema de slots): mitad del gap en los cuatro lados,
    /// igual que el <c>SlotMargin</c> de los widgets normales, para que la banda quede alineada con ellos y separada
    /// del primer widget por el gap completo (gap/2 de la banda + gap/2 del widget).
    /// </summary>
    public Thickness WidgetBandMargin => new Thickness(WidgetGap / 2);

    /// <summary>
    /// Indica si los grid splitters del panel de widgets están activos. Es estado de UI transitorio:
    /// no se persiste y siempre arranca desactivado.
    /// </summary>
    public bool SplittersEnabled
    {
        get => _splittersEnabled;
        set
        {
            if (SetProperty(ref _splittersEnabled, value))
                AnimateWidgetGapForSplitters(value);
        }
    }

    /// <summary>
    /// Indica si los splitters deben mostrarse en el panel. Al activar, se pone a <c>true</c> solo cuando
    /// termina la animación de apertura del gap (para que aparezcan ya con su hueco); al desactivar, se
    /// pone a <c>false</c> de inmediato, antes de cerrar el gap.
    /// </summary>
    public bool SplittersVisible
    {
        get => _splittersVisible;
        private set => SetProperty(ref _splittersVisible, value);
    }
    #endregion

    #region Constructor
    /// <summary>
    /// Crea un nuevo ViewModel para el selector de layouts.
    /// Inicializa un layout por defecto; la configuración guardada se aplica posteriormente mediante <see cref="LoadConfig"/>.
    /// </summary>
    public LayoutSelectorViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings) : base(sharedDataService, appSettings)
    {
        _selectedLayout = Layouts.Get(LayoutType.TwoColumns50.Key);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Normaliza el valor del gap para evitar valores inválidos procedentes de configuración o entrada externa.
    /// </summary>
    private static double NormalizeWidgetGap(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return DefaultWidgetGap;

        return Math.Clamp(value, MinWidgetGap, MaxWidgetGap);
    }

    /// <summary>
    /// Normaliza el radio de esquina de los widgets para evitar valores inválidos
    /// procedentes de configuración o entrada externa.
    /// </summary>
    private static double NormalizeWidgetCornerRadius(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return DefaultWidgetCornerRadius;

        return Math.Clamp(value, MinWidgetCornerRadius, MaxWidgetCornerRadius);
    }

    /// <summary>
    /// Normaliza el margen exterior del panel para evitar valores inválidos procedentes de configuración o entrada externa.
    /// </summary>
    private static double NormalizeWidgetPanelMargin(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return DefaultWidgetPanelMargin;

        return Math.Clamp(value, MinWidgetPanelMargin, MaxWidgetPanelMargin);
    }

    /// <summary>
    /// Anima el gap entre widgets al activar/desactivar los splitters: al activarlos lo lleva al máximo
    /// permitido para dar aire a las barras de redimensionado; al desactivarlos lo devuelve al valor
    /// previo. Usa <see cref="AnimationService"/>.
    /// </summary>
    private void AnimateWidgetGapForSplitters(bool enabled)
    {
        bool wasAnimating = _gapAnimation?.IsRunning == true;
        _gapAnimation?.Cancel();

        if (enabled)
        {
            // Captura el gap del usuario solo al entrar desde un estado estable (no a mitad de otra
            // animación), para no guardar como "previo" un valor intermedio de la transición.
            if (!wasAnimating)
                _previousWidgetGap = WidgetGap;

            // Los splitters aparecen al terminar la apertura del gap, ya con su hueco hecho.
            var animation = AnimationService.CreateDoubleAnimation(value => WidgetGap = value, WidgetGap, MaxWidgetGap, 300);
            animation.Completed += () => { if (_splittersEnabled) SplittersVisible = true; };
            _gapAnimation = animation;
            _gapAnimation.Start();
        }
        else
        {
            // Ocultar los splitters de inmediato y, después, cerrar el gap.
            SplittersVisible = false;

            _gapAnimation = AnimationService.CreateDoubleAnimation(value => WidgetGap = value, WidgetGap, _previousWidgetGap, 300);
            _gapAnimation.Start();
        }
    }

    /// <summary>
    /// Recrea la colección estática de layouts aplicando el gap actual y mantiene seleccionado el mismo layout si sigue existiendo.
    /// </summary>
    /// <param name="preferredLayoutIndex">Índice del layout que se desea restaurar tras recrear los layouts.</param>
    private void RecreateLayoutsKeepingSelection(int? preferredLayoutIndex = null)
    {
        var layoutIndex = preferredLayoutIndex ?? SelectedLayout.Index;

        Layouts.CreateLayouts(_widgetGap);

        var layout = Layouts.Get(layoutIndex);

        SelectedLayout = layout.Index == layoutIndex ? layout : Layouts.Get(LayoutType.TwoColumns50.Key);
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Carga desde <see cref="AppSettings"/> el layout seleccionado y el gap configurado por el usuario.
    /// </summary>
    public override void LoadConfig()
    {
        var layoutIndex = _appSettings.LayoutSelectorControl.SelectedLayout;

        _widgetGap = NormalizeWidgetGap(_appSettings.LayoutSelectorControl.Gap);
        _previousWidgetGap = _widgetGap;
        _widgetCornerRadius = NormalizeWidgetCornerRadius(_appSettings.LayoutSelectorControl.CornerRadius);
        _widgetPanelMargin = NormalizeWidgetPanelMargin(_appSettings.LayoutSelectorControl.PanelMargin);

        OnPropertyChanged(nameof(WidgetGap));
        OnPropertyChanged(nameof(WidgetCornerRadius));
        OnPropertyChanged(nameof(WidgetPanelOuterMargin));
        OnPropertyChanged(nameof(WidgetPanelMargin));
        OnPropertyChanged(nameof(WidgetBandMargin));

        RecreateLayoutsKeepingSelection(layoutIndex);
    }

    /// <summary>
    /// Guarda en <see cref="AppSettings"/> el layout seleccionado y el gap actual.
    /// </summary>
    public override void SaveConfig()
    {
        _appSettings.LayoutSelectorControl.SelectedLayout = SelectedLayout.Index;
        // Persistir el gap base del usuario, no el inflado mientras los splitters están activos.
        _appSettings.LayoutSelectorControl.Gap = SplittersEnabled ? _previousWidgetGap : WidgetGap;
        _appSettings.LayoutSelectorControl.CornerRadius = WidgetCornerRadius;
        _appSettings.LayoutSelectorControl.PanelMargin = WidgetPanelOuterMargin;
    }

    /// <summary>
    /// Libera recursos asociados al ViewModel.
    /// Actualmente este ViewModel no mantiene suscripciones externas.
    /// </summary>
    public override void Dispose()
    {
    }
    #endregion
}