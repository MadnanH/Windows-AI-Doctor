namespace WAID.Domain.Diagnostics;

public sealed record DiagnosticFinding
{
    public DiagnosticFinding(string scannerId, string code, string title, string description,
        DiagnosticSeverity severity, string? recommendedRepairId = null, IReadOnlyDictionary<string, string>? evidence = null)
    {
        ScannerId = Require(scannerId, nameof(scannerId));
        Code = Require(code, nameof(code));
        Title = Require(title, nameof(title));
        Description = Require(description, nameof(description));
        Severity = severity;
        RecommendedRepairId = recommendedRepairId;
        Evidence = evidence ?? new Dictionary<string, string>();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string ScannerId { get; }
    public string Code { get; }
    public string Title { get; }
    public string Description { get; }
    public DiagnosticSeverity Severity { get; }
    public string? RecommendedRepairId { get; }
    public IReadOnlyDictionary<string, string> Evidence { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be empty.", name) : value.Trim();
}

