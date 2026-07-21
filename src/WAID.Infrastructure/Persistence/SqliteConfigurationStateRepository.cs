using System.Text.Json;
using Microsoft.Data.Sqlite;
using WAID.Application.Abstractions;
using WAID.Domain.Settings;

namespace WAID.Infrastructure.Persistence;

public sealed class SqliteConfigurationStateRepository(WaidDatabase database) : IConfigurationStateRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<ConfigurationState> GetAsync(CancellationToken token)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version,user_json,profile_json,updated_utc FROM configuration_state WHERE id=1;";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return DefaultState();
        var version = reader.GetInt32(0); var userJson = reader.GetString(1); var profileJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        var updated = DateTimeOffset.Parse(reader.GetString(3));
        if (version == 1)
        {
            var legacy = (JsonSerializer.Deserialize<ApplicationSettings>(userJson, Options) ?? new ApplicationSettings()) with { Version = ApplicationSettings.CurrentVersion };
            var migrated = new ConfigurationState(ConfigurationState.CurrentVersion,
                new(ConfigurationScope.User, "Migrated legacy settings", ConfigurationValues.From(legacy.Validate()), EmptyFlags(), EmptyLocks()), null, updated).Validate();
            await reader.DisposeAsync().ConfigureAwait(false);
            await SaveOnConnectionAsync(connection, migrated, token).ConfigureAwait(false);
            return migrated;
        }
        var user = JsonSerializer.Deserialize<ConfigurationLayer>(userJson, Options) ?? throw new WaidPersistenceException("WAID-CONFIG-STATE", "Saved user settings are invalid.", "Reset user settings or restore a database backup.");
        var profile = profileJson is null ? null : JsonSerializer.Deserialize<ConfigurationLayer>(profileJson, Options);
        return new ConfigurationState(version, user, profile, updated).Validate();
    }

    public async Task SaveAsync(ConfigurationState state, CancellationToken token)
    {
        state.Validate(); await using var connection = database.OpenConnection(); await SaveOnConnectionAsync(connection, state, token).ConfigureAwait(false);
    }

    private static async Task SaveOnConnectionAsync(SqliteConnection connection, ConfigurationState state, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO configuration_state(id,version,user_json,profile_json,updated_utc) VALUES(1,$version,$user,$profile,$updated) ON CONFLICT(id) DO UPDATE SET version=$version,user_json=$user,profile_json=$profile,updated_utc=$updated;";
        command.Parameters.AddWithValue("$version", state.Version); command.Parameters.AddWithValue("$user", JsonSerializer.Serialize(state.User, Options));
        command.Parameters.AddWithValue("$profile", state.ActiveProfile is null ? DBNull.Value : JsonSerializer.Serialize(state.ActiveProfile, Options)); command.Parameters.AddWithValue("$updated", state.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static ConfigurationState DefaultState() => new(ConfigurationState.CurrentVersion,
        new(ConfigurationScope.User, "Local user", new(), EmptyFlags(), EmptyLocks()), null, DateTimeOffset.UtcNow);
    private static IReadOnlyDictionary<string, bool> EmptyFlags() => new Dictionary<string, bool>();
    private static IReadOnlyList<string> EmptyLocks() => Array.Empty<string>();
}
