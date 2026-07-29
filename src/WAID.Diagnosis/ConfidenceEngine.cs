using WAID.EventAnalysis;

namespace WAID.Diagnosis;

public sealed class ConfidenceEngine
{
    public int Calculate(RuleMatch match, IReadOnlyCollection<CorrelatedEvidence> correlations)
    {
        ArgumentNullException.ThrowIfNull(match);
        var requiredBonus = Math.Max(0, match.Rule.RequiredCodes.Count - 1) * 3;
        var optionalMatches = match.Rule.AnyCodes.Count(code =>
            match.Evidence.Any(finding => string.Equals(finding.Code, code, StringComparison.OrdinalIgnoreCase)));
        var correlationBonus = correlations.Any(correlation =>
            correlation.Findings.Any(correlated => match.Evidence.Any(evidence => evidence.Id == correlated.Id))) ? 8 : 0;
        return Math.Clamp(match.Rule.BaseConfidence + requiredBonus + optionalMatches * 2 + correlationBonus, 1, 99);
    }

    public int Calculate(RuleMatch match, IReadOnlyCollection<CorrelatedEvidence> correlations, IReadOnlyCollection<WAID.Domain.Diagnostics.DiagnosticFinding> allFindings)
    {
        var score = Calculate(match, correlations);
        var contradictionCount = allFindings.Count(item => match.Rule.RequiredCodes.Concat(match.Rule.AnyCodes).Any(code => item.Code.Equals($"{code}_HEALTHY", StringComparison.OrdinalIgnoreCase) || item.Code.Equals($"NO_{code}", StringComparison.OrdinalIgnoreCase)));
        return Math.Clamp(score - contradictionCount * 12, 1, 99);
    }
}
