namespace WAID.Diagnosis;

public sealed class ExplanationEngine
{
    public string Explain(RuleMatch match, int confidence)
    {
        var evidence = string.Join(", ", match.Evidence.Select(item => item.Title).Distinct(StringComparer.OrdinalIgnoreCase));
        return $"{match.Rule.Explanation} Evidence: {evidence}. Confidence: {confidence}%.";
    }
}
