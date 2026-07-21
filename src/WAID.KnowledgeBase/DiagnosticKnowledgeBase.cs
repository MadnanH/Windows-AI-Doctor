using System.Reflection;
using System.Text.Json;
using WAID.Domain.Diagnostics;

namespace WAID.KnowledgeBase;

public sealed record KnowledgeRule(
    string Id, string Cause, string Explanation, DiagnosticSeverity Severity,
    int RepairPriority, string? RepairId, int BaseConfidence,
    IReadOnlyCollection<string> RequiredCodes, IReadOnlyCollection<string> AnyCodes);

public sealed record KnowledgeReference(
    string Category, string Code, string Meaning, DiagnosticSeverity Severity, string? RepairId);

public sealed class DiagnosticKnowledgeBase
{
    private readonly IReadOnlyList<KnowledgeRule> _rules;
    private readonly IReadOnlyList<KnowledgeReference> _references;
    public DiagnosticKnowledgeBase() : this(LoadEmbedded<KnowledgeRule>("diagnostic-knowledge.json"), LoadEmbedded<KnowledgeReference>("reference-knowledge.json")) { }
    public DiagnosticKnowledgeBase(IEnumerable<KnowledgeRule> rules) : this(rules, []) { }
    public DiagnosticKnowledgeBase(IEnumerable<KnowledgeRule> rules, IEnumerable<KnowledgeReference> references)
    {
        _rules = rules?.ToArray() ?? throw new ArgumentNullException(nameof(rules));
        _references = references?.ToArray() ?? throw new ArgumentNullException(nameof(references));
        if (_rules.Select(rule => rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _rules.Count)
            throw new InvalidOperationException("Knowledge rule ids must be unique.");
        if (_references.Select(item => $"{item.Category}:{item.Code}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != _references.Count)
            throw new InvalidOperationException("Knowledge reference keys must be unique within a category.");
    }
    public IReadOnlyList<KnowledgeRule> Rules => _rules;
    public IReadOnlyList<KnowledgeReference> References => _references;
    public KnowledgeRule? Find(string id) => _rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));
    public KnowledgeReference? FindReference(string category, string code) => _references.FirstOrDefault(item =>
        string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<T> LoadEmbedded<T>(string fileName)
    {
        var assembly = typeof(DiagnosticKnowledgeBase).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Knowledge base resource is missing.");
        return JsonSerializer.Deserialize<T[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
            ?? throw new InvalidOperationException("Knowledge base is invalid.");
    }
}
