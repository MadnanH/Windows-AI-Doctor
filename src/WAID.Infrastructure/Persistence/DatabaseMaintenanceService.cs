using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;

namespace WAID.Infrastructure.Persistence;

public sealed class DatabaseMaintenanceService(
    WaidDatabase database,
    string backupDirectory,
    TimeProvider timeProvider,
    ILogger<DatabaseMaintenanceService> logger,
    IAuditTrailService auditTrail) : IDatabaseMaintenanceService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DatabaseHealth> CheckHealthAsync(CancellationToken token)
    {
        try
        {
            await using var connection = database.OpenConnection();
            var integrity = await ScalarAsync(connection, "PRAGMA quick_check;", token).ConfigureAwait(false);
            var foreignKeys = await ScalarAsync(connection, "SELECT CASE WHEN EXISTS(SELECT 1 FROM pragma_foreign_key_check) THEN 'failed' ELSE 'ok' END;", token).ConfigureAwait(false);
            var version = int.Parse(await ScalarAsync(connection, "PRAGMA user_version;", token).ConfigureAwait(false));
            var journal = await ScalarAsync(connection, "PRAGMA journal_mode;", token).ConfigureAwait(false);
            var healthy = integrity.Equals("ok", StringComparison.OrdinalIgnoreCase) && foreignKeys == "ok" && version == WaidDatabase.CurrentSchemaVersion;
            var migration = database.LastMigrationStatus is { } status
                ? $"{status.CompletedAtUtc:u}: {status.Detail}"
                : "No migration status is available for this process.";
            return new(healthy ? DatabaseHealthState.Healthy : DatabaseHealthState.RecoveryRequired, version, WaidDatabase.CurrentSchemaVersion,
                journal, database.DatabasePath, backupDirectory, migration,
                healthy ? "Integrity, foreign keys, schema version, and recovery journal are valid." : $"Integrity={integrity}; foreign keys={foreignKeys}; schema={version}.");
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or WaidPersistenceException)
        {
            logger.LogError("Database health check failed with {FailureType}", exception.GetType().Name);
            return new(DatabaseHealthState.Unavailable, 0, WaidDatabase.CurrentSchemaVersion, "unavailable", database.DatabasePath, backupDirectory,
                database.LastMigrationStatus?.Detail ?? "Unavailable", "The database could not be checked. Restore a verified backup or contact support.");
        }
    }

    public async Task<DatabaseMaintenanceResult> CreateBackupAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(backupDirectory);
            var path = Path.Combine(backupDirectory, $"waid-{timeProvider.GetUtcNow():yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.db");
            await using var source = database.OpenConnection();
            await using var destination = Open(path, SqliteOpenMode.ReadWriteCreate);
            source.BackupDatabase(destination);
            var validation = await ScalarAsync(destination, "PRAGMA quick_check;", token).ConfigureAwait(false);
            if (validation != "ok") { File.Delete(path); return DatabaseMaintenanceResult.Failure("WAID-DB-BACKUP-INVALID", "The backup failed validation and was removed."); }
            ApplyRetentionCore(10, token);
            await AuditAsync("DatabaseBackup", path, AuditResult.Succeeded, "A verified local database backup was created.", token).ConfigureAwait(false);
            logger.LogInformation("Verified database backup created at {BackupFileName}", Path.GetFileName(path));
            return DatabaseMaintenanceResult.Success(path, "Database backup created and verified.");
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            logger.LogError("Database backup failed with {FailureType}", exception.GetType().Name);
            return DatabaseMaintenanceResult.Failure("WAID-DB-BACKUP", "The database backup could not be created. Check available storage and folder permissions.");
        }
        finally { _gate.Release(); }
    }

    public async Task<DatabaseMaintenanceResult> RestoreAsync(string backupPath, bool explicitlyApproved, CancellationToken token)
    {
        if (!explicitlyApproved) return DatabaseMaintenanceResult.Failure("WAID-DB-RESTORE-APPROVAL", "Explicit approval is required before database recovery.");
        if (string.IsNullOrWhiteSpace(backupPath) || !Path.IsPathFullyQualified(backupPath) || !File.Exists(backupPath))
            return DatabaseMaintenanceResult.Failure("WAID-DB-RESTORE-PATH", "Select an existing database backup.");
        var fullBackupPath = Path.GetFullPath(backupPath);
        var allowedRoot = Path.GetFullPath(backupDirectory) + Path.DirectorySeparatorChar;
        if (!fullBackupPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            return DatabaseMaintenanceResult.Failure("WAID-DB-RESTORE-LOCATION", "For safety, restore a backup from WAID's database backup folder.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var backup = Open(fullBackupPath, SqliteOpenMode.ReadOnly);
            if (await ScalarAsync(backup, "PRAGMA quick_check;", token).ConfigureAwait(false) != "ok")
                return DatabaseMaintenanceResult.Failure("WAID-DB-RESTORE-CORRUPT", "The selected backup failed its integrity check.");
            var version = int.Parse(await ScalarAsync(backup, "PRAGMA user_version;", token).ConfigureAwait(false));
            var applicationId = int.Parse(await ScalarAsync(backup, "PRAGMA application_id;", token).ConfigureAwait(false));
            if (applicationId != WaidDatabase.WaidApplicationId || version < 1)
                return DatabaseMaintenanceResult.Failure("WAID-DB-RESTORE-FOREIGN", "The selected file is not a verified WAID database backup.");
            if (version > WaidDatabase.CurrentSchemaVersion)
                return DatabaseMaintenanceResult.Failure("WAID-DB-RESTORE-NEWER", "The selected backup was created by a newer WAID version.");

            var safety = await CreateBackupAsyncCore(token).ConfigureAwait(false);
            await using var destination = database.OpenConnection();
            backup.BackupDatabase(destination);
            await database.InitializeAsync(token).ConfigureAwait(false);
            await AuditAsync("DatabaseRestore", database.DatabasePath, AuditResult.Succeeded, $"Database restored after safety backup {Path.GetFileName(safety)}.", token).ConfigureAwait(false);
            return DatabaseMaintenanceResult.Success(safety, "Database recovery completed. Restart WAID before continuing.");
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or WaidPersistenceException)
        {
            logger.LogError("Database restore failed with {FailureType}", exception.GetType().Name);
            await AuditAsync("DatabaseRestore", database.DatabasePath, AuditResult.Failed, "Database recovery failed safely.", CancellationToken.None).ConfigureAwait(false);
            return DatabaseMaintenanceResult.Failure("WAID-DB-RESTORE", "Database recovery failed. The pre-recovery database was not intentionally deleted.");
        }
        finally { _gate.Release(); }
    }

    public async Task<DatabaseMaintenanceResult> ApplyRetentionAsync(int retainCount, CancellationToken token)
    {
        if (retainCount is < 1 or > 100) return DatabaseMaintenanceResult.Failure("WAID-DB-RETENTION", "Backup retention must be between 1 and 100 files.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(backupDirectory)) return DatabaseMaintenanceResult.Success(backupDirectory, "No database backups require retention.");
            var files = ApplyRetentionCore(retainCount, token);
            return DatabaseMaintenanceResult.Success(backupDirectory, $"Retained the newest {Math.Min(files.Length, retainCount)} database backups.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Database backup retention failed with {FailureType}", exception.GetType().Name);
            return DatabaseMaintenanceResult.Failure("WAID-DB-RETENTION", "Old database backups could not be removed. Check folder permissions.");
        }
        finally { _gate.Release(); }
    }

    private async Task<string> CreateBackupAsyncCore(CancellationToken token)
    {
        Directory.CreateDirectory(backupDirectory);
        var path = Path.Combine(backupDirectory, $"waid-pre-restore-{timeProvider.GetUtcNow():yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.db");
        await using var source = database.OpenConnection(); await using var destination = Open(path, SqliteOpenMode.ReadWriteCreate);
        source.BackupDatabase(destination);
        if (await ScalarAsync(destination, "PRAGMA quick_check;", token).ConfigureAwait(false) != "ok") throw new InvalidDataException("Safety backup validation failed.");
        return path;
    }

    private FileInfo[] ApplyRetentionCore(int retainCount, CancellationToken token)
    {
        var files = new DirectoryInfo(backupDirectory).GetFiles("waid-*.db").OrderByDescending(item => item.CreationTimeUtc).ToArray();
        foreach (var file in files.Skip(retainCount)) { token.ThrowIfCancellationRequested(); file.Delete(); }
        return files;
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString()); connection.Open(); return connection;
    }

    private static async Task<string> ScalarAsync(SqliteConnection connection, string sql, CancellationToken token)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? string.Empty; }

    private async Task AuditAsync(string action, string target, AuditResult result, string detail, CancellationToken token)
    {
        var write = await auditTrail.AppendAsync(new(Guid.NewGuid(), timeProvider.GetUtcNow(), AuditActor.User, action, Path.GetFileName(target), result,
            SafetyLevel.Moderate, false, true, Guid.NewGuid(), Guid.NewGuid(), detail), token).ConfigureAwait(false);
        if (!write.Succeeded) logger.LogWarning("Database maintenance audit could not be stored: {FailureCode}", write.FailureCode);
    }
}
