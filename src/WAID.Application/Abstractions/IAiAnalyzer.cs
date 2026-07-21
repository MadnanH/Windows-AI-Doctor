using WAID.Domain.Diagnostics;
namespace WAID.Application.Abstractions;
public interface IAiAnalyzer { string ProviderName { get; } Task<AiAnalysis> AnalyzeAsync(IReadOnlyCollection<DiagnosticFinding> findings, CancellationToken cancellationToken); }
public sealed record AiAnalysis(string Summary, IReadOnlyCollection<string> Recommendations, double Confidence);
