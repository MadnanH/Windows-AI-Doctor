using WAID.Domain.Diagnostics;
using WAID.Domain.Settings;
using WAID.Infrastructure.Persistence;
using WAID.Diagnosis;
using WAID.Health;
using WAID.Application.Services;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Tests;

public sealed class SqliteRepositoriesTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"waid-tests-{Guid.NewGuid():N}.db");
    private WaidDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = new WaidDatabase($"Data Source={_databasePath};Foreign Keys=True;Pooling=False");
        await _database.InitializeAsync(CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Scan_round_trip_preserves_sessions_findings_and_evidence()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        var session = new ScanSession(Guid.NewGuid(), started);
        var finding = new DiagnosticFinding(
            "storage", "STORAGE_LOW", "Low storage", "Less than ten percent remains.",
            DiagnosticSeverity.Warning, "waid.cleanup",
            new Dictionary<string, string> { ["freeBytes"] = "1024" });
        session.AddFindings([finding]);
        session.Complete(started.AddSeconds(5));
        var repository = new SqliteScanRepository(_database);

        await repository.SaveAsync(session, CancellationToken.None);
        var loaded = Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None));

        Assert.Equal(session.Id, loaded.Id);
        Assert.Equal(session.CompletedAtUtc, loaded.CompletedAtUtc);
        var loadedFinding = Assert.Single(loaded.Findings);
        Assert.Equal(finding.Id, loadedFinding.Id);
        Assert.Equal("1024", loadedFinding.Evidence["freeBytes"]);
    }

    [Fact]
    public async Task Settings_round_trip_validated_values()
    {
        var repository = new SqliteSettingsRepository(_database);
        var settings = new ApplicationSettings
        {
            EnableAiAnalysis = true,
            Theme = "Dark",
            ScanTimeoutSeconds = 300
        };

        await repository.SaveAsync(settings, CancellationToken.None);
        var loaded = await repository.GetAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task Repair_history_round_trip_preserves_safety_metadata()
    {
        var created = DateTimeOffset.UtcNow.AddSeconds(-5);
        var transaction = new WAID.Domain.Repairs.RepairTransaction(Guid.NewGuid(), "waid.sfc", created);
        transaction.BeginPreparation();
        transaction.RecordBackup("C:\\waid-backup");
        transaction.RecordRestorePoint("Before SFC");
        transaction.BeginExecution();
        transaction.Complete(WAID.Domain.Repairs.RepairResult.Success("SFC completed"), created.AddSeconds(5));
        var repository = new SqliteRepairHistoryRepository(_database);

        await repository.SaveAsync(transaction, CancellationToken.None);
        var loaded = Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None));

        Assert.Equal(transaction.Id, loaded.TransactionId);
        Assert.Equal(WAID.Domain.Repairs.RepairTransactionStatus.Succeeded, loaded.Status);
        Assert.Equal("C:\\waid-backup", loaded.BackupLocation);
        Assert.Equal("Before SFC", loaded.RestorePointDescription);
    }

    [Fact]
    public async Task Diagnosis_round_trip_preserves_health_and_findings()
    {
        var scan = new ScanSession(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-1));
        var finding = new DiagnosticFinding("waid.smart", "SMART_WARNING", "Drive warning", "SMART predicts failure.", DiagnosticSeverity.Critical);
        scan.AddFindings([finding]);
        scan.Complete(DateTimeOffset.UtcNow);
        await new SqliteScanRepository(_database).SaveAsync(scan, CancellationToken.None);
        var report = new AIReport(DateTimeOffset.UtcNow, "Storage requires attention.", new HealthScore(100, 100, 100, 100, 100, 70, 100, 96), [], [], [finding]);
        var repository = new SqliteDiagnosisRepository(_database);

        await repository.SaveAsync(scan.Id, report, CancellationToken.None);
        var loaded = await repository.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(96, loaded.Health.Overall);
        Assert.Equal("SMART_WARNING", Assert.Single(loaded.Findings).Code);
    }

    [Fact]
    public async Task Continuous_health_schedule_snapshot_and_approval_round_trip()
    {
        var schedules=new SqliteScanScheduleRepository(_database);var schedule=new ScanSchedule(true,ScheduleFrequency.Weekly,TimeSpan.FromHours(1),DayOfWeek.Monday,new(8,30),true,true,DateTimeOffset.UtcNow);await schedules.SaveAsync(schedule,CancellationToken.None);Assert.Equal(schedule,await schedules.GetAsync(CancellationToken.None));
        var snapshots=new SqliteHealthSnapshotRepository(_database);var snapshot=new HealthSnapshot(Guid.NewGuid(),DateTimeOffset.UtcNow,new HealthScore(90,90,90,90,90,90,90,90),[],MonitoringState.Running);await snapshots.SaveAsync(snapshot,CancellationToken.None);Assert.Equal(snapshot.Id,Assert.Single(await snapshots.GetRecentAsync(1,CancellationToken.None)).Id);
        var approvals=new SqliteRepairApprovalRepository(_database);var approval=new RepairApproval(Guid.NewGuid(),"waid.dism",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,true,"evidence",["action"]);await approvals.SaveAsync(approval,CancellationToken.None);Assert.Equal(approval.Id,Assert.Single(await approvals.GetRecentAsync(1,CancellationToken.None)).Id);
    }

    [Fact]
    public async Task Scanner_execution_round_trip_preserves_provenance_evidence_failure_and_resources()
    {
        var started=DateTimeOffset.UtcNow.AddSeconds(-1);var session=new ScanSession(Guid.NewGuid(),started);var finding=new DiagnosticFinding("scanner","CODE","Title","Detail",DiagnosticSeverity.Warning,evidence:new Dictionary<string,string>{{"key","value"}});session.AddFindings([finding]);session.Complete(DateTimeOffset.UtcNow);
        var observation=new ScannerObservation("temperature","42",started,"provider:counter",new Dictionary<string,string>{{"unit","C"}});
        var execution=new ScannerExecutionRecord(Guid.NewGuid(),session.Id,"scanner","Scanner","Hardware","2.1.0",ScannerExecutionStatus.Success,started,session.CompletedAtUtc!.Value,1000,1,null,"Completed",new(2048,12.5),[observation],[finding]);
        var repository=new SqliteScanRepository(_database);await repository.SaveAsync(session,[execution],CancellationToken.None);
        var loaded=Assert.Single(await repository.GetExecutionsAsync(session.Id,CancellationToken.None));
        Assert.Equal("2.1.0",loaded.Version);Assert.Equal(1000,loaded.DurationMilliseconds);Assert.Equal(2048,loaded.Resources.ManagedMemoryDeltaBytes);Assert.Equal("provider:counter",Assert.Single(loaded.Observations).SourceReference);Assert.Equal(finding.Id,Assert.Single(loaded.Findings).Id);
    }

    [Fact]
    public void Scan_data_sanitizer_redacts_sensitive_evidence_before_persistence()
    {
        var finding=new DiagnosticFinding("scanner","CODE","User C:\\Users\\person","token=abc",DiagnosticSeverity.Warning,evidence:new Dictionary<string,string>{{"serialNumber","ABC"},{"path",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}});
        var output=new ScannerOutput([new("password","secret=abc",DateTimeOffset.UtcNow,"source")],[finding]);
        var sanitized=new WAID.Infrastructure.Diagnostics.ScanDataSanitizer().Sanitize(ScannerMetadata.Legacy("scanner","Scanner"),output);
        Assert.Equal("[REDACTED]",Assert.Single(sanitized.Observations).Value);Assert.Equal("[REDACTED]",Assert.Single(sanitized.Findings).Evidence["serialNumber"]);Assert.DoesNotContain("token=abc",sanitized.Findings.Single().Description,StringComparison.OrdinalIgnoreCase);
    }
}
