using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MM4LB.Enums;
using MM4LB.Helpers;
using MM4LB.Services;
using SkiaSharp;

namespace MM4LB.Controls.Views;

/// <summary>
/// Self-contained chart component: a toolbar to pick the <see cref="ChartType"/> for THIS chart plus the chart it
/// drives (a cartesian chart for bars/line/area, a pie chart for pie/doughnut). The series are built in code-behind
/// from the data dependency properties (<see cref="Values"/>, <see cref="Labels"/>, …) — deliberately NOT via
/// x:Bind, which fails silently in the XAML compiler for some LiveCharts properties. Colours come from the active
/// <see cref="ThemeService"/> and refresh when the theme changes.
/// </summary>
public sealed partial class ChartTypeSelectorControl : UserControl
{
    private ThemeService? _themeService;

    /// <summary>Umbrales de los botones "Top X" (en orden); "All" no tiene umbral.</summary>
    private static readonly int[] TopThresholds = { 5, 10, 20, 50, 100 };

    /// <summary>Evita la reentrada al deshacer el "checked" de un <c>ToggleSplitButton</c> dentro de su propio evento.</summary>
    private bool _suppressToggle;

    /// <summary>Hay un <see cref="Rebuild"/> ya encolado para el final del batch de cambios de DP (ver <see cref="ScheduleRebuild"/>).</summary>
    private bool _rebuildScheduled;

    /// <summary>Máximo de los valores mostrados, para acotar el eje (línea de referencia o ajuste ±10%).</summary>
    private double _valueAxisDataMax;

    /// <summary>Mínimo de los valores mostrados, para el ajuste del eje de valores (<see cref="FitValueAxis"/>).</summary>
    private double _valueAxisDataMin;

    /// <summary>Índice ORIGINAL (en <see cref="Values"/>) de cada categoría mostrada, para mapear los clics tras Top N/orden.</summary>
    private int[] _categoryOriginalIndex = System.Array.Empty<int>();

    /// <summary>Se dispara al pulsar una columna/punto: da el índice ORIGINAL (en <see cref="Values"/>) de la categoría.</summary>
    public event System.Action<int>? CategoryClicked;

    public ChartTypeSelectorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Cartesian.DataPointerDown += OnCartesianPointerDown;

        // Suscripción PERMANENTE al cambio de tema (no atada a Loaded/Unloaded): dentro de un FlipView / del panel de
        // widgets estos controles se descargan y virtualizan, y si la suscripción dependiera de Loaded podrían estar
        // desuscritos justo cuando se cambia el tema y no reconstruirse (el caso de las gráficas de GlobalStatistics).
        // ThemeService es singleton y el conjunto de gráficas es fijo, así que no hay fuga relevante.
        _themeService = App.GetService<ThemeService>();
        if (_themeService != null)
            _themeService.ThemeChanged += OnThemeChanged;

