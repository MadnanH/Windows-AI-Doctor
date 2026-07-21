using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
using WAID.Domain.Settings;
using WAID.Diagnosis;

namespace WAID.Infrastructure.Persistence;

public sealed class SqliteScanRepository(WaidDatabase database) : IScanRepository, IScanRunRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public Task SaveAsync(ScanSession session, CancellationToken token) => SaveAsync(session, [], token);

    public async Task SaveAsync(ScanSession session, IReadOnlyCollection<ScannerExecutionRecord> executions, CancellationToken token)
    {
        if (!session.IsCompleted) throw new InvalidOperationException("Only completed scans can be persisted.");
        await using var connection = database.OpenConnection(); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        var status = executions.Any(item => item.Status == ScannerExecutionStatus.Cancelled) ? "Cancelled"
            : executions.Any(item => item.Status is ScannerExecutionStatus.Failed or ScannerExecutionStatus.TimedOut or ScannerExecutionStatus.PermissionDenied or ScannerExecutionStatus.Skipped) ? "Partial" : "Completed";
        await ExecuteAsync(connection, transaction, "INSERT INTO scan_sessions(id,started_utc,completed_utc,status,framework_version) VALUES($id,$started,$completed,$status,'2.0.0')", token,
            ("$id", session.Id), ("$started", session.StartedAtUtc.ToString("O")), ("$completed", session.CompletedAtUtc!.Value.ToString("O")), ("$status", status));
        foreach (var finding in session.Findings)
            await ExecuteAsync(connection, transaction, "INSERT INTO findings(id,session_id,scanner_id,code,title,description,severity,repair_id,evidence_json) VALUES($id,$session,$scanner,$code,$title,$description,$severity,$repair,$evidence)", token,
                ("$id", finding.Id), ("$session", session.Id), ("$scanner", finding.ScannerId), ("$code", finding.Code), ("$title", finding.Title), ("$description", finding.Description), ("$severity", (int)finding.Severity), ("$repair", finding.RecommendedRepairId), ("$evidence", JsonSerializer.Serialize(finding.Evidence, Options)));
        foreach (var execution in executions)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO scanner_executions(id,session_id,scanner_id,display_name,category,scanner_version,status,started_utc,completed_utc,duration_ms,attempts,failure_code,detail,resource_json)
                VALUES($id,$session,$scanner,$name,$category,$version,$status,$started,$completed,$duration,$attempts,$failure,$detail,$resource)
                """, token, ("$id", execution.Id), ("$session", session.Id), ("$scanner", execution.ScannerId), ("$name", execution.DisplayName), ("$category", execution.Category), ("$version", execution.Version),
                ("$status", (int)execution.Status), ("$started", execution.StartedAtUtc.ToString("O")), ("$completed", execution.CompletedAtUtc.ToString("O")), ("$duration", execution.DurationMilliseconds),
                ("$attempts", execution.Attempts), ("$failure", execution.FailureCode), ("$detail", execution.Detail), ("$resource", JsonSerializer.Serialize(execution.Resources, Options)));
            foreach (var observation in execution.Observations)
                await ExecuteAsync(connection, transaction, "INSERT INTO evidence(id,scan_session_id,source,captured_utc,evidence_json) VALUES($id,$session,$source,$captured,$json)", token,
                    ("$id", Guid.NewGuid()), ("$session", session.Id), ("$source", execution.ScannerId), ("$captured", observation.ObservedAtUtc.ToString("O")), ("$json", JsonSerializer.Serialize(new StoredEvidence(execution.Id, observation), Options)));
        }
        await transaction.CommitAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken token)
    {
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        var sessions = new List<ScanSession>(); await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT id,started_utc,completed_utc FROM scan_sessions ORDER BY started_utc DESC LIMIT $count"; command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false); var rows = new List<(Guid Id, DateTimeOffset Started, DateTimeOffset Completed)>();
        while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add((Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        await reader.DisposeAsync().ConfigureAwait(false);
        foreach (var row in rows) { var session = new ScanSession(row.Id, row.Started); session.AddFindings(await ReadFindingsAsync(connection, row.Id, null, token).ConfigureAwait(false)); session.Complete(row.Completed); sessions.Add(session); }
        return sessions;
    }

    public async Task<IReadOnlyList<ScannerExecutionRecord>> GetExecutionsAsync(Guid sessionId, CancellationToken token)
    {
        var records = new List<ScannerExecutionRecord>(); await using var connection = database.OpenConnection();
        var observations = new List<StoredEvidence>(); await using (var evidence = connection.CreateCommand())
        { evidence.CommandText = "SELECT evidence_json FROM evidence WHERE scan_session_id=$session"; evidence.Parameters.AddWithValue("$session", sessionId); await using var reader = await evidence.ExecuteReaderAsync(token).ConfigureAwait(false); while (await reader.ReadAsync(token).ConfigureAwait(false)) { var item = JsonSerializer.Deserialize<StoredEvidence>(reader.GetString(0), Options); if (item is not null) observations.Add(item); } }
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT id,scanner_id,display_name,category,scanner_version,status,started_utc,completed_utc,duration_ms,attempts,failure_code,detail,resource_json FROM scanner_executions WHERE session_id=$session ORDER BY started_utc,scanner_id"; command.Parameters.AddWithValue("$session", sessionId);
        await using var executionReader = await command.ExecuteReaderAsync(token).ConfigureAwait(false); var rows = new List<object[]>(); while (await executionReader.ReadAsync(token).ConfigureAwait(false)) { var values = new object[13]; executionReader.GetValues(values); rows.Add(values); } await executionReader.DisposeAsync().ConfigureAwait(false);
        foreach (var row in rows)
        {
            var id = Guid.Parse((string)row[0]); var scannerId = (string)row[1];
            records.Add(new(id, sessionId, scannerId, (string)row[2], (string)row[3], (string)row[4], (ScannerExecutionStatus)Convert.ToInt32(row[5]), DateTimeOffset.Parse((string)row[6]), DateTimeOffset.Parse((string)row[7]), Convert.ToInt64(row[8]), Convert.ToInt32(row[9]),
                row[10] is DBNull ? null : (string)row[10], row[11] is DBNull ? null : (string)row[11], JsonSerializer.Deserialize<ScannerResourceUsage>((string)row[12], Options) ?? new(0, 0), observations.Where(item => item.ExecutionId == id).Select(item => item.Observation).ToArray(), await ReadFindingsAsync(connection, sessionId, scannerId, token).ConfigureAwait(false)));
        }
        return records;
    }

    private static async Task<IReadOnlyList<DiagnosticFinding>> ReadFindingsAsync(SqliteConnection connection, Guid sessionId, string? scannerId, CancellationToken token)
    {
        var findings = new List<DiagnosticFinding>(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,scanner_id,code,title,description,severity,repair_id,evidence_json FROM findings WHERE session_id=$id AND ($scanner IS NULL OR scanner_id=$scanner) ORDER BY rowid";
        command.Parameters.AddWithValue("$id", sessionId); command.Parameters.AddWithValue("$scanner", scannerId ?? (object)DBNull.Value); await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) findings.Add(new(reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), (DiagnosticSeverity)reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(7), Options), Guid.Parse(reader.GetString(0))));
        return findings;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object? Value)[] values)
    { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private sealed record StoredEvidence(Guid ExecutionId, ScannerObservation Observation);
}

public sealed class SqliteSettingsRepository(WaidDatabase database) : ISettingsRepository
{
    private readonly SqliteConfigurationStateRepository _configuration = new(database);
    public async Task<ApplicationSettings> GetAsync(CancellationToken token) { var values = (await _configuration.GetAsync(token).ConfigureAwait(false)).User.Values; return new ApplicationSettings { RunScansAtStartup = values.RunScansAtStartup ?? false, EnableAiAnalysis = values.EnableAiAnalysis ?? false, AllowTelemetry = values.AllowTelemetry ?? false, AiProvider = values.AiProvider ?? "None", Theme = values.Theme ?? "System", ScanTimeoutSeconds = values.ScanTimeoutSeconds ?? 120, EnableExperimentalFeatures = values.EnableExperimentalFeatures ?? false }.Validate(); }
    public async Task SaveAsync(ApplicationSettings settings, CancellationToken token) { settings.Validate(); var state = await _configuration.GetAsync(token).ConfigureAwait(false); await _configuration.SaveAsync(state with { User = state.User with { Values = ConfigurationValues.From(settings) }, UpdatedAtUtc = DateTimeOffset.UtcNow }, token).ConfigureAwait(false); }
}

public sealed class SqliteDiagnosisRepository(WaidDatabase database) : IDiagnosisRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task SaveAsync(Guid scanSessionId, AIReport report, CancellationToken token) { ArgumentNullException.ThrowIfNull(report); await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO diagnosis_reports(id,scan_session_id,generated_utc,report_json) VALUES($id,$scan,$generated,$json)"; command.Parameters.AddWithValue("$id", Guid.NewGuid()); command.Parameters.AddWithValue("$scan", scanSessionId); command.Parameters.AddWithValue("$generated", report.GeneratedAtUtc.ToString("O")); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(report, JsonOptions)); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    public async Task<AIReport?> GetLatestAsync(CancellationToken token) { await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT report_json FROM diagnosis_reports ORDER BY generated_utc DESC LIMIT 1"; var json = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string; return json is null ? null : JsonSerializer.Deserialize<AIReport>(json, JsonOptions) ?? throw new InvalidOperationException("The latest diagnosis report could not be read."); }
}
