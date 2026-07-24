using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Services;

namespace MM4LB.Helpers;

/// <summary>
/// Ayuda de UI declarativa, gobernada por un único toggle global (<see cref="SharedDataService.HelpTooltipsEnabled"/>)
/// y localizada por el <see cref="LocalizationService"/>. Dos attached properties:
///
/// - <c>help:Help.Key</c>: clave de recurso de un TOOLTIP. Pone el tooltip localizado en el control cuando la ayuda
///   está activada y lo quita cuando está desactivada. Se actualiza en caliente al cambiar el toggle o el idioma.
/// - <c>help:Help.AffordanceVisible</c>: si es true, la VISIBILIDAD del elemento sigue al toggle (para ocultar los
///   iconos "Help" que abren los paneles/TeachingTips cuando la ayuda está desactivada).
///
/// Se suscribe por elemento y limpia en <c>Unloaded</c> para no fugar. Ver docs/Plan-Localizacion-Ayuda.md (F0).
/// </summary>
public static class Help
{
    #region Attached properties
    /// <summary>Clave de recurso del tooltip localizado (gated por el toggle de ayuda).</summary>
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(Help), new PropertyMetadata(null, OnKeyChanged));

    public static string GetKey(DependencyObject o) => (string)o.GetValue(KeyProperty);
    public static void SetKey(DependencyObject o, string value) => o.SetValue(KeyProperty, value);

    /// <summary>Si es true, la visibilidad del elemento sigue al toggle de ayuda (para iconos de ayuda/TeachingTips).</summary>
    public static readonly DependencyProperty AffordanceVisibleProperty =
        DependencyProperty.RegisterAttached("AffordanceVisible", typeof(bool), typeof(Help), new PropertyMetadata(false, OnAffordanceChanged));

    public static bool GetAffordanceVisible(DependencyObject o) => (bool)o.GetValue(AffordanceVisibleProperty);
    public static void SetAffordanceVisible(DependencyObject o, bool value) => o.SetValue(AffordanceVisibleProperty, value);
    #endregion

    #region Infra
    // Un binder por elemento (débil, no impide la recolección del elemento).
    private static readonly ConditionalWeakTable<FrameworkElement, Binder> _binders = new();

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe)
            GetBinder(fe).SetTooltipKey(e.NewValue as string);
    }

    private static void OnAffordanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe)
            GetBinder(fe).SetAffordance(e.NewValue is true);
    }

    private static Binder GetBinder(FrameworkElement fe)
    {
        if (!_binders.TryGetValue(fe, out Binder? binder))
        {
            binder = new Binder(fe);
            _binders.Add(fe, binder);
        }
        return binder;
    }

    /// <summary>
    /// Mantiene el tooltip y/o la visibilidad de un elemento sincronizados con el toggle de ayuda y el idioma activo.
    /// </summary>
    private sealed class Binder
    {
        private readonly FrameworkElement _element;
        private string? _tooltipKey;
        private bool _affordance;
        private bool _subscribed;

        public Binder(FrameworkElement element)
        {
            _element = element;
            _element.Loaded += (_, _) => { Subscribe(); Apply(); };
            _element.Unloaded += (_, _) => Unsubscribe();
            if (_element.IsLoaded)
                Subscribe();
            Apply();
        }

        public void SetTooltipKey(string? key) { _tooltipKey = key; Apply(); }
        public void SetAffordance(bool on) { _affordance = on; Apply(); }

        private static SharedDataService Shared => App.GetService<SharedDataService>();

        private void Subscribe()
        {
            if (_subscribed)
                return;
            _subscribed = true;
            Shared.PropertyChanged += OnSharedChanged;
            if (LocalizationService.Instance is LocalizationService loc)
                loc.LanguageChanged += OnLanguageChanged;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;
            _subscribed = false;
            Shared.PropertyChanged -= OnSharedChanged;
            if (LocalizationService.Instance is LocalizationService loc)
                loc.LanguageChanged -= OnLanguageChanged;
        }

        private void OnSharedChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SharedDataService.HelpTooltipsEnabled))
                Apply();
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => Apply();

        private void Apply()
        {
            bool enabled = Shared.HelpTooltipsEnabled;

            if (!string.IsNullOrEmpty(_tooltipKey))
                ToolTipService.SetToolTip(_element, enabled ? LocalizationService.Instance?[_tooltipKey!] : null);

            if (_affordance)
                _element.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    #endregion
}
