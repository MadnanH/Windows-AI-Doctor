namespace WAID.Diagnosis;

public sealed class RecommendationEngine
{
    public RepairRecommendation Create(RuleMatch match, int confidence) =>
        new(match.Rule.RepairId,
            match.Rule.RepairId is null ? "Review evidence and obtain specialist guidance" : $"Run {FriendlyName(match.Rule.RepairId)}",
            match.Rule.RepairPriority, match.Rule.Severity, confidence);

    private static string FriendlyName(string id) => id switch
    {
        "waid.dism" => "DISM component store repair",
        "waid.sfc" => "System File Checker repair",
        "waid.windows-update-reset" => "Windows Update reset",
        "waid.dns-reset" => "DNS reset",
        "waid.winsock-reset" => "Winsock reset",
        "waid.tcpip-reset" => "TCP/IP reset",
        _ => id
    };
}
