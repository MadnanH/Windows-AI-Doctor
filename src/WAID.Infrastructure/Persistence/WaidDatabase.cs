using Microsoft.Data.Sqlite;
namespace WAID.Infrastructure.Persistence;
public sealed class WaidDatabase(string connectionString)
{
    public SqliteConnection OpenConnection() { var connection = new SqliteConnection(connectionString); connection.Open(); return connection; }
    public async Task InitializeAsync(CancellationToken token)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS scan_sessions(id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, completed_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS findings(id TEXT PRIMARY KEY, session_id TEXT NOT NULL, scanner_id TEXT NOT NULL, code TEXT NOT NULL, title TEXT NOT NULL, description TEXT NOT NULL, severity INTEGER NOT NULL, repair_id TEXT NULL, evidence_json TEXT NOT NULL, FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS repair_history(
                transaction_id TEXT PRIMARY KEY,
                repair_id TEXT NOT NULL,
                status INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                completed_utc TEXT NULL,
                summary TEXT NULL,
                details TEXT NULL,
                backup_location TEXT NULL,
                restore_point_description TEXT NULL,
                events_json TEXT NOT NULL);
            PRAGMA user_version=2;
            """;
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
