using Microsoft.Data.Sqlite;
using WAID.Infrastructure.Persistence;
using WAID.Infrastructure.Plugins;
namespace WAID.Infrastructure.Tests;
public sealed class PluginInventoryPersistenceTests
{
 [Fact] public async Task Certification_permissions_failure_and_signature_inventory_round_trip()
 {var path=Path.Combine(Path.GetTempPath(),$"waid-plugin-inventory-{Guid.NewGuid():N}.db");try{var db=new WaidDatabase($"Data Source={path};Pooling=False");await db.InitializeAsync(CancellationToken.None);var repo=new SqlitePluginInventoryRepository(db);var record=new PluginInventoryRecord("com.vendor.tool","Vendor Tool","2.1.0",PluginState.Quarantined,false,"Missing or invalid",["Scanner"],["SystemRead"],"Load failure log",@"C:\plugins\tool.json",DateTimeOffset.UtcNow);await repo.SaveAsync(record,CancellationToken.None);var loaded=Assert.Single(await repo.GetAllAsync(CancellationToken.None));Assert.Equal(record.Id,loaded.Id);Assert.Equal(record.State,loaded.State);Assert.Equal(record.SignatureStatus,loaded.SignatureStatus);Assert.Equal(record.Permissions,loaded.Permissions);Assert.Equal(record.Detail,loaded.Detail);}finally{SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}}
}