using MM4LB.Services;

namespace MM4LB.Helpers;

/// <summary>
/// Helpers de formato localizado para enlazar desde XAML con <c>x:Bind</c> (funciones estáticas). Resuelven la cadena
/// de formato desde el <see cref="LocalizationService"/> en el idioma activo. Se reevalúan cuando cambian sus
/// argumentos (contadores); el cambio de idioma en caliente se refleja la próxima vez que cambie el contador.
/// </summary>
public static class LocFmt
{
    /// <summary>"Showing {0} of {1}" localizado (contador de elementos mostrados / total). Compartido por varias vistas.</summary>
    public static string ShowingXofY(int shown, int total)
        => LocalizationService.Instance is LocalizationService loc
            ? loc.Format(LocKeys.Common_ShowingXofY_Format, shown, total)
            : $"Showing {shown} of {total}";
}
