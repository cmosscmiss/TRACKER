using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tracker.Services;

namespace Tracker.Controls.Views;

/// <summary>
/// Página de la categoría "About" de la ventana de configuración: identidad de la app, descripción, detalles del
/// build/runtime y componentes de terceros con su licencia. Solo lectura; se enlaza al
/// <see cref="ViewModels.SettingsDialogViewModel"/> (DataContext heredado).
/// </summary>
public sealed partial class AboutSettingsControl : UserControl
{
    private readonly ThemeService _themeService = App.GetService<ThemeService>();

    public AboutSettingsControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _themeService.ThemeChanged += OnThemeChanged;
        UpdateLogoGradient();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _themeService.ThemeChanged -= OnThemeChanged;

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateLogoGradient();

    /// <summary>
    /// Repinta el degradado del recuadro del icono con los colores del tema ACTUAL. Sus <c>GradientStop</c> toman el
    /// color de un recurso de tipo <c>Color</c>, que se resuelve una sola vez al cargar y NO se propaga al cambiar de
    /// tema (solo los <c>Brush</c> se mutan in situ), así que hay que reasignarlo por código.
    /// </summary>
    private void UpdateLogoGradient()
    {
        if (LogoStopLight is null)
            return;

        LogoStopLight.Color = WithOpacity80(_themeService.CardBackgroundLightColor);
        LogoStopDark.Color = WithOpacity80(_themeService.BackgroundColor);
    }

    /// <summary>El mismo alfa (80%) que llevan los recursos <c>...ColorOpacity80</c> que usa el XAML.</summary>
    private static Windows.UI.Color WithOpacity80(Windows.UI.Color color)
        => Windows.UI.Color.FromArgb((byte)(255 * 0.8), color.R, color.G, color.B);
}
