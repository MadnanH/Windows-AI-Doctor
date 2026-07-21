using System.Text;
using System.Text.Json;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Diagnostics;

public sealed class OperationContextAccessor : IOperationContextAccessor
{
    private readonly AsyncLocal<WaidOperationContext?> _current = new();
    public WaidOperationContext? Current => _current.Value;
    public IDisposable Begin(string category, Guid? correlationId = null, Guid? operationId = null)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Operation category is required.", nameof(category));
        var previous = _current.Value;
        _current.Value = new(correlationId ?? previous?.CorrelationId ?? Guid.NewGuid(), operationId ?? Guid.NewGuid(), category.Trim());
        return new Scope(() => _current.Value = previous);
    }
    private sealed class Scope(Action dispose) : IDisposable { private int _disposed; public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose(); } }
}

public sealed class LocalAuditTrailService(string auditDirectory, int retentionDays, TimeProvider timeProvider) : IAuditTrailService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _retentionApplied;

    public async Task<AuditWriteResult> AppendAsync(AuditRecord record, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(record); token.ThrowIfCancellationRequested();
        var safe = record with { Action = Safe(record.Action), Target = Safe(record.Target), Detail = Safe(record.Detail) };
        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(auditDirectory);
                if (Interlocked.Exchange(ref _retentionApplied, 1) == 0) ApplyRetentionCore(token);
                var path = Path.Combine(auditDirectory, $"audit-{safe.TimestampUtc:yyyyMMdd}.jsonl");
                await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true);
                await JsonSerializer.SerializeAsync(stream, safe, Options, token).ConfigureAwait(false);
                await stream.WriteAsync("\n"u8.ToArray(), token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                return new(true, safe.Id);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (IOException) { return new(false, safe.Id, "AUDIT_IO_FAILURE"); }
        catch (UnauthorizedAccessException) { return new(false, safe.Id, "AUDIT_ACCESS_DENIED"); }
    }

    public async Task<IReadOnlyList<AuditRecord>> SearchAsync(AuditQuery query, CancellationToken token)
    {
        Validate(query); if (!Directory.Exists(auditDirectory)) return [];
        var records = new List<AuditRecord>();
        foreach (var file in Directory.EnumerateFiles(auditDirectory, "audit-*.jsonl").OrderByDescending(path => path, StringComparer.Ordinal))
        {
            foreach (var line in await File.ReadAllLinesAsync(file, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var item = JsonSerializer.Deserialize<AuditRecord>(line, Options); if (item is null || !Matches(item, query)) continue;
                    records.Add(item); if (records.Count >= query.MaximumRecords) return records.OrderByDescending(item => item.TimestampUtc).ToArray();
                }
                catch (JsonException) { }
            }
        }
        return records.OrderByDescending(item => item.TimestampUtc).ToArray();
    }

    public async Task ApplyRetentionAsync(CancellationToken token)
    {
        if (!Directory.Exists(auditDirectory)) return;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { ApplyRetentionCore(token); }
        finally { _gate.Release(); }
    }

    private void ApplyRetentionCore(CancellationToken token)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(auditDirectory, "audit-*.jsonl")) { token.ThrowIfCancellationRequested(); if (File.GetLastWriteTimeUtc(file) < cutoff) try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static bool Matches(AuditRecord item, AuditQuery query) =>
        (!query.FromUtc.HasValue || item.TimestampUtc >= query.FromUtc) && (!query.ToUtc.HasValue || item.TimestampUtc <= query.ToUtc) &&
        (string.IsNullOrWhiteSpace(query.Action) || string.Equals(item.Action, query.Action, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(query.SearchText) || $"{item.Action} {item.Target} {item.Result} {item.Detail}".Contains(query.SearchText, StringComparison.OrdinalIgnoreCase));
    private static void Validate(AuditQuery query) { ArgumentNullException.ThrowIfNull(query); if (query.MaximumRecords is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(query)); if (query.FromUtc > query.ToUtc) throw new ArgumentException("Audit start time must precede end time.", nameof(query)); }
    private static string Safe(string value) => ReportRedactor.RedactText(value ?? string.Empty);
}
