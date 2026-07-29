using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.KnowledgeBase;

namespace WAID.Diagnosis;

public sealed class DiagnosisEngine(
    DiagnosticKnowledgeBase knowledgeBase,
    RuleEngine ruleEngine,
    CorrelationScanner correlationScanner,
    RootCauseAnalyzer rootCauseAnalyzer,
    HealthScoreEngine healthScoreEngine,
    AIReportBuilder reportBuilder)
{
    public Task<AIReport> DiagnoseAsync(IReadOnlyCollection<DiagnosticFinding> findings, CancellationToken cancellationToken)
        => DiagnoseAsync(findings, null, cancellationToken);

    public Task<AIReport> DiagnoseAsync(IReadOnlyCollection<DiagnosticFinding> findings, AIReport? previous, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var correlations = correlationScanner.Scan(findings);
        var matches = ruleEngine.Evaluate(findings, knowledgeBase.Rules);
        var causes = rootCauseAnalyzer.Analyze(matches, correlations, findings, previous);
        var health = healthScoreEngine.Calculate(findings);
        var supportedFindingIds = causes.SelectMany(cause => cause.SupportingEvidence).Select(finding => finding.Id).ToHashSet();
        var explanations = causes.Select(cause => cause.ExplanationDetail).Concat(findings.Where(finding => !supportedFindingIds.Contains(finding.Id)).Select(finding => ExplanationEngine.Unsupported("No deterministic knowledge rule currently supports a root-cause conclusion for this finding.", finding))).ToArray();
        return Task.FromResult(reportBuilder.Build(findings, health, causes, correlations) with { Explanations = explanations });
    }
}
