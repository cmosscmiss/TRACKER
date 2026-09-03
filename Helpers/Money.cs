using System;
using System.Globalization;
using Tracker.Services;

namespace Tracker.Helpers;

/// <summary>
/// Divisas de las tiendas y su conversión a EUROS, la moneda de referencia de la aplicación.
///
/// Los marketplaces europeos cotizan en euros, pero amazon.com lo hace en dólares y amazon.co.jp en yenes, así que
/// para poder COMPARAR las tiendas de un producto (mejor precio, alerta, filtro de rango, gráficas) sus importes se
/// pasan a euros con dos tasas FIJAS configurables en Ajustes (unidades por euro: 1 € = X $ / 1 € = Y ¥). Es una
/// aproximación deliberada: no se consulta ningún servicio de cambio, el usuario ajusta las tasas cuando quiere.
///
/// Las monedas que no son € / $ / ¥ (tiendas ajenas a Amazon, cuyo símbolo se parsea de la página) NO son
/// convertibles: se dejan tal cual y se comparan solo entre ellas, como se hacía antes.
/// </summary>
public static class Money
{
    #region Constants
    /// <summary>Símbolo del euro, la moneda de referencia de la aplicación.</summary>
    public const string Euro = "€";

    /// <summary>Símbolo del dólar estadounidense (amazon.com).</summary>
    public const string Dollar = "$";

    /// <summary>Símbolo del yen japonés (amazon.co.jp).</summary>
    public const string Yen = "¥";
    #endregion

    #region Methods (public)
    /// <summary>
    /// Normaliza el texto de una moneda a uno de los símbolos soportados (<see cref="Euro"/>, <see cref="Dollar"/>,
    /// <see cref="Yen"/>), o <c>null</c> si no es ninguno de ellos. Admite los códigos ISO y las variantes de ancho
    /// completo que usan las páginas japonesas (￥) y americanas (US$).
    /// </summary>
    public static string? Normalize(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return null;

        string text = currency.Trim();

        if (text.Contains(Euro, StringComparison.Ordinal) || text.Contains("EUR", StringComparison.OrdinalIgnoreCase))
            return Euro;
        if (text.Contains(Yen, StringComparison.Ordinal) || text.Contains('￥') || text.Contains('円') ||
            text.Contains("JPY", StringComparison.OrdinalIgnoreCase))
            return Yen;
        if (text.Contains(Dollar, StringComparison.Ordinal) || text.Contains("USD", StringComparison.OrdinalIgnoreCase))
            return Dollar;

        return null;
    }

    /// <summary>True si la moneda se puede convertir a euros (es €, $ o ¥).</summary>
    public static bool IsConvertible(string? currency) => Normalize(currency) is not null;

    /// <summary>
    /// Convierte un importe a EUROS según su moneda, o <c>null</c> si la moneda no es convertible (ver
    /// <see cref="IsConvertible"/>). Los importes que ya están en euros se devuelven tal cual.
    /// </summary>
    public static decimal? ToEuro(decimal amount, string? currency)
    {
        string? normalized = Normalize(currency);
        if (normalized is null)
            return null;
        if (normalized == Euro)
            return amount;

        decimal rate = normalized == Dollar ? DollarsPerEuro : YensPerEuro;
        return rate > 0 ? amount / rate : null;
    }

    /// <summary>
    /// Moneda con la que se MUESTRA un importe de esta moneda: el euro si es convertible (los precios comparables se
    /// enseñan siempre convertidos), y la propia moneda si no lo es.
    /// </summary>
    public static string DisplayCurrency(string? currency) => Normalize(currency) is null ? currency ?? string.Empty : Euro;

    /// <summary>
    /// Formatea un importe con su moneda ("39,99 €", "3.980 ¥"). El yen no tiene decimales, así que se redondea a
    /// entero con separador de miles; el resto se formatea con dos decimales, como el resto de la aplicación.
    /// </summary>
    public static string Format(decimal amount, string? currency)
    {
        bool yen = Normalize(currency) == Yen;
        string text = amount.ToString(yen ? "#,##0" : "0.00", CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(currency) ? text : $"{text} {currency}";
    }
    #endregion

    #region Methods (private)
    /// <summary>Dólares por euro (1 € = X $) del ajuste global; si aún no hay servicios, la tasa por defecto.</summary>
    private static decimal DollarsPerEuro => Rates(shared => shared.DollarsPerEuro, Models.AppSettings.GeneralSettings.DefaultDollarsPerEuro);

    /// <summary>Yenes por euro (1 € = Y ¥) del ajuste global; si aún no hay servicios, la tasa por defecto.</summary>
    private static decimal YensPerEuro => Rates(shared => shared.YensPerEuro, Models.AppSettings.GeneralSettings.DefaultYensPerEuro);

    /// <summary>
    /// Lee una tasa del <see cref="SharedDataService"/>. Money se usa también desde los modelos, que pueden
    /// evaluarse antes de que el host de servicios exista (o en herramientas sin él): en ese caso se cae a la tasa
    /// por defecto en vez de reventar.
    /// </summary>
    private static decimal Rates(Func<SharedDataService, double> selector, double fallback)
    {
        try
        {
            return (decimal)selector(App.GetService<SharedDataService>());
        }
        catch
        {
            return (decimal)fallback;
        }
    }
    #endregion
}
