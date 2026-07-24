using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Reads, on demand, the per-game data of the LaunchBox metadata database
/// (<c>Metadata\LaunchBox.Metadata.db</c>) for a single game (by its <see cref="Game.DatabaseId"/>) and projects
/// it into detail groups via <see cref="GameDetails.DatabaseGroups"/>. Used by the Game Details widget to append
/// the database part of the sheet to the collection (XML) part.
///
/// The database is opened read-only, so the user's live LaunchBox database is never locked nor modified.
/// </summary>
public sealed class GameMetadataService
{
    private readonly AppSettings _appSettings;
    private readonly ExceptionService _exceptionService;

    public GameMetadataService(IOptions<AppSettings> appSettings, ExceptionService exceptionService)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
        _exceptionService = exceptionService;
    }

    /// <summary>
    /// Reads the game's database metadata for <paramref name="databaseId"/>: whether it exists, its detail groups
    /// (catalog + alternate titles) and known-image stats (total count and number of distinct media types).
    /// Returns Found=false with empty/zero values when the id is missing/invalid, the database file is absent, the
    /// game is not in the database, or a read error occurs.
    /// </summary>
    public async Task<(bool Found, IReadOnlyList<GameDetails.Group> Groups, int KnownImagesTotal, int KnownImageTypeCount)> GetMetadataAsync(string databaseId)
    {
        string databaseFile = _appSettings.LaunchBox.LaunchboxGamesDbFile;

        // Unmatched collection games store "0"/"" as DatabaseID: nothing to look up.
        if (string.IsNullOrEmpty(databaseId) || databaseId == "0" || !long.TryParse(databaseId, out long id))
            return (false, Array.Empty<GameDetails.Group>(), 0, 0);

        if (string.IsNullOrEmpty(databaseFile) || !File.Exists(databaseFile))
            return (false, Array.Empty<GameDetails.Group>(), 0, 0);

        try
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databaseFile,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync();

            // --- Games row (catalog) ---
            List<string>? catalogValues = null;
            string columns = string.Join(", ", GameDetails.DatabaseCatalogFields.Select(f => f.Column));
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT {columns} FROM Games WHERE DatabaseID = @id";
                command.Parameters.AddWithValue("@id", id);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    catalogValues = new List<string>(reader.FieldCount);
                    for (int i = 0; i < reader.FieldCount; i++)
                        catalogValues.Add(reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "");
                }
            }

            // Game not in the database: no database part.
            if (catalogValues == null)
                return (false, Array.Empty<GameDetails.Group>(), 0, 0);

            // --- Known images grouped by type ---
            var imageCountsByType = new List<KeyValuePair<string, int>>();
            int totalImages = 0;
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Type, COUNT(*) FROM GameImages WHERE DatabaseId = @id GROUP BY Type ORDER BY Type";
                command.Parameters.AddWithValue("@id", id);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string type = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    int count = Convert.ToInt32(reader.GetValue(1));
                    totalImages += count;
                    imageCountsByType.Add(new KeyValuePair<string, int>(type, count));
                }
            }

            // --- Alternate / regional titles ---
            var alternateTitles = new List<KeyValuePair<string, string>>();
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT AlternateName, Region FROM GameAlternateTitles WHERE DatabaseID = @id";
                command.Parameters.AddWithValue("@id", id);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string name = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    string region = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    if (!string.IsNullOrEmpty(name))
                        alternateTitles.Add(new KeyValuePair<string, string>(region, name));
                }
            }

            return (true, GameDetails.DatabaseGroups(catalogValues, alternateTitles), totalImages, imageCountsByType.Count);
        }
        catch (Exception ex)
        {
            _exceptionService.Handle(ex, MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.GameMetadata_ReadError_Error] ?? "There was an error reading the game metadata from the LaunchBox database.");
            return (false, Array.Empty<GameDetails.Group>(), 0, 0);
        }
    }
}
