namespace WAID.Domain.Repairs;

public sealed record RepairHistoryEntry(
    Guid TransactionId,
    string RepairId,
    RepairTransactionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Summary,
    string? Details,
    string? BackupLocation,
    string? RestorePointDescription,
    IReadOnlyCollection<string> Events);
