using WAID.Application.Services;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class RepairOutcomePersistenceTests
{
 [Fact]public async Task Immutable_chain_ignores_duplicate_identity_and_filters(){var path=Path.Combine(Path.GetTempPath(),$"waid-outcomes-{Guid.NewGuid():N}.db");try{var db=new WaidDatabase($"Data Source={path};Pooling=False");await db.InitializeAsync(CancellationToken.None);var repo=new SqliteRepairOutcomeRepository(db);var id=Guid.NewGuid();var entry=new RepairAuditEntry(id,Guid.NewGuid(),null,"waid.test",RepairAuditKind.Validation,DateTimeOffset.UtcNow,"user","validated",["evidence"],RepairOutcomeClass.ValidatedSuccess);await repo.AppendAsync(entry,CancellationToken.None);await repo.AppendAsync(entry with{Summary="attempted overwrite"},CancellationToken.None);var loaded=Assert.Single(await repo.QueryAsync(new("waid.test",RepairAuditKind.Validation,Limit:10),CancellationToken.None));Assert.Equal("validated",loaded.Summary);}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}}
 [Fact]public async Task Aggregate_rebuild_replaces_derived_rows_without_changing_audit(){var path=Path.Combine(Path.GetTempPath(),$"waid-outcomes-{Guid.NewGuid():N}.db");try{var db=new WaidDatabase($"Data Source={path};Pooling=False");await db.InitializeAsync(CancellationToken.None);var repo=new SqliteRepairOutcomeRepository(db);var item=new RepairOutcomeAggregate("waid.test",1,1,0,0,0,0,100,"v",DateTimeOffset.UtcNow,"observed");await repo.ReplaceAggregatesAsync([item],CancellationToken.None);await repo.ReplaceAggregatesAsync([item with{Total=2}],CancellationToken.None);Assert.Equal(2,Assert.Single(await repo.GetAggregatesAsync(CancellationToken.None)).Total);Assert.Empty(await repo.QueryAsync(new(),CancellationToken.None));}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}}
}
