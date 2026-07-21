using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
namespace WAID.Application.Services;
public sealed class ScanOrchestrator(IEnumerable<ISystemScanner> scanners, IScanRepository repository, TimeProvider timeProvider)
{
    public async Task<ScanSession> RunAsync(bool isAdministrator, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var session = new ScanSession(Guid.NewGuid(), started);
        var available = scanners.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < available.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(available[index].DisplayName, index, available.Length));
            session.AddFindings(await available[index].ScanAsync(new(session.Id, isAdministrator, started), cancellationToken).ConfigureAwait(false));
        }
        session.Complete(timeProvider.GetUtcNow());
        await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        progress?.Report(new("Complete", available.Length, available.Length));
        return session;
    }
}
public sealed record ScanProgress(string CurrentScanner, int CompletedScanners, int TotalScanners) { public double Percentage => TotalScanners == 0 ? 100 : CompletedScanners * 100d / TotalScanners; }
