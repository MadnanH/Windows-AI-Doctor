namespace WAID.Domain.Repairs;

public sealed record RepairPolicy(
    SafetyLevel SafetyLevel,
    bool RequiresAdministrator = true,
    bool RequiresRestorePoint = true,
    bool RequiresBackup = true,
    bool SupportsRollback = true)
{
    public RepairPolicy Validate()
    {
        if (SupportsRollback && !RequiresBackup)
            throw new InvalidOperationException("Rollback requires a backup.");

        return this;
    }
}
