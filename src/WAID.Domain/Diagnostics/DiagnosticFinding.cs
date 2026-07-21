namespace WAID.Domain.Diagnostics;

public sealed record DiagnosticFinding
{
    public DiagnosticFinding(string scannerId, string code, string title, string description,
        DiagnosticSeverity severity, string? recommendedRepairId = null,
        IReadOnlyDictionary<string, string>? evidence = null, Guid? id = null)
    {
        ScannerId = Require(scannerId, nameof(scannerId));
        Code = Require(code, nameof(code));
        Title = Require(title, nameof(title));
        Description = Require(description, nameof(description));
        Severity = severity;
        RecommendedRepairId = recommendedRepairId;
        Evidence = evidence ?? new Dictionary<string, string>();
        Id = id switch
        {
            null => Guid.NewGuid(),
            { } value when value != Guid.Empty => value,
            _ => throw new ArgumentException("A finding id cannot be empty.", nameof(id))
        };
    }

    public Guid Id { get; }
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
