using System.Text.Json;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Diagnostics;

public sealed class LocalDiagnosticsService(string logDirectory, string exportDirectory, IAuditTrailService auditTrail) : ILocalDiagnosticsService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public async Task<IReadOnlyList<TechnicalLogEntry>> SearchLogsAsync(TechnicalLogQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query); if (query.MaximumRecords is < 1 or > 3000) throw new ArgumentOutOfRangeException(nameof(query));
        if (!Directory.Exists(logDirectory)) return [];
        var entries = new List<TechnicalLogEntry>();
        foreach (var file in Directory.EnumerateFiles(logDirectory, "waid-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(14))
        {
            foreach (var line in (await File.ReadAllLinesAsync(file, token).ConfigureAwait(false)).Reverse())
            {
                token.ThrowIfCancellationRequested(); var entry = Parse(line); if (entry is null || !Matches(entry, query)) continue;
                entries.Add(entry); if (entries.Count >= query.MaximumRecords) return entries.OrderByDescending(item => item.TimestampUtc).ToArray();
            }
        }
        return entries.OrderByDescending(item => item.TimestampUtc).ToArray();
    }

    public async Task<string> ExportSanitizedAsync(AuditQuery auditQuery, TechnicalLogQuery logQuery, CancellationToken token)
    {
        Directory.CreateDirectory(exportDirectory);
        var path = Path.Combine(exportDirectory, $"WAID-Support-Logs-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json");
        var temporary = path + ".tmp";
        var content = new { generatedAtUtc = DateTimeOffset.UtcNow, redactionNotice = "Credentials, tokens, product keys, profile paths, and sensitive identifiers are excluded or redacted.", logs = await SearchLogsAsync(logQuery, token).ConfigureAwait(false), audit = await auditTrail.SearchAsync(auditQuery, token).ConfigureAwait(false) };
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(content, Options), token).ConfigureAwait(false);
        File.Move(temporary, path); return path;
    }

    private static TechnicalLogEntry? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line); var root = document.RootElement;
            var timestamp = root.TryGetProperty("Timestamp", out var time) && time.TryGetDateTimeOffset(out var parsed) ? parsed : DateTimeOffset.MinValue;
            var level = Get(root, "Level", "Information"); var message = ReportRedactor.RedactText(Get(root, "RenderedMessage", Get(root, "MessageTemplate", "Log event")));
            var properties = root.TryGetProperty("Properties", out var values) ? values : default;
            var category = properties.ValueKind == JsonValueKind.Object ? Get(properties, "SourceContext", "WAID") : "WAID";
            var eventId = 0; if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty("EventId", out var id)) { if (id.ValueKind == JsonValueKind.Number) id.TryGetInt32(out eventId); else if (id.ValueKind == JsonValueKind.Object && id.TryGetProperty("Id", out var nested)) nested.TryGetInt32(out eventId); }
            return new(timestamp, level.Trim('"'), ReportRedactor.RedactText(category.Trim('"')), eventId, GuidValue(properties, "CorrelationId"), GuidValue(properties, "OperationId"), message.Trim('"'), ReportRedactor.RedactText(line));
        }
        catch (JsonException) { return null; }
    }
    private static string Get(JsonElement element, string name, string fallback) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.ToString() : fallback;
    private static Guid? GuidValue(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && Guid.TryParse(value.ToString().Trim('"'), out var id) ? id : null;
    private static bool Matches(TechnicalLogEntry entry, TechnicalLogQuery query) => (string.IsNullOrWhiteSpace(query.SearchText) || $"{entry.Category} {entry.Message} {entry.TechnicalDetail}".Contains(query.SearchText, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(query.MinimumLevel) || Rank(entry.Level) >= Rank(query.MinimumLevel));
    private static int Rank(string level) => level.ToLowerInvariant() switch { "verbose" or "trace" => 0, "debug" => 1, "information" => 2, "warning" => 3, "error" => 4, "fatal" or "critical" => 5, _ => 0 };
}
