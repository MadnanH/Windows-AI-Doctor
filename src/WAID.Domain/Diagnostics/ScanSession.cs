namespace WAID.Domain.Diagnostics;

public sealed class ScanSession
{
    private readonly List<DiagnosticFinding> _findings = [];

    public ScanSession(Guid id, DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("A scan id is required.", nameof(id));
        Id = id;
        StartedAtUtc = startedAtUtc;
    }

    public Guid Id { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public bool IsCompleted => CompletedAtUtc.HasValue;
    public IReadOnlyList<DiagnosticFinding> Findings => _findings.AsReadOnly();

    public void AddFindings(IEnumerable<DiagnosticFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (IsCompleted) throw new InvalidOperationException("A completed scan cannot be changed.");
        _findings.AddRange(findings);
    }

    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (IsCompleted) throw new InvalidOperationException("The scan is already complete.");
        if (completedAtUtc < StartedAtUtc) throw new ArgumentOutOfRangeException(nameof(completedAtUtc));
        CompletedAtUtc = completedAtUtc;
    }
}

