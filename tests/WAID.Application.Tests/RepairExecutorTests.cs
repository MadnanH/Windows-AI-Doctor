using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;

namespace WAID.Application.Tests;

public sealed class RepairExecutorTests
{
    [Fact]
    public async Task Rejects_unconfirmed_repair_before_any_system_action()
    {
        var fixture = new Fixture();

        var transaction = await fixture.Executor.ExecuteAsync("waid.test", null, false, CancellationToken.None);

        Assert.Equal(RepairTransactionStatus.Failed, transaction.Status);
        Assert.False(fixture.Module.WasExecuted);
        Assert.Equal(0, fixture.Backup.CallCount);
        Assert.Single(fixture.History.Transactions);
    }

    [Fact]
    public async Task Rejects_repair_when_process_is_not_administrator()
    {
        var fixture = new Fixture { Administrator = { IsAdmin = false } };

        var transaction = await fixture.Executor.ExecuteAsync("waid.test", null, true, CancellationToken.None);

        Assert.Equal(RepairTransactionStatus.Failed, transaction.Status);
        Assert.Contains("Administrator", transaction.Result!.Summary);
        Assert.False(fixture.Module.WasExecuted);
    }

    [Fact]
    public async Task Creates_restore_point_and_backup_before_successful_execution()
    {
        var fixture = new Fixture();

        var transaction = await fixture.Executor.ExecuteAsync("waid.test", null, true, CancellationToken.None);

        Assert.Equal(RepairTransactionStatus.Succeeded, transaction.Status);
        Assert.Equal(1, fixture.RestorePoint.CreateCallCount);
        Assert.Equal(1, fixture.Backup.CallCount);
        Assert.True(fixture.Module.WasExecuted);
        Assert.NotNull(transaction.RestorePointDescription);
        Assert.NotNull(transaction.BackupLocation);
    }

    [Fact]
    public async Task Rolls_back_backup_when_module_reports_failure()
    {
        var fixture = new Fixture();
        fixture.Module.Result = RepairResult.Failure("Synthetic failure");

        var transaction = await fixture.Executor.ExecuteAsync("waid.test", null, true, CancellationToken.None);

        Assert.Equal(RepairTransactionStatus.RolledBack, transaction.Status);
        Assert.True(transaction.Result!.RollbackAttempted);
        Assert.True(transaction.Result.RollbackSucceeded);
        Assert.Equal(1, fixture.Rollback.CallCount);
    }

    [Fact]
    public async Task Continues_with_backup_when_system_restore_is_unavailable()
    {
        var fixture = new Fixture();
        fixture.RestorePoint.Available = false;

        var transaction = await fixture.Executor.ExecuteAsync("waid.test", null, true, CancellationToken.None);

        Assert.Equal(RepairTransactionStatus.Succeeded, transaction.Status);
        Assert.Null(transaction.RestorePointDescription);
        Assert.Equal(1, fixture.Backup.CallCount);
    }

    [Fact]
    public async Task Does_not_execute_when_required_backup_is_incomplete()
    {
        var fixture = new Fixture();
        fixture.Backup.Complete = false;

        var transaction = await fixture.Executor.ExecuteAsync("waid.test", null, true, CancellationToken.None);

        Assert.Equal(RepairTransactionStatus.Failed, transaction.Status);
        Assert.False(fixture.Module.WasExecuted);
        Assert.Contains("incomplete", transaction.Result!.Details!, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Executor = new(
                new RepairRegistry([Module]), Administrator, RestorePoint, Backup, Rollback,
                History, TimeProvider.System, NullLogger<RepairExecutor>.Instance);
        }

        public FakeModule Module { get; } = new();
        public FakeAdministrator Administrator { get; } = new();
        public FakeRestorePoint RestorePoint { get; } = new();
        public FakeBackup Backup { get; } = new();
        public FakeRollback Rollback { get; } = new();
        public FakeHistory History { get; } = new();
        public RepairExecutor Executor { get; }
    }

    private sealed class FakeModule : IRepairModule
    {
        public string Id => "waid.test";
        public string DisplayName => "Test repair";
        public string Description => "Test repair plan";
        public RepairPolicy Policy { get; } = new(SafetyLevel.High);
        public bool WasExecuted { get; private set; }
        public RepairResult Result { get; set; } = RepairResult.Success("Repaired");
        public Task<RepairPlan> CreatePlanAsync(DiagnosticFinding? finding, CancellationToken token) =>
            Task.FromResult(new RepairPlan([new(RepairResourceKind.RegistryKey, @"HKLM\Software\WAID")], Description));
        public Task<RepairResult> ExecuteAsync(RepairExecutionContext context, CancellationToken token)
        {
            WasExecuted = true;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeAdministrator : IAdministratorService
    {
        public bool IsAdmin { get; set; } = true;
        public bool IsAdministrator() => IsAdmin;
    }

    private sealed class FakeRestorePoint : IRestorePointManager
    {
        public bool Available { get; set; } = true;
        public int CreateCallCount { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(Available);
        public Task<RestorePointResult> CreateAsync(string description, CancellationToken token)
        {
            CreateCallCount++;
            return Task.FromResult(new RestorePointResult(true, description));
        }
    }

    private sealed class FakeBackup : IBackupManager
    {
        public int CallCount { get; private set; }
        public bool Complete { get; set; } = true;
        public Task<BackupSnapshot> CreateAsync(Guid id, IReadOnlyCollection<RepairResource> resources, CancellationToken token)
        {
            CallCount++;
            return Task.FromResult(Complete
                ? new BackupSnapshot("C:\\backup", [new(resources.Single(), "C:\\backup\\item.reg")], [])
                : new BackupSnapshot("C:\\backup", [], ["Registry export failed"]));
        }
    }

    private sealed class FakeRollback : IRollbackManager
    {
        public int CallCount { get; private set; }
        public Task<RollbackResult> RollbackAsync(BackupSnapshot snapshot, CancellationToken token)
        {
            CallCount++;
            return Task.FromResult(new RollbackResult(true, ["restored"], []));
        }
    }

    private sealed class FakeHistory : IRepairHistoryRepository
    {
        public List<RepairTransaction> Transactions { get; } = [];
        public Task SaveAsync(RepairTransaction transaction, CancellationToken token) { Transactions.Add(transaction); return Task.CompletedTask; }
        public Task<IReadOnlyList<RepairHistoryEntry>> GetRecentAsync(int count, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<RepairHistoryEntry>>([]);
    }
}
