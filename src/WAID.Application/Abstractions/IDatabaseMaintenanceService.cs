namespace WAID.Application.Abstractions;

public enum DatabaseHealthState { Healthy, RecoveryRequired, Unavailable }

public sealed record DatabaseHealth(
    DatabaseHealthState State,
    int SchemaVersion,
    int SupportedSchemaVersion,
    string JournalMode,
    string DatabaseLocation,
    string BackupLocation,
    string LastMigration,
    string Detail);

public sealed record DatabaseMaintenanceResult(bool Succeeded, string? Path, string? FailureCode, string Message)
{
    public static DatabaseMaintenanceResult Success(string path, string message) => new(true, path, null, message);
    public static DatabaseMaintenanceResult Failure(string code, string message) => new(false, null, code, message);
}

public interface IDatabaseMaintenanceService
{
    Task<DatabaseHealth> CheckHealthAsync(CancellationToken token);
    Task<DatabaseMaintenanceResult> CreateBackupAsync(CancellationToken token);
    Task<DatabaseMaintenanceResult> RestoreAsync(string backupPath, bool explicitlyApproved, CancellationToken token);
    Task<DatabaseMaintenanceResult> ApplyRetentionAsync(int retainCount, CancellationToken token);
}
