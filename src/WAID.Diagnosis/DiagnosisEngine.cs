using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.KnowledgeBase;

namespace WAID.Diagnosis;

public sealed class DiagnosisEngine(
    DiagnosticKnowledgeBase knowledgeBase,
    RuleEngine ruleEngine,
    EventCorrelationEngine correlationEngine,
    RootCauseAnalyzer rootCauseAnalyzer,
    HealthScoreEngine healthScoreEngine,
    AIReportBuilder reportBuilder)
{
    public Task<AIReport> DiagnoseAsync(IReadOnlyCollection<DiagnosticFinding> findings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var correlations = correlationEngine.Correlate(findings);
        var matches = ruleEngine.Evaluate(findings, knowledgeBase.Rules);
        var causes = rootCauseAnalyzer.Analyze(matches, correlations);
        var health = healthScoreEngine.Calculate(findings);
        return Task.FromResult(reportBuilder.Build(findings, health, causes, correlations));
    }
}
