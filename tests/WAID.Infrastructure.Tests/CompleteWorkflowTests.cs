using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.Infrastructure.Persistence;
using WAID.KnowledgeBase;

namespace WAID.Infrastructure.Tests;

public sealed class CompleteWorkflowTests
{
    [Fact]
    public async Task Scan_diagnose_recommend_confirm_repair_and_verify_complete_workflow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"waid-workflow-{Guid.NewGuid():N}.db");
        try
        {
            var database = new WaidDatabase($"Data Source={path};Foreign Keys=True;Pooling=False");
            await database.InitializeAsync(CancellationToken.None);
            var scans = new SqliteScanRepository(database);
            var scanner = new ComponentStoreScanner();
            var orchestrator = new ScanOrchestrator([scanner], scans, TimeProvider.System, NullLogger<ScanOrchestrator>.Instance);

            var session = await orchestrator.RunAsync(true, null, CancellationToken.None);
            var loadedSession = Assert.Single(await scans.GetRecentAsync(1, CancellationToken.None));
            Assert.Equal(session.Id, loadedSession.Id);

            var confidence = new ConfidenceEngine();
            var diagnosis = new DiagnosisEngine(new DiagnosticKnowledgeBase(), new RuleEngine(), new CorrelationScanner(new EventCorrelationEngine()), new RootCauseAnalyzer(confidence, new RecommendationEngine(), new ExplanationEngine()), new HealthScoreEngine(), new AIReportBuilder(TimeProvider.System));
            var report = await diagnosis.DiagnoseAsync(loadedSession.Findings, CancellationToken.None);
            var recommendation = Assert.Single(report.RootCauses).Recommendation;
            Assert.Equal("waid.dism", recommendation.RepairId);
            var diagnosisRepository = new SqliteDiagnosisRepository(database);
            await diagnosisRepository.SaveAsync(session.Id, report, CancellationToken.None);
            Assert.Equal(report.Summary, (await diagnosisRepository.GetLatestAsync(CancellationToken.None))!.Summary);

            var module = new VerifiableRepairModule();
            var administrator = new AdministratorGate();
            var history = new SqliteRepairHistoryRepository(database);
            var executor = new RepairExecutor(new RepairRegistry([module]), administrator, new UnavailableRestorePoint(), new NoBackup(), new NoRollback(), history, TimeProvider.System, NullLogger<RepairExecutor>.Instance);
            var rejected = await executor.ExecuteAsync(recommendation.RepairId!, null, false, CancellationToken.None);
            Assert.Equal(RepairTransactionStatus.Failed, rejected.Status);
            Assert.False(module.Executed);

            var repaired = await executor.ExecuteAsync(recommendation.RepairId!, null, true, CancellationToken.None);
            Assert.Equal(RepairTransactionStatus.Succeeded, repaired.Status);
            Assert.True(module.Executed);
            Assert.True(administrator.Checked);
            Assert.Contains((await history.GetRecentAsync(10, CancellationToken.None)), item => item.Status == RepairTransactionStatus.Succeeded);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class ComponentStoreScanner : ISystemScanner
    {
        public string Id => "workflow.scanner";
        public string DisplayName => "Workflow scanner";
        public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) => Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([
            new(Id, "CBS_CORRUPTION", "Component store corruption", "CBS reported corruption.", DiagnosticSeverity.Critical),
            new(Id, "UPDATE_EVENT_20", "Update failed", "Windows Update installation failed.", DiagnosticSeverity.Warning)
        ]);
    }
    private sealed class VerifiableRepairModule : IRepairModule
    {
        public string Id => "waid.dism";
        public string DisplayName => "DISM";
        public string Description => "Verify workflow repair";
        public RepairPolicy Policy { get; } = new(SafetyLevel.High, RequiresRestorePoint: false, RequiresBackup: false, SupportsRollback: false);
        public bool Executed { get; private set; }
        public Task<RepairPlan> CreatePlanAsync(DiagnosticFinding? finding, CancellationToken token) => Task.FromResult(new RepairPlan([], Description));
        public Task<RepairResult> ExecuteAsync(RepairExecutionContext context, CancellationToken token) { Executed = true; return Task.FromResult(RepairResult.Success("Verified")); }
    }
    private sealed class AdministratorGate : IAdministratorService { public bool Checked { get; private set; } public bool IsAdministrator() { Checked = true; return true; } }
    private sealed class UnavailableRestorePoint : IRestorePointManager { public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(false); public Task<RestorePointResult> CreateAsync(string description, CancellationToken token) => throw new InvalidOperationException(); }
    private sealed class NoBackup : IBackupManager { public Task<BackupSnapshot> CreateAsync(Guid transactionId, IReadOnlyCollection<RepairResource> resources, CancellationToken token) => throw new InvalidOperationException(); }
    private sealed class NoRollback : IRollbackManager { public Task<RollbackResult> RollbackAsync(BackupSnapshot snapshot, CancellationToken token) => throw new InvalidOperationException(); }
}
