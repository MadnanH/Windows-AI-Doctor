using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WAID.Domain.Diagnostics;

namespace WAID.KnowledgeBase;

public sealed record KnowledgeRule(string Id, string Cause, string Explanation, DiagnosticSeverity Severity,
    int RepairPriority, string? RepairId, int BaseConfidence, IReadOnlyCollection<string> RequiredCodes, IReadOnlyCollection<string> AnyCodes);
public sealed record KnowledgeReference(string Category, string Code, string Meaning, DiagnosticSeverity Severity, string? RepairId);
public sealed record KnowledgeDocument<T>(int SchemaVersion, IReadOnlyList<T> Entries);

public static class KnowledgeBaseSchema
{
    public const int CurrentVersion = 2;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static KnowledgeDocument<T> Read<T>(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var migrated = document.RootElement.Deserialize<T[]>(Options) ?? throw new InvalidOperationException("Legacy knowledge entries are invalid.");
            return new(CurrentVersion, migrated);
        }
        var version = document.RootElement.TryGetProperty("schemaVersion", out var value) ? value.GetInt32() : 0;
        if (version is < 1 or > CurrentVersion) throw new NotSupportedException($"Knowledge schema version {version} is not supported; expected 1-{CurrentVersion}.");
        var entries = document.RootElement.GetProperty("entries").Deserialize<T[]>(Options) ?? throw new InvalidOperationException("Knowledge entries are invalid.");
        return new(CurrentVersion, entries);
    }
}

public sealed class DiagnosticKnowledgeBase
{
    private static readonly HashSet<string> RepairIds = new(StringComparer.OrdinalIgnoreCase)
        { "waid.dism", "waid.sfc", "waid.windows-update-reset", "waid.dns-reset", "waid.winsock-reset", "waid.tcpip-reset" };
    private readonly IReadOnlyList<KnowledgeRule> _rules;
    private readonly IReadOnlyList<KnowledgeReference> _references;
    public DiagnosticKnowledgeBase() : this(LoadEmbedded<KnowledgeRule>("diagnostic-knowledge.json"), LoadEmbedded<KnowledgeReference>("reference-knowledge.json")) { }
    public DiagnosticKnowledgeBase(IEnumerable<KnowledgeRule> rules) : this(rules, []) { }
    public DiagnosticKnowledgeBase(IEnumerable<KnowledgeRule> rules, IEnumerable<KnowledgeReference> references)
    {
        _rules = rules?.ToArray() ?? throw new ArgumentNullException(nameof(rules));
        _references = references?.ToArray() ?? throw new ArgumentNullException(nameof(references));
        Validate();
    }
    public IReadOnlyList<KnowledgeRule> Rules => _rules;
    public IReadOnlyList<KnowledgeReference> References => _references;
    public KnowledgeRule? Find(string id) => _rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));
    public KnowledgeReference? FindReference(string category, string code) => _references.FirstOrDefault(item =>
        string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

    private void Validate()
    {
        if (_rules.Any(r => string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Cause) || string.IsNullOrWhiteSpace(r.Explanation) || r.BaseConfidence is < 0 or > 100 || r.RepairPriority < 1 || r.RequiredCodes is null || r.AnyCodes is null))
            throw new InvalidOperationException("Knowledge rules contain missing or invalid required fields.");
        if (_rules.Select(rule => rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _rules.Count)
            throw new InvalidOperationException("Knowledge rule ids must be unique.");
        if (_references.Any(r => string.IsNullOrWhiteSpace(r.Category) || string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Meaning)))
            throw new InvalidOperationException("Knowledge references contain missing required fields.");
        if (_references.Select(item => $"{item.Category}:{item.Code}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != _references.Count)
            throw new InvalidOperationException("Knowledge reference keys must be unique within a category.");
        var invalidMappings = _rules.Select(r => r.RepairId).Concat(_references.Select(r => r.RepairId)).Where(id => id is not null && !RepairIds.Contains(id)).Distinct().ToArray();
        if (invalidMappings.Length > 0) throw new InvalidOperationException($"Unknown repair mapping(s): {string.Join(", ", invalidMappings)}.");
    }

    private static IReadOnlyList<T> LoadEmbedded<T>(string fileName)
    {
        var assembly = typeof(DiagnosticKnowledgeBase).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Knowledge base resource is missing.");
        return KnowledgeBaseSchema.Read<T>(stream).Entries;
    }
}
