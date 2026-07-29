using WAID.EventAnalysis;
using WAID.Domain.Diagnostics;

namespace WAID.Diagnosis;

public sealed class RootCauseAnalyzer(
    ConfidenceEngine confidenceEngine,
    RecommendationEngine recommendationEngine,
    ExplanationEngine explanationEngine)
{
    public IReadOnlyList<RootCause> Analyze(
        IReadOnlyCollection<RuleMatch> matches,
        IReadOnlyCollection<CorrelatedEvidence> correlations,
        IReadOnlyCollection<DiagnosticFinding>? allFindings = null,
        AIReport? previous = null) =>
        matches.Select(match =>
        {
            var confidence = confidenceEngine.Calculate(match, correlations, allFindings ?? match.Evidence);
            return new RootCause(match.Rule.Id, match.Rule.Cause, match.Evidence, confidence,
                match.Rule.Severity, match.Rule.RepairPriority,
                recommendationEngine.Create(match, confidence), explanationEngine.Explain(match, confidence))
            {
                ExplanationDetail = explanationEngine.Build(match, confidence, matches, allFindings ?? match.Evidence, correlations, previous)
            };
        }).OrderBy(cause => cause.RepairPriority).ThenByDescending(cause => cause.Confidence).ToArray();
}
