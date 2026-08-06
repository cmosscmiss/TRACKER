using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control de PRUEBA para cambiar EN CALIENTE los colores del tema: un botón que abre un popup con un combobox para
/// elegir qué color del tema editar y un <see cref="ColorPicker"/> estándar (con los inputs de texto ocultos) para
/// fijarlo. Al cambiar el color se llama a <see cref="ThemeService.OverrideColor"/>, que lo aplica en vivo a la app.
/// </summary>
public sealed partial class ThemeColorEditorControl : UserControl
{
    /// <summary>
    /// Una entrada de color del tema: nombre visible + nombre BASE (p. ej. "Accent"). <see cref="Swatch"/> devuelve el
    /// brush VIVO del recurso (que el <see cref="ThemeService"/> muta in situ), para que la muestra se actualice sola.
    /// </summary>
    private sealed record ThemeColorOption(string Display, string BaseName)
    {
        /// <summary>Brush base y variantes de opacidad (mismos que muta el <see cref="ThemeService"/> in situ).</summary>
        public Brush? Swatch => Resolve(string.Empty);
        public Brush? SwatchOpacity80 => Resolve("Opacity80");
        public Brush? SwatchOpacity60 => Resolve("Opacity60");
        public Brush? SwatchOpacity40 => Resolve("Opacity40");
        public Brush? SwatchOpacity20 => Resolve("Opacity20");

        private Brush? Resolve(string suffix)
            => Application.Current.Resources.TryGetValue(BaseName + "Brush" + suffix, out object? b) && b is Brush brush ? brush : null;
    }

    /// <summary>Evita que fijar el picker al elegir un color del combobox dispare un override (bucle).</summary>
    private bool _suppressPickerChange;

    public ThemeColorEditorControl()
    {
        InitializeComponent();

        var options = new List<ThemeColorOption>
        {
            new("Acento", "Accent"),
            new("Acento claro", "AccentLight"),
            new("Acento oscuro", "AccentDark"),
            new("Fondo", "Background"),
            new("Fondo claro", "BackgroundLight"),
            new("Tarjeta", "CardBackground"),
            new("Tarjeta clara", "CardBackgroundLight"),
            new("Texto", "Text"),
            new("Texto secundario", "TextSecondary"),
            new("Peligro", "Danger"),
            new("Éxito", "Success"),
            new("Aviso", "Warning"),
            new("Badge sin imagen", "BadgeNoImage"),
            new("Badge una imagen", "BadgeOneImage"),
            new("Badge varias imágenes", "BadgeMoreThanOneImage"),
        };
        ColorCombo.ItemsSource = options;
        ColorList.ItemsSource = options;
        ColorCombo.SelectedIndex = 0;
    }

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
