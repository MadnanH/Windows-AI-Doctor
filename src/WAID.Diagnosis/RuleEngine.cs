using WAID.Domain.Diagnostics;
using WAID.KnowledgeBase;

namespace WAID.Diagnosis;

public sealed class RuleEngine
{
    public IReadOnlyList<RuleMatch> Evaluate(
        IReadOnlyCollection<DiagnosticFinding> findings,
        IReadOnlyCollection<KnowledgeRule> rules)
    {
        var codes = findings.ToLookup(finding => finding.Code, StringComparer.OrdinalIgnoreCase);
        var matches = new List<RuleMatch>();
        foreach (var rule in rules)
        {
            if (!rule.RequiredCodes.All(codes.Contains)) continue;
            if (rule.AnyCodes.Count > 0 && !rule.AnyCodes.Any(codes.Contains)) continue;
            var relevant = rule.RequiredCodes.Concat(rule.AnyCodes.Where(codes.Contains))
                .SelectMany(code => codes[code]).DistinctBy(finding => finding.Id).ToArray();
            matches.Add(new(rule, relevant));
        }
        return matches;
    }
}
