using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.Templates;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

public sealed partial class ToolbarControl : UserControl
{
    #region Constants
    private const int PanelFadeOutDuration = 120;
    private const int PanelFadeInDuration = 160;
    private const int SelectionLineFadeDuration = 160;
    private const int SelectionLineMoveDuration = 200;
    private const int ToolbarResizeDuration = 200;
    #endregion

    #region Attributes
    private sealed record ToolbarPanelDefinition(ToolbarButtonIcon Button, FrameworkElement Panel, double ExpandedWidth, double ExpandedHeight);
    private readonly Dictionary<string, ToolbarPanelDefinition> _panelDefinitions = new();
    private ToolbarPanelDefinition? _expandedPanel;

    private bool _initialized;
    private double _collapsedHeight;
    private double _collapsedWidth;
    private bool _selectorTransitionRunning;
    #endregion

    #region Dependency Properties
    public static readonly DependencyProperty TogglesDisabledProperty = DependencyProperty.Register(nameof(TogglesDisabled), typeof(bool), typeof(ToolbarControl), new PropertyMetadata(true, OnTogglesDisabledChanged));

    public bool TogglesDisabled
    {
        get => (bool)GetValue(TogglesDisabledProperty);
        set => SetValue(TogglesDisabledProperty, value);
    }

    public static readonly DependencyProperty PlatformListBehavesAsListProperty = DependencyProperty.Register(nameof(PlatformListBehavesAsList), typeof(bool), typeof(ToolbarControl), new PropertyMetadata(true, OnPlatformListBehavesAsListChanged));

    public bool PlatformListBehavesAsList
    {
        get => (bool)GetValue(PlatformListBehavesAsListProperty);
        set => SetValue(PlatformListBehavesAsListProperty, value);
    }

    public static readonly DependencyProperty PlatformDetailsVisibleProperty = DependencyProperty.Register(nameof(PlatformDetailsVisible), typeof(bool), typeof(ToolbarControl), new PropertyMetadata(true, OnPlatformDetailsVisibleChanged));

    public bool PlatformDetailsVisible
    {
        get => (bool)GetValue(PlatformDetailsVisibleProperty);
        set => SetValue(PlatformDetailsVisibleProperty, value);
    }

    public static readonly DependencyProperty GameListDockedAsideProperty =
        DependencyProperty.Register(
            nameof(GameListDockedAside),
            typeof(bool),
            typeof(ToolbarControl),
            new PropertyMetadata(false, OnGameListDockedAsideChanged));

    public bool GameListDockedAside
    {
        get => (bool)GetValue(GameListDockedAsideProperty);
        set => SetValue(GameListDockedAsideProperty, value);
    }

    public static readonly DependencyProperty SplittersEnabledProperty = DependencyProperty.Register(nameof(SplittersEnabled), typeof(bool), typeof(ToolbarControl), new PropertyMetadata(false, OnSplittersEnabledChanged));

    public bool SplittersEnabled
    {
        get => (bool)GetValue(SplittersEnabledProperty);
        set => SetValue(SplittersEnabledProperty, value);
    }

    public static readonly DependencyProperty ImageTypeBandVisibleProperty = DependencyProperty.Register(nameof(ImageTypeBandVisible), typeof(bool), typeof(ToolbarControl), new PropertyMetadata(true, OnImageTypeBandVisibleChanged));

    /// <summary>Visibilidad de la banda fija del selector de tipo de medio (parte alta del WidgetPanel).</summary>
    public bool ImageTypeBandVisible
    {
        get => (bool)GetValue(ImageTypeBandVisibleProperty);
        set => SetValue(ImageTypeBandVisibleProperty, value);
    }
    #endregion

    #region Constructor
    public ToolbarControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    #endregion

    #region Subscribed events
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        foreach (var btn in ToolbarButtonsPanel.Children.OfType<ToolbarButtonIcon>())
            btn.Clicked += (_, __) => OnButtonSelected(btn);

        foreach (var sw in ToolbarSwitchesPanel.Children.OfType<ToolbarButtonIcon>())
            sw.Clicked += (_, __) => OnToggleClicked(sw);

