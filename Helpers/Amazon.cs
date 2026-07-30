using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MM4LB.Helpers;

/// <summary>
/// Utilidades para los marketplaces de Amazon soportados. Un producto de Amazon se identifica por su ASIN (10
/// caracteres), que es común a todos los marketplaces, así que desde un único enlace se puede construir la URL del
/// MISMO producto en cada país (es/de/fr/be/nl).
/// </summary>
public static class Amazon
{
    /// <summary>Moneda de los marketplaces soportados: todos son de la eurozona (es/de/fr/be/nl), así que siempre euros.</summary>
    public const string Currency = "€";

    /// <summary>Marketplaces soportados, en orden: código de país → host y etiqueta legible.</summary>
    public static readonly IReadOnlyList<(string Code, string Host, string Label)> Marketplaces = new[]
    {
        ("es", "www.amazon.es",     "Amazon.es"),
        ("de", "www.amazon.de",     "Amazon.de"),
        ("fr", "www.amazon.fr",     "Amazon.fr"),
        ("be", "www.amazon.com.be", "Amazon.be"),
        ("nl", "www.amazon.nl",     "Amazon.nl"),
    };

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

    /// <summary>Host del marketplace para un código de país (es/de/fr/be/nl), o <c>null</c> si no está soportado.</summary>
    public static string? HostForCountry(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        foreach ((string Code, string Host, string Label) marketplace in Marketplaces)
            if (string.Equals(marketplace.Code, code, StringComparison.OrdinalIgnoreCase))
                return marketplace.Host;

        return null;
    }

    /// <summary>
    /// Código de país (es/de/fr/be/nl) del marketplace de una URI de Amazon, o <c>null</c> si no es un marketplace
    /// soportado. Tolera el prefijo <c>www.</c> (compara por el dominio sin él).
    /// </summary>
    public static string? CountryForHost(Uri? uri)
    {
        if (uri is null || !IsAmazon(uri))
            return null;

        string host = uri.Host;
        foreach ((string Code, string Host, string Label) marketplace in Marketplaces)
        {
            string domain = marketplace.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? marketplace.Host.Substring(4)
                : marketplace.Host;

            if (string.Equals(host, marketplace.Host, StringComparison.OrdinalIgnoreCase) ||
                host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return marketplace.Code;
        }

        return null;
    }

    /// <summary>URL del producto (por su ASIN) y etiqueta en cada marketplace soportado.</summary>
    public static IEnumerable<(string Url, string Label)> ProductUrlsForAsin(string asin)
    {
        foreach ((string Code, string Host, string Label) marketplace in Marketplaces)
            yield return ($"https://{marketplace.Host}/dp/{asin}", marketplace.Label);
    }
}
