using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;

namespace WAID.Diagnosis;

public sealed class ExplanationEngine
{
    public const string SchemaVersion = "1.0";
    public const string RuleVersion = "knowledge-v2";
    public const string CalibrationVersion = "deterministic-v1";

    public string Explain(RuleMatch match, int confidence) => Build(match, confidence, [], [], [], null).Rationale;

    public DiagnosisExplanation Build(RuleMatch match, int confidence, IReadOnlyCollection<RuleMatch> allMatches, IReadOnlyCollection<DiagnosticFinding> allFindings, IReadOnlyCollection<CorrelatedEvidence> correlations, AIReport? previous)
    {
        ArgumentNullException.ThrowIfNull(match);
        var counter = FindCounterEvidence(match, allFindings);
        var evidence = match.Evidence.OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id)
            .Select(item => Link(item, Weight(item, match, correlations), true)).ToArray();
        var counterLinks = counter.OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).Select(item => Link(item, .8, false)).ToArray();
        var alternatives = allMatches.Where(other => !string.Equals(other.Rule.Id, match.Rule.Id, StringComparison.OrdinalIgnoreCase)).OrderByDescending(other => other.Rule.BaseConfidence).ThenBy(other => other.Rule.Id, StringComparer.Ordinal)
            .Take(3).Select(other => new DiagnosticAlternative(other.Rule.Id, other.Rule.Cause, Math.Clamp(other.Rule.BaseConfidence, 1, 99), $"Its evidence matches fewer or lower-weight signals than {match.Rule.Cause}.")).ToArray();
        var supportingWeight = Math.Round(evidence.Sum(item => item.Weight), 2); var counterWeight = Math.Round(counterLinks.Sum(item => item.Weight), 2);
        var calibration = Calibrate(confidence, supportingWeight, counterWeight, evidence.Length, counterLinks.Length);
        var evidenceNames = string.Join(", ", evidence.Select(item => item.Title).Distinct(StringComparer.OrdinalIgnoreCase));
        var rationale = $"{match.Rule.Explanation} Supporting evidence: {(evidenceNames.Length == 0 ? "none" : evidenceNames)}. Confidence: {confidence}%.";
        var prior = previous?.RootCauses.FirstOrDefault(item => string.Equals(item.Id, match.Rule.Id, StringComparison.OrdinalIgnoreCase));
        var change = prior is null ? "No comparable earlier diagnosis is available." : prior.Confidence == confidence ? "Confidence is unchanged from the previous diagnosis." : $"Confidence changed from {prior.Confidence}% to {confidence}% because the current evidence set changed.";
        return new(ExplanationSupportState.Supported, match.Rule.Cause, rationale, evidence, alternatives, counterLinks, Impact(match.Rule.Severity), Urgency(match.Rule.Severity), NextStep(match), change, calibration, match.Rule.Id, RuleVersion, SchemaVersion);
    }

    public static DiagnosisExplanation Unsupported(string reason, DiagnosticFinding? finding = null) => new(ExplanationSupportState.Unsupported, finding?.Title ?? "Unsupported diagnosis", reason, finding is null ? [] : [Link(finding, 0, true)], [], [], "Impact cannot be determined from the available rule set.", "Review when more evidence is available.", "Inspect the cited finding and collect additional supported evidence; no repair is recommended.", "No supported historical comparison is available.", Calibrate(0, 0, 0, finding is null ? 0 : 1, 0), "unsupported", RuleVersion, SchemaVersion);

    public static ConfidenceCalibration Calibrate(int score, double support, double counter, int supportCount, int counterCount)
    {
        score = Math.Clamp(score, 0, 99); var band = score switch { >= 90 => ConfidenceBand.VeryHigh, >= 75 => ConfidenceBand.High, >= 50 => ConfidenceBand.Moderate, _ => ConfidenceBand.Low };
        var interpretation = band switch { ConfidenceBand.VeryHigh => "Multiple strong signals align; still verify before repair.", ConfidenceBand.High => "Evidence strongly supports this cause, with some uncertainty.", ConfidenceBand.Moderate => "Evidence is mixed or incomplete; gather more evidence.", _ => "Evidence is insufficient for a reliable machine-specific conclusion." };
        return new(score, band, support, counter, supportCount, counterCount, CalibrationVersion, interpretation);
    }

    private static IReadOnlyList<DiagnosticFinding> FindCounterEvidence(RuleMatch match, IReadOnlyCollection<DiagnosticFinding> findings) => findings.Where(item => match.Rule.RequiredCodes.Concat(match.Rule.AnyCodes).Any(code => item.Code.Equals($"{code}_HEALTHY", StringComparison.OrdinalIgnoreCase) || item.Code.Equals($"NO_{code}", StringComparison.OrdinalIgnoreCase))).DistinctBy(item => item.Id).ToArray();
    private static double Weight(DiagnosticFinding finding, RuleMatch match, IReadOnlyCollection<CorrelatedEvidence> correlations) { var weight = match.Rule.RequiredCodes.Contains(finding.Code, StringComparer.OrdinalIgnoreCase) ? 1d : .65d; if (correlations.Any(c => c.Findings.Any(f => f.Id == finding.Id))) weight += .25; return Math.Round(weight, 2); }
    private static ExplanationEvidenceLink Link(DiagnosticFinding finding, double weight, bool supports) => new(finding.Id, finding.Code, finding.Title, weight, supports, finding.ScannerId);
    private static string Impact(DiagnosticSeverity severity) => severity switch { DiagnosticSeverity.Critical => "The issue can cause data loss, security exposure, or system instability.", DiagnosticSeverity.Warning => "The issue can reduce reliability or prevent a Windows feature from working correctly.", _ => "The issue currently has limited observed impact." };
    private static string Urgency(DiagnosticSeverity severity) => severity switch { DiagnosticSeverity.Critical => "Address promptly after protecting important data.", DiagnosticSeverity.Warning => "Review soon; immediate interruption is usually unnecessary.", _ => "Monitor and review during routine maintenance." };
    private static string NextStep(RuleMatch match) => match.Rule.RepairId is null ? "Review the cited evidence and collect additional diagnostics before changing the system." : $"Review the {match.Rule.RepairId} repair preview, safeguards, and approval requirements; do not execute from this explanation.";
}

public static class ExplanationRenderer
{
    public static string RenderPlainText(DiagnosisExplanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        var alternatives = explanation.Alternatives.Count == 0 ? "None identified." : string.Join("; ", explanation.Alternatives.Select(item => $"{item.Cause} ({item.Confidence}%)"));
        var counter = explanation.CounterEvidence.Count == 0 ? "None observed." : string.Join("; ", explanation.CounterEvidence.Select(item => item.Title));
        return $"Problem: {explanation.ProblemStatement}\nState: {explanation.State}\nRationale: {explanation.Rationale}\nConfidence: {explanation.Calibration.Score}% ({explanation.Calibration.Band}) - {explanation.Calibration.Interpretation}\nAlternatives: {alternatives}\nCounter-evidence: {counter}\nImpact: {explanation.Impact}\nUrgency: {explanation.Urgency}\nNext step: {explanation.NextStep}\nChange over time: {explanation.ChangeOverTime}";
    }
}