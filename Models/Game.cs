using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace MM4LB.Models;

/// <summary>
/// Represents a single game in the LaunchBox game collection.
/// Contains normalized ROM information, title, version, search strings,
/// and references to associated images.
/// </summary>
public class Game : ObservableObject
{
    private string _rom = string.Empty;
    private string _romFileName = string.Empty;

    /// <summary>
    /// Tokens extracted from ROM, title and version (typically content inside brackets or parentheses).
    /// Used to generate search strings for image matching.
    /// </summary>
    private List<string> _tokens = new();

    /// <summary>
    /// Images for the game for all image types. If null the property is null it means that no attempt to load all the images of the game has been done.
    /// </summary>
    public List<GameImage> AllImages { get; protected set; } = new();

    /// <summary>
    /// Images associated with the game for the currently selected image type set.
    /// </summary>
    public ObservableCollection<GameImage> Images { get; protected set; } = new();

    /// <summary>
    /// LaunchBox database ID for the game.
    /// </summary>
    public string DatabaseId { get; protected set; }

    /// <summary>
    /// Indicates whether the game is part of the user's collection.
    /// Defaults to true.
    /// </summary>
    public bool InCollection { get; protected set; } = true;

    /// <summary>
    /// Indicates whether the game exists in the LaunchBox database.
    /// </summary>
    public bool InLaunchboxDb { get; set; }

    /// <summary>
    /// Ficha completa del juego (todos los campos del &lt;Game&gt; del XML de colección, agrupados para mostrar).
    /// La puebla <see cref="Platform.SetGames"/> a partir del nodo XML; es <c>null</c> en los juegos que solo
    /// existen en la base de datos de LaunchBox (sin nodo de colección). Ver <see cref="GameDetails"/>.
    /// </summary>
    public GameDetails? Details { get; protected set; }

    /// <summary>
    /// Normalized ROM name for the game.
    /// The setter:
    /// - Removes directory path (keeps only filename)
    /// - Removes file extension
    /// </summary>
    public string Rom
    {
        get => _rom;
        protected set
        {
            _rom = value;
            // Keep only the filename (remove path)
            _rom = _rom[(_rom.LastIndexOf("\\") + 1)..];
            // Remove file extension
            _rom = Regex.Replace(_rom, @"\.[^.]*$", "");
        }
    }

    /// <summary>
    /// ROM filename including extension, but without directory path.
    /// </summary>
    public string RomFileName
    {
        get => _romFileName;
        protected set
        {
            _romFileName = value;
            // Keep only the filename (remove path)
            _romFileName = _romFileName[(_romFileName.LastIndexOf("\\") + 1)..];
        }
    }

    /// <summary>
    /// Simplified ROM name:
    /// Removes everything after the first bracket or parenthesis.
    /// Useful for matching images with cleaner filenames.
    /// </summary>
    public string RomSimplified => Regex.Replace(Rom, @"[(\[)\]].*$", "").Trim();

    /// <summary>
    /// List of search strings used to match image filenames with this game.
    /// Generated from ROM, title, version and extracted tokens.
    /// </summary>
    public List<string> SearchStrings { get; protected set; } = new();

    /// <summary>
    /// Conjunto hash de <see cref="SearchStrings"/> para comprobar pertenencia en O(1). Se construye una sola
    /// vez al final de <see cref="GenerateSearchStrings"/> (los search strings no se mutan después). Sustituye al
    /// <c>SearchStrings.IndexOf(...) != -1</c> (O(S) lineal) en el emparejado juego↔imagen. Comparador ordinal
    /// por defecto, idéntico al de <see cref="List{T}.IndexOf(T)"/>.
    /// </summary>
    public HashSet<string> SearchStringsSet { get; protected set; } = new();

    /// <summary>
    /// Title of the game.
    /// </summary>
    public string Title { get; protected set; }

    /// <summary>
    /// Version of the game (if present in the LaunchBox XML).
    /// </summary>
    public string Version { get; protected set; }

    /// <summary>
    /// Creates a new Game instance with normalized ROM data and generated search strings.
    /// </summary>
    public Game(string databaseId, string rom, string title, string version, bool inCollection = true, bool inLaunchBoxDb = false, GameDetails? details = null)
    {
        DatabaseId = databaseId;
        Rom = rom;
        RomFileName = rom;
        Title = title;
        Version = version;
        InCollection = inCollection;
        InLaunchboxDb = inLaunchBoxDb;
        Details = details;
        GenerateSearchStrings();
    }

    /// <summary>
    /// Generates all search strings used to match this game with image filenames.
    /// Combines:
    /// - Normalized ROM strings
    /// - Title variations (cleaned, underscored)
    /// - Tokens extracted from ROM, title and version
    /// - Database ID
    /// </summary>
    private void GenerateSearchStrings()
    {
        /// <summary>
        /// Adds tokenized variations of the provided base strings.
        /// Each token is appended to each base string unless already present.
        /// </summary>
        void AddTokenisedSearchStrings(List<string> stringsToAddTokens)
        {
            foreach (string token in _tokens)
            {
                foreach (string searchString in stringsToAddTokens)
                {
                    if (!searchString.Contains(token) &&
                        !SearchStrings.Contains($"{searchString} {token}"))
                    {
                        SearchStrings.Add($"{searchString} {token}");
                    }
                }
            }

            // Always include the database ID as a search string.
            SearchStrings.Add(DatabaseId.ToLower().Trim());
        }

        List<string> stringsToAddTokens = new();

        // ROM base search string
        string aux = Rom.ToLower().Trim();
        if (!SearchStrings.Contains(aux)) SearchStrings.Add(aux);
        stringsToAddTokens.Add(aux);

        // Tokens from ROM
        _tokens.AddRange(Utilities.GetTokens(aux));

        // ROM simplified (before brackets)
        aux = Regex.Replace(Rom, @"[(\[)\]].*$", "").ToLower().Trim();
        if (!SearchStrings.Contains(aux)) SearchStrings.Add(aux);
        stringsToAddTokens.Add(aux);

        // Title variations
        aux = Utilities.RemoveAllSpecialCharacters(Title).ToLower().Trim();
        if (!SearchStrings.Contains(aux)) SearchStrings.Add(aux);
        stringsToAddTokens.Add(aux);

        aux = Utilities.ReplaceAllSpecialCharactersWithUnderscores(Title).ToLower().Trim();
        if (!SearchStrings.Contains(aux)) SearchStrings.Add(aux);
        stringsToAddTokens.Add(aux);

        aux = Utilities.ReplaceSpecialCharactersWithUnderscores(Title).ToLower().Trim();
        if (!SearchStrings.Contains(aux)) SearchStrings.Add(aux);
        stringsToAddTokens.Add(aux);

        // Tokens from title
        _tokens.AddRange(Utilities.GetTokens(aux));

        // Tokens from version
        _tokens.AddRange(Utilities.GetTokens($" {Version.ToLower()} "));

        // Generate tokenized combinations
        AddTokenisedSearchStrings(stringsToAddTokens);

        // Índice hash de las search strings (ya completas y sin mutación posterior) para el emparejado O(1).
        SearchStringsSet = new HashSet<string>(SearchStrings);
    }

    /// <summary>
    /// Compares two Game objects based on their core identifying fields.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            return false;

        Game g = (Game)obj;
        return DatabaseId.Equals(g.DatabaseId) &&
               Rom.Equals(g.Rom) &&
               Title.Equals(g.Title) &&
               Version.Equals(g.Version);
    }

    public override int GetHashCode() => System.HashCode.Combine(DatabaseId, Rom, Title, Version);
}