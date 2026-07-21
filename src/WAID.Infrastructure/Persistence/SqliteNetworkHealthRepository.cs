using System.Text.Json;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Persistence;

public sealed class SqliteNetworkHealthRepository(WaidDatabase database) : INetworkHealthRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<NetworkHealthReport?> GetLatestAsync(CancellationToken token)
    {
        await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT report_json FROM network_health_runs ORDER BY generated_utc DESC LIMIT 1";
        var json = await command.ExecuteScalarAsync(token) as string;
        return json is null ? null : JsonSerializer.Deserialize<NetworkHealthReport>(json, JsonOptions);
    }
    public async Task SaveAsync(NetworkHealthReport report, CancellationToken token)
    {
        await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO network_health_runs(id,generated_utc,snapshot_json,tests_json,report_json) VALUES($id,$time,$snapshot,$tests,$report)";
        command.Parameters.AddWithValue("$id", report.Id.ToString()); command.Parameters.AddWithValue("$time", report.GeneratedAtUtc.ToString("O")); command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(report.Snapshot, JsonOptions)); command.Parameters.AddWithValue("$tests", JsonSerializer.Serialize(report.Snapshot.Tests, JsonOptions)); command.Parameters.AddWithValue("$report", JsonSerializer.Serialize(report, JsonOptions));
        await command.ExecuteNonQueryAsync(token);
    }
}
