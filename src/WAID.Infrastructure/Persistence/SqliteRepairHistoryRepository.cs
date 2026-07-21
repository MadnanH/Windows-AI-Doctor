using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;

namespace WAID.Infrastructure.Persistence;

public sealed class SqliteRepairHistoryRepository(WaidDatabase database) : IRepairHistoryRepository
{
    public async Task SaveAsync(RepairTransaction transaction, CancellationToken cancellationToken)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO repair_history(
                transaction_id,repair_id,status,created_utc,completed_utc,summary,details,
                backup_location,restore_point_description,events_json)
            VALUES($id,$repair,$status,$created,$completed,$summary,$details,$backup,$restore,$events)
            ON CONFLICT(transaction_id) DO UPDATE SET
                status=$status,completed_utc=$completed,summary=$summary,details=$details,
                backup_location=$backup,restore_point_description=$restore,events_json=$events
            """;
        command.Parameters.AddWithValue("$id", transaction.Id.ToString());
        command.Parameters.AddWithValue("$repair", transaction.RepairId);
        command.Parameters.AddWithValue("$status", (int)transaction.Status);
        command.Parameters.AddWithValue("$created", transaction.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$completed", transaction.CompletedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$summary", transaction.Result?.Summary ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$details", transaction.Result?.Details ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$backup", transaction.BackupLocation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$restore", transaction.RestorePointDescription ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$events", JsonSerializer.Serialize(transaction.Events));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RepairHistoryEntry>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        var entries = new List<RepairHistoryEntry>();
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT transaction_id,repair_id,status,created_utc,completed_utc,summary,details,
                   backup_location,restore_point_description,events_json
            FROM repair_history ORDER BY created_utc DESC LIMIT $count
            """;
        command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new(
                Guid.Parse(reader.GetString(0)), reader.GetString(1),
                (RepairTransactionStatus)reader.GetInt32(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                JsonSerializer.Deserialize<string[]>(reader.GetString(9)) ?? []));
        }
        return entries;
    }
}
