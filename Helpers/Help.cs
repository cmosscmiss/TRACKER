using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tracker.Services;

namespace Tracker.Helpers;

/// <summary>
/// Tooltips de UI gobernados por un único toggle global (<see cref="SharedDataService.HelpTooltipsEnabled"/>) y
/// localizados por el <see cref="LocalizationService"/>. La attached property <c>help:Help.Key</c> recibe la CLAVE de
/// recurso del tooltip: pone el tooltip localizado cuando los tooltips están activados y lo quita cuando están
/// desactivados. Se actualiza en caliente al cambiar el toggle o el idioma. Se suscribe por elemento (tabla débil) y
/// limpia en <c>Unloaded</c> para no fugar.
/// </summary>
public static class Help
{
    /// <summary>Clave de recurso del tooltip localizado (gateado por el toggle de tooltips).</summary>
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(Help), new PropertyMetadata(null, OnKeyChanged));

    public static string GetKey(DependencyObject o) => (string)o.GetValue(KeyProperty);
    public static void SetKey(DependencyObject o, string value) => o.SetValue(KeyProperty, value);

    // Un binder por elemento (débil, no impide la recolección del elemento).
    private static readonly ConditionalWeakTable<FrameworkElement, Binder> _binders = new();

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe)
            GetBinder(fe).SetTooltipKey(e.NewValue as string);
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

    /// <summary>Mantiene el tooltip de un elemento sincronizado con el toggle de tooltips y el idioma activo.</summary>
    private sealed class Binder
    {
        private readonly FrameworkElement _element;
        private string? _tooltipKey;
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
            if (string.IsNullOrEmpty(_tooltipKey))
            {
                ToolTipService.SetToolTip(_element, null);
                return;
            }

            ToolTipService.SetToolTip(_element, Shared.HelpTooltipsEnabled ? LocalizationService.Instance?[_tooltipKey!] : null);
        }
    }
}