        // El toggle de splitters vive fuera de ToolbarSwitchesPanel (grupo propio tras el botón de
        // ajustes), por lo que el bucle anterior no lo alcanza; se engancha aquí explícitamente.
        SplittersToggle.Clicked += (_, __) => OnToggleClicked(SplittersToggle);

        // El botón de ajustes abre la configuración de la app (diálogo sobre el overlay; vive en el grupo derecho,
        // fuera de ToolbarButtonsPanel, así que no lo alcanza el bucle de botones de selector).
        SettingsButton.Clicked += (_, __) => OpenSettings();

        // Grabar template: captura pantallazo + elige slot + nombre + guarda. Vive en el grupo derecho.
        TemplateSaveButton.Clicked += (_, __) => SaveTemplate();

        // Al activar un template en el selector (app o usuario), se carga por su ruta. El panel se deja abierto para
        // poder probar varios templates seguidos sin reabrirlo.
        ucTemplateSlots.TemplateActivated += (_, jsonPath) =>
            App.GetService<TemplateService>().LoadTemplate(jsonPath);

        await Task.Yield();

        _collapsedHeight = ToolbarBorder.ActualHeight;
        _collapsedWidth = ToolbarBorder.ActualWidth;
        ToolbarBorder.Height = _collapsedHeight;
        ToolbarBorder.Width = _collapsedWidth;
        ToolbarSelectedItemLine.Opacity = 0;
        ToolbarSelectedItemLine.Width = 0;
        ToolbarSelectedItemLineTranslate.X = 0;

        ApplyGameListToggleState(GameListDockedAside);
        ApplyPlatformDetailsToggleState(PlatformDetailsVisible);
        ApplyPlatformListToggleState(PlatformListBehavesAsList);
        ApplySplittersToggleState(SplittersEnabled);
        ApplyImageTypeBandToggleState(ImageTypeBandVisible);
        ApplySwitchesEnabledState(!TogglesDisabled);

        _panelDefinitions.Clear();
        _panelDefinitions[LayoutSelectorButton.Name] = new ToolbarPanelDefinition(LayoutSelectorButton, ToolbarLayoutSelector, 1000, 414);
        _panelDefinitions[WidgetSelectorButton.Name] = new ToolbarPanelDefinition(WidgetSelectorButton, ToolbarWidgetSelector, 1200, 600);
        _panelDefinitions[TemplateSelectorButton.Name] = new ToolbarPanelDefinition(TemplateSelectorButton, ToolbarTemplateSelector, 1100, 660);
        _panelDefinitions[SettingsSelectorButton.Name] = new ToolbarPanelDefinition(SettingsSelectorButton, ToolbarSettingsSelector, 980, 630);

