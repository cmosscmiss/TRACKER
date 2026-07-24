using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

/// <summary>
/// Grupo de opciones EXCLUYENTES de una toolbar (layout, calidad de vídeo, aspect ratio, vista, …). Según el ajuste
/// global <see cref="AppSettings.GeneralSettings.ToolbarGroupsDisplayMode"/> se muestra como una fila de
/// <c>AppBarToggleButton</c> (Expanded) o como un único <c>SplitButton</c> con desplegable de radio-items
/// (Collapsed). La selección se sincroniza con <see cref="SelectedValue"/> (TwoWay). Las opciones se declaran como
/// hijos (<see cref="ExclusiveOption"/>) en XAML, o dinámicamente vía <see cref="ItemsSource"/>.
///
/// En modo Auto el estado (expandido/colapsado) lo dirige el <see cref="AdaptiveToolbarPanel"/> que contiene el
/// toolbar, colapsando todos sus grupos a la vez cuando el toolbar no cabe en el ancho del widget.
/// </summary>
[ContentProperty(Name = nameof(Options))]
public sealed partial class ExclusiveOptionsControl : UserControl
{
    private readonly List<(AppBarToggleButton Button, ExclusiveOption Option)> _expandedButtons = new();
    private SplitButton? _splitButton;
    private readonly List<(RadioMenuFlyoutItem Item, ExclusiveOption Option)> _menuItems = new();

    /// <summary>En modo Auto, estado colapsado dirigido por el <see cref="AdaptiveToolbarPanel"/>.</summary>
    private bool _autoCollapsed;

    /// <summary>Opciones del grupo (hijos del control en XAML). Se ignoran si se fija <see cref="ItemsSource"/>.</summary>
    public IList<ExclusiveOption> Options { get; } = new List<ExclusiveOption>();

    private bool _built;

    /// <summary>Servicio compartido; se usa para recibir el cambio en caliente del modo de grupos de las toolbars.</summary>
    private SharedDataService? _sharedDataService;

    /// <summary>Fuente efectiva de opciones: <see cref="ItemsSource"/> si está fijada, si no los hijos declarados.</summary>
    private IReadOnlyList<ExclusiveOption> EffectiveOptions =>
        ItemsSource is { } source ? source.Cast<ExclusiveOption>().ToList() : (IReadOnlyList<ExclusiveOption>)Options;

    #region Dependency Properties
    /// <summary>Etiqueta/tooltip del grupo (se usa como ToolTip del SplitButton en modo colapsado).</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string), typeof(ExclusiveOptionsControl), new PropertyMetadata(string.Empty));

