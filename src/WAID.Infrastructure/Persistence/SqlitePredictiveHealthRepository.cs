using System.Text.Json;
using WAID.Application.Services;
using WAID.Health;

namespace WAID.Infrastructure.Persistence;

public sealed class SqlitePredictiveHealthRepository(WaidDatabase database) : IPredictiveHealthRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task SaveAsync(PredictiveHealthReport report, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO predictive_health_runs(id,generated_utc,model_version,report_json) VALUES($id,$generated,$version,$json);";
        command.Parameters.AddWithValue("$id", report.Id.ToString()); command.Parameters.AddWithValue("$generated", report.GeneratedAtUtc.ToString("O")); command.Parameters.AddWithValue("$version", report.ModelVersion); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(report, JsonOptions));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
    public async Task<PredictiveHealthReport?> GetLatestAsync(CancellationToken token)
    {
        await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT report_json FROM predictive_health_runs ORDER BY generated_utc DESC LIMIT 1;";
        var json = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string; return json is null ? null : JsonSerializer.Deserialize<PredictiveHealthReport>(json, JsonOptions);
    }
}
