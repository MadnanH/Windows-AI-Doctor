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
    private sealed class RecordingRepository : IScanRepository
    {
        public ScanSession? Saved { get; private set; }
        public Task SaveAsync(ScanSession session, CancellationToken token) { Saved = session; return Task.CompletedTask; }
        public Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken token) => Task.FromResult<IReadOnlyList<ScanSession>>([]);
    }
}