        // Suscripción PERMANENTE al cambio de idioma (mismo motivo que el tema: estos controles se virtualizan dentro
        // de FlipView/panel). Refresca las caras de los split-buttons (fijadas por código); los menús usan {loc:Str}.
        if (LocalizationService.Instance is LocalizationService loc)
            loc.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>El idioma cambió: reconstruye las etiquetas de las caras de los split-buttons.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateButtons();
        UpdateTopNButtons();
        UpdateSortButtons();
    }

    /// <summary>Traduce el clic en una columna/punto al índice ORIGINAL de la categoría y lo notifica (single-serie).</summary>
    private void OnCartesianPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
    {
        ChartPoint? point = points?.FirstOrDefault();
        if (point is null)
            return;

        int display = point.Index;
        if (display >= 0 && display < _categoryOriginalIndex.Length)
            CategoryClicked?.Invoke(_categoryOriginalIndex[display]);
    }

    /// <summary>Texto localizado (o la clave si no hay servicio); para las caras fijadas por código.</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;

    #region Dependency properties
    /// <summary>Optional title shown above the chart (styled with TitleStyle). Hidden when null/empty.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(string.Empty, OnTitleChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }


    /// <summary>Chart type currently selected for this chart (defaults to <see cref="ChartType.Column"/>).</summary>
    public static readonly DependencyProperty SelectedChartTypeProperty = DependencyProperty.Register(
        nameof(SelectedChartType), typeof(ChartType), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(ChartType.Column, OnChartChanged));

    public ChartType SelectedChartType
    {
        get => (ChartType)GetValue(SelectedChartTypeProperty);
        set => SetValue(SelectedChartTypeProperty, value);
    }

    /// <summary>
    /// Orden de los elementos de la gráfica (sin orden / ascendente / descendente por valor). Expuesta como
    /// DependencyProperty para poder enlazarla TwoWay y persistirla; la tipa el enum
    /// <see cref="MM4LB.Enums.SortMode"/>, de ahí el nombre <c>SortOrder</c> (evita el choque nombre-tipo).
    /// </summary>
    public static readonly DependencyProperty SortOrderProperty = DependencyProperty.Register(
        nameof(SortOrder), typeof(SortMode), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(SortMode.None, OnSortOrTopNChanged));

    public SortMode SortOrder
    {
        get => (SortMode)GetValue(SortOrderProperty);
        set => SetValue(SortOrderProperty, value);
    }

    /// <summary>
    /// Top N seleccionado: nº máximo de elementos (de mayor valor) a mostrar; 0 = todos. DependencyProperty para
    /// poder enlazarla TwoWay y persistirla.
    /// </summary>
    public static readonly DependencyProperty TopNProperty = DependencyProperty.Register(
        nameof(TopN), typeof(int), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(0, OnSortOrTopNChanged));

    public int TopN
    {
        get => (int)GetValue(TopNProperty);
        set => SetValue(TopNProperty, value);
    }

    /// <summary>One numeric value per point/slice (the bar height, line point, or slice size).</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(null, OnChartChanged));

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>One label per point/slice (category name shown on the axis / pie legend / tooltip).</summary>
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(null, OnChartChanged));

    public IReadOnlyList<string> Labels
    {
        get => (IReadOnlyList<string>)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>Index of the point/slice to highlight (e.g. the selected platform), or -1 for none.</summary>
    public static readonly DependencyProperty HighlightIndexProperty = DependencyProperty.Register(
        nameof(HighlightIndex), typeof(int), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(-1, OnChartChanged));

    public int HighlightIndex
    {
        get => (int)GetValue(HighlightIndexProperty);
        set => SetValue(HighlightIndexProperty, value);
    }

    /// <summary>Standard numeric format string applied to values (tooltip, data labels, value axis). Default "0".</summary>
    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(ChartTypeSelectorControl),
        new PropertyMetadata("0", OnChartChanged));

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    /// <summary>Suffix appended after the formatted value (e.g. "%", " GB"). Default "".</summary>
    public static readonly DependencyProperty ValueSuffixProperty = DependencyProperty.Register(
        nameof(ValueSuffix), typeof(string), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(string.Empty, OnChartChanged));

    public string ValueSuffix
    {
        get => (string)GetValue(ValueSuffixProperty);
        set => SetValue(ValueSuffixProperty, value);
    }

    /// <summary>Upper limit of the value axis (NaN = automatic). Default NaN.</summary>
    public static readonly DependencyProperty ValueMaxProperty = DependencyProperty.Register(
        nameof(ValueMax), typeof(double), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(double.NaN, OnChartChanged));

    public double ValueMax
    {
        get => (double)GetValue(ValueMaxProperty);
        set => SetValue(ValueMaxProperty, value);
    }

    /// <summary>
    /// Si es true, el eje de valores se ajusta al rango de datos con un margen del ±10% (en vez de empezar en 0),
    /// para que se vea bien la variación (p. ej. precios). Ignora la línea de referencia para el límite.
    /// </summary>
    public static readonly DependencyProperty FitValueAxisProperty = DependencyProperty.Register(
        nameof(FitValueAxis), typeof(bool), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(false, OnChartChanged));

    public bool FitValueAxis
    {
        get => (bool)GetValue(FitValueAxisProperty);
        set => SetValue(FitValueAxisProperty, value);
    }

    /// <summary>Rotation (degrees) of the category-axis labels when categories are on the X axis. Default 45.</summary>
    public static readonly DependencyProperty LabelsRotationProperty = DependencyProperty.Register(
        nameof(LabelsRotation), typeof(double), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(45.0, OnChartChanged));

    public double LabelsRotation
    {
        get => (double)GetValue(LabelsRotationProperty);
        set => SetValue(LabelsRotationProperty, value);
    }

    /// <summary>
    /// Valor de referencia: dibuja una línea punteada en color de acento sobre el eje de valores a esa altura
    /// (horizontal en barras verticales/línea/área; vertical en barras horizontales). NaN = sin línea. No aplica
    /// a pie/doughnut.
    /// </summary>
    public static readonly DependencyProperty ReferenceValueProperty = DependencyProperty.Register(
        nameof(ReferenceValue), typeof(double), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(double.NaN, OnChartChanged));

    public double ReferenceValue
    {
        get => (double)GetValue(ReferenceValueProperty);
        set => SetValue(ReferenceValueProperty, value);
    }

    /// <summary>
    /// Optional multi-series data: one inner list of values per series, each aligned to <see cref="Labels"/>
    /// (the shared categories). When set (non-empty) the control renders STACKED series —stacked bars
    /// (<see cref="ChartType.Column"/>/<see cref="ChartType.Row"/>) and stacked area (<see cref="ChartType.Area"/>)—
    /// or overlaid lines, instead of the single <see cref="Values"/> series. <see cref="HighlightIndex"/> does not
    /// apply in this mode; pie/doughnut show one slice per series (each series' total). Top N / sort act on the
    /// per-category total across all series. <see cref="SeriesNames"/> supplies the legend labels.
    /// </summary>
    public static readonly DependencyProperty SeriesValuesProperty = DependencyProperty.Register(
        nameof(SeriesValues), typeof(IReadOnlyList<IReadOnlyList<double>>), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(null, OnChartChanged));

    public IReadOnlyList<IReadOnlyList<double>> SeriesValues
    {
        get => (IReadOnlyList<IReadOnlyList<double>>)GetValue(SeriesValuesProperty);
        set => SetValue(SeriesValuesProperty, value);
    }

    /// <summary>Legend name of each series in <see cref="SeriesValues"/> (same order). Optional.</summary>
    public static readonly DependencyProperty SeriesNamesProperty = DependencyProperty.Register(
        nameof(SeriesNames), typeof(IReadOnlyList<string>), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(null, OnChartChanged));

    public IReadOnlyList<string> SeriesNames
    {
        get => (IReadOnlyList<string>)GetValue(SeriesNamesProperty);
        set => SetValue(SeriesNamesProperty, value);
    }

    /// <summary>
    /// Máximo de caracteres de las etiquetas del eje de categorías (X, o Y en barras horizontales). Si una etiqueta
    /// excede el límite se recorta a ese nº de caracteres y se le añaden tres puntos ("…" estilo ellipsis). No
    /// afecta a los tooltips (conservan el texto completo). Por defecto 10.
    /// </summary>
    public static readonly DependencyProperty MaxLabelLengthProperty = DependencyProperty.Register(
        nameof(MaxLabelLength), typeof(int), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(10, OnChartChanged));

    public int MaxLabelLength
    {
        get => (int)GetValue(MaxLabelLengthProperty);
        set => SetValue(MaxLabelLengthProperty, value);
    }

    /// <summary>
    /// Si es true (por defecto), se muestran las etiquetas del eje de categorías (el eje X en columnas/línea/área,
    /// o el Y en barras horizontales) — p. ej. las fechas de actualización del gráfico de precios. Si es false, el
    /// eje sigue ahí (líneas/escala) pero sin texto de categoría. No afecta a pie/doughnut.
    /// </summary>
    public static readonly DependencyProperty ShowCategoryLabelsProperty = DependencyProperty.Register(
        nameof(ShowCategoryLabels), typeof(bool), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(true, OnChartChanged));

    public bool ShowCategoryLabels
    {
        get => (bool)GetValue(ShowCategoryLabelsProperty);
        set => SetValue(ShowCategoryLabelsProperty, value);
    }

    /// <summary>
    /// Si es true (por defecto), se muestra el separador horizontal entre la cabecera (título) y la toolbar. Se pone
    /// a false en widgets sin título (p. ej. el resumen de precios), donde ese separador queda suelto arriba.
    /// </summary>
    public static readonly DependencyProperty ShowHeaderSeparatorProperty = DependencyProperty.Register(
        nameof(ShowHeaderSeparator), typeof(bool), typeof(ChartTypeSelectorControl),
        new PropertyMetadata(true, OnHeaderSeparatorChanged));

    public bool ShowHeaderSeparator
    {
        get => (bool)GetValue(ShowHeaderSeparatorProperty);
        set => SetValue(ShowHeaderSeparatorProperty, value);
    }
    #endregion

    #region Lifecycle / events
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Al (re)cargarse en el árbol, reconstruye por si el tema cambió mientras estaba descargado/virtualizado.
        UpdateTitle();
        UpdateHeaderSeparator();
        UpdateButtons();
        UpdateSortButtons();
        Rebuild();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>
    /// Difiere un único <see cref="Rebuild"/> al final del batch actual de cambios de DP. Un refresco de
    /// StatsPlatform/PlatformDetails fija varias DPs de datos seguidas (Values, Labels, SeriesValues...), y sin
    /// esto cada una dispararía un Rebuild completo; aquí se coalescen en uno solo vía el DispatcherQueue. Si aún
    /// no hay cola (muy pronto en el ciclo de vida), reconstruye ya para no perder el redibujado.
    /// </summary>
    private void ScheduleRebuild()
    {
        if (_rebuildScheduled) { return; }

        var queue = DispatcherQueue;
        if (queue == null) { Rebuild(); return; }

        _rebuildScheduled = true;
        queue.TryEnqueue(() =>
        {
            _rebuildScheduled = false;
            Rebuild();
        });
    }

    private static void OnChartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ChartTypeSelectorControl)d;
        control.UpdateButtons();
        control.ScheduleRebuild();
    }

    /// <summary>Refresca las caras/marcas de las toolbars de orden y Top N y redibuja al cambiar sus DPs (orden / Top N).</summary>
    private static void OnSortOrTopNChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ChartTypeSelectorControl)d;
        control.UpdateSortButtons();
        control.UpdateTopNButtons();
        control.ScheduleRebuild();
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChartTypeSelectorControl)d).UpdateTitle();

    private static void OnHeaderSeparatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChartTypeSelectorControl)d).UpdateHeaderSeparator();

    /// <summary>Muestra u oculta el separador horizontal de la cabecera según <see cref="ShowHeaderSeparator"/>.</summary>
    private void UpdateHeaderSeparator()
    {
        if (HeaderSeparator != null)
            HeaderSeparator.Visibility = ShowHeaderSeparator ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Pone el título (en el control y en el TeachingTip) y lo oculta si está vacío.</summary>
    private void UpdateTitle()
    {
        if (TitleText == null)
            return;

        string title = Title;
        TitleText.Text = title ?? string.Empty;
        TitleText.Visibility = string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// La parte principal del ToggleSplitButton no alterna un estado: abre el desplegable (igual que el chevron),
    /// de modo que el botón se comporta como un selector cuya cara muestra la opción activa.
    /// </summary>
    private void OnSplitToggle(ToggleSplitButton sender, ToggleSplitButtonIsCheckedChangedEventArgs args)
    {
        if (_suppressToggle)
            return;

        // Deshace el estado "checked" y abre el flyout de opciones.
        _suppressToggle = true;
        sender.IsChecked = false;
        _suppressToggle = false;
        sender.Flyout?.ShowAt(sender);
    }

    /// <summary>Clic en una opción del desplegable de tipo: fija el tipo (su <c>Tag</c>); el resto lo hace <see cref="OnChartChanged"/>.</summary>
    private void OnTypeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement item && item.Tag is string tag && Enum.TryParse(tag, out ChartType type))
            SelectedChartType = type;
    }

    /// <summary>Marca la opción del tipo activo y refleja su etiqueta en la cara del botón.</summary>
    private void UpdateButtons()
    {
        if (ChartTypeButton == null)
            return;

        ChartType selected = SelectedChartType;
        ColumnItem.IsChecked = selected == ChartType.Column;
        RowItem.IsChecked = selected == ChartType.Row;
        LineItem.IsChecked = selected == ChartType.Line;
        AreaItem.IsChecked = selected == ChartType.Area;
        PieItem.IsChecked = selected == ChartType.Pie;
        DoughnutItem.IsChecked = selected == ChartType.Doughnut;
        ChartTypeButton.Content = ChartTypeLabel(selected);
    }

    /// <summary>Etiqueta corta de cada tipo de gráfica (cara del botón).</summary>
    private static string ChartTypeLabel(ChartType type) => type switch
    {
        ChartType.Row => L(LocKeys.ChartType_HBars_Label),
        ChartType.Line => L(LocKeys.ChartType_Line_Label),
        ChartType.Area => L(LocKeys.ChartType_Area_Label),
        ChartType.Pie => L(LocKeys.ChartType_Pie_Label),
        ChartType.Doughnut => L(LocKeys.ChartType_Ring_Label),
        _ => L(LocKeys.ChartType_Bars_Label),
    };

    /// <summary>Clic en una opción Top N: fija el límite (su <c>Tag</c>; "All"=0) y redibuja.</summary>
    private void OnTopNClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement item && item.Tag is string tag)
            TopN = tag == "All" || !int.TryParse(tag, out int n) ? 0 : n;

        UpdateTopNButtons();
        Rebuild();
    }

    /// <summary>
    /// Ajusta el desplegable Top N al nº de elementos: cada opción "Top X" solo se ve si hay más de X elementos;
    /// el botón entero (y su separador) se ocultan si no califica ninguno (≤ 5). Marca la opción activa y la
    /// refleja en la cara del botón. NO toca <see cref="TopN"/> aunque la serie encoja (se conserva la intención).
    /// </summary>
    private void UpdateTopNButtons()
    {
        if (TopNButton == null)
            return;

        int count = EffectiveCount;

        Top5Item.Visibility = count > 5 ? Visibility.Visible : Visibility.Collapsed;
        Top10Item.Visibility = count > 10 ? Visibility.Visible : Visibility.Collapsed;
        Top20Item.Visibility = count > 20 ? Visibility.Visible : Visibility.Collapsed;
        Top50Item.Visibility = count > 50 ? Visibility.Visible : Visibility.Collapsed;
        Top100Item.Visibility = count > 100 ? Visibility.Visible : Visibility.Collapsed;

        // El botón (y su separador) solo tienen sentido si califica al menos el menor umbral.
        Visibility topNVisibility = count > TopThresholds[0] ? Visibility.Visible : Visibility.Collapsed;
        TopNButton.Visibility = topNVisibility;
        if (TopNSeparator != null)
            TopNSeparator.Visibility = topNVisibility;

        // Si el Top N activo no es alcanzable (no hay tantos elementos) NO se revierte la DP: se conserva la
        // intención del usuario (persistida) y ApplyTopN/BuildColumnOrder ya la tratan como "todos" mientras
        // sobren pocos elementos. Evita machacar el valor guardado en el instante de carga (datos aún a 0).
        Top5Item.IsChecked = TopN == 5;
        Top10Item.IsChecked = TopN == 10;
        Top20Item.IsChecked = TopN == 20;
        Top50Item.IsChecked = TopN == 50;
        Top100Item.IsChecked = TopN == 100;
        AllItem.IsChecked = TopN == 0;
        TopNButton.Content = TopN == 0 ? L(LocKeys.ChartType_All_Label) : $"Top {TopN}";
    }

    /// <summary>Clic en una opción de orden: fija el modo (su <c>Tag</c>) y redibuja.</summary>
    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement item && item.Tag is string tag && Enum.TryParse(tag, out SortMode mode))
            SortOrder = mode;

        UpdateSortButtons();
        Rebuild();
    }

    /// <summary>Marca la opción de orden activa y refleja su etiqueta en la cara del botón.</summary>
    private void UpdateSortButtons()
    {
        if (SortButton == null)
            return;

        SortNoneItem.IsChecked = SortOrder == SortMode.None;
        SortAscItem.IsChecked = SortOrder == SortMode.Ascending;
        SortDescItem.IsChecked = SortOrder == SortMode.Descending;
        SortButton.Content = SortOrder switch
        {
            SortMode.Ascending => L(LocKeys.ChartType_SortAsc_Label),
            SortMode.Descending => L(LocKeys.ChartType_SortDesc_Label),
            _ => L(LocKeys.ChartType_SortNone_Label),
        };
    }

    #endregion

    #region Series building
    private bool IsPieType => SelectedChartType == ChartType.Pie || SelectedChartType == ChartType.Doughnut;

    /// <summary>True cuando hay datos multi-serie (modo apilado); tiene prioridad sobre <see cref="Values"/>.</summary>
    private bool IsMultiSeries => SeriesValues != null && SeriesValues.Count > 0;

    /// <summary>Nº de categorías (puntos del eje) del modo activo: el mayor largo de serie en multi-serie, o el de <see cref="Values"/>.</summary>
    private int EffectiveCount
    {
        get
        {
            if (!IsMultiSeries)
                return Values?.Count ?? 0;

            int max = 0;
            foreach (IReadOnlyList<double> s in SeriesValues)
                max = Math.Max(max, s?.Count ?? 0);
            return max;
        }
    }

    private string Format(double v) => v.ToString(ValueFormat ?? "0") + (ValueSuffix ?? string.Empty);

    /// <summary>Etiqueta del eje de valores: entero (sin decimales) con el sufijo de moneda. Los tooltips/data-labels usan <see cref="Format"/>.</summary>
    private string FormatAxis(double v) => v.ToString("0") + (ValueSuffix ?? string.Empty);

    /// <summary>Reconstruye las series del tipo actual a partir de los datos, o vacía y oculta si no hay datos.</summary>
    private void Rebuild()
    {
        if (Cartesian == null || Pie == null)
            return; // plantilla aún no cargada

        UpdateTopNButtons();   // ajusta visibilidad/estado de Top N al nº actual de elementos (puede revertir a All)

        if (IsMultiSeries)
        {
            RebuildMultiSeries();
            return;
        }

        // Modo de una sola serie: la leyenda no aplica (cada punto ya va etiquetado en eje/tooltip).
        Cartesian.LegendPosition = LegendPosition.Hidden;

        double[] values = Values?.ToArray() ?? Array.Empty<double>();
        string[] labels = Labels?.ToArray() ?? Array.Empty<string>();
        // Etiquetas alineadas con los valores (rellena/recorta por si difieren de longitud).
        if (labels.Length != values.Length)
        {
            var fixedLabels = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                fixedLabels[i] = i < labels.Length ? labels[i] : string.Empty;
            labels = fixedLabels;
        }

        int[] indices = Enumerable.Range(0, values.Length).ToArray();
        int highlightIndex = HighlightIndex;
        (values, labels, indices, highlightIndex) = ApplyTopN(values, labels, indices, highlightIndex);
        (values, labels, indices, highlightIndex) = ApplySort(values, labels, indices, highlightIndex);
        _categoryOriginalIndex = indices;

        bool hasData = values.Length > 0;
        bool pie = IsPieType;

        Cartesian.Visibility = hasData && !pie ? Visibility.Visible : Visibility.Collapsed;
        Pie.Visibility = hasData && pie ? Visibility.Visible : Visibility.Collapsed;

        if (!hasData)
        {
            Cartesian.Series = Array.Empty<ISeries>();
            Cartesian.Sections = Array.Empty<RectangularSection>();
            Pie.Series = Array.Empty<ISeries>();
            return;
        }

        if (pie)
        {
            Pie.Series = BuildPieSeries(values, labels, highlightIndex);
            Cartesian.Sections = Array.Empty<RectangularSection>();   // pie no usa ejes/secciones
        }
        else
        {
            _valueAxisDataMax = values.Length > 0 ? values.Max() : 0;   // para acotar el eje (referencia / ajuste ±10%)
            _valueAxisDataMin = values.Length > 0 ? values.Min() : 0;
            Cartesian.Series = BuildCartesianSeries(values, labels, highlightIndex);
            (Cartesian.XAxes, Cartesian.YAxes) = BuildAxes(labels);
            Cartesian.Sections = BuildReferenceSections();
        }
    }

    /// <summary>
    /// Línea de referencia punteada en color de acento sobre el eje de valores a <see cref="ReferenceValue"/>
    /// (horizontal en columnas/línea/área, vertical en barras horizontales). Vacío si NaN.
    /// </summary>
    private RectangularSection[] BuildReferenceSections()
    {
        double value = ReferenceValue;
        if (double.IsNaN(value))
            return Array.Empty<RectangularSection>();

        (SKColor accent, SKColor _, SKColor _, SKColor _) = ResolveColors();
        var stroke = new SolidColorPaint(accent, 2) { PathEffect = new DashEffect(new float[] { 2, 4 }) };

        // ZIndex alto para que la línea quede POR ENCIMA de las barras (las secciones, por defecto, van detrás).
        // El valor va en Y (columnas/línea/área) → línea horizontal; en Row el valor va en X → línea vertical.
        return SelectedChartType == ChartType.Row
            ? new[] { new RectangularSection { Xi = value, Xj = value, Stroke = stroke, ZIndex = 1000 } }
            : new[] { new RectangularSection { Yi = value, Yj = value, Stroke = stroke, ZIndex = 1000 } };
    }

    /// <summary>
    /// SELECCIONA los <see cref="TopN"/> elementos de MENOR valor (precio más bajo; incluyendo SIEMPRE el resaltado
    /// aunque no esté entre los más baratos), conservando el ORDEN ORIGINAL (el orden de visualización lo aplica
    /// <see cref="ApplySort"/> aparte). Si <see cref="TopN"/> es 0 (All) o hay menos elementos que el límite,
    /// devuelve los datos tal cual. Remapea el índice de resaltado.
    /// </summary>
    private (double[] Values, string[] Labels, int[] Indices, int HighlightIndex) ApplyTopN(double[] values, string[] labels, int[] indices, int highlightIndex)
    {
        if (TopN <= 0 || values.Length <= TopN)
            return (values, labels, indices, highlightIndex);

        // Índices de los Top N por MENOR valor (precio más bajo); añade el resaltado si quedó fuera; luego vuelve al orden original.
        var keep = Enumerable.Range(0, values.Length)
            .OrderBy(i => values[i])
            .Take(TopN)
            .ToList();

        if (highlightIndex >= 0 && highlightIndex < values.Length && !keep.Contains(highlightIndex))
            keep.Add(highlightIndex);

        keep.Sort();   // orden original (por índice); el orden de visualización lo decide ApplySort

        var filteredValues = new double[keep.Count];
        var filteredLabels = new string[keep.Count];
        var filteredIndices = new int[keep.Count];
        int newHighlight = -1;
        for (int k = 0; k < keep.Count; k++)
        {
            filteredValues[k] = values[keep[k]];
            filteredLabels[k] = labels[keep[k]];
            filteredIndices[k] = indices[keep[k]];
            if (keep[k] == highlightIndex)
                newHighlight = k;
        }

        return (filteredValues, filteredLabels, filteredIndices, newHighlight);
    }

    /// <summary>
    /// Ordena los elementos por valor según <see cref="SortOrder"/> (ascendente/descendente), o los deja en su
    /// orden actual si es <see cref="SortMode.None"/>. Remapea el índice de resaltado.
    /// </summary>
    private (double[] Values, string[] Labels, int[] Indices, int HighlightIndex) ApplySort(double[] values, string[] labels, int[] indices, int highlightIndex)
    {
        if (SortOrder == SortMode.None || values.Length == 0)
            return (values, labels, indices, highlightIndex);

        var order = Enumerable.Range(0, values.Length).ToList();
        order.Sort((a, b) => SortOrder == SortMode.Ascending
            ? values[a].CompareTo(values[b])
            : values[b].CompareTo(values[a]));

        var sortedValues = new double[values.Length];
        var sortedLabels = new string[values.Length];
        var sortedIndices = new int[values.Length];
        int newHighlight = -1;
        for (int k = 0; k < order.Count; k++)
        {
            sortedValues[k] = values[order[k]];
            sortedLabels[k] = labels[order[k]];
            sortedIndices[k] = indices[order[k]];
            if (order[k] == highlightIndex)
                newHighlight = k;
        }

        return (sortedValues, sortedLabels, sortedIndices, newHighlight);
    }

    private (SKColor accent, SKColor accentDark, SKColor accentLight, SKColor text) ResolveColors()
    {
        if (_themeService == null)
            _themeService = App.GetService<ThemeService>();

        if (_themeService == null)
            return (SKColors.SteelBlue, SKColors.DarkSlateBlue, SKColors.LightSteelBlue, SKColors.Gray);

        return (ToSk(_themeService.AccentColor), ToSk(_themeService.AccentDarkColor), ToSk(_themeService.AccentLightColor), ToSk(_themeService.TextColor));
    }

    private static SKColor ToSk(Windows.UI.Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>Serie base (+ resaltado si <paramref name="hi"/> &gt;= 0) del tipo cartesiano actual.</summary>
    private ISeries[] BuildCartesianSeries(double[] values, string[] labels, int hi)
    {
        (SKColor accent, SKColor accentDark, SKColor accentLight, SKColor _) = ResolveColors();
        string[] snap = labels;
        string Tip(int i, double v) => $"{(i >= 0 && i < snap.Length ? snap[i] : string.Empty)}: {Format(v)}";

        var nullable = new double?[values.Length];
        var highlight = new double?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            nullable[i] = values[i];
            if (i == hi)
                highlight[i] = values[i];
        }

        ISeries baseSeries;
        switch (SelectedChartType)
        {
            case ChartType.Row:
                baseSeries = new RowSeries<double?>
                {
                    Values = nullable, Name = "", Rx = 2, Ry = 2, MaxBarWidth = double.PositiveInfinity,
                    // IgnoresBarPosition también en la base para que ocupe todo el alto de la categoría (sin
                    // repartirlo con la serie de resaltado) y la barra seleccionada quede del mismo tamaño.
                    IgnoresBarPosition = true,
                    Fill = new SolidColorPaint(accentDark),
                    XToolTipLabelFormatter = point => Tip(point.Index, point.Coordinate.PrimaryValue),
                };
                break;
            case ChartType.Line:
                baseSeries = new LineSeries<double?>
                {
                    Values = nullable, Name = "", LineSmoothness = 0, GeometrySize = 6,
                    Stroke = new SolidColorPaint(accent, 2), Fill = null,
                    GeometryFill = new SolidColorPaint(accent), GeometryStroke = new SolidColorPaint(accent, 2),
                    YToolTipLabelFormatter = point => Tip(point.Index, point.Coordinate.PrimaryValue),
                };
                break;
            case ChartType.Area:
                baseSeries = new LineSeries<double?>
                {
                    Values = nullable, Name = "", LineSmoothness = 0, GeometrySize = 6,
                    Stroke = new SolidColorPaint(accent, 2), Fill = new SolidColorPaint(accentDark.WithAlpha(0x55)),
                    GeometryFill = new SolidColorPaint(accent), GeometryStroke = new SolidColorPaint(accent, 2),
                    YToolTipLabelFormatter = point => Tip(point.Index, point.Coordinate.PrimaryValue),
                };
                break;
            case ChartType.Column:
            default:
                baseSeries = new ColumnSeries<double?>
                {
                    Values = nullable, Name = "", Rx = 2, Ry = 2, MaxBarWidth = double.PositiveInfinity,
                    // IgnoresBarPosition también en la base para que ocupe todo el ancho de la categoría (sin
                    // repartirlo con la serie de resaltado) y la barra seleccionada quede del mismo tamaño.
                    IgnoresBarPosition = true,
                    Fill = new SolidColorPaint(accentDark),
                    YToolTipLabelFormatter = point => Tip(point.Index, point.Coordinate.PrimaryValue),
                };
                break;
        }

        if (hi < 0 || hi >= values.Length)
            return new[] { baseSeries };

        return new[] { baseSeries, BuildHighlightSeries(highlight, accent, accentLight) };
    }

    /// <summary>Serie de resaltado (solo el índice seleccionado): barra superpuesta o punto grande según el tipo.</summary>
    private ISeries BuildHighlightSeries(double?[] highlight, SKColor accent, SKColor accentLight)
    {
        switch (SelectedChartType)
        {
            case ChartType.Column:
                return new ColumnSeries<double?>
                {
                    Values = highlight, Name = "", Rx = 2, Ry = 2, MaxBarWidth = double.PositiveInfinity,
                    Fill = new SolidColorPaint(accentLight), Stroke = new SolidColorPaint(accent, 2),
                    IgnoresBarPosition = true, IsHoverable = false,
                    DataLabelsPaint = new SolidColorPaint(accentLight), DataLabelsPosition = DataLabelsPosition.Top,
                    DataLabelsFormatter = point => Format(point.Coordinate.PrimaryValue),
                };
            case ChartType.Row:
                return new RowSeries<double?>
                {
                    Values = highlight, Name = "", Rx = 2, Ry = 2, MaxBarWidth = double.PositiveInfinity,
                    Fill = new SolidColorPaint(accentLight), Stroke = new SolidColorPaint(accent, 2),
                    IgnoresBarPosition = true, IsHoverable = false,
                    DataLabelsPaint = new SolidColorPaint(accentLight), DataLabelsPosition = DataLabelsPosition.End,
                    DataLabelsFormatter = point => Format(point.Coordinate.PrimaryValue),
                };
            default:
                return new LineSeries<double?>
                {
                    Values = highlight, Name = "", Stroke = null, Fill = null, GeometrySize = 14,
                    GeometryFill = new SolidColorPaint(accentLight), GeometryStroke = new SolidColorPaint(accent, 2),
                    IsHoverable = false,
                    DataLabelsPaint = new SolidColorPaint(accentLight), DataLabelsPosition = DataLabelsPosition.Top,
                    DataLabelsFormatter = point => Format(point.Coordinate.PrimaryValue),
                };
        }
    }

    /// <summary>Ejes: las categorías van en X (verticales) salvo en barras horizontales (Row), donde van en Y.</summary>
    /// <summary>
    /// Recorta cada etiqueta a <see cref="MaxLabelLength"/> caracteres, añadiendo "..." si excede el límite
    /// (estilo ellipsis). Devuelve un nuevo array; no muta el original (que se usa en tooltips). Si el límite es
    /// &lt;= 0 las deja intactas.
    /// </summary>
    private string[] TruncateLabels(string[] labels)
    {
        int max = MaxLabelLength;
        if (max <= 0 || labels.Length == 0)
            return labels;

        var result = new string[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            string label = labels[i] ?? string.Empty;
            result[i] = label.Length > max ? label.Substring(0, max) + "..." : label;
        }

        return result;
    }

    private (ICartesianAxis[] XAxes, ICartesianAxis[] YAxes) BuildAxes(string[] labels)
    {
        (SKColor _, SKColor _, SKColor _, SKColor text) = ResolveColors();

        // Las etiquetas del eje se recortan a MaxLabelLength (+ "..."); los tooltips usan el texto completo aparte.
        string[] axisLabels = TruncateLabels(labels);

        // Si el usuario ocultó las etiquetas de categoría, el eje se conserva (escala/líneas) pero sin texto.
        SolidColorPaint? categoryLabelsPaint = ShowCategoryLabels ? new SolidColorPaint(text) : null;
        ICartesianAxis Category(double rotation) => new Axis
        {
            Labels = axisLabels, LabelsRotation = rotation, TextSize = 11, LabelsPaint = categoryLabelsPaint
        };
        ICartesianAxis Value()
        {
            (double? min, double? max) = ResolveValueAxisLimits();
            return new Axis
            {
                MinLimit = min,
                MaxLimit = max,
                // Etiquetas del eje de valores (leyenda de precios) SIEMPRE en enteros, sin decimales; los tooltips y
                // las data-labels siguen usando ValueFormat (con decimales). MinStep 1 evita ticks fraccionarios que
                // producirían etiquetas enteras repetidas.
                MinStep = 1,
                TextSize = 11, LabelsPaint = new SolidColorPaint(text), Labeler = FormatAxis
            };
        }

        if (SelectedChartType == ChartType.Row)
            return (new[] { Value() }, new[] { Category(0) });   // valores en X, categorías en Y (horizontales)

        return (new[] { Category(LabelsRotation) }, new[] { Value() });
    }

    /// <summary>
    /// Límite superior del eje de valores. Si <see cref="ValueMax"/> está fijado, lo usa. Si hay una línea de
    /// referencia (<see cref="ReferenceValue"/>), acota el eje a los DATOS (techo "bonito" justo encima, sin margen
    /// extra), en vez de dejar que la sección de referencia (a menudo muy por encima de los valores) estire el eje y
    /// deje un gran hueco vacío arriba. Sin referencia, devuelve null (auto) como siempre.
    /// </summary>
    /// <summary>
    /// Límites (min, max) del eje de valores. Con <see cref="FitValueAxis"/>, ajusta el eje al rango de datos con un
    /// margen del ±10% (para ver bien la variación, p. ej. precios). Si no, comportamiento clásico: min 0 y max según
    /// <see cref="ResolveValueAxisMax"/>.
    /// </summary>
    private (double? Min, double? Max) ResolveValueAxisLimits()
    {
        if (FitValueAxis && _valueAxisDataMax > 0 && _valueAxisDataMax >= _valueAxisDataMin)
        {
            double min = Math.Max(0, _valueAxisDataMin * 0.9);
            double max = _valueAxisDataMax * 1.1;
            if (max <= min)
                max = min + 1;
            return (min, max);
        }

        return (0, ResolveValueAxisMax());
    }

    private double? ResolveValueAxisMax()
    {
        if (!double.IsNaN(ValueMax))
            return ValueMax;

        if (double.IsNaN(ReferenceValue) || _valueAxisDataMax <= 0)
            return null;   // auto

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(_valueAxisDataMax)));
        double step = magnitude / 2;
        if (step <= 0)
            step = 1;

        return Math.Ceiling(_valueAxisDataMax / step) * step;   // techo "bonito" justo por encima de los datos, sin margen extra
    }

    /// <summary>Series de pie/doughnut: una por porción, color en degradado del acento; porción resaltada (<paramref name="hi"/>) en acento claro.</summary>
    private ISeries[] BuildPieSeries(double[] values, string[] labels, int hi)
    {
        (SKColor accent, SKColor accentDark, SKColor accentLight, SKColor text) = ResolveColors();
        double innerRadius = SelectedChartType == ChartType.Doughnut ? 60 : 0;

        var series = new ISeries[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double value = values[i];
            string label = labels[i];
            bool isHighlight = i == hi;
            double t = values.Length <= 1 ? 0 : (double)i / (values.Length - 1);

            series[i] = new PieSeries<double>
            {
                Values = new[] { value },
                Name = label,
                InnerRadius = innerRadius,
                Fill = new SolidColorPaint(isHighlight ? accentLight : LerpColor(accentDark, accentLight, t)),
                Stroke = isHighlight ? new SolidColorPaint(accent, 2) : null,
                DataLabelsPaint = new SolidColorPaint(text),
                DataLabelsFormatter = point => Format(value),
            };
        }

        return series;
    }

    // ----------------------------------------------------
    // Multi-series (stacked) building
    // ----------------------------------------------------

    /// <summary>
    /// Reconstruye una gráfica MULTI-SERIE: series apiladas (barras/área) u líneas superpuestas en el chart
    /// cartesiano, o un porción por serie (su total) en pie/doughnut. Top N y orden actúan sobre el TOTAL por
    /// categoría (suma de las series). No hay resaltado en este modo.
    /// </summary>
    private void RebuildMultiSeries()
    {
        _categoryOriginalIndex = Array.Empty<int>();   // multi-serie: el clic-para-seleccionar no aplica
        int seriesCount = SeriesValues.Count;
        int catCount = EffectiveCount;

        // Etiquetas alineadas al nº de categorías (rellena/recorta).
        string[] labels = Labels?.ToArray() ?? Array.Empty<string>();
        if (labels.Length != catCount)
        {
            var fixedLabels = new string[catCount];
            for (int i = 0; i < catCount; i++)
                fixedLabels[i] = i < labels.Length ? labels[i] : string.Empty;
            labels = fixedLabels;
        }

        // Matriz [serie][categoría], coercionando cada serie al nº de categorías (huecos = 0).
        var matrix = new double[seriesCount][];
        for (int s = 0; s < seriesCount; s++)
        {
            matrix[s] = new double[catCount];
            IReadOnlyList<double> src = SeriesValues[s];
            for (int c = 0; c < catCount; c++)
                matrix[s][c] = src != null && c < src.Count ? src[c] : 0;
        }

        // Total por categoría (criterio de Top N / orden).
        var totals = new double[catCount];
        for (int c = 0; c < catCount; c++)
            for (int s = 0; s < seriesCount; s++)
                totals[c] += matrix[s][c];

        int[] order = BuildColumnOrder(totals);   // categorías a mostrar, ya ordenadas

        bool hasData = order.Length > 0 && seriesCount > 0;
        bool pie = IsPieType;

        Cartesian.Visibility = hasData && !pie ? Visibility.Visible : Visibility.Collapsed;
        Pie.Visibility = hasData && pie ? Visibility.Visible : Visibility.Collapsed;

        if (!hasData)
        {
            Cartesian.Series = Array.Empty<ISeries>();
            Cartesian.Sections = Array.Empty<RectangularSection>();
            Pie.Series = Array.Empty<ISeries>();
            return;
        }

        if (pie)
        {
            // Una porción por serie = total de la serie sobre las categorías mostradas.
            var seriesTotals = new double[seriesCount];
            for (int s = 0; s < seriesCount; s++)
                foreach (int c in order)
                    seriesTotals[s] += matrix[s][c];

            Pie.Series = BuildMultiPieSeries(seriesTotals);
            Cartesian.Sections = Array.Empty<RectangularSection>();
            return;
        }

        // Reordena cada serie y las etiquetas según el orden de categorías calculado.
        var orderedSeries = new double[seriesCount][];
        for (int s = 0; s < seriesCount; s++)
        {
            orderedSeries[s] = new double[order.Length];
            for (int k = 0; k < order.Length; k++)
                orderedSeries[s][k] = matrix[s][order[k]];
        }
        var orderedLabels = new string[order.Length];
        for (int k = 0; k < order.Length; k++)
            orderedLabels[k] = labels[order[k]];

        (SKColor _, SKColor _, SKColor _, SKColor legendText) = ResolveColors();
        Cartesian.LegendPosition = LegendPosition.Bottom;
        Cartesian.LegendTextPaint = new SolidColorPaint(legendText);
        Cartesian.LegendTextSize = 11;
        // Rango de datos para el ajuste del eje de valores (±10%). Multi-serie no usa línea de referencia.
        double dataMin = double.MaxValue, dataMax = double.MinValue;
        foreach (double[] serie in orderedSeries)
            foreach (double v in serie)
            {
                if (v < dataMin) dataMin = v;
                if (v > dataMax) dataMax = v;
            }
        _valueAxisDataMin = dataMax >= dataMin ? dataMin : 0;
        _valueAxisDataMax = dataMax >= dataMin ? dataMax : 0;

        Cartesian.Series = BuildMultiCartesianSeries(orderedSeries);
        (Cartesian.XAxes, Cartesian.YAxes) = BuildAxes(orderedLabels);
        Cartesian.Sections = BuildReferenceSections();
    }

    /// <summary>
    /// Orden de las categorías a mostrar según el estado de Top N (las de MENOR total: precio más bajo) y de orden
    /// (asc/desc por total), espejo del comportamiento de una sola serie pero sobre el total por categoría.
    /// </summary>
    private int[] BuildColumnOrder(double[] totals)
    {
        int n = totals.Length;
        var keep = new List<int>(n);
        if (TopN > 0 && n > TopN)
        {
            keep.AddRange(Enumerable.Range(0, n).OrderBy(i => totals[i]).Take(TopN));   // los TopN de MENOR total (precio más bajo)
            keep.Sort();   // vuelve al orden original; el de visualización lo decide el orden de abajo
        }
        else
        {
            keep.AddRange(Enumerable.Range(0, n));
        }

        if (SortOrder == SortMode.Ascending)
            keep.Sort((a, b) => totals[a].CompareTo(totals[b]));
        else if (SortOrder == SortMode.Descending)
            keep.Sort((a, b) => totals[b].CompareTo(totals[a]));

        return keep.ToArray();
    }

    /// <summary>Color de la serie <paramref name="index"/> de <paramref name="count"/>: degradado del acento (oscuro→claro).</summary>
    private static SKColor SeriesColor(SKColor accentDark, SKColor accentLight, int index, int count)
    {
        double t = count <= 1 ? 0 : (double)index / (count - 1);
        return LerpColor(accentDark, accentLight, t);
    }

    /// <summary>Series apiladas (barras/área) o líneas superpuestas, una por fila de <paramref name="series"/>; color en degradado de acento.</summary>
    private ISeries[] BuildMultiCartesianSeries(double[][] series)
    {
        (SKColor accent, SKColor accentDark, SKColor accentLight, SKColor _) = ResolveColors();
        int count = series.Length;
        var result = new ISeries[count];

        for (int s = 0; s < count; s++)
        {
            SKColor color = SeriesColor(accentDark, accentLight, s, count);
            string name = SeriesNames != null && s < SeriesNames.Count ? SeriesNames[s] : $"Series {s + 1}";
            double[] values = series[s];
            string seriesName = name;
            string Tip(double v) => $"{seriesName}: {Format(v)}";

            switch (SelectedChartType)
            {
                case ChartType.Row:
                    result[s] = new StackedRowSeries<double>
                    {
                        Values = values, Name = name, Rx = 2, Ry = 2,
                        Fill = new SolidColorPaint(color),
                        XToolTipLabelFormatter = point => Tip(point.Coordinate.PrimaryValue),
                    };
                    break;
                case ChartType.Line:
                    result[s] = new LineSeries<double>
                    {
                        Values = values, Name = name, LineSmoothness = 0, GeometrySize = 6, Fill = null,
                        Stroke = new SolidColorPaint(color, 2),
                        GeometryFill = new SolidColorPaint(color), GeometryStroke = new SolidColorPaint(color, 2),
                        YToolTipLabelFormatter = point => Tip(point.Coordinate.PrimaryValue),
                    };
                    break;
                case ChartType.Area:
                    result[s] = new StackedAreaSeries<double>
                    {
                        Values = values, Name = name, LineSmoothness = 0, GeometrySize = 0,
                        Fill = new SolidColorPaint(color.WithAlpha(0xCC)), Stroke = new SolidColorPaint(color, 2),
                        YToolTipLabelFormatter = point => Tip(point.Coordinate.PrimaryValue),
                    };
                    break;
                case ChartType.Column:
                default:
                    result[s] = new StackedColumnSeries<double>
                    {
                        Values = values, Name = name, Rx = 2, Ry = 2,
                        Fill = new SolidColorPaint(color),
                        YToolTipLabelFormatter = point => Tip(point.Coordinate.PrimaryValue),
                    };
                    break;
            }
        }

        return result;
    }

    /// <summary>Pie/doughnut multi-serie: una porción por serie (su total), color en degradado de acento; nombre = etiqueta de la serie.</summary>
    private ISeries[] BuildMultiPieSeries(double[] seriesTotals)
    {
        (SKColor _, SKColor accentDark, SKColor accentLight, SKColor text) = ResolveColors();
        double innerRadius = SelectedChartType == ChartType.Doughnut ? 60 : 0;
        int count = seriesTotals.Length;

        var series = new ISeries[count];
        for (int s = 0; s < count; s++)
        {
            double value = seriesTotals[s];
            string name = SeriesNames != null && s < SeriesNames.Count ? SeriesNames[s] : $"Series {s + 1}";
            series[s] = new PieSeries<double>
            {
                Values = new[] { value },
                Name = name,
                InnerRadius = innerRadius,
                Fill = new SolidColorPaint(SeriesColor(accentDark, accentLight, s, count)),
                DataLabelsPaint = new SolidColorPaint(text),
                DataLabelsFormatter = point => Format(value),
            };
        }

        return series;
    }

    /// <summary>Interpola linealmente entre dos colores (t en 0..1), opaco; degradado de las porciones del pie.</summary>
    private static SKColor LerpColor(SKColor a, SKColor b, double t)
    {
        byte Lerp(byte x, byte y) => (byte)(x + (y - x) * t);
        return new SKColor(Lerp(a.Red, b.Red), Lerp(a.Green, b.Green), Lerp(a.Blue, b.Blue), 255);
    }
    #endregion
}
