using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tracker.Services;

namespace Tracker.Controls.Views;

/// <summary>
/// Control de PRUEBA para cambiar EN CALIENTE los colores del tema: un combobox para elegir qué color editar y un
/// <see cref="ColorPicker"/> estándar (con los inputs de texto ocultos) para fijarlo. Al cambiar el color se llama a
/// <see cref="ThemeService.OverrideColor"/>, que lo aplica en vivo a toda la app. A la derecha lista todos los colores
/// con su muestra base + variantes de opacidad y su código hex actual. Se muestra dentro de un diálogo estándar.
/// </summary>
public sealed partial class ThemeColorEditorControl : UserControl
{
    /// <summary>
    /// Una entrada de color del tema: nombre visible + nombre BASE (p. ej. "Accent"). <see cref="Swatch"/> y las
    /// variantes devuelven el brush VIVO del recurso (que el <see cref="ThemeService"/> muta in situ). <see cref="RgbText"/>
    /// muestra el código hex actual y se refresca (vía <see cref="NotifyColorChanged"/>) cuando el color cambia.
    /// </summary>
    private sealed class ThemeColorOption : INotifyPropertyChanged
    {
        public string Display { get; }
        public string BaseName { get; }

        public ThemeColorOption(string display, string baseName)
        {
            Display = display;
            BaseName = baseName;
        }

        public Brush? Swatch => Resolve(string.Empty);
        public Brush? SwatchOpacity80 => Resolve("Opacity80");
        public Brush? SwatchOpacity60 => Resolve("Opacity60");
        public Brush? SwatchOpacity40 => Resolve("Opacity40");
        public Brush? SwatchOpacity20 => Resolve("Opacity20");

        /// <summary>Código hex del color actual, entre paréntesis (p. ej. "(#546E7A)"), o vacío si no se resuelve.</summary>
        public string RgbText => Resolve(string.Empty) is SolidColorBrush s
            ? $"(#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2})"
            : string.Empty;

        private Brush? Resolve(string suffix)
            => Application.Current.Resources.TryGetValue(BaseName + "Brush" + suffix, out object? b) && b is Brush brush ? brush : null;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Notifica que el color pudo cambiar, para refrescar el código hex mostrado (las muestras se actualizan solas).</summary>
        public void NotifyColorChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RgbText)));
    }

    /// <summary>Todas las opciones de color (para refrescar sus códigos hex al cambiar el tema).</summary>
    private readonly List<ThemeColorOption> _options;

    /// <summary>Evita que fijar el picker al elegir un color del combobox dispare un override (bucle).</summary>
    private bool _suppressPickerChange;

    public ThemeColorEditorControl()
    {
        InitializeComponent();

        // Todos los colores del tema por su nombre BASE; el nombre visible se localiza como "ThemeColors_Name_" + base.
        string[] bases =
        {
            "Accent", "AccentLight", "AccentDark",
            "Background", "BackgroundLight",
            "CardBackground", "CardBackgroundLight",
            "Text", "TextSecondary",
            "Danger", "Success", "Warning",
            "ExtraColor1", "ExtraColor2", "ExtraColor3", "ExtraColor4",
        };
        _options = bases.Select(b => new ThemeColorOption(L("ThemeColors_Name_" + b), b)).ToList();

        ColorCombo.ItemsSource = _options;
        ColorList.ItemsSource = _options;
        ColorCombo.SelectedIndex = 0;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => App.GetService<ThemeService>().ThemeChanged += OnThemeChanged;

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => App.GetService<ThemeService>().ThemeChanged -= OnThemeChanged;

    /// <summary>Cambió algún color del tema: refresca el código hex de todas las filas (las muestras se actualizan solas).</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        foreach (ThemeColorOption option in _options)
            option.NotifyColorChanged();
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;

    /// <summary>Al elegir un color del tema, refleja su valor ACTUAL en el picker (sin disparar un override).</summary>
    private void OnColorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorCombo.SelectedItem is not ThemeColorOption option)
            return;

        _suppressPickerChange = true;
        Picker.Color = App.GetService<ThemeService>().GetThemeColor(option.BaseName);
        _suppressPickerChange = false;
    }

    /// <summary>Al mover el picker, aplica EN CALIENTE el color al elemento del tema seleccionado.</summary>
    private void OnPickerColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_suppressPickerChange || ColorCombo.SelectedItem is not ThemeColorOption option)
            return;

        App.GetService<ThemeService>().OverrideColor(option.BaseName, args.NewColor);
    }

    /// <summary>Revierte el color seleccionado a su original y refleja el resultado en el picker (sin re-disparar override).</summary>
    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        if (ColorCombo.SelectedItem is not ThemeColorOption option)
            return;

        ThemeService theme = App.GetService<ThemeService>();
        theme.RevertColor(option.BaseName);

        _suppressPickerChange = true;
        Picker.Color = theme.GetThemeColor(option.BaseName);
        _suppressPickerChange = false;
    }
}
