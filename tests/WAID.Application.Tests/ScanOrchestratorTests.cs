using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
namespace WAID.Application.Tests;
public sealed class ScanOrchestratorTests
{
    [Fact] public async Task Runs_scanners_persists_and_completes_session()
    {
        var repository = new RecordingRepository();
        var result = await new ScanOrchestrator([new FakeScanner()], repository, TimeProvider.System).RunAsync(false, null, CancellationToken.None);
        Assert.True(result.IsCompleted); Assert.Single(result.Findings); Assert.Same(result, repository.Saved);
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
