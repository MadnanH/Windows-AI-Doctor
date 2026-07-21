using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;
using WAID.Health;

namespace WAID.Diagnosis;

public sealed class AIReportBuilder(TimeProvider timeProvider)
{
    public AIReport Build(
        IReadOnlyCollection<DiagnosticFinding> findings,
        HealthScore health,
        IReadOnlyCollection<RootCause> causes,
        IReadOnlyCollection<CorrelatedEvidence> correlations)
    {
        var summary = causes.Count == 0
            ? findings.Count == 0 ? "No health issues were detected." : $"Found {findings.Count} item(s), but no known root-cause pattern matched."
            : $"Identified {causes.Count} likely root cause(s). The highest-priority cause is {causes.First().LikelyCause.ToLowerInvariant()}.";
        return new(timeProvider.GetUtcNow(), summary, health, causes, correlations, findings);
    }
}
