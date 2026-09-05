using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Tracker.Controls.Views;

/// <summary>
/// Histograma de barras verticales que acompaña al slider de rango de precio: una barra por tramo del recorrido, con
/// altura proporcional al número de productos cuyo precio cae en ese tramo. Las barras que quedan FUERA del rango
/// elegido con los pulgares se pintan con <see cref="OutOfRangeBrush"/>, de modo que al mover el slider se ve al
/// momento qué parte de la lista se está descartando.
///
/// El control solo dibuja: los conteos ya vienen agrupados en <see cref="Values"/> (los calcula el ViewModel), y se
/// asume que reparten <see cref="Minimum"/>..<see cref="Maximum"/> en tramos iguales.
/// </summary>
public sealed partial class PriceHistogramControl : UserControl
{
    #region Constructor
    public PriceHistogramControl()
    {
        InitializeComponent();
    }
    #endregion

    #region Dependency properties
    /// <summary>Conteo de productos por tramo, del precio más bajo al más alto. Null o vacío deja el control en blanco.</summary>
    public IReadOnlyList<int>? Values
    {
        get => GetValue(ValuesProperty) as IReadOnlyList<int>;
        set => SetValue(ValuesProperty, value);
    }

    // La DP se registra como object a propósito: un tipo genérico de .NET (IReadOnlyList&lt;int&gt;) se proyecta a
    // WinRT como IVectorView y la comprobación de tipo del SetValue puede rechazar el array que envía el binding.
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(object), typeof(PriceHistogramControl), new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Extremo inferior del recorrido (el mismo que el Minimum del slider).</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(PriceHistogramControl), new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Extremo superior del recorrido (el mismo que el Maximum del slider).</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(PriceHistogramControl), new PropertyMetadata(1d, OnVisualPropertyChanged));

    /// <summary>Pulgar izquierdo del slider: por debajo de este precio las barras quedan fuera del rango.</summary>
    public double RangeStart
    {
        get => (double)GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    public static readonly DependencyProperty RangeStartProperty = DependencyProperty.Register(
        nameof(RangeStart), typeof(double), typeof(PriceHistogramControl), new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Pulgar derecho del slider: por encima de este precio las barras quedan fuera del rango.</summary>
    public double RangeEnd
    {
        get => (double)GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
    }

    public static readonly DependencyProperty RangeEndProperty = DependencyProperty.Register(
        nameof(RangeEnd), typeof(double), typeof(PriceHistogramControl), new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Color de las barras DENTRO del rango elegido.</summary>
    public Brush? InRangeBrush
    {
        get => (Brush?)GetValue(InRangeBrushProperty);
        set => SetValue(InRangeBrushProperty, value);
    }

    public static readonly DependencyProperty InRangeBrushProperty = DependencyProperty.Register(
        nameof(InRangeBrush), typeof(Brush), typeof(PriceHistogramControl), new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Color de las barras que el rango elegido deja fuera.</summary>
    public Brush? OutOfRangeBrush
    {
        get => (Brush?)GetValue(OutOfRangeBrushProperty);
        set => SetValue(OutOfRangeBrushProperty, value);
    }

    public static readonly DependencyProperty OutOfRangeBrushProperty = DependencyProperty.Register(
        nameof(OutOfRangeBrush), typeof(Brush), typeof(PriceHistogramControl), new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Separación (px) entre barras.</summary>
    public double BarSpacing
    {
        get => (double)GetValue(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    public static readonly DependencyProperty BarSpacingProperty = DependencyProperty.Register(
        nameof(BarSpacing), typeof(double), typeof(PriceHistogramControl), new PropertyMetadata(3d, OnVisualPropertyChanged));

    /// <summary>Altura mínima (px) de una barra con al menos un producto, para que un tramo con pocos no desaparezca.</summary>
    public double MinBarHeight
    {
        get => (double)GetValue(MinBarHeightProperty);
        set => SetValue(MinBarHeightProperty, value);
    }

    public static readonly DependencyProperty MinBarHeightProperty = DependencyProperty.Register(
        nameof(MinBarHeight), typeof(double), typeof(PriceHistogramControl), new PropertyMetadata(3d, OnVisualPropertyChanged));
    #endregion

    #region Methods (private)
    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((PriceHistogramControl)sender).Rebuild();

    /// <summary>
    /// Reconstruye las barras. Cada tramo es una columna del Grid y, dentro, dos filas proporcionales: el hueco de
    /// arriba (máximo menos el conteo) y la barra de abajo (el conteo). Así la altura la reparte el propio Grid y no
    /// hace falta recalcular nada cuando cambia el tamaño del control.
    /// </summary>
    private void Rebuild()
    {
        BarsHost.Children.Clear();
        BarsHost.ColumnDefinitions.Clear();
        BarsHost.ColumnSpacing = BarSpacing;

        IReadOnlyList<int>? values = Values;
        if (values is null || values.Count == 0)
            return;

        int highest = 0;
        foreach (int value in values)
            if (value > highest)
                highest = value;

        // Sin ningún producto con precio no hay nada que dibujar (todas las barras serían de altura cero).
        if (highest == 0)
            return;

        // Ancho del tramo en unidades de precio, para saber a qué barra le toca cada lado del rango.
        double span = Maximum - Minimum;
        double bucketWidth = span > 0 ? span / values.Count : 0;

        for (int index = 0; index < values.Count; index++)
        {
            BarsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int value = values[index];
            if (value <= 0)
                continue;

            var cell = new Grid();
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(highest - value, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(value, GridUnitType.Star) });

            // El centro del tramo decide el color: dentro del rango elegido o fuera de él.
            double center = bucketWidth > 0 ? Minimum + ((index + 0.5) * bucketWidth) : Minimum;
            bool inRange = center >= RangeStart && center <= RangeEnd;

            var bar = new Border
            {
                Background = inRange ? InRangeBrush : OutOfRangeBrush,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                MinHeight = MinBarHeight,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            Grid.SetRow(bar, 1);
            cell.Children.Add(bar);
            Grid.SetColumn(cell, index);
            BarsHost.Children.Add(cell);
        }
    }
    #endregion
}
