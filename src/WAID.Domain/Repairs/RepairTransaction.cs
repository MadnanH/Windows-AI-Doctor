namespace WAID.Domain.Repairs;

public enum RepairTransactionStatus
{
    Pending,
    Preparing,
    Executing,
    Succeeded,
    Failed,
    RolledBack,
    Cancelled
}

public sealed class RepairTransaction
{
    private readonly List<string> _events = [];

    public RepairTransaction(Guid id, string repairId, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("A transaction id is required.", nameof(id));
        Id = id;
        RepairId = string.IsNullOrWhiteSpace(repairId)
            ? throw new ArgumentException("A repair id is required.", nameof(repairId))
            : repairId;
        CreatedAtUtc = createdAtUtc;
        Status = RepairTransactionStatus.Pending;
    }

    public Guid Id { get; }
    public string RepairId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public RepairTransactionStatus Status { get; private set; }
    public string? BackupLocation { get; private set; }
    public string? RestorePointDescription { get; private set; }
    public RepairResult? Result { get; private set; }
    public IReadOnlyList<string> Events => _events.AsReadOnly();

    public void BeginPreparation() => Transition(RepairTransactionStatus.Pending, RepairTransactionStatus.Preparing);
    public void BeginExecution() => Transition(RepairTransactionStatus.Preparing, RepairTransactionStatus.Executing);

    public void RecordBackup(string location)
    {
        if (Status != RepairTransactionStatus.Preparing) throw new InvalidOperationException("Backup can only be recorded during preparation.");
        BackupLocation = string.IsNullOrWhiteSpace(location) ? throw new ArgumentException("Backup location is required.", nameof(location)) : location;
        AddEvent($"Backup created: {location}");
    }

    public void RecordRestorePoint(string description)
    {
        if (Status != RepairTransactionStatus.Preparing) throw new InvalidOperationException("Restore point can only be recorded during preparation.");
        RestorePointDescription = description;
        AddEvent($"Restore point created: {description}");
    }

    public void AddEvent(string message)
    {
        if (!string.IsNullOrWhiteSpace(message)) _events.Add(message.Trim());
    }

    public void Complete(RepairResult result, DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Status != RepairTransactionStatus.Executing) throw new InvalidOperationException("Only an executing repair can complete.");
        if (completedAtUtc < CreatedAtUtc) throw new ArgumentOutOfRangeException(nameof(completedAtUtc));
        Result = result;
        Status = result.Succeeded ? RepairTransactionStatus.Succeeded : RepairTransactionStatus.Failed;
        CompletedAtUtc = completedAtUtc;
    }

    public void Fail(RepairResult result, DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded) throw new ArgumentException("A failure result is required.", nameof(result));
        if (Status is not (RepairTransactionStatus.Preparing or RepairTransactionStatus.Executing))
            throw new InvalidOperationException("Only a preparing or executing repair can fail.");
        Result = result;
        Status = RepairTransactionStatus.Failed;
        CompletedAtUtc = completedAtUtc;
    }

    public void MarkRolledBack(RepairResult result, DateTimeOffset completedAtUtc)
    {
        if (Status != RepairTransactionStatus.Failed) throw new InvalidOperationException("Only a failed repair can be rolled back.");
        Result = result;
        Status = result.RollbackSucceeded ? RepairTransactionStatus.RolledBack : RepairTransactionStatus.Failed;
        CompletedAtUtc = completedAtUtc;
    }

    public void Cancel(DateTimeOffset completedAtUtc)
    {
        if (Status is RepairTransactionStatus.Succeeded or RepairTransactionStatus.RolledBack)
            throw new InvalidOperationException("A completed repair cannot be cancelled.");
        Status = RepairTransactionStatus.Cancelled;
        CompletedAtUtc = completedAtUtc;
    }

    private void Transition(RepairTransactionStatus expected, RepairTransactionStatus next)
    {
        if (Status != expected) throw new InvalidOperationException($"Expected {expected} state, but transaction is {Status}.");
        Status = next;
    }
}
