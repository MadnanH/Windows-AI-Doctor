using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.KnowledgeBase;

namespace WAID.Diagnosis;

public sealed record RuleMatch(KnowledgeRule Rule, IReadOnlyCollection<DiagnosticFinding> Evidence);

public sealed record RepairRecommendation(
    string? RepairId, string Title, int Priority, DiagnosticSeverity Severity, int Confidence);

public sealed record RootCause(
    string Id, string LikelyCause, IReadOnlyCollection<DiagnosticFinding> SupportingEvidence,
    int Confidence, DiagnosticSeverity Severity, int RepairPriority,
    RepairRecommendation Recommendation, string Explanation);

public sealed record AIReport(
    DateTimeOffset GeneratedAtUtc,
    string Summary,
    HealthScore Health,
    IReadOnlyCollection<RootCause> RootCauses,
    IReadOnlyCollection<CorrelatedEvidence> Correlations,
    IReadOnlyCollection<DiagnosticFinding> Findings);
