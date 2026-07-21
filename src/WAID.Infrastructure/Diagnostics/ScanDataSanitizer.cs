using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;

namespace WAID.Infrastructure.Diagnostics;

public sealed class ScanDataSanitizer : IScanDataSanitizer
{
    public ScannerOutput Sanitize(ScannerMetadata metadata, ScannerOutput output)
    {
        var observations = output.Observations.Select(item => item with
        {
            Value = ReportRedactor.IsSensitiveName(item.Key) ? "[REDACTED]" : ReportRedactor.RedactText(item.Value),
            SourceReference = ReportRedactor.RedactText(item.SourceReference),
            Attributes = Sanitize(item.Attributes)
        }).ToArray();
        var findings = output.Findings.Select(item => new DiagnosticFinding(item.Id, item.ScannerId, item.Code,
            ReportRedactor.RedactText(item.Title), ReportRedactor.RedactText(item.Description), item.Severity, item.RecommendedRepairId, Sanitize(item.Evidence))).ToArray();
        return new(observations, findings);
    }

    private static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string>? values) =>
        values is null ? new Dictionary<string, string>() : values.ToDictionary(item => item.Key,
            item => ReportRedactor.IsSensitiveName(item.Key) ? "[REDACTED]" : ReportRedactor.RedactText(item.Value), StringComparer.OrdinalIgnoreCase);
}
