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

public sealed record RestorePointResult(bool Succeeded, string Description, string? Error = null);

public interface IBackupManager
{
    Task<BackupSnapshot> CreateAsync(
        Guid transactionId,
        IReadOnlyCollection<RepairResource> resources,
        CancellationToken cancellationToken);
}

public sealed record BackupSnapshot(
    string Location,
    IReadOnlyCollection<BackupItem> Items,
    IReadOnlyCollection<string> Warnings);

public sealed record BackupItem(RepairResource Resource, string BackupPath);

public interface IRollbackManager
{
    Task<RollbackResult> RollbackAsync(BackupSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record RollbackResult(bool Succeeded, IReadOnlyCollection<string> Actions, IReadOnlyCollection<string> Errors);

public interface IRepairHistoryRepository
{
    Task SaveAsync(RepairTransaction transaction, CancellationToken cancellationToken);
    Task<IReadOnlyList<RepairHistoryEntry>> GetRecentAsync(int count, CancellationToken cancellationToken);
}
