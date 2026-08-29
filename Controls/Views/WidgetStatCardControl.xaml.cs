using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Tracker.Models;

namespace Tracker.Controls.Views;

/// <summary>
/// Defines the visual layouts supported by <see cref="WidgetStatCardControl"/>.
/// 
/// The values correspond to the selected visual alternatives.
/// </summary>
public enum WidgetStatVisualStyle
{
    /// <summary>
    /// Style 3: dark card with a lateral accent bar and compact icon box.
    /// </summary>
    LateralAccentBar,

    /// <summary>
    /// Style 4: dark card with a stronger neon icon box.
    /// </summary>
    IconBoxSoftNeon,

    /// <summary>
    /// Style 5: card with a colored split panel for the icon area.
    /// </summary>
    SplitPanel
}

/// <summary>
/// Defines the color variants supported by <see cref="WidgetStatCardControl"/>.
/// 
/// The variants are resolved exclusively through the application's theme resources.
/// </summary>
public enum WidgetStatColorVariant
{
    /// <summary>
    /// Uses AccentBrush and its opacity variants.
    /// </summary>
    ThemeAccent,

    /// <summary>
    /// Uses AccentLightBrush and its opacity variants.
    /// </summary>
    AccentLight,

    /// <summary>
    /// Uses AccentDarkBrush and its opacity variants.
    /// </summary>
    AccentDark,

    /// <summary>
    /// Uses SuccessBrush and its opacity variants.
    /// </summary>
    Success,

    /// <summary>
    /// Uses DangerBrush and its opacity variants.
    /// </summary>
    Danger,

    /// <summary>
    /// Uses WarningBrush and its opacity variants.
    /// </summary>
    Warning
}

/// <summary>
/// Reusable visual control for displaying a single image statistic.
/// 
/// The control shows a highlighted value, a short label, an optional description
/// and, optionally, either a glyph icon or an image icon.
/// 
/// It can also host custom content inside the card. When custom content is
/// provided, the default label/value/description block is hidden.
/// 
/// If both <see cref="IconImageSource"/> and <see cref="Glyph"/> are provided,
/// the image icon takes precedence.
/// </summary>
[ContentProperty(Name = nameof(CustomContent))]
public sealed partial class WidgetStatCardControl : UserControl
{
    #region Attributes
    private bool _isInitialized;

    /// <summary>True while the card is narrow enough to force its icon off (see <see cref="IconCollapsePillCount"/>).</summary>
    private bool _iconCollapsedByWidth;

    /// <summary>True while the card is narrow enough to shrink its main value font (step before collapsing the icon).</summary>
    private bool _valueFontShrunk;
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Glyph used by the internal FontIcon.
    /// 
    /// This is used only when <see cref="IconImageSource"/> is null.
    /// </summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(WidgetStatCardControl), new PropertyMetadata(string.Empty, OnIconPropertyChanged));

    /// <summary>
    /// Image source used as the card icon.
    /// 
    /// When this property is set, it takes precedence over <see cref="Glyph"/>.
    /// </summary>
    public ImageSource? IconImageSource
    {
        get => (ImageSource?)GetValue(IconImageSourceProperty);
        set => SetValue(IconImageSourceProperty, value);
    }

    public static readonly DependencyProperty IconImageSourceProperty = DependencyProperty.Register(nameof(IconImageSource), typeof(ImageSource), typeof(WidgetStatCardControl), new PropertyMetadata(null, OnIconPropertyChanged));

    /// <summary>
    /// Main value displayed by the card.
    /// 
    /// The property is defined as object so it can receive strings, numbers or
    /// formatted binding expressions.
    /// </summary>
    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(object), typeof(WidgetStatCardControl), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Short label displayed above the main value.
    /// </summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(WidgetStatCardControl), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Optional supporting description displayed below the main value.
    /// </summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(WidgetStatCardControl), new PropertyMetadata(string.Empty, OnDescriptionChanged));

    /// <summary>
    /// Optional custom content displayed inside the card.
    /// 
    /// When this property is set, the default label, value and description block
    /// is hidden and the custom content is shown instead.
    /// </summary>
    public object? CustomContent
    {
        get => GetValue(CustomContentProperty);
        set => SetValue(CustomContentProperty, value);
    }

