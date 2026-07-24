using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using MM4LB.Enums;
using MM4LB.Models;
using SkiaSharp;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Estado observable de la configuración de una gráfica con toolbar (tipo / orden / Top N). Cada VM de estadísticas
/// tiene una instancia por gráfica y la enlaza a su <c>ChartTypeSelectorControl</c>. Los métodos <see cref="ApplyFrom"/>
/// y <see cref="StoreTo"/> mapean con los ajustes persistidos (<see cref="AppSettings.ChartViewSettings"/>), antes
/// triplicados como <c>ApplyChartConfig</c>/<c>StoreChartConfig</c> en StatsGlobal/StatsPlatform/PlatformDetails.
/// </summary>
public class ChartViewState : ObservableObject
{
    private ChartType _chartType = ChartType.Column;
    private SortMode _sortOrder = SortMode.None;
    private int _topN;

    public ChartType ChartType
    {
        get => _chartType;
        set => SetProperty(ref _chartType, value);
    }

    public SortMode SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    public int TopN
    {
        get => _topN;
        set => SetProperty(ref _topN, value);
    }

    /// <summary>Copia los ajustes persistidos a este estado observable (disco → vista). No-op si son null.</summary>
    public void ApplyFrom(AppSettings.ChartViewSettings? settings)
    {
        if (settings == null)
            return;

        ChartType = settings.ChartType;
        SortOrder = settings.SortOrder;
        TopN = settings.TopN;
    }

    /// <summary>Copia este estado observable a los ajustes persistidos (vista → disco).</summary>
    public void StoreTo(AppSettings.ChartViewSettings settings)
    {
        settings.ChartType = ChartType;
        settings.SortOrder = SortOrder;
        settings.TopN = TopN;
    }
}

/// <summary>Series, ejes y secciones ya construidos de una gráfica de cobertura, listos para volcar en las propiedades observables del VM.</summary>
public readonly record struct CoverageChartVisual(
    ISeries[] Series,
    ICartesianAxis[] XAxes,
    ICartesianAxis[] YAxes,
    RectangularSection[] Sections);

/// <summary>
/// Construye la gráfica de área de cobertura (0..100 %) compartida por la cobertura por-juego
/// (<c>StatsPlatformViewModel</c>) y por-plataforma (<c>PlatformDetailsViewModel</c>): un punto por elemento en el
/// eje X (sin etiquetas), cobertura en el eje Y, y el elemento seleccionado resaltado con un punto y una línea
/// vertical punteada hasta el eje X. Solo cambia la entidad y el origen de datos entre ambos usos; la forma del
/// chart es idéntica, así que cada VM aporta sus porcentajes ya calculados, el índice seleccionado, el resolvedor
/// de nombre para el tooltip y los colores del tema.
/// </summary>
public static class CoverageChartBuilder
{
    /// <param name="coveragePercents">Cobertura 0..100 de cada elemento, en orden de eje X.</param>
    /// <param name="selectedIndex">Índice del elemento seleccionado a resaltar, o -1 si no hay.</param>
    /// <param name="nameFor">Devuelve el nombre del elemento en el índice dado (para el tooltip).</param>
    public static CoverageChartVisual Build(
        IReadOnlyList<double> coveragePercents,
        int selectedIndex,
        Func<int, string> nameFor,
        SKColor accent,
        SKColor accentLight,
        SKColor text)
    {
        int count = coveragePercents.Count;

        double?[] values = new double?[count];
        double?[] highlightValues = new double?[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = coveragePercents[i];
        }

        double selectedPct = 0;
        if (selectedIndex >= 0 && selectedIndex < count)
        {
            selectedPct = coveragePercents[selectedIndex];
            highlightValues[selectedIndex] = selectedPct;
        }

        ISeries[] series = new ISeries[]
        {
            new LineSeries<double?>
            {
                Values = values,
                Name = "Coverage",
                Stroke = new SolidColorPaint(accent, 2),
                Fill = new SolidColorPaint(accent.WithAlpha(0x55)),
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0,
                YToolTipLabelFormatter = point =>
                {
                    int i = point.Index;
                    string name = i >= 0 && i < count ? nameFor(i) : string.Empty;
                    return $"{name}: {point.Coordinate.PrimaryValue:0}%";
                },
            },
            new LineSeries<double?>
            {
                Values = highlightValues,
                Name = "Selected",
                Stroke = null,
                Fill = null,
                GeometrySize = 14,
                GeometryFill = new SolidColorPaint(accentLight),
                GeometryStroke = new SolidColorPaint(accent, 2),
                IsHoverable = false,
            },
        };

        // Eje X: un punto por elemento, sin etiquetas ni separadores.
        ICartesianAxis[] xAxes = new ICartesianAxis[]
        {
            new Axis { LabelsPaint = null, TicksPaint = null, SeparatorsPaint = null }
        };

        // Eje Y: cobertura 0..100 %.
        ICartesianAxis[] yAxes = new ICartesianAxis[]
        {
            new Axis { MinLimit = 0, MaxLimit = 100, TextSize = 11, LabelsPaint = new SolidColorPaint(text), Labeler = v => $"{v:0}%" }
        };

        // Línea vertical punteada desde el punto del elemento seleccionado hasta el eje X.
        RectangularSection[] sections = selectedIndex < 0
            ? Array.Empty<RectangularSection>()
            : new[]
            {
                new RectangularSection
                {
                    Xi = selectedIndex,
                    Xj = selectedIndex,
                    Yi = 0,
                    Yj = selectedPct,
                    Stroke = new SolidColorPaint(accentLight, 2) { PathEffect = new DashEffect(new float[] { 4, 4 }) }
                }
            };

        return new CoverageChartVisual(series, xAxes, yAxes, sections);
    }
}
