using WAID.Domain.Diagnostics;

namespace WAID.Health;

public enum HealthCategory { Hardware, Windows, Drivers, Security, Performance, Storage, Network }

public sealed record HealthScore(
    int Hardware, int Windows, int Drivers, int Security,
    int Performance, int Storage, int Network, int Overall)
{
    public int this[HealthCategory category] => category switch
    {
        HealthCategory.Hardware => Hardware, HealthCategory.Windows => Windows,
        HealthCategory.Drivers => Drivers, HealthCategory.Security => Security,
        HealthCategory.Performance => Performance, HealthCategory.Storage => Storage,
        HealthCategory.Network => Network, _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

public sealed class HealthScoreEngine
{
    private static readonly IReadOnlyDictionary<HealthCategory, double> Weights =
        new Dictionary<HealthCategory, double>
        {
            [HealthCategory.Hardware] = .18, [HealthCategory.Windows] = .18,
            [HealthCategory.Drivers] = .12, [HealthCategory.Security] = .17,
            [HealthCategory.Performance] = .10, [HealthCategory.Storage] = .15,
            [HealthCategory.Network] = .10
        };

    public HealthScore Calculate(IReadOnlyCollection<DiagnosticFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var scores = Enum.GetValues<HealthCategory>().ToDictionary(category => category, _ => 100);
        foreach (var finding in findings)
        {
            var category = Categorize(finding);
            scores[category] = Math.Max(0, scores[category] - Penalty(finding.Severity));
        }
        var overall = (int)Math.Round(scores.Sum(item => item.Value * Weights[item.Key]), MidpointRounding.AwayFromZero);
        return new(scores[HealthCategory.Hardware], scores[HealthCategory.Windows], scores[HealthCategory.Drivers],
            scores[HealthCategory.Security], scores[HealthCategory.Performance], scores[HealthCategory.Storage],
            scores[HealthCategory.Network], Math.Clamp(overall, 0, 100));
    }

    public static HealthCategory Categorize(DiagnosticFinding finding)
    {
        if (finding.Evidence.TryGetValue("category", out var value) &&
            Enum.TryParse<HealthCategory>(value, true, out var category)) return category;
        var id = finding.ScannerId.ToLowerInvariant();
        if (id.Contains("driver")) return HealthCategory.Drivers;
        if (id.Contains("defender") || id.Contains("security")) return HealthCategory.Security;
        if (id.Contains("network") || id.Contains("dns")) return HealthCategory.Network;
        if (id.Contains("storage") || id.Contains("smart") || id.Contains("disk")) return HealthCategory.Storage;
        if (id.Contains("cpu") || id.Contains("memory") || id.Contains("gpu")) return HealthCategory.Performance;
        if (id.Contains("hardware") || id.Contains("bsod")) return HealthCategory.Hardware;
        return HealthCategory.Windows;
    }

    private static int Penalty(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Information => 3, DiagnosticSeverity.Warning => 12,
        DiagnosticSeverity.Critical => 30, _ => 0
    };
}