    public static readonly DependencyProperty CustomContentProperty = DependencyProperty.Register(nameof(CustomContent), typeof(object), typeof(WidgetStatCardControl), new PropertyMetadata(null, OnCustomContentChanged));

    /// <summary>
    /// Visual layout used by the card.
    /// </summary>
    public WidgetStatVisualStyle VisualStyle
    {
        get => (WidgetStatVisualStyle)GetValue(VisualStyleProperty);
        set => SetValue(VisualStyleProperty, value);
    }

    public static readonly DependencyProperty VisualStyleProperty = DependencyProperty.Register(nameof(VisualStyle), typeof(WidgetStatVisualStyle), typeof(WidgetStatCardControl), new PropertyMetadata(WidgetStatVisualStyle.IconBoxSoftNeon, OnStatePropertyChanged));

    /// <summary>
    /// Theme color variant used by the card.
    /// </summary>
    public WidgetStatColorVariant ColorVariant
    {
        get => (WidgetStatColorVariant)GetValue(ColorVariantProperty);
        set => SetValue(ColorVariantProperty, value);
    }

    public static readonly DependencyProperty ColorVariantProperty = DependencyProperty.Register(nameof(ColorVariant), typeof(WidgetStatColorVariant), typeof(WidgetStatCardControl), new PropertyMetadata(WidgetStatColorVariant.ThemeAccent, OnStatePropertyChanged));

    /// <summary>
    /// Indicates whether the card should display its outer border.
    /// </summary>
    public bool HasBorder
    {
        get => (bool)GetValue(HasBorderProperty);
        set => SetValue(HasBorderProperty, value);
    }

    public static readonly DependencyProperty HasBorderProperty = DependencyProperty.Register(nameof(HasBorder), typeof(bool), typeof(WidgetStatCardControl), new PropertyMetadata(true, OnStatePropertyChanged));

    /// <summary>
    /// Vertical alignment applied to the content area (default stat block or custom content).
    ///
    /// Defaults to <see cref="VerticalAlignment.Center"/> so existing cards keep centering their
    /// content. Set it to <see cref="VerticalAlignment.Stretch"/> when the hosted content (e.g. a
    /// chart) must fill the whole height of the card.
    /// </summary>
    public VerticalAlignment ContentVerticalAlignment
    {
        get => (VerticalAlignment)GetValue(ContentVerticalAlignmentProperty);
        set => SetValue(ContentVerticalAlignmentProperty, value);
    }

    public static readonly DependencyProperty ContentVerticalAlignmentProperty = DependencyProperty.Register(nameof(ContentVerticalAlignment), typeof(VerticalAlignment), typeof(WidgetStatCardControl), new PropertyMetadata(VerticalAlignment.Center));

    /// <summary>
    /// Number of pills grouped next to this card's icon. When greater than 0 the card hides its icon (collapsing
    /// the icon column to reclaim its space) while its own <see cref="FrameworkElement.ActualWidth"/> stays below
    /// the threshold matching that pill count, read from <see cref="AppSettings.GeneralSettings"/>
    /// (PillIconCollapseWidth2/3/4). Used by stat groups that pack several borderless pills next to one icon: as the
    /// hosting widget is narrowed past the threshold the icon is dropped so the pills keep their text legible. 0
    /// (default) keeps the icon at any width, so every other usage is unaffected. The width compared is the
    /// control's own slice, not the window's, so it works inside the independently resizable widget panels.
    /// </summary>
    public int IconCollapsePillCount
    {
        get => (int)GetValue(IconCollapsePillCountProperty);
        set => SetValue(IconCollapsePillCountProperty, value);
    }

    public static readonly DependencyProperty IconCollapsePillCountProperty = DependencyProperty.Register(nameof(IconCollapsePillCount), typeof(int), typeof(WidgetStatCardControl), new PropertyMetadata(0, OnIconCollapseWidthChanged));
    #endregion

    #region Constructors
    /// <summary>
    /// Initializes a new instance of <see cref="WidgetStatCardControl"/>.
    /// </summary>
    public WidgetStatCardControl()
    {
        InitializeComponent();

        _isInitialized = true;

        Loaded += WidgetStatCardControl_Loaded;
        SizeChanged += WidgetStatCardControl_SizeChanged;
    }
    #endregion

    #region Event handlers

