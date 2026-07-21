using WAID.Domain.Diagnostics;

namespace WAID.EventAnalysis;

public sealed class CorrelationScanner(EventCorrelationEngine engine)
{
    public IReadOnlyList<CorrelatedEvidence> Scan(IReadOnlyCollection<DiagnosticFinding> findings) =>
        engine.Correlate(findings);
}
