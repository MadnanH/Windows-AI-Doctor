using System.Text.Json;
using Microsoft.Data.Sqlite;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Persistence;

public sealed class SqliteDriverHealthRepository(WaidDatabase database) : IDriverHealthRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<DriverHealthReport?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection=database.OpenConnection(); await using var command=connection.CreateCommand();
        command.CommandText="SELECT report_json FROM driver_analysis_runs ORDER BY generated_utc DESC LIMIT 1;";
        var json=await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is null ? null : JsonSerializer.Deserialize<DriverHealthReport>(json,JsonOptions);
    }
    public async Task SaveAsync(DriverInventorySnapshot snapshot,DriverHealthReport report,CancellationToken cancellationToken)
    {
        await using var connection=database.OpenConnection(); await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false); await using var command=connection.CreateCommand(); command.Transaction=transaction;
        command.CommandText="INSERT INTO driver_analysis_runs(id,generated_utc,is_administrator,inventory_json,report_json) VALUES($id,$time,$admin,$inventory,$report);";
        command.Parameters.AddWithValue("$id",report.Id.ToString()); command.Parameters.AddWithValue("$time",report.GeneratedAtUtc.ToString("O")); command.Parameters.AddWithValue("$admin",snapshot.IsAdministrator?1:0); command.Parameters.AddWithValue("$inventory",JsonSerializer.Serialize(snapshot,JsonOptions)); command.Parameters.AddWithValue("$report",JsonSerializer.Serialize(report,JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