    /// <summary>Valor (clave) de la opción seleccionada. TwoWay con el ViewModel.</summary>
    public string? SelectedValue
    {
        get => (string?)GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }
    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(nameof(SelectedValue), typeof(string), typeof(ExclusiveOptionsControl), new PropertyMetadata(null, OnSelectedValueChanged));

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ExclusiveOptionsControl)d).RefreshSelection();

    /// <summary>Colección dinámica de opciones (alternativa a declararlas como hijos). Ej.: aspect ratio, resolución.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ExclusiveOptionsControl), new PropertyMetadata(null, OnItemsSourceChanged));

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ExclusiveOptionsControl)d;
        if (control._built)
        {
            control.Build();
        }
    }
    #endregion

    #region Constructor
    public ExclusiveOptionsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Suscripción al cambio en caliente del modo de grupos (atada a Loaded/Unloaded: hay muchos de estos controles
        // en las toolbars de los widgets). Se re-suscribe sin duplicar por si se remonta.
        _sharedDataService ??= App.GetService<SharedDataService>();
        _sharedDataService.ToolbarGroupsDisplayModeChanged -= OnToolbarGroupsDisplayModeChanged;
        _sharedDataService.ToolbarGroupsDisplayModeChanged += OnToolbarGroupsDisplayModeChanged;

        // Reconstruye al cambiar de idioma para re-resolver las etiquetas por LabelKey (i18n en caliente).
        if (LocalizationService.Instance is LocalizationService loc)
        {
            loc.LanguageChanged -= OnLanguageChanged;
            loc.LanguageChanged += OnLanguageChanged;
        }
        Build();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_sharedDataService != null)
            _sharedDataService.ToolbarGroupsDisplayModeChanged -= OnToolbarGroupsDisplayModeChanged;
        if (LocalizationService.Instance is LocalizationService loc)
            loc.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>El idioma cambió: reconstruye para re-resolver las etiquetas localizadas (LabelKey) y el Header.</summary>
    private void OnLanguageChanged(object? sender, System.EventArgs e) => Build();

    /// <summary>Texto de una opción: si tiene <see cref="ExclusiveOption.LabelKey"/> se resuelve por localización; si no, su <see cref="ExclusiveOption.Label"/>.</summary>
    private static string ResolveLabel(ExclusiveOption option)
        => !string.IsNullOrEmpty(option.LabelKey) && LocalizationService.Instance is LocalizationService loc
            ? loc[option.LabelKey]
            : option.Label;

    /// <summary>El modo de grupos cambió en la ventana de configuración: reconstruye según el nuevo modo (en caliente).</summary>
    private void OnToolbarGroupsDisplayModeChanged(object? sender, System.EventArgs e)
    {
        _autoCollapsed = false;   // el estado Auto lo redirige el panel contenedor tras reconstruir
        Build();
    }

    /// <summary>Modo de visualización global de los grupos de toolbar.</summary>
    private static ToolbarGroupsDisplayMode Mode => App.GetService<IOptions<AppSettings>>().Value.General.ToolbarGroupsDisplayMode;

    /// <summary>True si el ajuste global es Auto (el <see cref="AdaptiveToolbarPanel"/> dirige el colapso).</summary>
    public bool IsAutoMode => Mode == ToolbarGroupsDisplayMode.Auto;

    /// <summary>
    /// En modo Auto, fija el estado colapsado dirigido por el panel contenedor y reconstruye si cambia. En los modos
    /// explícitos (Expanded/Collapsed) es un no-op: el estado lo decide el ajuste global.
    /// </summary>
    public void ApplyAutoCollapsed(bool collapsed)
    {
        if (!IsAutoMode || _autoCollapsed == collapsed)
        {
            return;
        }

        _autoCollapsed = collapsed;
        if (_built)
        {
            Build();
        }
    }

    /// <summary>Construye el árbol visual según el modo global (Expanded/Collapsed; Auto según el panel contenedor).</summary>
    private void Build()
    {
        ToolbarGroupsDisplayMode mode = Mode;
        bool collapsed = mode == ToolbarGroupsDisplayMode.Collapsed || (mode == ToolbarGroupsDisplayMode.Auto && _autoCollapsed);

        Root.Children.Clear();
        _expandedButtons.Clear();
        _menuItems.Clear();
        _splitButton = null;

        if (collapsed)
        {
            BuildCollapsed();
        }
        else
        {
            BuildExpanded();
        }

        _built = true;
        RefreshSelection();
    }

    private void BuildExpanded()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        foreach (ExclusiveOption option in EffectiveOptions)
        {
            var button = new AppBarToggleButton { Label = ResolveLabel(option) };
            if (!string.IsNullOrEmpty(option.Glyph))
            {
                button.Icon = new FontIcon { Glyph = option.Glyph };
            }

            ExclusiveOption captured = option;
            button.Click += (_, _) => Select(captured.Value);

            panel.Children.Add(button);
            _expandedButtons.Add((button, option));
        }

        Root.Children.Add(panel);
    }

    private void BuildCollapsed()
    {
        var flyout = new MenuFlyout();
        foreach (ExclusiveOption option in EffectiveOptions)
        {
            var item = new RadioMenuFlyoutItem { Text = ResolveLabel(option), GroupName = $"grp_{GetHashCode()}" };
            ExclusiveOption captured = option;
            item.Click += (_, _) => Select(captured.Value);
            flyout.Items.Add(item);
            _menuItems.Add((item, option));
        }

        _splitButton = new SplitButton { Flyout = flyout };
        ToolTipService.SetToolTip(_splitButton, string.IsNullOrEmpty(Header) ? null : Header);
        // El botón principal también abre el desplegable (la cara muestra la selección).
        _splitButton.Click += (s, _) => flyout.ShowAt((SplitButton)s);

        Root.Children.Add(_splitButton);
    }

    /// <summary>Fija la selección y refresca los visuales.</summary>
    private void Select(string value)
    {
        SelectedValue = value; // dispara OnSelectedValueChanged → RefreshSelection
    }

    /// <summary>Refleja <see cref="SelectedValue"/> en los botones/menu y en la cara del SplitButton.</summary>
    private void RefreshSelection()
    {
        foreach ((AppBarToggleButton button, ExclusiveOption option) in _expandedButtons)
        {
            button.IsChecked = option.Value == SelectedValue;
        }

        foreach ((RadioMenuFlyoutItem item, ExclusiveOption option) in _menuItems)
        {
            item.IsChecked = option.Value == SelectedValue;
        }

        if (_splitButton != null)
        {
            ExclusiveOption? selected = EffectiveOptions.FirstOrDefault(o => o.Value == SelectedValue) ?? EffectiveOptions.FirstOrDefault();
            _splitButton.Content = BuildFace(selected);
        }
    }

    /// <summary>Cara del SplitButton: glyph (si hay) encima de la etiqueta de la opción seleccionada.</summary>
    private static object BuildFace(ExclusiveOption? option)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (option != null && !string.IsNullOrEmpty(option.Glyph))
        {
            panel.Children.Add(new FontIcon { Glyph = option.Glyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center });
        }
        panel.Children.Add(new TextBlock { Text = option != null ? ResolveLabel(option) : string.Empty, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
        return panel;
    }
}
