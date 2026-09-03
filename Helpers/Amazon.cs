using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tracker.Helpers;

/// <summary>
/// Utilidades para los marketplaces de Amazon soportados. Un producto de Amazon se identifica por su ASIN (10
/// caracteres), que es común a todos los marketplaces, así que desde un único enlace se puede construir la URL del
/// MISMO producto en cada país (es/de/fr/be/nl/us/jp).
/// </summary>
public static class Amazon
{
    /// <summary>
    /// Moneda por defecto cuando no se conoce el marketplace: euro (la mayoría de las tiendas soportadas son de la
    /// eurozona). La moneda REAL de cada marketplace la da <see cref="CurrencyForHost"/>.
    /// </summary>
    public const string DefaultCurrency = Money.Euro;

    /// <summary>
    /// Marketplaces soportados, en orden: código de país → host, etiqueta legible y moneda. Los eurozona van primero
    /// (son los habituales); <c>us</c> y <c>jp</c> cierran la lista porque cotizan en otra divisa (se comparan
    /// convertidos a euros, ver <see cref="Money"/>).
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Host, string Label, string Currency)> Marketplaces = new[]
    {
        ("es", "www.amazon.es",     "Amazon.es",    Money.Euro),
        ("de", "www.amazon.de",     "Amazon.de",    Money.Euro),
        ("fr", "www.amazon.fr",     "Amazon.fr",    Money.Euro),
        ("be", "www.amazon.com.be", "Amazon.be",    Money.Euro),
        ("nl", "www.amazon.nl",     "Amazon.nl",    Money.Euro),
        ("us", "www.amazon.com",    "Amazon.com",   Money.Dollar),
        ("jp", "www.amazon.co.jp",  "Amazon.co.jp", Money.Yen),
    };

    /// <summary>
    /// Marketplaces ALTERNATIVOS: los de fuera de la eurozona (amazon.com y amazon.co.jp). Sus precios solo se leen en
    /// los productos que lo piden expresamente (<see cref="Tracker.Models.Product.IncludeAlternativeStores"/>): son
    /// tiendas donde muchos artículos europeos no existen, y leerlas siempre alargaría cada pasada de precios sin dar
    /// nada a cambio.
    /// </summary>
    public static readonly IReadOnlyList<string> AlternativeCountries = new[] { "us", "jp" };

    private static readonly Regex AsinRegex = new(@"/(?:dp|gp/product|gp/aw/d)/([A-Z0-9]{10})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True si la URI es de algún dominio de Amazon.</summary>
    public static bool IsAmazon(Uri uri) => uri.Host.Contains("amazon", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extrae el ASIN (10 caracteres) de una URL de producto de Amazon, o <c>null</c> si no lo encuentra.</summary>
    public static string? ExtractAsin(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        Match match = AsinRegex.Match(url);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Host del marketplace para un código de país (es/de/fr/be/nl/us/jp), o <c>null</c> si no está soportado.</summary>
    public static string? HostForCountry(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        foreach ((string Code, string Host, string Label, string Currency) marketplace in Marketplaces)
            if (string.Equals(marketplace.Code, code, StringComparison.OrdinalIgnoreCase))
                return marketplace.Host;

        return null;
    }

    /// <summary>
    /// Código de país (es/de/fr/be/nl/us/jp) del marketplace de una URI de Amazon, o <c>null</c> si no es un
    /// marketplace soportado. Tolera el prefijo <c>www.</c> (compara por el dominio sin él).
    /// </summary>
    public static string? CountryForHost(Uri? uri) => MarketplaceForHost(uri)?.Code;

    /// <summary>
    /// Moneda del marketplace de una URL de Amazon (€ en la eurozona, $ en amazon.com, ¥ en amazon.co.jp), o
    /// <c>null</c> si la URL no es de un marketplace soportado. Es más fiable que parsear el símbolo de la página.
    /// </summary>
    public static string? CurrencyForHost(Uri? uri) => MarketplaceForHost(uri)?.Currency;

    /// <summary>True si el código de país es de un marketplace alternativo (ver <see cref="AlternativeCountries"/>).</summary>
    public static bool IsAlternativeCountry(string? code)
        => code is not null && AlternativeCountries.Any(country => string.Equals(country, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True si la URL es de un marketplace ALTERNATIVO (amazon.com / amazon.co.jp). Las URLs de otras tiendas —los
    /// marketplaces europeos y las páginas ajenas a Amazon— devuelven false: se leen siempre.
    /// </summary>
    public static bool IsAlternativeUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsAlternativeCountry(CountryForHost(uri));

    /// <summary>URL de búsqueda por texto en un marketplace (host de un país), con la consulta escapada.</summary>
    public static string SearchUrl(string host, string query)
        => $"https://{host}/s?k={Uri.EscapeDataString(query ?? string.Empty)}";

    /// <summary>URL del producto (por su ASIN), etiqueta y moneda en cada marketplace soportado.</summary>
    public static IEnumerable<(string Url, string Label, string Currency)> ProductUrlsForAsin(string asin)
    {
        foreach ((string Code, string Host, string Label, string Currency) marketplace in Marketplaces)
            yield return ($"https://{marketplace.Host}/dp/{asin}", marketplace.Label, marketplace.Currency);
    }

    /// <summary>Marketplace soportado al que pertenece una URI, o <c>null</c>. Tolera el prefijo <c>www.</c>.</summary>
    private static (string Code, string Host, string Label, string Currency)? MarketplaceForHost(Uri? uri)
    {
        if (uri is null || !IsAmazon(uri))
            return null;

        string host = uri.Host;
        foreach ((string Code, string Host, string Label, string Currency) marketplace in Marketplaces)
        {
            string domain = marketplace.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? marketplace.Host.Substring(4)
                : marketplace.Host;

            if (string.Equals(host, marketplace.Host, StringComparison.OrdinalIgnoreCase) ||
                host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return marketplace;
        }

        return null;
    }
}
