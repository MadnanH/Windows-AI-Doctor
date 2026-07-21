using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using Microsoft.Extensions.Logging;
namespace WAID.Application.Services;
public sealed class ScanOrchestrator(
    IEnumerable<ISystemScanner> scanners,
    IScanRepository repository,
    TimeProvider timeProvider,
    ILogger<ScanOrchestrator> logger)
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
            try
            {
                session.AddFindings(await available[index]
                    .ScanAsync(new(session.Id, isAdministrator, started), cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scanner {ScannerId} failed", available[index].Id);
                session.AddFindings([
                    new DiagnosticFinding(
                        available[index].Id,
                        "SCANNER_FAILED",
                        $"{available[index].DisplayName} could not complete",
                        "This diagnostic check encountered an unexpected error. Other checks continued.",
                        DiagnosticSeverity.Warning,
                        evidence: new Dictionary<string, string>
                        {
                            ["exceptionType"] = exception.GetType().Name
                        })
                ]);
            }
        }
        session.Complete(timeProvider.GetUtcNow());
        await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        progress?.Report(new("Complete", available.Length, available.Length));
        return session;
    }
}
public sealed record ScanProgress(string CurrentScanner, int CompletedScanners, int TotalScanners) { public double Percentage => TotalScanners == 0 ? 100 : CompletedScanners * 100d / TotalScanners; }
