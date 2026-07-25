using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MM4LB.Models;

/// <summary>
/// Static class used to convert strings to different formats.
/// </summary>
public static class Utilities
{
    // Regex de caracteres inválidos de nombre de fichero, precompilados (antes se creaban en CADA llamada; se
    // invocan por cada Game al generar search strings).
    private static readonly Regex _invalidFileNameCharsAndApostrophe = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "'"}]");
    private static readonly Regex _invalidFileNameChars = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]");
    private static readonly Regex _invalidFileNameCharsApostropheHyphen = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "'-"}]");

    // Resto de regex con patrón literal, también precompilados una vez.
    private static readonly Regex _ampersandRuns = new(@"\&+");
    private static readonly Regex _whitespaceRuns = new(@"\s+");
    private static readonly Regex _parenthesisToken = new(@"\((.*?)\)");
    private static readonly Regex _bracketToken = new(@"\[(.*?)\]");

    /// <summary>
    /// Converts the parameter to a searchable string (by default, Google Images, unless passed a true as parameter in which case it uses SteamGridDb).
    /// </summary>
    /// <param name="param">String to convert</param>
    /// <param name="steamGridDb">Google images (default) or SteamGrid Db if true</param>
    /// <returns>Converted string</returns>
    public static string ConvertToSearchString(string param, bool steamGridDb = false)
    {
        // TODO: THE URL FOR STEAMGRID AND GOOGLE TO BE TAKEN OUT OF HERE TO A GLOBAL VARIABLE
        return steamGridDb ? _whitespaceRuns.Replace("https://www.steamgriddb.com/search/grids?term=" + _ampersandRuns.Replace(param, "") + "&tbm=isch", "+") : _whitespaceRuns.Replace("https://www.google.com/search?q=" + _ampersandRuns.Replace(param, "") + "&tbm=isch", "+");
    }

    /// <summary>
    /// Get tokens of a string (between parenthesis and brackets)
    /// </summary>
    /// <param name="param">String to tokenise</param>
    /// <returns>List of tokens extracted from the string</returns>
    public static List<string> GetTokens(string param)
    {
        List<string> tokens = new();
        string lower = param.ToLower();
        tokens.AddRange(_parenthesisToken.Matches(lower).Cast<Match>().Select(m => m.Value).ToList());
        tokens.AddRange(_bracketToken.Matches(lower).Cast<Match>().Select(m => m.Value).ToList());
        return tokens;
    }

    /// <summary>
    /// Removes the tokens of the string (anything within () or []).
    /// </summary>
    /// <param name="imageFileName">String to convert</param>
    /// <returns>Converted string</returns>
    public static string RemoveTokens(string imageFileName)
    {
        string aux = imageFileName;
        foreach (string token in GetTokens(aux))
        {
            aux = aux.Replace(token, "");
        }
        return aux.Trim();
    }

    /// <summary>
    /// Replaces all the special Windows characters, not allowed as part of a filename, for an underscore (including ').
    /// </summary>
    /// <param name="param">String to convert</param>
    /// <returns>Convertid string</returns>
    public static string ReplaceAllSpecialCharactersWithUnderscores(string param)
    {
        string auxString = _invalidFileNameCharsAndApostrophe.Replace(param.Replace("\\(.*$", ""), "_");
        return auxString.Replace("[ ]{2,}", " ");
    }

    /// <summary>
    /// Replaces the ' of a string for an underscore and removes the special characters.
    /// </summary>
    /// <param name="param">String to convert</param>
    /// <returns>Converted string</returns>
    public static string ReplaceSpecialCharactersWithUnderscores(string param)
    {
        string auxString = _invalidFileNameChars.Replace(param.Replace("\\(.*$", ""), "");
        // More than 1 consecutive blank gets transformed to 1 blank.
        auxString = auxString.Replace("[ ]{2,}", " ");
        return auxString.Replace("\'", "_");
    }

    /// <summary>
    /// Removes all special characters of a string (plus the ' and -), and converts multiple consecutive blanks to just 1.
    /// </summary>
    /// <param name="param">String to convert</param>
    /// <returns>Converted string</returns>
    public static string RemoveAllSpecialCharacters(string param)
    {
        string auxString = _invalidFileNameCharsApostropheHyphen.Replace(param.Replace("\\(.*$", ""), "");
        return auxString.Replace("[ ]{2,}", " ");
    }
}