        _initialized = true;
    }

    private async void OnButtonSelected(ToolbarButtonIcon selectedButton)
    {
        if (!_initialized || _selectorTransitionRunning)
            return;

        _selectorTransitionRunning = true;

        try
        {
            bool wasChecked = selectedButton.IsChecked;

            foreach (var btn in ToolbarButtonsPanel.Children.OfType<ToolbarButtonIcon>())
                btn.IsChecked = false;

            selectedButton.IsChecked = !wasChecked;

            if (selectedButton.IsChecked)
            {
                await MoveSelectionLineToAsync(selectedButton);
            }
            else
            {
                HideSelectionLine();
            }

            await HandleButtonActionAsync(selectedButton);
        }
        finally
        {
            _selectorTransitionRunning = false;
        }
    }

    private void OnToggleClicked(ToolbarButtonIcon sw)
    {
        if (TogglesDisabled)
            return;

        switch (sw.Name)
        {
            case "PlatformDetailsToggle":
                sw.IsChecked = !sw.IsChecked;
                PlatformDetailsVisible = sw.IsChecked;
                break;

            case "PlatformListToggle":
                sw.IsChecked = !sw.IsChecked;
                PlatformListBehavesAsList = sw.IsChecked;
                break;

            case "GameListToggle":
                sw.IsChecked = !sw.IsChecked;
                GameListDockedAside = sw.IsChecked;
                break;

            case "SplittersToggle":
                sw.IsChecked = !sw.IsChecked;
                SplittersEnabled = sw.IsChecked;
                break;

            case "ImageTypeBandToggle":
                sw.IsChecked = !sw.IsChecked;
                ImageTypeBandVisible = sw.IsChecked;
                break;

            default:
                sw.IsChecked = !sw.IsChecked;
                break;
        }
    }

    private static void OnTogglesDisabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToolbarControl control)
            return;

        if (e.NewValue is bool disabled)
        {
            control.ApplySwitchesEnabledState(!disabled);
        }
    }

    private static void OnGameListDockedAsideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToolbarControl control)
            return;

        if (e.NewValue is bool dockedAside)
        {
            control.ApplyGameListToggleState(dockedAside);
        }
    }

    private static void OnPlatformDetailsVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToolbarControl control)
            return;

        if (e.NewValue is bool isVisible)
        {
            control.ApplyPlatformDetailsToggleState(isVisible);
        }
    }

    private static void OnPlatformListBehavesAsListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToolbarControl control)
            return;

        if (e.NewValue is bool behavesAsList)
        {
            control.ApplyPlatformListToggleState(behavesAsList);
        }
    }

    private static void OnSplittersEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToolbarControl control)
            return;

        if (e.NewValue is bool enabled)
        {
            control.ApplySplittersToggleState(enabled);
        }
    }

    private static void OnImageTypeBandVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToolbarControl control)
            return;

        if (e.NewValue is bool isVisible)
        {
            control.ApplyImageTypeBandToggleState(isVisible);
        }
    }

    #endregion

    #region Methods (private) - Switches

    private void ApplyGameListToggleState(bool dockedAside)
    {
        if (GameListToggle is null)
            return;

        GameListToggle.IsChecked = dockedAside;
    }

    private void ApplyPlatformDetailsToggleState(bool isVisible)
    {
        if (PlatformDetailsToggle is null)
            return;

        PlatformDetailsToggle.IsChecked = isVisible;
    }

    private void ApplyPlatformListToggleState(bool behavesAsList)
    {
        if (PlatformListToggle is null)
            return;

        PlatformListToggle.IsChecked = behavesAsList;
    }

    private void ApplySplittersToggleState(bool enabled)
    {
        if (SplittersToggle is null)
            return;

        SplittersToggle.IsChecked = enabled;
    }

    private void ApplyImageTypeBandToggleState(bool isVisible)
    {
        if (ImageTypeBandToggle is null)
            return;

        ImageTypeBandToggle.IsChecked = isVisible;
    }

    private void ApplySwitchesEnabledState(bool enabled)
    {
        if (PlatformDetailsToggle is null || PlatformListToggle is null || GameListToggle is null || SplittersToggle is null || ImageTypeBandToggle is null)
            return;

        PlatformDetailsToggle.IsEnabled = enabled;
        PlatformListToggle.IsEnabled = enabled;
        GameListToggle.IsEnabled = enabled;
        SplittersToggle.IsEnabled = enabled;
        ImageTypeBandToggle.IsEnabled = enabled;

        PlatformDetailsToggle.Opacity = enabled ? 1.0 : 0.45;
        PlatformListToggle.Opacity = enabled ? 1.0 : 0.45;
        GameListToggle.Opacity = enabled ? 1.0 : 0.45;
        SplittersToggle.Opacity = enabled ? 1.0 : 0.45;
        ImageTypeBandToggle.Opacity = enabled ? 1.0 : 0.45;
    }

    #endregion

    #region Methods (private) - Toolbar buttons

    private async Task HandleButtonActionAsync(ToolbarButtonIcon btn)
    {
        var targetPanel = _panelDefinitions.TryGetValue(btn.Name, out var definition)
            ? definition
            : null;

        if (targetPanel is null)
        {
            if (_expandedPanel is not null)
                await CollapseCurrentSelectorAsync();

            return;
        }

        if (btn.IsChecked)
        {
            await ShowSelectorAsync(targetPanel);
        }
        else
        {
            await CollapseCurrentSelectorAsync();
        }
    }

    private async Task ShowSelectorAsync(ToolbarPanelDefinition targetPanel)
    {
        if (_expandedPanel == targetPanel)
            return;

        var previousPanel = _expandedPanel?.Panel;
        var nextPanel = targetPanel.Panel;

        if (previousPanel is not null)
        {
            await AnimateOpacityAsync(previousPanel, previousPanel.Opacity, 0, PanelFadeOutDuration);

            previousPanel.Visibility = Visibility.Collapsed;
        }

        // Al abrir el selector de templates, recarga los slots (para ver los recién grabados).
        if (ReferenceEquals(nextPanel, ToolbarTemplateSelector))
            await ucTemplateSlots.RefreshAsync();

        nextPanel.Visibility = Visibility.Visible;
        nextPanel.Opacity = 0;

        await AnimateToolbarSizeAsync(targetPanel.ExpandedWidth, targetPanel.ExpandedHeight, ToolbarResizeDuration);

        await AnimateOpacityAsync(nextPanel, 0, 1, PanelFadeInDuration);

        _expandedPanel = targetPanel;
    }

    private async Task CollapseCurrentSelectorAsync()
    {
        if (_expandedPanel is null)
            return;

        var currentPanel = _expandedPanel.Panel;

        await AnimateOpacityAsync(currentPanel, currentPanel.Opacity, 0, PanelFadeOutDuration);

        currentPanel.Visibility = Visibility.Collapsed;

        await AnimateToolbarSizeAsync(_collapsedWidth, _collapsedHeight, ToolbarResizeDuration);

        _expandedPanel = null;
    }

    #endregion

    #region Methods (private) - Selection line

    private void HideSelectionLine()
    {
        AnimationService.RunAnimations(new[]
        {
            AnimationService.CreateOpacityAnimation(
                ToolbarSelectedItemLine,
                ToolbarSelectedItemLine.Opacity,
                0.0,
                SelectionLineFadeDuration)
        });
    }

    private Task MoveSelectionLineToAsync(FrameworkElement element)
    {
        var tcs = new TaskCompletionSource<bool>();

        var transform = element.TransformToVisual(ToolbarActions);
        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

        double targetX = point.X;
        double targetWidth = element.ActualWidth;

        ToolbarSelectedItemLine.Width = targetWidth;

        AnimationService.RunAnimations(new[]
        {
            AnimationService.CreateOpacityAnimation(
                ToolbarSelectedItemLine,
                ToolbarSelectedItemLine.Opacity,
                1.0,
                SelectionLineFadeDuration),

            AnimationService.CreateTranslateAnimation(
                ToolbarSelectedItemLineTranslate,
                ToolbarSelectedItemLineTranslate.X,
                targetX,
                0,
                0,
                SelectionLineMoveDuration)
        },
        onAllCompleted: () =>
        {
            tcs.TrySetResult(true);
        });

        return tcs.Task;
    }

    #endregion

    #region Methods (private) - Animations

    private async Task AnimateToolbarSizeAsync(double targetWidth, double targetHeight, int duration)
    {
        double currentWidth = GetCurrentToolbarWidth();
        double currentHeight = GetCurrentToolbarHeight();

        ToolbarBorder.Width = currentWidth;
        ToolbarBorder.Height = currentHeight;

        bool expandingWidth = targetWidth > currentWidth;
        bool expandingHeight = targetHeight > currentHeight;

        bool contractingWidth = targetWidth < currentWidth;
        bool contractingHeight = targetHeight < currentHeight;

        // Expansión: primero crece en X, después en Y.
        if (expandingWidth || expandingHeight)
        {
            if (expandingWidth)
            {
                await AnimateToolbarWidthAsync(currentWidth, targetWidth, duration);
                currentWidth = targetWidth;
            }

            if (expandingHeight)
            {
                await AnimateToolbarHeightAsync(currentHeight, targetHeight, duration);
                currentHeight = targetHeight;
            }
        }

        // Contracción: primero reduce en Y, después en X.
        if (contractingHeight || contractingWidth)
        {
            if (contractingHeight)
            {
                await AnimateToolbarHeightAsync(currentHeight, targetHeight, duration);
                currentHeight = targetHeight;
            }

            if (contractingWidth)
            {
                await AnimateToolbarWidthAsync(currentWidth, targetWidth, duration);
            }
        }
    }

    private Task AnimateToolbarWidthAsync(double from, double to, int duration)
    {
        if (Math.Abs(from - to) < 0.1)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource<bool>();

        AnimationService.RunAnimations(new[]
        {
            AnimationService.CreateWidthAnimation(
                ToolbarBorder,
                from,
                to,
                duration)
        },
        onAllCompleted: () =>
        {
            tcs.TrySetResult(true);
        });

        return tcs.Task;
    }

    private Task AnimateToolbarHeightAsync(double from, double to, int duration)
    {
        if (Math.Abs(from - to) < 0.1)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource<bool>();

        AnimationService.RunAnimations(new[]
        {
            AnimationService.CreateHeightAnimation(
                ToolbarBorder,
                from,
                to,
                duration)
        },
        onAllCompleted: () =>
        {
            tcs.TrySetResult(true);
        });

        return tcs.Task;
    }

    private Task AnimateOpacityAsync(UIElement element, double from, double to, int duration)
    {
        if (Math.Abs(from - to) < 0.01)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource<bool>();

        AnimationService.RunAnimations(new[]
        {
            AnimationService.CreateOpacityAnimation(
                element,
                from,
                to,
                duration)
        },
        onAllCompleted: () =>
        {
            tcs.TrySetResult(true);
        });

        return tcs.Task;
    }

    private double GetCurrentToolbarWidth()
    {
        return double.IsNaN(ToolbarBorder.Width) || ToolbarBorder.Width <= 0
            ? ToolbarBorder.ActualWidth
            : ToolbarBorder.Width;
    }

    private double GetCurrentToolbarHeight()
    {
        return double.IsNaN(ToolbarBorder.Height) || ToolbarBorder.Height <= 0
            ? ToolbarBorder.ActualHeight
            : ToolbarBorder.Height;
    }

    #endregion

    #region Methods (private) - Settings

    /// <summary>Abre la configuración de la app como diálogo (AppDialog) sobre el overlay de la aplicación.</summary>
    private async void OpenSettings()
    {
        if (XamlRoot is null)
            return;

        await App.GetService<DialogsService>().ShowSettingsAsync(XamlRoot);
    }

    /// <summary>
    /// Graba un template: primero captura un pantallazo de la app (antes de abrir ningún diálogo, para que la imagen
    /// refleje el estado actual), luego pide SLOT + nombre y guarda JSON + JPG en ese slot (sobreescribe si está
    /// ocupado). Refresca los slots del selector por si está abierto.
    /// </summary>
    private async void SaveTemplate()
    {
        if (XamlRoot is null)
            return;

        // 1) Elegir slot + nombre. Si se cancela, no se captura nada (se evita un screenshot que no se usaría).
        (int Slot, string Name)? choice = await App.GetService<DialogsService>().ShowSaveTemplateAsync(XamlRoot);
        if (choice is null)
            return;

        // 2) Fuerza el cierre de la toolbar (colapsa el selector expandido) y captura DESPUÉS de cerrar el diálogo, de
        //    modo que ni el diálogo ni el panel expandido salgan en la imagen. Se dejan pasar unos frames para que el
        //    popup y la animación de colapso salgan de la composición antes de capturar.
        await CollapseExpandedSelectorAsync();
        await Task.Delay(80);
        byte[]? screenshot = await App.GetService<WindowService>().CaptureActiveWindowJpegAsync();

        // 3) Guardar en el slot (sobreescribe json + jpg) y refrescar el selector.
        await App.GetService<TemplateService>().SaveToSlotAsync(choice.Value.Slot, choice.Value.Name, screenshot);
        await ucTemplateSlots.RefreshAsync();
    }

    #endregion

    #region Methods (public)

    public async Task CollapseExpandedSelectorAsync()
    {
        if (_expandedPanel is null || _selectorTransitionRunning)
            return;

        _selectorTransitionRunning = true;

        try
        {
            foreach (var btn in ToolbarButtonsPanel.Children.OfType<ToolbarButtonIcon>())
                btn.IsChecked = false;

            HideSelectionLine();

            await CollapseCurrentSelectorAsync();
        }
        finally
        {
            _selectorTransitionRunning = false;
        }
    }

    #endregion
}