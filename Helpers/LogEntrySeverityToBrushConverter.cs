using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Tracker.Models;

namespace Tracker.Helpers;

/// <summary>
/// Devuelve el pincel de texto de una entrada del ACTIVITY LOG según su <see cref="LogEntrySeverity"/>: error
/// (Danger), warning (Warning), finalizada (texto secundario) o en curso (texto normal). El mensaje y la
/// duración se enlazan a esto, de modo que comparten color en error y warning. Reutiliza los brushes por rama
/// (ver <see cref="ThemeBrushConverter"/>) en vez de crear uno nuevo en cada evaluación.
/// </summary>
public class LogEntrySeverityToBrushConverter : ThemeBrushConverter
{
    public override object Convert(object value, Type targetType, object parameter, string language)
    {
        if (ThemeService is null) { return Transparent; }

        LogEntrySeverity severity = value is LogEntrySeverity s ? s : LogEntrySeverity.Running;

        return severity switch
        {
            LogEntrySeverity.Error => GetBrush(LogEntrySeverity.Error, ts => ts.DangerColor),
            LogEntrySeverity.Warning => GetBrush(LogEntrySeverity.Warning, ts => ts.WarningColor),
            LogEntrySeverity.Finished => GetBrush(LogEntrySeverity.Finished, ts => ts.TextSecondaryColor),
            _ => GetBrush(LogEntrySeverity.Running, ts => ts.TextColor)
        };
    }
}
