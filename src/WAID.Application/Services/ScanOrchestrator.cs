using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;

namespace WAID.Application.Services;

public enum ScannerExecutionStatus { Success, Unavailable, TimedOut, PermissionDenied, Cancelled, Failed }

public sealed record ScannerExecutionPolicy(TimeSpan Timeout, int SafeRetryCount = 0)
{
    public ScannerExecutionPolicy Validate() => Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(10)
        ? throw new InvalidOperationException("Scanner timeout must be between zero and ten minutes.")
        : SafeRetryCount is < 0 or > 1
            ? throw new InvalidOperationException("A scanner may be retried at most once.")
            : this;
}

public sealed class ScannerPolicyRegistry
{
    private readonly IReadOnlyDictionary<string, ScannerExecutionPolicy> _overrides;
    public ScannerPolicyRegistry(ScannerExecutionPolicy defaultPolicy, IReadOnlyDictionary<string, ScannerExecutionPolicy>? overrides = null)
    {
        DefaultPolicy = defaultPolicy.Validate();
        _overrides = overrides ?? new Dictionary<string, ScannerExecutionPolicy>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in _overrides.Values) policy.Validate();
    }
    public ScannerExecutionPolicy DefaultPolicy { get; }
    public ScannerExecutionPolicy For(string scannerId) => _overrides.TryGetValue(scannerId, out var policy) ? policy : DefaultPolicy;
}

public sealed class ScanOrchestrator(
    IEnumerable<ISystemScanner> scanners,
    IScanRepository repository,
    TimeProvider timeProvider,
    ILogger<ScanOrchestrator> logger,
    ScannerPolicyRegistry? policies = null)
{
    private readonly ScannerPolicyRegistry _policies = policies ?? new(new(TimeSpan.FromSeconds(45)));

    public async Task<ScanSession> RunAsync(bool isAdministrator, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var session = new ScanSession(Guid.NewGuid(), started);
        var available = scanners.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < available.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scanner = available[index];
            progress?.Report(new(scanner.DisplayName, index, available.Length, null, "Running"));
            var status = await RunScannerAsync(scanner, session, isAdministrator, started, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(scanner.DisplayName, index + 1, available.Length, status, Describe(status)));
        }
        session.Complete(timeProvider.GetUtcNow());
        await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        progress?.Report(new("Complete", available.Length, available.Length, ScannerExecutionStatus.Success, "Scan complete"));
        return session;
    }

    private async Task<ScannerExecutionStatus> RunScannerAsync(ISystemScanner scanner, ScanSession session, bool isAdministrator, DateTimeOffset started, CancellationToken cancellationToken)
    {
        var policy = _policies.For(scanner.Id);
        for (var attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(policy.Timeout);
            try
            {
                var findings = await scanner.ScanAsync(new(session.Id, isAdministrator, started), timeout.Token).ConfigureAwait(false);
                session.AddFindings(findings);
                var unavailable = findings.Any(f => f.Code == "SCANNER_UNAVAILABLE");
                logger.LogInformation("Scanner {ScannerId} completed with status {Status} and {FindingCount} findings", scanner.Id, unavailable ? ScannerExecutionStatus.Unavailable : ScannerExecutionStatus.Success, findings.Count);
                return unavailable ? ScannerExecutionStatus.Unavailable : ScannerExecutionStatus.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                AddDegraded(session, scanner, "SCANNER_TIMED_OUT", "timed out", ScannerExecutionStatus.TimedOut, policy.Timeout, attempt);
                return ScannerExecutionStatus.TimedOut;
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception, "Scanner {ScannerId} was denied permission", scanner.Id);
                AddDegraded(session, scanner, "SCANNER_PERMISSION_DENIED", "was denied permission", ScannerExecutionStatus.PermissionDenied, policy.Timeout, attempt);
                return ScannerExecutionStatus.PermissionDenied;
            }
            catch (IOException exception) when (attempt < policy.SafeRetryCount)
            {
                logger.LogWarning(exception, "Read-only scanner {ScannerId} transiently failed; performing bounded retry {Attempt}", scanner.Id, attempt + 1);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scanner {ScannerId} failed", scanner.Id);
                session.AddFindings([Failure(scanner, "SCANNER_FAILED", "encountered an unexpected error", ScannerExecutionStatus.Failed, policy.Timeout, attempt, exception.GetType().Name)]);
                return ScannerExecutionStatus.Failed;
            }
        }
    }

    private void AddDegraded(ScanSession session, ISystemScanner scanner, string code, string reason, ScannerExecutionStatus status, TimeSpan timeout, int attempt)
    {
        logger.LogWarning("Scanner {ScannerId} completed with degraded status {Status}; partial evidence count {PartialEvidenceCount}", scanner.Id, status, 0);
        session.AddFindings([Failure(scanner, code, reason, status, timeout, attempt, null)]);
    }

    private static DiagnosticFinding Failure(ISystemScanner scanner, string code, string reason, ScannerExecutionStatus status, TimeSpan timeout, int attempt, string? exceptionType) =>
        new(scanner.Id, code, $"{scanner.DisplayName} {reason}", "This check did not produce a complete result. Other checks continued; unavailable data is not treated as healthy.", DiagnosticSeverity.Warning,
            evidence: new Dictionary<string, string> {
                ["status"] = status.ToString(), ["reason"] = reason, ["timeoutMilliseconds"] = ((long)timeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["attempts"] = (attempt + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), ["partialEvidenceCount"] = "0", ["exceptionType"] = exceptionType ?? "None" });

    private static string Describe(ScannerExecutionStatus status) => status switch
    {
        ScannerExecutionStatus.Success => "Completed",
        ScannerExecutionStatus.Unavailable => "Unavailable on this system",
        ScannerExecutionStatus.TimedOut => "Timed out; remaining checks continued",
        ScannerExecutionStatus.PermissionDenied => "Permission denied",
        ScannerExecutionStatus.Cancelled => "Cancelled",
        _ => "Failed; remaining checks continued"
    };
}

public sealed record ScanProgress(string CurrentScanner, int CompletedScanners, int TotalScanners,
    ScannerExecutionStatus? Status = null, string? Detail = null)
{
    public double Percentage => TotalScanners == 0 ? 100 : CompletedScanners * 100d / TotalScanners;
}
