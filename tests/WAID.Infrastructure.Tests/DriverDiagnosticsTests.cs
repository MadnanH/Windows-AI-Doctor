using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Infrastructure.Diagnostics;
using WAID.Infrastructure.Persistence;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Tests;

public sealed class DriverDiagnosticsTests
{
    private static readonly DateTimeOffset Now = new(2026,7,21,12,0,0,TimeSpan.Zero);

    [Fact]
    public void Rules_require_evidence_and_keep_duplicate_and_orphan_signals_conservative()
    {
        var old=Now.AddYears(-3); var drivers=new[]{Driver("a","Device A",hardware:"same",present:true),Driver("b","Device B",hardware:"same",present:true),Driver("c","Old device",present:false,date:old)};
        var findings=DriverConflictAnalyzer.Evaluate(new(Now,"X64",drivers,[],true,[]));
        Assert.Equal(2,findings.Count(x=>x.Kind==DriverFindingKind.Duplicate));
        var orphan=Assert.Single(findings.Where(x=>x.Kind==DriverFindingKind.Orphaned)); Assert.Equal("Information",orphan.Severity); Assert.Equal(.65,orphan.Confidence); Assert.NotEmpty(orphan.Evidence);
    }

    [Fact]
    public void Signature_and_problem_code_rules_report_uncertainty_and_sources()
    {
        var driver=Driver("a","Audio",signed:false,problem:43);
        var findings=DriverConflictAnalyzer.Evaluate(new(Now,"X64",[driver],[],true,[]));
        var unsigned=Assert.Single(findings.Where(x=>x.Kind==DriverFindingKind.Unsigned)); Assert.Equal(.9,unsigned.Confidence); Assert.Contains(unsigned.Evidence,x=>x.SourceReference=="Win32_PnPSignedDriver");
        var problem=Assert.Single(findings.Where(x=>x.Kind==DriverFindingKind.ProblemCode)); Assert.Equal("Critical",problem.Severity); Assert.Single(findings.Where(x=>x.Kind==DriverFindingKind.Failed));
    }

    [Fact]
    public void Standard_user_signature_result_has_lower_confidence_and_explicit_limitation()
    {
        var findings=DriverConflictAnalyzer.Evaluate(new(Now,"X64",[Driver("a","Audio",signed:false)],[],false,["Standard-user collection may omit protected details."]));
        Assert.Equal(.72,Assert.Single(findings).Confidence);
    }

    [Fact]
    public void Event_correlation_only_links_supported_load_failure_events_to_known_devices()
    {
        var events=new[]{new DriverEventEvidence(Now,219,"Kernel-PnP","a","Driver failed to load"),new DriverEventEvidence(Now,41,"Kernel-Power","a","Power loss")};
        var findings=DriverConflictAnalyzer.Evaluate(new(Now,"X64",[Driver("a","Network")],events,true,[]));
        var load=Assert.Single(findings); Assert.Equal(DriverFindingKind.LoadFailure,load.Kind); Assert.Contains(load.Evidence,x=>x.Signal=="eventId"&&x.Value=="219");
    }

    [Fact]
    public void Crash_and_update_events_are_only_findings_when_linked_to_a_known_device()
    {
        var events=new[]{new DriverEventEvidence(Now,4101,"Display","a","Display recovered"),new DriverEventEvidence(Now,20001,"UserPnp","a","Driver installed"),new DriverEventEvidence(Now,4101,"Display","missing","Unlinked")};
        var findings=DriverConflictAnalyzer.Evaluate(new(Now,"X64",[Driver("a","Display")],events,true,[]));
        Assert.Contains(findings,x=>x.Kind==DriverFindingKind.Failed&&x.Confidence==.82); Assert.Contains(findings,x=>x.Kind==DriverFindingKind.RecentlyChanged&&x.Confidence==.78); Assert.DoesNotContain(findings,x=>x.DeviceKey=="missing");
    }

