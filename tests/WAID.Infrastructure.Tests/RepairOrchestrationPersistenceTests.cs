using WAID.Application.Services;
using WAID.Domain.Repairs;
using WAID.Infrastructure.Persistence;
namespace WAID.Infrastructure.Tests;
public sealed class RepairOrchestrationPersistenceTests
{
 [Fact]public async Task Lifecycle_round_trips_and_nonterminal_state_survives_restart(){var path=Path.Combine(Path.GetTempPath(),$"waid-orchestration-{Guid.NewGuid():N}.db");try{var db=new WaidDatabase($"Data Source={path};Pooling=False");await db.InitializeAsync(CancellationToken.None);var repo=new SqliteRepairOrchestrationRepository(db);var now=DateTimeOffset.UtcNow;var record=new RepairOrchestrationRecord(Guid.NewGuid(),"waid.test",now,now,RepairOrchestrationStage.Executing,RepairOrchestrator.LifecycleVersion,new("description",["action"],[],SafetyLevel.High,true,true,true,true,[],[]),new(true,true,now,"User",RepairOrchestrator.LifecycleVersion),Guid.NewGuid(),RepairTransactionStatus.Executing,@"%USERPROFILE%\backup","restore",false,null,[],"executing");await repo.SaveAsync(record,CancellationToken.None);var restarted=new SqliteRepairOrchestrationRepository(db);var loaded=await restarted.GetAsync(record.Id,CancellationToken.None);Assert.NotNull(loaded);Assert.Equal(record.Id,loaded.Id);Assert.Equal(record.Stage,loaded.Stage);Assert.Equal(record.Simulation!.Actions,loaded.Simulation!.Actions);Assert.Equal(record.Id,Assert.Single(await restarted.GetNonTerminalAsync(CancellationToken.None)).Id);}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}}
}
