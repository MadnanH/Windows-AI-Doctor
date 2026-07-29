using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.KnowledgeBase;

namespace WAID.Diagnosis;

public sealed record RuleMatch(KnowledgeRule Rule, IReadOnlyCollection<DiagnosticFinding> Evidence);

public sealed record RepairRecommendation(
    string? RepairId, string Title, int Priority, DiagnosticSeverity Severity, int Confidence);

public enum ExplanationSupportState { Supported, Unsupported }
public enum ConfidenceBand { Low, Moderate, High, VeryHigh }
public sealed record ExplanationEvidenceLink(Guid FindingId, string Code, string Title, double Weight, bool Supports, string SourceReference);
public sealed record DiagnosticAlternative(string RuleId, string Cause, int Confidence, string WhyLessLikely);
public sealed record ConfidenceCalibration(int Score, ConfidenceBand Band, double SupportingWeight, double CounterWeight, int SupportingCount, int CounterCount, string CalibrationVersion, string Interpretation);
public sealed record DiagnosisExplanation(
    ExplanationSupportState State, string ProblemStatement, string Rationale,
    IReadOnlyList<ExplanationEvidenceLink> Evidence, IReadOnlyList<DiagnosticAlternative> Alternatives,
    IReadOnlyList<ExplanationEvidenceLink> CounterEvidence, string Impact, string Urgency,
    string NextStep, string ChangeOverTime, ConfidenceCalibration Calibration,
    string RuleId, string RuleVersion, string ExplanationVersion);

public sealed record RootCause(
    string Id, string LikelyCause, IReadOnlyCollection<DiagnosticFinding> SupportingEvidence,
    int Confidence, DiagnosticSeverity Severity, int RepairPriority,
    RepairRecommendation Recommendation, string Explanation)
{
    public DiagnosisExplanation ExplanationDetail { get; init; } = ExplanationEngine.Unsupported("Explanation metadata is unavailable for this legacy diagnosis.");
}

public sealed record AIReport(
    DateTimeOffset GeneratedAtUtc,
    string Summary,
    HealthScore Health,
    IReadOnlyCollection<RootCause> RootCauses,
    IReadOnlyCollection<CorrelatedEvidence> Correlations,
    IReadOnlyCollection<DiagnosticFinding> Findings)
{
    public IReadOnlyList<DiagnosisExplanation> Explanations { get; init; } = [];
    public string ExplanationSchemaVersion { get; init; } = ExplanationEngine.SchemaVersion;
}
