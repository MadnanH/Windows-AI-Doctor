using System.Text.Json;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Diagnostics;

public abstract class PowerShellDiagnosticScanner(IPowerShellRunner powerShell) : ISystemScanner
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public virtual ScannerMetadata Metadata => new(Id, DisplayName, $"Collects and evaluates {DisplayName} evidence from Windows.", Category(Id),
        new Version(1, 0, 0), [ScannerPrerequisites.Windows, ScannerPrerequisites.PowerShell], [], TimeSpan.FromSeconds(45));
    protected abstract string Script { get; }

    public async Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var result = await powerShell.RunAsync(Script, new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var errors = string.Join(Environment.NewLine, result.Errors);
            if (IsUnavailable(errors))
                return [new DiagnosticFinding(Id, "SCANNER_UNAVAILABLE", $"{DisplayName} is unavailable", "This version of Windows or this computer does not expose the required diagnostic interface. No result was inferred.", DiagnosticSeverity.Information, evidence: new Dictionary<string, string> { ["reason"] = "WindowsApiUnavailable" })];
            throw new InvalidOperationException(errors);
        }
        var json = string.Join(Environment.NewLine, result.Output).Trim();
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        using var document = JsonDocument.Parse(json);
        var records = document.RootElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<ScannerRecord[]>(json, options) ?? []
            : [JsonSerializer.Deserialize<ScannerRecord>(json, options) ?? new(null, null, null, null, null, null)];
        return records.Where(record => !string.IsNullOrWhiteSpace(record.Code)).Select(record => new DiagnosticFinding(
            Id, record.Code!, string.IsNullOrWhiteSpace(record.Title) ? record.Code! : record.Title,
            string.IsNullOrWhiteSpace(record.Description) ? "Windows reported a diagnostic condition." : record.Description,
            Enum.TryParse<DiagnosticSeverity>(record.Severity, true, out var severity) ? severity : DiagnosticSeverity.Warning,
            record.RepairId, record.Evidence ?? new Dictionary<string, string>())).ToArray();
    }

    private static bool IsUnavailable(string error) =>
        error.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Invalid namespace", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Invalid class", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("class not found", StringComparison.OrdinalIgnoreCase);

    private static string Category(string id) => id switch
    {
        "waid.defender" => "Security",
        "waid.network" => "Network",
        "waid.storage-health" or "waid.smart" => "Storage",
        "waid.drivers" or "waid.gpu" => "Drivers and hardware",
        "waid.memory" or "waid.cpu" or "waid.startup" => "Performance",
        "waid.bsod" or "waid.battery" => "Hardware",
        _ => "Windows"
    };

    private sealed record ScannerRecord(
        string? Code, string? Title, string? Description, string? Severity,
        string? RepairId, Dictionary<string, string>? Evidence);
}