    [Fact]
    public async Task Version_change_is_detected_against_persisted_snapshot()
    {
        var previous=new DriverHealthReport(Guid.NewGuid(),Now.AddDays(-1),[Driver("a","GPU",version:"1.0")],[],[],[]); var repository=new MemoryRepository(previous);
        var analyzer=new DriverConflictAnalyzer(new SnapshotProvider(new(Now,"X64",[Driver("a","GPU",version:"2.0")],[],true,[])),repository,NullLogger<DriverConflictAnalyzer>.Instance);
        var report=await analyzer.AnalyzeAsync(CancellationToken.None);
        Assert.Equal("VersionChanged",Assert.Single(report.Changes).ChangeType); Assert.Equal(DriverFindingKind.RecentlyChanged,Assert.Single(report.Findings).Kind); Assert.Same(report,repository.Saved);
    }

    [Fact]
    public async Task Provider_normalizes_identity_redacts_user_and_preserves_non_admin_state()
    {
        var user=Environment.UserName; var json=$$"""{"architecture":"X64","devices":[{"deviceId":"PCI\\VEN_1234","name":"{{user}} device","class":"Net","manufacturer":"Vendor","provider":"Vendor","version":"1.2","driverDate":"2026-01-01T00:00:00Z","infName":"oem1.inf","present":true,"enabled":true,"problemCode":0,"signed":true,"signatureStatus":"Valid","hardwareId":"PCI\\VEN_1234","architecture":"X64"}],"events":[]}""";
        var snapshot=await new WindowsDriverInventoryProvider(new JsonRunner(json),new Admin(false)).CollectAsync(CancellationToken.None);
        var item=Assert.Single(snapshot.Drivers); Assert.DoesNotContain(user,item.DeviceName,StringComparison.OrdinalIgnoreCase); Assert.Equal(20,item.DeviceKey.Length); Assert.False(snapshot.IsAdministrator); Assert.NotEmpty(snapshot.Limitations);
    }

    [Fact]
    public async Task Sqlite_repository_round_trip_preserves_inventory_findings_and_events()
    {
        var path=Path.Combine(Path.GetTempPath(),$"waid-drivers-{Guid.NewGuid():N}.db"); try { var database=new WaidDatabase($"Data Source={path};Pooling=False"); await database.InitializeAsync(CancellationToken.None); var repository=new SqliteDriverHealthRepository(database); var snapshot=new DriverInventorySnapshot(Now,"X64",[Driver("a","GPU")],[new(Now,219,"Kernel-PnP","a","load failure")],true,[]); var report=new DriverHealthReport(Guid.NewGuid(),Now,snapshot.Drivers,DriverConflictAnalyzer.Evaluate(snapshot),[],[]); await repository.SaveAsync(snapshot,report,CancellationToken.None); var loaded=await repository.GetLatestAsync(CancellationToken.None); Assert.Equal(report.Id,loaded!.Id); Assert.Single(loaded.Inventory); Assert.Single(loaded.Findings); } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
    }

    private static DriverInventoryItem Driver(string key,string name,string hardware="",bool present=true,bool signed=true,int problem=0,DateTimeOffset? date=null,string version="1.0")=>new(key,name,"System","Vendor","Vendor",version,date??Now.AddMonths(-1),"oem1.inf",null,present,problem!=22,problem,signed,signed?"Valid":"Unsigned",string.IsNullOrEmpty(hardware)?key:hardware,"X64");
    private sealed class SnapshotProvider(DriverInventorySnapshot snapshot):IDriverInventoryProvider { public Task<DriverInventorySnapshot> CollectAsync(CancellationToken cancellationToken)=>Task.FromResult(snapshot); }
    private sealed class MemoryRepository(DriverHealthReport? latest):IDriverHealthRepository { public DriverHealthReport? Saved{get;private set;} public Task<DriverHealthReport?> GetLatestAsync(CancellationToken cancellationToken)=>Task.FromResult(latest); public Task SaveAsync(DriverInventorySnapshot snapshot,DriverHealthReport report,CancellationToken cancellationToken){Saved=report;return Task.CompletedTask;} }
    private sealed class JsonRunner(string json):IPowerShellRunner { public Task<PowerShellResult> RunAsync(string script,IReadOnlyDictionary<string,object?> parameters,CancellationToken token)=>Task.FromResult(new PowerShellResult([json],[])); }
    private sealed class Admin(bool value):IAdministratorService { public bool IsAdministrator()=>value; }
}
