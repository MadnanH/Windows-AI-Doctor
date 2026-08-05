using WAID.Domain.Repairs;

namespace WAID.Application.Abstractions;

public interface IAdministratorService
{
    bool IsAdministrator();
}

public interface IRestorePointManager
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken);
}

public sealed record RestorePointResult(bool Succeeded, string Description, string? Error = null, string? ProviderId = null, DateTimeOffset? CreatedAtUtc = null);

public interface IBackupManager
{
    Task<BackupSnapshot> CreateAsync(
        Guid transactionId,
        IReadOnlyCollection<RepairResource> resources,
        CancellationToken cancellationToken);
}

public enum RecoveryCapabilityLevel { None, RestorePointOnly, ResourceBackup, VerifiedRollback }
public enum RecoveryArtifactProtection { Unknown, LocalAccessRestricted }
public enum RecoveryArtifactState { Creating, Valid, Invalid, Expired, RolledBack, RollbackFailed, Deleted }

public sealed record BackupSnapshot(
    string Location,
    IReadOnlyCollection<BackupItem> Items,
    IReadOnlyCollection<string> Warnings,
    string ManifestSha256 = "",
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset ExpiresAtUtc = default,
    RecoveryArtifactProtection Protection = RecoveryArtifactProtection.Unknown,
    RecoveryCapabilityLevel Capability = RecoveryCapabilityLevel.None,
    bool IsValidated = false,
    string? ValidationFailureCode = null);

public sealed record BackupItem(RepairResource Resource, string BackupPath, string Sha256 = "", long Length = 0);

public sealed record RecoveryArtifactRecord(Guid Id, Guid TransactionId, string Location, string ManifestSha256, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, RecoveryArtifactProtection Protection, RecoveryCapabilityLevel Capability, RecoveryArtifactState State, DateTimeOffset ValidatedAtUtc, string ValidationDetail, DateTimeOffset? RolledBackAtUtc = null, string? RollbackDetail = null);

public interface IRecoveryArtifactRepository
{
    Task SaveAsync(RecoveryArtifactRecord artifact, CancellationToken cancellationToken);
    Task<RecoveryArtifactRecord?> GetByTransactionAsync(Guid transactionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecoveryArtifactRecord>> GetRecentAsync(int count, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecoveryArtifactRecord>> GetExpiredAsync(DateTimeOffset nowUtc, int count, CancellationToken cancellationToken);
}

public sealed record RecoveryCleanupResult(int Deleted, int Failed, IReadOnlyList<string> Errors);
public interface IRecoveryRetentionService { Task<RecoveryCleanupResult> DeleteExpiredAsync(CancellationToken cancellationToken); }

public interface IRecoveryStorageProbe { bool HasAvailableSpace(string path, long requiredBytes); }
public sealed record RecoveryRollbackResult(bool Succeeded, bool Verified, string Detail);
public interface IRecoveryWorkflow { Task<RecoveryRollbackResult> RollbackAsync(Guid transactionId, bool explicitlyConfirmed, CancellationToken cancellationToken); }

public interface IRollbackManager
{
    Task<RollbackResult> RollbackAsync(BackupSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record RollbackResult(bool Succeeded, IReadOnlyCollection<string> Actions, IReadOnlyCollection<string> Errors, bool Verified = false, string VerificationDetail = "Rollback was not verified.");

public interface IRepairHistoryRepository
{
    Task SaveAsync(RepairTransaction transaction, CancellationToken cancellationToken);
    Task<IReadOnlyList<RepairHistoryEntry>> GetRecentAsync(int count, CancellationToken cancellationToken);
}
