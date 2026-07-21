namespace WAID.Application.Abstractions;

public enum DriverFindingKind { Unsigned, Failed, Disabled, Duplicate, Orphaned, Incompatible, RecentlyChanged, ProblemCode, LoadFailure }

public sealed record DriverInventoryItem(
    string DeviceKey, string DeviceName, string DeviceClass, string? Manufacturer, string? Provider,
    string? DriverVersion, DateTimeOffset? DriverDateUtc, string? InfName, string? BinaryPath,
    bool IsPresent, bool IsEnabled, int ProblemCode, bool SignatureValid, string SignatureStatus,
    string? HardwareKey, string? Architecture);

public sealed record DriverEventEvidence(DateTimeOffset OccurredAtUtc, int EventId, string Provider, string DeviceKey, string Summary);

public sealed record DriverInventorySnapshot(DateTimeOffset CollectedAtUtc, string OperatingSystemArchitecture,
    IReadOnlyList<DriverInventoryItem> Drivers, IReadOnlyList<DriverEventEvidence> Events, bool IsAdministrator,
    IReadOnlyList<string> Limitations);

public sealed record DriverEvidence(string Signal, string Value, string SourceReference, DateTimeOffset ObservedAtUtc);

public sealed record DriverHealthFinding(string Id, string DeviceKey, string DeviceName, DriverFindingKind Kind,
    string Title, string Explanation, string Severity, double Confidence, IReadOnlyList<DriverEvidence> Evidence,
    string RecommendedAction, bool RequiresAdministrator);

public sealed record DriverChange(string DeviceKey, string DeviceName, string ChangeType, string PreviousValue,
    string CurrentValue, DateTimeOffset DetectedAtUtc);

public sealed record DriverHealthReport(Guid Id, DateTimeOffset GeneratedAtUtc, IReadOnlyList<DriverInventoryItem> Inventory,
    IReadOnlyList<DriverHealthFinding> Findings, IReadOnlyList<DriverChange> Changes, IReadOnlyList<string> Limitations);

public interface IDriverInventoryProvider
{
    Task<DriverInventorySnapshot> CollectAsync(CancellationToken cancellationToken);
}

public interface IDriverHealthRepository
{
    Task<DriverHealthReport?> GetLatestAsync(CancellationToken cancellationToken);
    Task SaveAsync(DriverInventorySnapshot snapshot, DriverHealthReport report, CancellationToken cancellationToken);
}

public interface IDriverConflictAnalyzer
{
    Task<DriverHealthReport> AnalyzeAsync(CancellationToken cancellationToken);
}
