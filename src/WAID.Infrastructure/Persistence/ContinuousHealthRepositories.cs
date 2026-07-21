using System.Globalization;
using System.Text.Json;
using WAID.Application.Services;

namespace WAID.Infrastructure.Persistence;

public sealed class SqliteHealthSnapshotRepository(WaidDatabase database) : IHealthSnapshotRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public async Task SaveAsync(HealthSnapshot snapshot, CancellationToken token)
    {
        await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO health_snapshots VALUES($id,$captured,$json)"; command.Parameters.AddWithValue("$id", snapshot.Id); command.Parameters.AddWithValue("$captured", snapshot.CapturedAtUtc.ToString("O")); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(snapshot, Options));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<HealthSnapshot>> GetRecentAsync(int count, CancellationToken token)
    {
        if (count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
        var items = new List<HealthSnapshot>(); await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT snapshot_json FROM health_snapshots ORDER BY captured_utc DESC LIMIT $count"; command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false); while (await reader.ReadAsync(token).ConfigureAwait(false)) items.Add(JsonSerializer.Deserialize<HealthSnapshot>(reader.GetString(0), Options) ?? throw new InvalidOperationException("A health snapshot is invalid.")); return items;
    }
}

public sealed class SqliteScanScheduleRepository(WaidDatabase database) : IScanScheduleRepository
{
    private static readonly ScanSchedule Default = new(false, ScheduleFrequency.Daily, TimeSpan.FromHours(24), DayOfWeek.Sunday, new TimeOnly(9, 0), true, false);
    public async Task<ScanSchedule> GetAsync(CancellationToken token) { await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT schedule_json FROM scan_schedule WHERE id=1"; var json = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string; return json is null ? Default : (JsonSerializer.Deserialize<ScanSchedule>(json) ?? Default).Validate(); }
    public async Task SaveAsync(ScanSchedule schedule, CancellationToken token) { schedule.Validate(); await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO scan_schedule VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET schedule_json=$json"; command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(schedule)); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
}

public sealed class SqliteRepairApprovalRepository(WaidDatabase database) : IRepairApprovalRepository
{
    public async Task SaveAsync(RepairApproval approval, CancellationToken token) { await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO repair_approvals VALUES($id,$requested,$json)"; command.Parameters.AddWithValue("$id", approval.Id); command.Parameters.AddWithValue("$requested", approval.RequestedAtUtc.ToString("O")); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(approval)); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    public async Task<IReadOnlyList<RepairApproval>> GetRecentAsync(int count, CancellationToken token) { if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count)); var items = new List<RepairApproval>(); await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT approval_json FROM repair_approvals ORDER BY requested_utc DESC LIMIT $count"; command.Parameters.AddWithValue("$count", count); await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false); while(await reader.ReadAsync(token).ConfigureAwait(false)) items.Add(JsonSerializer.Deserialize<RepairApproval>(reader.GetString(0)) ?? throw new InvalidOperationException("A repair approval is invalid.")); return items; }
}
