using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class DatabaseReliabilityTests
{
    [Fact]
    public async Task Fresh_install_applies_all_ordered_migrations_and_wal()
    {
        using var fixture = new DatabaseFixture();
        await fixture.Database.InitializeAsync(CancellationToken.None);

        await using var connection = fixture.Database.OpenConnection();
        Assert.Equal(WaidDatabase.CurrentSchemaVersion, await ScalarIntAsync(connection, "PRAGMA user_version;"));
        Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal("ok", await ScalarAsync(connection, "PRAGMA quick_check;"));
        Assert.Equal(32, await ScalarIntAsync(connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public async Task Representative_upgrade_preserves_existing_data(int version)
    {
        using var fixture = new DatabaseFixture();
        await using (var connection = fixture.Database.OpenConnection())
        {
            await ExecuteAsync(connection, LegacySchema(version));
            await ExecuteAsync(connection, "INSERT INTO settings VALUES(1,'{\"theme\":\"Dark\"}','2026-01-01T00:00:00Z');");
            await ExecuteAsync(connection, $"PRAGMA user_version={version};");
        }

        await fixture.Database.InitializeAsync(CancellationToken.None);

        await using var verified = fixture.Database.OpenConnection();
        Assert.Equal(WaidDatabase.CurrentSchemaVersion, await ScalarIntAsync(verified, "PRAGMA user_version;"));
        Assert.Contains("Dark", await ScalarAsync(verified, "SELECT json FROM settings WHERE id=1;"));
        Assert.True(Directory.GetFiles(fixture.BackupDirectory, "waid-pre-migration-*.db").Length == 1);
    }

    [Fact]
    public async Task Failed_migration_rolls_back_active_step_and_preserves_version_and_data()
    {
        using var fixture = new DatabaseFixture();
        await using (var connection = fixture.Database.OpenConnection())
        {
            await ExecuteAsync(connection, """
                CREATE TABLE settings(id INTEGER PRIMARY KEY CHECK(id=1),json TEXT NOT NULL,updated_utc TEXT NOT NULL);
                INSERT INTO settings VALUES(1,'preserve-me','2026-01-01T00:00:00Z');
                CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY,applied_utc TEXT NOT NULL,description TEXT NOT NULL);
                CREATE TRIGGER reject_migration BEFORE INSERT ON schema_migrations BEGIN SELECT RAISE(ABORT,'simulated interruption'); END;
                PRAGMA user_version=6;
                """);
        }

        var failure = await Assert.ThrowsAsync<WaidPersistenceException>(() => fixture.Database.InitializeAsync(CancellationToken.None));

        Assert.Equal("WAID-DB-MIGRATION", failure.Code);
        await using var verified = fixture.Database.OpenConnection();
        Assert.Equal(6, await ScalarIntAsync(verified, "PRAGMA user_version;"));
        Assert.Equal("preserve-me", await ScalarAsync(verified, "SELECT json FROM settings WHERE id=1;"));
        Assert.Equal(0, await ScalarIntAsync(verified, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='alerts';"));
    }

    [Fact]
    public async Task Concurrent_initialization_is_idempotent()
    {
        using var fixture = new DatabaseFixture();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => fixture.Database.InitializeAsync(CancellationToken.None)));
        await using var connection = fixture.Database.OpenConnection();
        Assert.Equal(WaidDatabase.CurrentSchemaVersion, await ScalarIntAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarIntAsync(connection, "SELECT count(*) FROM schema_migrations WHERE version=8;"));
    }

    [Fact]
    public async Task Corrupt_database_returns_typed_recovery_failure()
    {
        using var fixture = new DatabaseFixture();
        await File.WriteAllTextAsync(fixture.Database.DatabasePath, "not a sqlite database");
        var failure = await Assert.ThrowsAsync<WaidPersistenceException>(() => fixture.Database.InitializeAsync(CancellationToken.None));
        Assert.Equal("WAID-DB-CORRUPT", failure.Code);
    }

    [Fact]
    public async Task Newer_database_is_rejected_without_modification()
    {
        using var fixture = new DatabaseFixture();
        await using (var connection = fixture.Database.OpenConnection()) await ExecuteAsync(connection, $"PRAGMA user_version={WaidDatabase.CurrentSchemaVersion + 1};");
        var failure = await Assert.ThrowsAsync<WaidPersistenceException>(() => fixture.Database.InitializeAsync(CancellationToken.None));
        Assert.Equal("WAID-DB-NEWER", failure.Code);
        await using var verified = fixture.Database.OpenConnection();
        Assert.Equal(WaidDatabase.CurrentSchemaVersion + 1, await ScalarIntAsync(verified, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task Backup_is_verified_and_restore_requires_approval()
    {
        using var fixture = new DatabaseFixture();
        await fixture.Database.InitializeAsync(CancellationToken.None);
        var service = fixture.CreateMaintenance();
        await using (var connection = fixture.Database.OpenConnection()) await ExecuteAsync(connection, "INSERT INTO settings VALUES(1,'original','2026-01-01T00:00:00Z');");
        var backup = await service.CreateBackupAsync(CancellationToken.None);
        Assert.True(backup.Succeeded);
        Assert.True(File.Exists(backup.Path));
        Assert.False((await service.RestoreAsync(backup.Path!, false, CancellationToken.None)).Succeeded);
        await using (var connection = fixture.Database.OpenConnection()) await ExecuteAsync(connection, "UPDATE settings SET json='changed' WHERE id=1;");

        var restored = await service.RestoreAsync(backup.Path!, true, CancellationToken.None);

        Assert.True(restored.Succeeded, restored.Message);
        await using var verified = fixture.Database.OpenConnection();
        Assert.Equal("original", await ScalarAsync(verified, "SELECT json FROM settings WHERE id=1;"));
        Assert.NotEmpty(fixture.Audit.Records);
    }

    [Fact]
    public async Task Backup_retention_is_bounded_and_idempotent()
    {
        using var fixture = new DatabaseFixture();
        await fixture.Database.InitializeAsync(CancellationToken.None);
        var service = fixture.CreateMaintenance();
        for (var index = 0; index < 12; index++) Assert.True((await service.CreateBackupAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(10, Directory.GetFiles(fixture.BackupDirectory, "waid-*.db").Length);
        Assert.True((await service.ApplyRetentionAsync(3, CancellationToken.None)).Succeeded);
        Assert.Equal(3, Directory.GetFiles(fixture.BackupDirectory, "waid-*.db").Length);
        Assert.True((await service.ApplyRetentionAsync(3, CancellationToken.None)).Succeeded);
        Assert.Equal(3, Directory.GetFiles(fixture.BackupDirectory, "waid-*.db").Length);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task<string> ScalarAsync(SqliteConnection connection, string sql)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty; }
    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql) => int.Parse(await ScalarAsync(connection, sql));
    private static string LegacySchema(int version) => """
        CREATE TABLE scan_sessions(id TEXT PRIMARY KEY,started_utc TEXT NOT NULL,completed_utc TEXT NOT NULL);
        CREATE TABLE findings(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,scanner_id TEXT NOT NULL,code TEXT NOT NULL,title TEXT NOT NULL,description TEXT NOT NULL,severity INTEGER NOT NULL,repair_id TEXT NULL,evidence_json TEXT NOT NULL,FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE);
        CREATE TABLE settings(id INTEGER PRIMARY KEY CHECK(id=1),json TEXT NOT NULL,updated_utc TEXT NOT NULL);
        """ + (version >= 6 ? """
        CREATE TABLE repair_history(transaction_id TEXT PRIMARY KEY,repair_id TEXT NOT NULL,status INTEGER NOT NULL,created_utc TEXT NOT NULL,completed_utc TEXT NULL,summary TEXT NULL,details TEXT NULL,backup_location TEXT NULL,restore_point_description TEXT NULL,events_json TEXT NOT NULL);
        CREATE TABLE diagnosis_reports(id TEXT PRIMARY KEY,scan_session_id TEXT NOT NULL,generated_utc TEXT NOT NULL,report_json TEXT NOT NULL,FOREIGN KEY(scan_session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE);
        CREATE TABLE health_snapshots(id TEXT PRIMARY KEY,captured_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL);
        CREATE TABLE scan_schedule(id INTEGER PRIMARY KEY CHECK(id=1),schedule_json TEXT NOT NULL);
        CREATE TABLE repair_approvals(id TEXT PRIMARY KEY,requested_utc TEXT NOT NULL,approval_json TEXT NOT NULL);
        """ : string.Empty);

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"waid-db-reliability-{Guid.NewGuid():N}");
        public DatabaseFixture() { Directory.CreateDirectory(_root); Database = new WaidDatabase($"Data Source={Path.Combine(_root, "waid.db")};Foreign Keys=True;Pooling=False"); }
        public WaidDatabase Database { get; }
        public string BackupDirectory => Path.Combine(_root, "Backups", "Database");
        public RecordingAudit Audit { get; } = new();
        public DatabaseMaintenanceService CreateMaintenance() => new(Database, BackupDirectory, TimeProvider.System, NullLogger<DatabaseMaintenanceService>.Instance, Audit);
        public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class RecordingAudit : IAuditTrailService
    {
        public List<AuditRecord> Records { get; } = [];
        public Task<AuditWriteResult> AppendAsync(AuditRecord record, CancellationToken token) { Records.Add(record); return Task.FromResult(new AuditWriteResult(true, record.Id)); }
        public Task<IReadOnlyList<AuditRecord>> SearchAsync(AuditQuery query, CancellationToken token) => Task.FromResult<IReadOnlyList<AuditRecord>>(Records);
        public Task ApplyRetentionAsync(CancellationToken token) => Task.CompletedTask;
    }
}
