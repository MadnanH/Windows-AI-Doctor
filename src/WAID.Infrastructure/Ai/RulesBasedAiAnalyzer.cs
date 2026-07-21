using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
namespace WAID.Infrastructure.Ai;
public sealed class RulesBasedAiAnalyzer : IAiAnalyzer
{
    public string ProviderName=>"Local rules";
    public Task<AiAnalysis> AnalyzeAsync(IReadOnlyCollection<DiagnosticFinding> findings,CancellationToken token) { token.ThrowIfCancellationRequested(); var critical=findings.Count(x=>x.Severity==DiagnosticSeverity.Critical); var recommendations=findings.Where(x=>x.RecommendedRepairId is not null).Select(x=>$"Review: {x.Title}").Distinct().ToArray(); return Task.FromResult(new AiAnalysis(findings.Count==0?"No issues were detected.":$"Detected {findings.Count} issue(s), including {critical} critical issue(s).",recommendations,1)); }
}