    /// <summary>
    /// Applies the initial visual states once the control is loaded.
    /// </summary>
    private void WidgetStatCardControl_Loaded(object sender, RoutedEventArgs e)
    {
        EvaluateIconCollapse();
        EvaluateValueFontShrink();
        UpdateStates(false);
        PropagateValueFontToPills();    // if I'm a group: push my state down to my pills
        SyncValueFontFromHostGroup();   // if I'm a pill: pull the current state from my group
    }

    /// <summary>
    /// Re-evaluates the width-based degradation as the card is resized (e.g. the hosting widget panel is dragged
    /// narrower/wider): first the main value font shrinks (all pills of the group together), then (narrower still)
    /// the icon is dropped. Only touches each piece when its own threshold is actually crossed.
    /// </summary>
    private void WidgetStatCardControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (EvaluateValueFontShrink())
        {
            PropagateValueFontToPills();
        }

        if (EvaluateIconCollapse())
        {
            RefreshLayoutStatesAfterIconChange();
        }
    }
    #endregion

    #region Property changed callbacks
    /// <summary>
    /// Updates the visual states when the selected style or color variant changes.
    /// </summary>
    private static void OnStatePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WidgetStatCardControl control && control._isInitialized)
        {
            control.UpdateStates(true);
        }
    }

    /// <summary>
    /// Updates the icon state when either the glyph or image source changes.
    /// </summary>
    private static void OnIconPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WidgetStatCardControl control && control._isInitialized)
        {
            control.UpdateStates(true);
        }
    }

    /// <summary>
    /// Re-evaluates the width-based icon collapse when the threshold changes.
    /// </summary>
    private static void OnIconCollapseWidthChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WidgetStatCardControl control && control._isInitialized && control.EvaluateIconCollapse())
        {
            control.RefreshLayoutStatesAfterIconChange();
        }
    }

    /// <summary>
    /// Updates the visibility of the optional description when its value changes.
    /// </summary>
    private static void OnDescriptionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WidgetStatCardControl control && control._isInitialized)
        {
            control.UpdateDescriptionVisibility();
        }
    }

    /// <summary>
    /// Updates the content state when custom content is added or removed.
    /// </summary>
    private static void OnCustomContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WidgetStatCardControl control && control._isInitialized)
        {
            control.UpdateContentState(true);
        }
    }
    #endregion

    #region Private methods
    /// <summary>
    /// Applies the color, layout, icon and content visual states.
    /// 
    /// The icon state is applied after the style state because the "no icon" case
    /// must override layout decisions such as icon column width or split panel
    /// visibility.
    /// </summary>
    private void UpdateStates(bool useTransitions)
    {
        UpdateColorVariantState(useTransitions);
        UpdateVisualStyleState(useTransitions);
        UpdateIconState(useTransitions);
        UpdateContentState(useTransitions);
        UpdateBorderState(useTransitions);
        UpdateDescriptionVisibility();
    }

    /// <summary>
    /// Applies the border state depending on whether the card border should be shown.
    /// </summary>
    private void UpdateBorderState(bool useTransitions)
    {
        string stateName = HasBorder
            ? "BorderStateVisible"
            : "BorderStateHidden";

        VisualStateManager.GoToState(this, stateName, useTransitions);
    }

    /// <summary>
    /// Applies the visual state corresponding to the selected color variant.
    /// </summary>
    private void UpdateColorVariantState(bool useTransitions)
    {
        string stateName = ColorVariant switch
        {
            WidgetStatColorVariant.AccentLight => "ColorVariantAccentLight",
            WidgetStatColorVariant.AccentDark => "ColorVariantAccentDark",
            WidgetStatColorVariant.Success => "ColorVariantSuccess",
            WidgetStatColorVariant.Danger => "ColorVariantDanger",
            WidgetStatColorVariant.Warning => "ColorVariantWarning",
            _ => "ColorVariantThemeAccent"
        };

        VisualStateManager.GoToState(this, stateName, useTransitions);
    }

    /// <summary>
    /// Applies the visual state corresponding to the selected layout style.
    /// </summary>
    private void UpdateVisualStyleState(bool useTransitions)
    {
        VisualStateManager.GoToState(this, VisualStyleStateName(), useTransitions);
    }

    /// <summary>Name of the VisualState in the VisualStyleStates group for the current <see cref="VisualStyle"/>.</summary>
    private string VisualStyleStateName() => VisualStyle switch
    {
        WidgetStatVisualStyle.LateralAccentBar => "VisualStyleLateralAccentBar",
        WidgetStatVisualStyle.IconBoxSoftNeon => "VisualStyleIconBoxSoftNeon",
        WidgetStatVisualStyle.SplitPanel => "VisualStyleSplitPanel",
        _ => "VisualStyleIconBoxSoftNeon"
    };

    /// <summary>
    /// Reapplies the icon/layout states after a width-driven icon change. When the icon is recovered, the setters
    /// that <c>IconStateNone</c> shares with the active VisualStyle (icon column width, split-panel layer, text
    /// margin) are NOT restored by simply leaving IconStateNone, because VisualStateGroups don't coordinate: the
    /// shared properties fall back to the XAML base, not the VisualStyle. So after leaving the no-icon state we
    /// force the VisualStyle group to re-apply by bouncing through a scratch state (no transition, same frame, not
    /// visible). When collapsing, IconStateNone must stay on top, so we only switch the icon state.
    /// </summary>
    private void RefreshLayoutStatesAfterIconChange()
    {
        UpdateIconState(false);

        if (!_iconCollapsedByWidth)
        {
            string style = VisualStyleStateName();
            string scratch = style == "VisualStyleSplitPanel" ? "VisualStyleIconBoxSoftNeon" : "VisualStyleSplitPanel";
            VisualStateManager.GoToState(this, scratch, false);
            VisualStateManager.GoToState(this, style, false);
        }
    }

    /// <summary>
    /// Applies the correct icon state.
    /// 
    /// Image icons take precedence over glyph icons. If neither is available,
    /// the icon area is fully collapsed.
    /// </summary>
    private void UpdateIconState(bool useTransitions)
    {
        string stateName;

        if (_iconCollapsedByWidth)
        {
            // Width-based collapse wins over the icon source: the card is too narrow to spare the icon column.
            stateName = "IconStateNone";
        }
        else if (IconImageSource is not null)
        {
            stateName = "IconStateImage";
        }
        else if (!string.IsNullOrWhiteSpace(Glyph))
        {
            stateName = "IconStateGlyph";
        }
        else
        {
            stateName = "IconStateNone";
        }

        VisualStateManager.GoToState(this, stateName, useTransitions);
    }

    /// <summary>
    /// Recomputes <see cref="_iconCollapsedByWidth"/> from the current width and threshold. Returns true when the
    /// value changed, so callers know whether the icon state needs reapplying.
    /// </summary>
    private bool EvaluateIconCollapse()
    {
        double threshold = ResolveIconCollapseWidth();
        bool shouldCollapse = threshold > 0 && ActualWidth > 0 && ActualWidth < threshold;
        if (shouldCollapse == _iconCollapsedByWidth)
        {
            return false;
        }

        _iconCollapsedByWidth = shouldCollapse;
        return true;
    }

    /// <summary>
    /// Resolves the collapse threshold (DIPs) for this group's <see cref="IconCollapsePillCount"/> from the app
    /// settings. Returns 0 (icon never collapses by width) when no pill count is set or the settings are not yet
    /// available (e.g. the XAML designer, where the app host does not exist).
    /// </summary>
    private double ResolveIconCollapseWidth()
    {
        if (IconCollapsePillCount <= 0)
        {
            return 0;
        }

        AppSettings.GeneralSettings? general = TryGetGeneralSettings();
        if (general == null)
        {
            return 0;
        }

        return IconCollapsePillCount switch
        {
            <= 2 => general.PillIconCollapseWidth2,
            3 => general.PillIconCollapseWidth3,
            _ => general.PillIconCollapseWidth4,
        };
    }

    /// <summary>
    /// Recomputes <see cref="_valueFontShrunk"/> from the GROUP width and the shrink threshold for this group's
    /// pill count. Only groups (a card with a pill count) evaluate this; standalone pills get a 0 threshold and
    /// are driven by their hosting group instead, so the whole group shrinks together. Returns true on change.
    /// </summary>
    private bool EvaluateValueFontShrink()
    {
        double threshold = ResolveValueShrinkWidth();
        bool shrunk = threshold > 0 && ActualWidth > 0 && ActualWidth < threshold;
        if (shrunk == _valueFontShrunk)
        {
            return false;
        }

        _valueFontShrunk = shrunk;
        return true;
    }

    /// <summary>
    /// Resolves the value-font shrink threshold (DIPs) for this group's <see cref="IconCollapsePillCount"/> from
    /// the app settings. Returns 0 when no pill count is set (not a group) or the settings are not available.
    /// </summary>
    private double ResolveValueShrinkWidth()
    {
        if (IconCollapsePillCount <= 0)
        {
            return 0;
        }

        AppSettings.GeneralSettings? general = TryGetGeneralSettings();
        if (general == null)
        {
            return 0;
        }

        return IconCollapsePillCount switch
        {
            <= 2 => general.PillValueShrinkWidth2,
            3 => general.PillValueShrinkWidth3,
            _ => general.PillValueShrinkWidth4,
        };
    }

    /// <summary>
    /// Pushes the current compact/normal value-font state to every pill hosted in this group's content, so all the
    /// pills switch together (driven by the group width, not each pill's own slice). Only groups propagate.
    /// </summary>
    private void PropagateValueFontToPills()
    {
        if (IconCollapsePillCount <= 0)
        {
            return;
        }

        ApplyValueFontToPillDescendants(this, _valueFontShrunk);
    }

    /// <summary>
    /// Pulls the current compact/normal value-font state from this pill's hosting group, if any. Needed because a
    /// group may settle its state before its pills are realized (the custom content is built lazily), so a pill
    /// loading later would otherwise miss the group's push and stay at the normal font.
    /// </summary>
    private void SyncValueFontFromHostGroup()
    {
        if (IconCollapsePillCount > 0)
        {
            return;     // I'm a group, not a pill.
        }

        WidgetStatCardControl? group = FindHostGroup();
        if (group != null)
        {
            SetValueFontCompact(group._valueFontShrunk);
        }
    }

    /// <summary>Walks up the visual tree to the nearest hosting group card (one with a pill count), or null.</summary>
    private WidgetStatCardControl? FindHostGroup()
    {
        DependencyObject node = VisualTreeHelper.GetParent(this);
        while (node != null)
        {
            if (node is WidgetStatCardControl card && card.IconCollapsePillCount > 0)
            {
                return card;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    /// <summary>
    /// Walks the visual tree applying the compact/normal value font to every descendant pill (a nested
    /// <see cref="WidgetStatCardControl"/>), without recursing into a pill's own subtree.
    /// </summary>
    private static void ApplyValueFontToPillDescendants(DependencyObject root, bool compact)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is WidgetStatCardControl pill)
            {
                pill.SetValueFontCompact(compact);
            }
            else
            {
                ApplyValueFontToPillDescendants(child, compact);
            }
        }
    }

    /// <summary>
    /// Sets or clears the compact font size on this pill's main value. Called by the hosting group so all pills of
    /// the group change together. Clearing restores the size from <c>TitleStyle</c> (the normal size lives in one
    /// place, Typography.xaml).
    /// </summary>
    internal void SetValueFontCompact(bool compact)
    {
        if (compact)
        {
            double size = TryGetGeneralSettings()?.PillValueFontSizeCompact ?? 0;
            if (size > 0)
            {
                ValueText.FontSize = size;
                return;
            }
        }

        ValueText.ClearValue(TextBlock.FontSizeProperty);
    }

    /// <summary>
    /// Returns the shared general settings, or null when the app host is not available (e.g. the XAML designer).
    /// </summary>
    private static AppSettings.GeneralSettings? TryGetGeneralSettings()
    {
        try
        {
            return App.GetService<IOptions<AppSettings>>()?.Value?.General;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies the content state depending on whether custom content has been
    /// provided.
    /// </summary>
    private void UpdateContentState(bool useTransitions)
    {
        string stateName = CustomContent is null
            ? "ContentStateDefault"
            : "ContentStateCustom";

        VisualStateManager.GoToState(this, stateName, useTransitions);
    }

    /// <summary>
    /// Shows or hides the description text depending on whether a description
    /// has been provided.
    /// </summary>
    private void UpdateDescriptionVisibility()
    {
        DescriptionText.Visibility = string.IsNullOrWhiteSpace(Description)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
    #endregion
}