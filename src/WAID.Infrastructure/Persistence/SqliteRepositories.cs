using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Domain.Settings;
using WAID.Diagnosis;
namespace WAID.Infrastructure.Persistence;
public sealed class SqliteScanRepository(WaidDatabase database) : IScanRepository
{
    public async Task SaveAsync(ScanSession session, CancellationToken token)
    {
        if (!session.IsCompleted) throw new InvalidOperationException("Only completed scans can be persisted.");
        await using var connection = database.OpenConnection(); await using var transaction = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(connection, transaction, "INSERT INTO scan_sessions VALUES($id,$started,$completed)", token, ("$id", session.Id), ("$started", session.StartedAtUtc.ToString("O")), ("$completed", session.CompletedAtUtc!.Value.ToString("O")));
        foreach (var finding in session.Findings)
            await ExecuteAsync(connection, transaction, "INSERT INTO findings VALUES($id,$session,$scanner,$code,$title,$description,$severity,$repair,$evidence)", token, ("$id", finding.Id), ("$session", session.Id), ("$scanner", finding.ScannerId), ("$code", finding.Code), ("$title", finding.Title), ("$description", finding.Description), ("$severity", (int)finding.Severity), ("$repair", finding.RecommendedRepairId), ("$evidence", JsonSerializer.Serialize(finding.Evidence)));
        await transaction.CommitAsync(token);
    }
    public async Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken token)
    {
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        var sessions = new List<ScanSession>(); await using var connection = database.OpenConnection();
        await using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT id,started_utc,completed_utc FROM scan_sessions ORDER BY started_utc DESC LIMIT $count"; cmd.Parameters.AddWithValue("$count", count);
        await using var reader = await cmd.ExecuteReaderAsync(token);
        var rows=new List<(Guid Id,DateTimeOffset Started,DateTimeOffset Completed)>();
        while (await reader.ReadAsync(token)) rows.Add((Guid.Parse(reader.GetString(0)),DateTimeOffset.Parse(reader.GetString(1),CultureInfo.InvariantCulture),DateTimeOffset.Parse(reader.GetString(2),CultureInfo.InvariantCulture)));
        await reader.DisposeAsync();
        foreach(var row in rows) { var s=new ScanSession(row.Id,row.Started); await using var findings=connection.CreateCommand(); findings.CommandText="SELECT id,scanner_id,code,title,description,severity,repair_id,evidence_json FROM findings WHERE session_id=$id ORDER BY rowid"; findings.Parameters.AddWithValue("$id",row.Id); await using var findingReader=await findings.ExecuteReaderAsync(token); var loaded=new List<DiagnosticFinding>(); while(await findingReader.ReadAsync(token)) loaded.Add(new(findingReader.GetString(1),findingReader.GetString(2),findingReader.GetString(3),findingReader.GetString(4),(DiagnosticSeverity)findingReader.GetInt32(5),findingReader.IsDBNull(6)?null:findingReader.GetString(6),JsonSerializer.Deserialize<Dictionary<string,string>>(findingReader.GetString(7)),Guid.Parse(findingReader.GetString(0)))); s.AddFindings(loaded); s.Complete(row.Completed); sessions.Add(s); }
        return sessions;
    }
    private static async Task ExecuteAsync(SqliteConnection c, System.Data.Common.DbTransaction t, string sql, CancellationToken token, params (string, object?)[] values)
    { await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)t; cmd.CommandText = sql; foreach (var (key,value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value); await cmd.ExecuteNonQueryAsync(token); }
}
public sealed class SqliteSettingsRepository(WaidDatabase database) : ISettingsRepository
{
    private readonly SqliteConfigurationStateRepository _configuration = new(database);
    public async Task<ApplicationSettings> GetAsync(CancellationToken token)
    {
        var values = (await _configuration.GetAsync(token).ConfigureAwait(false)).User.Values;
        return new ApplicationSettings
        {
            RunScansAtStartup = values.RunScansAtStartup ?? false,
            EnableAiAnalysis = values.EnableAiAnalysis ?? false,
            AllowTelemetry = values.AllowTelemetry ?? false,
            AiProvider = values.AiProvider ?? "None",
            Theme = values.Theme ?? "System",
            ScanTimeoutSeconds = values.ScanTimeoutSeconds ?? 120,
            EnableExperimentalFeatures = values.EnableExperimentalFeatures ?? false
        }.Validate();
    }
    public async Task SaveAsync(ApplicationSettings settings, CancellationToken token)
    {
        settings.Validate(); var state = await _configuration.GetAsync(token).ConfigureAwait(false);
        var user = state.User with { Values = ConfigurationValues.From(settings) };
        await _configuration.SaveAsync(state with { User = user, UpdatedAtUtc = DateTimeOffset.UtcNow }, token).ConfigureAwait(false);
    }
}
public sealed class SqliteDiagnosisRepository(WaidDatabase database) : IDiagnosisRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(Guid scanSessionId, AIReport report, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO diagnosis_reports(id,scan_session_id,generated_utc,report_json) VALUES($id,$scan,$generated,$json)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid());
        command.Parameters.AddWithValue("$scan", scanSessionId);
        command.Parameters.AddWithValue("$generated", report.GeneratedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(report, JsonOptions));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<AIReport?> GetLatestAsync(CancellationToken token)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT report_json FROM diagnosis_reports ORDER BY generated_utc DESC LIMIT 1";
        var json = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
        return json is null ? null : JsonSerializer.Deserialize<AIReport>(json, JsonOptions)
            ?? throw new InvalidOperationException("The latest diagnosis report could not be read.");
    }
}
