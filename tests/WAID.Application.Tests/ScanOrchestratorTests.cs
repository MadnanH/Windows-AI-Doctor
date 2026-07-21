using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Null(repository.Saved);
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
        public async Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) { await Task.Delay(TimeSpan.FromSeconds(2), token); return []; }
    }
    private sealed class PermissionScanner : ISystemScanner
    {
        public string Id => "permission"; public string DisplayName => "Permission";
        public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token) => throw new UnauthorizedAccessException();
    }
    private sealed class RecordingRepository : IScanRepository
    {
        public ScanSession? Saved { get; private set; }
        public Task SaveAsync(ScanSession session, CancellationToken token) { Saved = session; return Task.CompletedTask; }
        public Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken token) => Task.FromResult<IReadOnlyList<ScanSession>>([]);
    }
}
