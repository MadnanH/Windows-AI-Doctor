using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WAID.Application.Services;

namespace WAID.Infrastructure.Diagnostics;

public sealed class DiagnosticReportExporter(string outputDirectory) : IDiagnosticReportExporter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex SensitiveName = new("(?i)(password|token|secret|product.?key|cookie|serial|device.?id|authorization)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SensitiveValue = new("(?i)(password|token|secret|product.?key|authorization|cookie)\\s*[:=]\\s*[^\\s;,]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    public Task<string> ExportJsonAsync(DiagnosticReportData report, CancellationToken token) => WriteAsync("json", SerializeSafe(report), token);
    public Task<string> ExportHtmlAsync(DiagnosticReportData report, CancellationToken token) => WriteAsync("html", RenderHtml(report), token);
    public async Task<string> ExportPackageAsync(DiagnosticReportData report, CancellationToken token)
    {
        Directory.CreateDirectory(outputDirectory); var path = Path.Combine(outputDirectory, Name("zip"));
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        await WriteEntryAsync(archive, "report.json", SerializeSafe(report), token).ConfigureAwait(false);
        await WriteEntryAsync(archive, "report.html", RenderHtml(report), token).ConfigureAwait(false);
        return path;
    }
    private async Task<string> WriteAsync(string extension, string content, CancellationToken token) { Directory.CreateDirectory(outputDirectory); var path = Path.Combine(outputDirectory, Name(extension)); await File.WriteAllTextAsync(path, content, Encoding.UTF8, token).ConfigureAwait(false); return path; }
    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content, CancellationToken token) { var entry = archive.CreateEntry(name, CompressionLevel.Optimal); await using var stream = entry.Open(); await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: false); await writer.WriteAsync(content.AsMemory(), token).ConfigureAwait(false); }
    private static string Name(string extension) => $"WAID-Report-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.{extension}";
    private static string SerializeSafe(DiagnosticReportData report)
    {
        var node = JsonSerializer.SerializeToNode(report, Options) ?? new JsonObject();
        Redact(node);
        return node.ToJsonString(Options);
    }
    private static void Redact(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (SensitiveName.IsMatch(property.Key)) obj.Remove(property.Key);
                else if (property.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[property.Key] = RedactText(text);
                else if (property.Value is not null) Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text)) array[index] = RedactText(text);
                else if (array[index] is not null) Redact(array[index]!);
        }
    }
    private static string RedactText(string text)
    {
        var redacted=SensitiveValue.Replace(text,"$1=[REDACTED]");var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)?redacted:redacted.Replace(profile,"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);
    }
    private static string RenderHtml(DiagnosticReportData report)
    {
        static string E(object? value) => WebUtility.HtmlEncode(RedactText(value?.ToString() ?? "Unavailable"));
        var findings = report.Diagnosis?.Findings.Select(f => $"<tr><td>{E(f.Severity)}</td><td>{E(f.Title)}</td><td>{E(f.Description)}</td></tr>") ?? [];
        var causes = report.Diagnosis?.RootCauses.Select(c => $"<li><strong>{E(c.LikelyCause)}</strong> - {c.Confidence}%: {E(c.Explanation)}</li>") ?? [];
        var repairs = report.RepairPlan.Select(r => $"<tr><td>{r.Order}</td><td>{E(r.Title)}</td><td>{r.ExpectedBenefit}%</td><td>{E(r.RiskLevel)}</td><td>{E(r.RequiresAdministrator)}</td><td>{E(r.RestartRequired)}</td></tr>");
        var evidence = report.Evidence.Select(item => $"<tr><td>{E(item.CollectedAtUtc)}</td><td>{E(item.Source)}</td><td>{E(item.Code)}</td><td>{E(string.Join("; ", item.Values.Select(value => $"{value.Key}={value.Value}")))}</td></tr>");
        var history = report.RepairHistory.Select(item => $"<tr><td>{E(item.CreatedAtUtc)}</td><td>{E(item.RepairId)}</td><td>{E(item.Status)}</td><td>{E(item.Summary)}</td></tr>");
        return $$"""<!doctype html><html lang="en"><head><meta charset="utf-8"><title>WAID diagnostic report</title><style>body{font-family:Segoe UI,Arial;margin:2rem;max-width:1100px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #bbb;padding:.5rem;text-align:left}.notice{padding:1rem;background:#fff4ce}</style></head><body><h1>Windows AI Doctor diagnostic report</h1><p>Version: {{E(report.ApplicationVersion)}}<br>Generated: {{E(report.GeneratedAtUtc)}}<br>System: {{E(report.SystemSummary)}}</p><h2>Redaction notice</h2><p class="notice">{{E(report.RedactionNotice)}}</p><h2>Health</h2><p>Overall: {{E(report.Diagnosis?.Health.Overall)}}/100</p><h2>Findings</h2><table><tr><th>Severity</th><th>Finding</th><th>Details</th></tr>{{string.Concat(findings)}}</table><h2>Evidence</h2><table><tr><th>Time</th><th>Source</th><th>Code</th><th>Values</th></tr>{{string.Concat(evidence)}}</table><h2>Root causes</h2><ul>{{string.Concat(causes)}}</ul><h2>Recommended repair order</h2><table><tr><th>Order</th><th>Repair</th><th>Benefit</th><th>Risk</th><th>Administrator</th><th>Restart</th></tr>{{string.Concat(repairs)}}</table><h2>Repair history</h2><table><tr><th>Time</th><th>Repair</th><th>Status</th><th>Summary</th></tr>{{string.Concat(history)}}</table><h2>Known limitations</h2><ul>{{string.Concat(report.KnownLimitations.Select(item => $"<li>{E(item)}</li>"))}}</ul></body></html>""";
    }
}
