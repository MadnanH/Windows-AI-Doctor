using WAID.EventAnalysis;

namespace WAID.Diagnosis;

public sealed class RootCauseAnalyzer(
    ConfidenceEngine confidenceEngine,
    RecommendationEngine recommendationEngine,
    ExplanationEngine explanationEngine)
{
    public IReadOnlyList<RootCause> Analyze(
        IReadOnlyCollection<RuleMatch> matches,
        IReadOnlyCollection<CorrelatedEvidence> correlations) =>
        matches.Select(match =>
        {
            var confidence = confidenceEngine.Calculate(match, correlations);
            return new RootCause(match.Rule.Id, match.Rule.Cause, match.Evidence, confidence,
                match.Rule.Severity, match.Rule.RepairPriority,
                recommendationEngine.Create(match, confidence), explanationEngine.Explain(match, confidence));
        }).OrderBy(cause => cause.RepairPriority).ThenByDescending(cause => cause.Confidence).ToArray();
}
