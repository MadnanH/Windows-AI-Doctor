using System.Reflection;
using System.Text.Json;
using WAID.Domain.Diagnostics;

namespace WAID.KnowledgeBase;

public sealed record KnowledgeRule(
    string Id, string Cause, string Explanation, DiagnosticSeverity Severity,
    int RepairPriority, string? RepairId, int BaseConfidence,
    IReadOnlyCollection<string> RequiredCodes, IReadOnlyCollection<string> AnyCodes);

public sealed class DiagnosticKnowledgeBase
{
    private readonly IReadOnlyList<KnowledgeRule> _rules;
    public DiagnosticKnowledgeBase() : this(LoadEmbedded()) { }
    public DiagnosticKnowledgeBase(IEnumerable<KnowledgeRule> rules)
    {
        _rules = rules?.ToArray() ?? throw new ArgumentNullException(nameof(rules));
        if (_rules.Select(rule => rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _rules.Count)
            throw new InvalidOperationException("Knowledge rule ids must be unique.");
    }
    public IReadOnlyList<KnowledgeRule> Rules => _rules;
    public KnowledgeRule? Find(string id) => _rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<KnowledgeRule> LoadEmbedded()
    {
        var assembly = typeof(DiagnosticKnowledgeBase).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("diagnostic-knowledge.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Knowledge base resource is missing.");
        return JsonSerializer.Deserialize<KnowledgeRule[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
            ?? throw new InvalidOperationException("Knowledge base is invalid.");
    }
}
