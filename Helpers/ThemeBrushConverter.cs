using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Tracker.Services;
using Windows.UI;

namespace Tracker.Helpers;

/// <summary>
/// Base de los converters que devuelven un <see cref="SolidColorBrush"/> con un color del tema activo. Antes
/// cada <c>Convert</c> creaba un brush nuevo (presión de GC en listas grandes con refresco). Aquí el brush se
/// cachea por rama lógica y se REUTILIZA entre evaluaciones; al cambiar de tema no se recrea, se muta su
/// <see cref="SolidColorBrush.Color"/> in situ, de modo que todos los elementos que comparten esa instancia se
/// actualizan en vivo (soporta cambio de tema en caliente sin depender de que se re-evalúen los bindings).
/// </summary>
public abstract class ThemeBrushConverter : IValueConverter
{
    private ThemeService? _themeService;

    /// <summary>Brush cacheado por clave de rama, junto al selector de color que lo refresca al cambiar de tema.</summary>
    private readonly Dictionary<object, (SolidColorBrush Brush, Func<ThemeService, Color> Selector)> _cache = new();

    /// <summary>
    /// Servicio de tema del que salen los colores. Al asignarlo se (re)suscribe a <see cref="ThemeService.ThemeChanged"/>
    /// para refrescar los brushes cacheados. Se asigna una vez al arranque (los converters son singletons).
    /// </summary>
    public ThemeService? ThemeService
    {
        get => _themeService;
        set
        {
            if (ReferenceEquals(_themeService, value)) { return; }
            if (_themeService != null) { _themeService.ThemeChanged -= OnThemeChanged; }
            _themeService = value;
            if (_themeService != null) { _themeService.ThemeChanged += OnThemeChanged; }
            RefreshBrushes();
        }
    }

    /// <summary>
    /// Devuelve el brush cacheado para <paramref name="key"/> (una rama lógica del converter), reutilizándolo
    /// entre evaluaciones. Guarda <paramref name="colorSelector"/> para poder refrescar ese MISMO brush al
    /// cambiar de tema. Pasa un lambda sin capturas (estático) para no asignar en cada llamada.
    /// </summary>
    protected SolidColorBrush GetBrush(object key, Func<ThemeService, Color> colorSelector)
    {
        if (!_cache.TryGetValue(key, out var entry))
        {
            entry = (new SolidColorBrush(colorSelector(_themeService!)), colorSelector);
            _cache[key] = entry;
        }
        return entry.Brush;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshBrushes();

    /// <summary>Reaplica el color del tema actual a cada brush cacheado (mutación in situ → actualización en vivo).</summary>
    private void RefreshBrushes()
    {
        if (_themeService == null) { return; }
        foreach (var entry in _cache.Values)
        {
            entry.Brush.Color = entry.Selector(_themeService);
        }
    }

    /// <summary>Brush transparente para el caso sin <see cref="ThemeService"/> (defensivo; se asigna al arranque).</summary>
    protected static SolidColorBrush Transparent { get; } = new(Colors.Transparent);

    public abstract object Convert(object value, Type targetType, object parameter, string language);

    public virtual object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
