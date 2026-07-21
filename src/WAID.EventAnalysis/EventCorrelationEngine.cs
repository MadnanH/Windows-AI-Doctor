using WAID.Domain.Diagnostics;

namespace WAID.EventAnalysis;

public sealed record CorrelatedEvidence(
    string CorrelationId,
    string Summary,
    IReadOnlyCollection<DiagnosticFinding> Findings,
    int Strength);

public sealed class EventCorrelationEngine
{
    public IReadOnlyList<CorrelatedEvidence> Correlate(IReadOnlyCollection<DiagnosticFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var codes = findings.ToLookup(finding => finding.Code, StringComparer.OrdinalIgnoreCase);
        var correlations = new List<CorrelatedEvidence>();
        Add(correlations, "power-storage", "Unexpected shutdown correlates with storage failure evidence", 94,
            findings, codes, ["EVENT_41", "SMART_WARNING"], ["NTFS_ERROR"]);
        Add(correlations, "update-servicing", "Windows Update failures correlate with servicing corruption", 97,
            findings, codes, ["CBS_CORRUPTION"], ["UPDATE_EVENT_20", "UPDATE_EVENT_25", "WINDOWS_UPDATE_FAILURE"]);
        Add(correlations, "driver-bsod", "Driver errors correlate with blue-screen evidence", 91,
            findings, codes, ["DRIVER_ERROR", "BSOD_DUMP"], []);
        Add(correlations, "gpu-crash", "Graphics errors correlate with application instability", 88,
            findings, codes, ["GPU_ERROR"], ["APP_CRASH", "EVENT_4101", "BSOD_DUMP"]);
        Add(correlations, "network-dns", "Network configuration errors correlate with DNS failures", 86,
            findings, codes, ["NETWORK_CONFIG_ERROR", "DNS_FAILURE"], []);
        return correlations;
    }

    private static void Add(
        ICollection<CorrelatedEvidence> target, string id, string summary, int strength,
        IReadOnlyCollection<DiagnosticFinding> findings, ILookup<string, DiagnosticFinding> codes,
        IReadOnlyCollection<string> required, IReadOnlyCollection<string> any)
    {
        if (!required.All(code => codes.Contains(code)) || (any.Count > 0 && !any.Any(code => codes.Contains(code)))) return;
        var matchedCodes = required.Concat(any.Where(codes.Contains)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Add(new(id, summary, findings.Where(finding => matchedCodes.Contains(finding.Code)).ToArray(), strength));
    }
}
