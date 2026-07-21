namespace WAID.Domain.Repairs;

public enum RepairResourceKind
{
    File,
    Directory,
    RegistryKey
}

public sealed record RepairResource(RepairResourceKind Kind, string Path)
{
    public RepairResource Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
            throw new InvalidOperationException("A repair resource path is required.");

        return this;
    }
}

public sealed record RepairPlan(IReadOnlyCollection<RepairResource> Resources, string Description)
{
    public static RepairPlan NoResourceChanges(string description) => new([], description);
}
