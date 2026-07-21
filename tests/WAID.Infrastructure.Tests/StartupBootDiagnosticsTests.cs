using Microsoft.Extensions.Logging.Abstractions;using WAID.Application.Abstractions;using WAID.Infrastructure.Diagnostics;using WAID.Infrastructure.Persistence;using WAID.Infrastructure.PowerShell;
namespace WAID.Infrastructure.Tests;
public sealed class StartupBootDiagnosticsTests
{
 private static readonly DateTimeOffset Now=new(2026,7,21,12,0,0,TimeSpan.Zero);
 [Theory][InlineData("\"C:\\Program Files\\Vendor\\app.exe\" --quiet","C:\\Program Files\\Vendor\\app.exe")][InlineData("C:\\Tools\\agent.exe /start","C:\\Tools\\agent.exe")][InlineData("app.exe -x","app.exe")]
 public void Quoted_and_unquoted_command_paths_are_parsed(string command,string expected)=>Assert.Equal(expected,CommandLineParser.TryGetExecutablePath(command));

 [Fact] public async Task Provider_deduplicates_the_same_normalized_entry_across_sources()
 {const string json="""{"items":[{"name":"Agent","source":"RunKey","publisher":"Vendor","command":"app.exe -x","enabled":true,"failed":false,"critical":false,"reference":"run","impact":null},{"name":"Agent","source":"StartupApplication","publisher":"Vendor","command":"app.exe -x","enabled":true,"failed":false,"critical":false,"reference":"wmi","impact":null}],"boots":[]}""";var snapshot=await new WindowsStartupInventoryProvider(new JsonRunner(json)).CollectAsync(CancellationToken.None);var entry=Assert.Single(snapshot.Entries);Assert.Equal(StartupSource.RunKey,entry.Source);Assert.NotEmpty(snapshot.Limitations);}

 [Fact] public void Boot_correlation_estimates_impact_only_when_slow_boot_and_large_inventory_coexist()
 {var entries=Enumerable.Range(0,25).Select(i=>Entry($"{i}",$"Entry {i}")).ToArray();var snapshot=new StartupSnapshot(Now,entries,[new(Now,70_000,10_000,"event:100")],[]);var recommendations=StartupBootAnalyzer.Evaluate(snapshot);Assert.Equal(25,recommendations.Count(x=>x.Concern==StartupConcern.Performance));Assert.All(recommendations.Where(x=>x.Concern==StartupConcern.Performance),x=>Assert.Contains(x.Evidence,e=>e.Signal=="source"));}

 [Fact] public void Measured_impact_is_preferred_over_estimation()
 {var recommendation=Assert.Single(StartupBootAnalyzer.Evaluate(new(Now,[Entry("a","Agent",impact:6000)],[],[])).Where(x=>x.Concern==StartupConcern.Performance));Assert.Equal(StartupImpact.High,recommendation.Impact);Assert.Contains(recommendation.Evidence,x=>x.Signal=="measuredImpactMs");}

 [Fact] public void Failed_and_missing_entries_have_separate_evidence_backed_concerns()
 {var entry=Entry("a","Agent",exists:false,failed:true);var recommendations=StartupBootAnalyzer.Evaluate(new(Now,[entry],[],[]));Assert.Contains(recommendations,x=>x.Concern==StartupConcern.Failed);Assert.Contains(recommendations,x=>x.Concern==StartupConcern.MissingTarget);Assert.All(recommendations,x=>Assert.NotEmpty(x.Evidence));}

 [Fact] public void Critical_entries_are_protected_in_recommendations_and_simulation()
 {var entry=Entry("critical","Windows Defender",exists:false,critical:true);var recommendation=Assert.Single(StartupBootAnalyzer.Evaluate(new(Now,[entry],[],[])));Assert.True(recommendation.IsProtected);Assert.False(recommendation.Reversible);Assert.Contains("Protected",recommendation.ActionPreview);var simulation=new StartupActionPlanner().SimulateDisable(entry);Assert.False(simulation.Allowed);Assert.False(simulation.WouldChangeState);}

 [Fact] public void Disable_simulation_produces_entry_bound_rollback_metadata_without_execution()
 {var planner=new StartupActionPlanner();var entry=Entry("a","Agent");var simulation=planner.SimulateDisable(entry);Assert.True(simulation.Allowed);Assert.True(simulation.WouldChangeState);var rollback=planner.SimulateRollback(entry,simulation.RollbackMetadata);Assert.True(rollback.Allowed);Assert.True(rollback.WouldChangeState);Assert.False(planner.SimulateRollback(Entry("b","Other"),simulation.RollbackMetadata).Allowed);}

 [Fact] public async Task Sqlite_round_trip_preserves_snapshots_boots_recommendations_and_rollback_schema()
 {var path=Path.Combine(Path.GetTempPath(),$"waid-boot-{Guid.NewGuid():N}.db");try{var db=new WaidDatabase($"Data Source={path};Pooling=False");await db.InitializeAsync(CancellationToken.None);var repo=new SqliteBootHealthRepository(db);var snapshot=new StartupSnapshot(Now,[Entry("a","Agent",impact:2000)],[new(Now,30_000,5_000,"event")],[]);var report=new BootHealthReport(Guid.NewGuid(),Now,snapshot.Entries,snapshot.BootMeasurements,StartupBootAnalyzer.Evaluate(snapshot),[],[]);await repo.SaveAsync(snapshot,report,CancellationToken.None);var loaded=await repo.GetLatestAsync(CancellationToken.None);Assert.Equal(report.Id,loaded!.Id);Assert.Single(loaded.Entries);Assert.Single(loaded.BootMeasurements);Assert.NotEmpty(loaded.Recommendations);}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}}

 private static StartupEntry Entry(string id,string name,bool exists=true,bool failed=false,bool critical=false,double? impact=null)=>new(id,name,StartupSource.RunKey,"Vendor","C:\\Tools\\app.exe","C:\\Tools\\app.exe",true,exists,failed,critical,false,impact,$"Run:{name}");
 private sealed class JsonRunner(string json):IPowerShellRunner{public Task<PowerShellResult> RunAsync(string script,IReadOnlyDictionary<string,object?> parameters,CancellationToken token)=>Task.FromResult(new PowerShellResult([json],[]));}
}
