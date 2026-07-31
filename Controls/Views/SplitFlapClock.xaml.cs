using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Views;

/// <summary>
/// Reloj split-flap que muestra un tiempo (HH:MM:SS) con dígitos que vuelcan (<see cref="SplitFlapDigit"/>). Se usa en
/// el footer como cuenta atrás hasta la siguiente actualización automática de precios; se le asigna el tiempo restante
/// vía <see cref="Value"/> y él reparte cada cifra en su dígito (solo se anima la que cambia).
/// </summary>
public sealed partial class SplitFlapClock : UserControl
{
    public SplitFlapClock()
    {
        InitializeComponent();
    }

    /// <summary>Tiempo a mostrar (se representa como HH:MM:SS, con las horas limitadas a dos dígitos).</summary>
    public TimeSpan Value
    {
        get => (TimeSpan)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(TimeSpan), typeof(SplitFlapClock),
        new PropertyMetadata(TimeSpan.Zero, OnValueChanged));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SplitFlapClock)d).Render((TimeSpan)e.NewValue);

    private void Render(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        int hours = Math.Min(99, (int)value.TotalHours);
        int minutes = value.Minutes;
        int seconds = value.Seconds;

        SetPair(H0, H1, hours);
        SetPair(M0, M1, minutes);
        SetPair(S0, S1, seconds);
    }

    /// <summary>Vuelca un valor 0..99 en dos dígitos (decenas y unidades).</summary>
    private static void SetPair(SplitFlapDigit tens, SplitFlapDigit units, int value)
    {
        string text = value.ToString("00");
        tens.SetDigit(text[0]);
        units.SetDigit(text[1]);
    }
}
