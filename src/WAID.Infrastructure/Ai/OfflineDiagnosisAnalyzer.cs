using WAID.Application.Abstractions;
using WAID.Diagnosis;
using WAID.Domain.Diagnostics;

namespace WAID.Infrastructure.Ai;

public sealed class OfflineDiagnosisAnalyzer(DiagnosisEngine diagnosisEngine) : IAiAnalyzer
{
    public string ProviderName => "WAID offline diagnostic engine";

    public async Task<AiAnalysis> AnalyzeAsync(
        IReadOnlyCollection<DiagnosticFinding> findings,
        CancellationToken cancellationToken)
    {
        var report = await diagnosisEngine.DiagnoseAsync(findings, cancellationToken).ConfigureAwait(false);
        var recommendations = report.RootCauses.Select(cause => cause.Recommendation.Title).Distinct().ToArray();
        var confidence = report.RootCauses.Count == 0 ? 1d : report.RootCauses.Average(cause => cause.Confidence) / 100d;
        return new(report.Summary, recommendations, confidence);
    }
}
