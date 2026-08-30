using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tracker.Services;
using Tracker.ViewModels;

namespace Tracker.Controls.Dialogs;

/// <summary>
/// Contenido de la ventana de configuración de la app, mostrado dentro de un <see cref="AppDialog"/> (que aporta la
/// cabecera con logo y los botones OK/Apply/Cancel sobre el overlay de la aplicación). Resuelve su
/// <see cref="SettingsDialogViewModel"/> de DI (staging) y expone <see cref="Apply"/>, que el diálogo invoca al
/// aceptar (OK) o al pulsar Apply. Ver <see cref="Services.DialogsService.ShowSettingsAsync"/>.
///
/// Implementa <see cref="IAppDialogApplyGate"/> para que el botón "Apply" solo esté activo mientras haya cambios sin
/// aplicar (<see cref="SettingsDialogViewModel.IsDirty"/>): arranca apagado, se enciende al tocar cualquier ajuste y
/// vuelve a apagarse al aplicar.
/// </summary>
public sealed partial class SettingsControl : UserControl, IAppDialogApplyGate
{
    /// <summary>
    /// Brushes con los que la lista de categorías pinta sus ítems (override de los de <c>ListViewItem</c> en el ámbito
    /// de ese ListView), y el nombre base del color del tema que le corresponde a cada uno. Están declarados en el XAML
    /// como <c>Color="{ThemeResource ...Color}"</c>, que se resuelve UNA sola vez al cargar: los recursos de tipo
    /// <c>Color</c> se REEMPLAZAN al cambiar de tema (solo los <c>Brush</c> se mutan in situ), así que sin este refresco
    /// la lista se quedaba con los colores del tema anterior al pulsar Apply con el diálogo abierto.
    /// </summary>
    private static readonly (string Key, string ColorName)[] SectionListBrushes =
    {
        ("ListViewItemBackgroundPointerOver", "AccentLight"),
        ("ListViewItemBackgroundPressed", "AccentLight"),
        ("ListViewItemBackgroundSelected", "Accent"),
        ("ListViewItemBackgroundSelectedPointerOver", "AccentLight"),
        ("ListViewItemBackgroundSelectedPressed", "AccentLight"),
        ("ListViewItemForeground", "Text"),
        ("ListViewItemForegroundPointerOver", "Text"),
        ("ListViewItemForegroundPressed", "Text"),
        ("ListViewItemForegroundSelected", "Text"),
        ("ListViewItemForegroundSelectedPointerOver", "Text"),
        ("ListViewItemForegroundSelectedPressed", "Text"),
    };

    private readonly SettingsDialogViewModel _viewModel = App.GetService<SettingsDialogViewModel>();
    private readonly ThemeService _themeService = App.GetService<ThemeService>();

    /// <summary>ViewModel (staging) de este contenido. Lo usa <see cref="Services.DialogsService"/> para orquestar el editor de colores.</summary>
    public SettingsDialogViewModel ViewModel => _viewModel;

    public SettingsControl()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _themeService.ThemeChanged += OnThemeChanged;
        RefreshSectionListBrushes();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _themeService.ThemeChanged -= OnThemeChanged;

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshSectionListBrushes();

    /// <summary>Repinta los brushes locales de la lista de categorías con los colores del tema ACTUAL (ver <see cref="SectionListBrushes"/>).</summary>
    private void RefreshSectionListBrushes()
    {
        foreach ((string key, string colorName) in SectionListBrushes)
        {
            if (SectionsList.Resources.TryGetValue(key, out object? value) && value is SolidColorBrush brush)
                brush.Color = _themeService.GetThemeColor(colorName);
        }
    }

    /// <summary>Hay cambios pendientes de aplicar (gate del botón "Apply" del diálogo).</summary>
    public bool IsApplyEnabled => _viewModel.IsDirty;

    /// <inheritdoc/>
    public event EventHandler? ApplyEnabledChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsDialogViewModel.IsDirty))
            ApplyEnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Aplica los cambios (staging → AppSettings, en caliente) y persiste. Lo llama el diálogo al aceptar/Apply.</summary>
    public void Apply() => _viewModel.Apply();

    /// <summary>Descarta el staging (deshace la vista previa en caliente). Lo llama el diálogo al cancelar/cerrar.</summary>
    public void Cancel() => _viewModel.Cancel();
}
