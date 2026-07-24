using MM4LB.Enums;
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
    // invocan por fichero en el procesado por lote y por cada Game al generar search strings).
    private static readonly Regex _invalidFileNameCharsAndApostrophe = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "'"}]");
    private static readonly Regex _invalidFileNameChars = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]");
    private static readonly Regex _invalidFileNameCharsApostropheHyphen = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "'-"}]");

    // Resto de regex con patrón literal, también precompilados una vez (antes se interpretaban en cada llamada;
    // los de tokens y normalizado de nombre corren por fichero en el matching por lote).
    private static readonly Regex _ampersandRuns = new(@"\&+");
    private static readonly Regex _whitespaceRuns = new(@"\s+");
    private static readonly Regex _imgTagSource = new("(<img.*src=\")(?'URL'[^\"]*)(.*[^<$])", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex _parenthesisToken = new(@"\((.*?)\)");
    private static readonly Regex _bracketToken = new(@"\[(.*?)\]");
    private static readonly Regex _fileExtension = new(@"\.[^.]*$");
    private static readonly Regex _trailingHyphenTwoDigits = new(@".{0}(\-\d\d\b)$");
    private static readonly Regex _dotUuid = new(@"\.[\da-f-]{36}");

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
    /// Converts the parameter to a YouTube search URL (used when the selected media type is a video).
    /// </summary>
    /// <param name="param">String to convert</param>
    /// <returns>YouTube search URL</returns>
    public static string ConvertToYoutubeSearchString(string param)
    {
        return _whitespaceRuns.Replace("https://www.youtube.com/results?search_query=" + _ampersandRuns.Replace(param, ""), "+");
    }

    /// <summary>
    /// Returns the urls of the src attributes of all image tags existing in the html passed as parameter.
    /// </summary>
    /// <param name="html"></param>
    /// <returns></returns>
    public static List<string> GetImageTagSource(string html)
    {
        List<string> result = new();
        MatchCollection matches = _imgTagSource.Matches(html);
        foreach (Match match in matches)
        {
            result.Add(match.Groups["URL"].Value);
        }
        return result;
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
    /// Converts from a file name (including path) to a game search string (for comparisons).
    /// </summary>
    /// <param name="imageFileName">String to convert</param>
    /// <returns>Converted string</returns>
    public static string ImageFileNameToGameString(string imageFileName)
    {
        // Removing everything after the last dot
        string aux = _fileExtension.Replace(Path.GetFileName(imageFileName), "").ToLower();
        // Removing the last 3 characters if they are a hyphen followed by 2 digits
        aux = _trailingHyphenTwoDigits.Replace(aux, "").Trim();
        // Removing everything after a dot if followed by an UUID
        aux = _dotUuid.Replace(Path.GetFileName(aux), "");
        // Removing the tokens (everything between brackets or parenthesis)
        aux = RemoveTokens(aux);
        return aux.Trim();
    }

    /// <summary>
    /// Returns the new file name of the image passed as parameter based on the processing settings selected.
    /// </summary>
    public static string ImageFileNameToProcessedImageFileName(GameImage image, Game game, List<GameImageCriterion> criteria)
    {
        string filePath = $@"{Path.GetDirectoryName(image.File)}\";
        string fileName = Path.GetFileNameWithoutExtension(image.File);
        string fileNameSuffix = _trailingHyphenTwoDigits.Match(fileName).Value;
        string fileRegion = image.FileLeafFolder;
        // Removing the last 3 characters if they are a hyphen followed by 2 digits
        fileName = _trailingHyphenTwoDigits.Replace(fileName, "").Trim();
        foreach (GameImageCriterion criterion in criteria)
        {
            if (criterion.IsActive)
            {
                if (criterion.Type.Value == SettingsType.FileName.Value)
                {
                    if (criterion.Name == FileNameSettings.DatabaseId.Value) { fileName = game.DatabaseId; }
                    if (criterion.Name == FileNameSettings.Rom.Value) { fileName = game.Rom; }
                    if (criterion.Name == FileNameSettings.RomSimplified.Value) { fileName = game.RomSimplified; }
                    if (criterion.Name == FileNameSettings.Title.Value) { fileName = game.Title; }
                }
                if (criterion.Type.Value == SettingsType.FileNameSuffix.Value)
                {
                    fileNameSuffix = criterion.Name == FileNameSuffixSettings.Suffix.Value ? "-01" : "";
                }
                if (criterion.Type.Value == SettingsType.Region.Value && criterion.Name == RegionSettings.RegionDiscard.Value && fileRegion != string.Empty)
                {
                    filePath = filePath.Replace($@"{image.FileLeafFolder}\", "");
                }
            }
        }
        return $@"{filePath}{ReplaceAllSpecialCharactersWithUnderscores(fileName)}{fileNameSuffix}{image.FileExtension}";
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