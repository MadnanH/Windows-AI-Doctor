using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WAID.Testing;
namespace WAID.Application.Tests;
public sealed class ScanOrchestratorTests
{
    [Fact] public async Task Runs_scanners_persists_and_completes_session()
    {
        var repository = new RecordingRepository();
        var result = await CreateOrchestrator([new FakeScanner()], repository).RunAsync(false, null, CancellationToken.None);
        Assert.True(result.IsCompleted); Assert.Single(result.Findings); Assert.Same(result, repository.Saved);
    }
    [Fact] public async Task Continues_when_one_scanner_fails()
    {
        var repository = new RecordingRepository();
        var result = await CreateOrchestrator([new ThrowingScanner(), new FakeScanner()], repository)
            .RunAsync(false, null, CancellationToken.None);

        Assert.True(result.IsCompleted);
        Assert.Contains(result.Findings, finding => finding.Code == "SCANNER_FAILED");
        Assert.Contains(result.Findings, finding => finding.Code == "TEST001");
    }

    [Fact] public async Task Cancellation_is_not_converted_to_a_finding()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new RecordingRepository();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateOrchestrator([new FakeScanner()], repository).RunAsync(false, null, cancellation.Token));
        Assert.NotNull(repository.Saved);
        Assert.True(repository.Saved.IsCompleted);
        Assert.Empty(repository.Saved.Findings);
    }

    [Fact] public async Task Timeout_is_reported_and_does_not_block_remaining_scanners()
    {
        var repository = new RecordingRepository();
        var policy = new ScannerPolicyRegistry(new ScannerExecutionPolicy(TimeSpan.FromMilliseconds(20)));
        var orchestrator = new ScanOrchestrator([new SlowScanner(), new FakeScanner()], repository, TimeProvider.System, NullLogger<ScanOrchestrator>.Instance, policy);
        var result = await orchestrator.RunAsync(false, null, CancellationToken.None);
        var timeout = Assert.Single(result.Findings.Where(f => f.Code == "SCANNER_TIMED_OUT"));
        Assert.Equal("TimedOut", timeout.Evidence["status"]);
        Assert.Contains(result.Findings, f => f.Code == "TEST001");
    }

    [Fact] public async Task Permission_denial_has_a_distinct_status()
    {
        var result = await CreateOrchestrator([new PermissionScanner()], new RecordingRepository()).RunAsync(false, null, CancellationToken.None);
        Assert.Contains(result.Findings, f => f.Code == "SCANNER_PERMISSION_DENIED" && f.Evidence["status"] == "PermissionDenied");
    }

    [Fact] public async Task Failed_dependency_skips_dependent_without_running_it()
    {
        var dependent = new DependentScanner("dependent", ["broken"]);
        var records = new RecordingRunRepository();
        var orchestrator = new ScanOrchestrator([new ThrowingScanner(), dependent], new RecordingRepository(), TimeProvider.System, NullLogger<ScanOrchestrator>.Instance, scanRuns: records);
        await orchestrator.RunAsync(false, null, CancellationToken.None);
        Assert.False(dependent.WasRun);
        Assert.Equal(ScannerExecutionStatus.Skipped, records.Executions.Single(item => item.ScannerId == "dependent").Status);
    }

    [Fact] public async Task Transient_io_failure_retries_once_and_records_attempts()
    {
        var records = new RecordingRunRepository(); var scanner = new RetryScanner();
        var policies = new ScannerPolicyRegistry(new(TimeSpan.FromSeconds(1), 1));
        var orchestrator = new ScanOrchestrator([scanner], new RecordingRepository(), TimeProvider.System, NullLogger<ScanOrchestrator>.Instance, policies, scanRuns: records);
        await orchestrator.RunAsync(false, null, CancellationToken.None);
        Assert.Equal(2, scanner.Attempts); Assert.Equal(2, Assert.Single(records.Executions).Attempts); Assert.Equal(ScannerExecutionStatus.Success, records.Executions[0].Status);
    }

    [Fact] public async Task Missing_administrator_prerequisite_is_an_honest_skip()
    {
        var scanner=new AdministratorScanner();var records=new RecordingRunRepository();
        await new ScanOrchestrator([scanner],new RecordingRepository(),TimeProvider.System,NullLogger<ScanOrchestrator>.Instance,scanRuns:records).RunAsync(false,null,CancellationToken.None);
        Assert.False(scanner.WasRun);var execution=Assert.Single(records.Executions);Assert.Equal(ScannerExecutionStatus.Skipped,execution.Status);Assert.Equal("SCANNER_PREREQUISITE",execution.FailureCode);
    }

    [Fact] public async Task Parallelism_is_bounded_and_independent_scanners_overlap()
    {
        var tracker = new ConcurrencyTracker(); var scanners = Enumerable.Range(0, 4).Select(index => new ConcurrentScanner($"parallel-{index}", tracker)).ToArray();
        var policies = new ScannerPolicyRegistry(new(TimeSpan.FromSeconds(5)), maximumParallelism: 2);
        var run = new ScanOrchestrator(scanners, new RecordingRepository(), TimeProvider.System, NullLogger<ScanOrchestrator>.Instance, policies).RunAsync(false, null, CancellationToken.None);
        using var watchdog=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await tracker.WaitForPeakAsync(2,watchdog.Token);Assert.Equal(2,tracker.Peak);tracker.Release();await run;
    }

    private static ScanOrchestrator CreateOrchestrator(IEnumerable<ISystemScanner> scanners, IScanRepository repository) =>
        new(scanners, repository, TimeProvider.System, NullLogger<ScanOrchestrator>.Instance);

    private sealed class ThrowingScanner : ISystemScanner
    {
        public string Id => "broken";
        public string DisplayName => "Broken";
        public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) =>
            throw new InvalidOperationException("Synthetic scanner failure");
    }
    private sealed class FakeScanner : ISystemScanner
    {
        public string Id => "test"; public string DisplayName => "Test";
        public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) => Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([new DiagnosticFinding(Id, "TEST001", "Finding", "Description", DiagnosticSeverity.Information)]);
    }
    private sealed class SlowScanner : ISystemScanner
    {
        public string Id => "slow"; public string DisplayName => "Slow";
        public async Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) { await Task.Delay(Timeout.InfiniteTimeSpan, token); return []; }
    }
    private sealed class PermissionScanner : ISystemScanner
    {
        public string Id => "permission"; public string DisplayName => "Permission";
        public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) => throw new UnauthorizedAccessException();
    }
    private sealed class DependentScanner(string id, IReadOnlyList<string> dependencies) : ISystemScanner
    { public string Id => id; public string DisplayName => id; public bool WasRun { get; private set; } public ScannerMetadata Metadata => new(Id, DisplayName, "dependency test", "Test", new(1,0), [], dependencies); public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) { WasRun=true; return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([]); } }
    private sealed class RetryScanner : ISystemScanner
    { public int Attempts { get; private set; } public string Id=>"retry"; public string DisplayName=>"Retry"; public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context,CancellationToken token) { Attempts++; if(Attempts==1)throw new IOException("transient"); return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([]); } }
    private sealed class AdministratorScanner:ISystemScanner
    { public string Id=>"administrator";public string DisplayName=>"Administrator";public bool WasRun{get;private set;}public ScannerMetadata Metadata=>new(Id,DisplayName,"administrator test","Test",new(1,0),[ScannerPrerequisites.Administrator],[]);public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context,CancellationToken token){WasRun=true;return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([]);} }
    private sealed class ConcurrencyTracker { private readonly AsyncTestGate _release=new();private readonly TaskCompletionSource<int> _peakReached=new(TaskCreationOptions.RunContinuationsAsynchronously);private int _active,_peak; public int Peak=>_peak; public void Enter(){var active=Interlocked.Increment(ref _active);int current;while(active>(current=_peak))Interlocked.CompareExchange(ref _peak,active,current);if(active>=2)_peakReached.TrySetResult(active);} public void Exit()=>Interlocked.Decrement(ref _active);public async Task WaitForPeakAsync(int expected,CancellationToken token){var peak=await _peakReached.Task.WaitAsync(token);Assert.True(peak>=expected);}public Task WaitAsync(CancellationToken token)=>_release.WaitForReleaseAsync(token);public void Release()=>_release.Release(); }
    private sealed class ConcurrentScanner(string id,ConcurrencyTracker tracker):ISystemScanner
    { public string Id=>id;public string DisplayName=>id;public async Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context,CancellationToken token){tracker.Enter();try{await tracker.WaitAsync(token);return [];}finally{tracker.Exit();}} }
    private sealed class RecordingRunRepository : IScanRunRepository
    { public IReadOnlyList<ScannerExecutionRecord> Executions { get; private set; }=[]; public Task SaveAsync(ScanSession session,IReadOnlyCollection<ScannerExecutionRecord> executions,CancellationToken token){Executions=executions.ToArray();return Task.CompletedTask;} public Task<IReadOnlyList<ScannerExecutionRecord>> GetExecutionsAsync(Guid sessionId,CancellationToken token)=>Task.FromResult(Executions); }
    private sealed class RecordingRepository : IScanRepository
    {
        public ScanSession? Saved { get; private set; }
        public Task SaveAsync(ScanSession session, CancellationToken token) { Saved = session; return Task.CompletedTask; }
        public Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken token) => Task.FromResult<IReadOnlyList<ScanSession>>([]);
    }
}
