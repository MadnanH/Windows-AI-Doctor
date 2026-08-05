using System.Text.Json;
using WAID.Infrastructure.Plugins;
namespace WAID.Infrastructure.Persistence;
public sealed class SqlitePluginInventoryRepository(WaidDatabase database):IPluginInventoryRepository
{
 public async Task SaveAsync(PluginInventoryRecord record,CancellationToken token){await using var c=database.OpenConnection();await using var q=c.CreateCommand();q.CommandText="INSERT INTO plugin_inventory(id,updated_utc,state,certified,inventory_json)VALUES($id,$time,$state,$certified,$json) ON CONFLICT(id) DO UPDATE SET updated_utc=$time,state=$state,certified=$certified,inventory_json=$json";q.Parameters.AddWithValue("$id",record.Id);q.Parameters.AddWithValue("$time",record.UpdatedAtUtc.ToString("O"));q.Parameters.AddWithValue("$state",(int)record.State);q.Parameters.AddWithValue("$certified",record.Certified?1:0);q.Parameters.AddWithValue("$json",JsonSerializer.Serialize(record));await q.ExecuteNonQueryAsync(token);}
 public async Task<IReadOnlyList<PluginInventoryRecord>>GetAllAsync(CancellationToken token){var result=new List<PluginInventoryRecord>();await using var c=database.OpenConnection();await using var q=c.CreateCommand();q.CommandText="SELECT inventory_json FROM plugin_inventory ORDER BY updated_utc DESC,id";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token)){var item=JsonSerializer.Deserialize<PluginInventoryRecord>(r.GetString(0));if(item is not null)result.Add(item);}return result;}
}
