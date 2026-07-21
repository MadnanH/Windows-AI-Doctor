using System.Text.Json;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Diagnostics;

public abstract class PowerShellDiagnosticScanner(IPowerShellRunner powerShell) : ISystemScanner
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    protected abstract string Script { get; }

    public async Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var result = await powerShell.RunAsync(Script, new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
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

    private sealed record ScannerRecord(
        string? Code, string? Title, string? Description, string? Severity,
        string? RepairId, Dictionary<string, string>? Evidence);
}
