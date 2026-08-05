using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;

namespace WAID.Application.Services;

public enum ScannerExecutionStatus { Planned, Running, Success, Unavailable, TimedOut, PermissionDenied, Cancelled, Failed, Skipped }

public sealed record ScannerExecutionPolicy(TimeSpan Timeout, int SafeRetryCount = 0)
{
    public ScannerExecutionPolicy Validate() => Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(10)
        ? throw new InvalidOperationException("Scanner timeout must be between zero and ten minutes.")
        : SafeRetryCount is < 0 or > 1 ? throw new InvalidOperationException("A scanner may be retried at most once.") : this;
}

public sealed class ScannerPolicyRegistry
{
    private readonly IReadOnlyDictionary<string, ScannerExecutionPolicy> _overrides;
    public ScannerPolicyRegistry(ScannerExecutionPolicy defaultPolicy, IReadOnlyDictionary<string, ScannerExecutionPolicy>? overrides = null, int maximumParallelism = 3)
    {
        DefaultPolicy = defaultPolicy.Validate(); MaximumParallelism = maximumParallelism is < 1 or > 8 ? throw new InvalidOperationException("Scanner parallelism must be between one and eight.") : maximumParallelism;
        _overrides = overrides ?? new Dictionary<string, ScannerExecutionPolicy>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in _overrides.Values) policy.Validate();
    }
    public ScannerExecutionPolicy DefaultPolicy { get; }
    public int MaximumParallelism { get; }
    public ScannerExecutionPolicy For(ScannerMetadata metadata) => _overrides.TryGetValue(metadata.Id, out var policy) ? policy
        : metadata.RecommendedTimeout is { } timeout ? new ScannerExecutionPolicy(timeout, DefaultPolicy.SafeRetryCount).Validate() : DefaultPolicy;
    public ScannerExecutionPolicy For(string scannerId) => _overrides.TryGetValue(scannerId, out var policy) ? policy : DefaultPolicy;
}

public sealed record ScannerResourceUsage(long ManagedMemoryDeltaBytes, double ProcessCpuMilliseconds);
public sealed record ScannerExecutionRecord(Guid Id, Guid SessionId, string ScannerId, string DisplayName, string Category, string Version,
    ScannerExecutionStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, long DurationMilliseconds, int Attempts,
    string? FailureCode, string? Detail, ScannerResourceUsage Resources, IReadOnlyCollection<ScannerObservation> Observations,
    IReadOnlyCollection<DiagnosticFinding> Findings);

public sealed class ScanOrchestrator(
    IEnumerable<ISystemScanner> scanners,
    IScanRepository repository,
    TimeProvider timeProvider,
    ILogger<ScanOrchestrator> logger,
    ScannerPolicyRegistry? policies = null,
    IOperationContextAccessor? operationContext = null,
    IScanRunRepository? scanRuns = null,
    IScanDataSanitizer? sanitizer = null,
    IEnterprisePolicyService? enterprisePolicy = null,
    IPerformanceTelemetry? performance = null)
{
    private readonly ScannerPolicyRegistry _policies = policies ?? new(new(TimeSpan.FromSeconds(45)));
    private readonly IScanDataSanitizer _sanitizer = sanitizer ?? new PassthroughSanitizer();

    public async Task<ScanSession> RunAsync(bool isAdministrator, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        using var measurement = performance?.Measure("scan.complete", PerformanceArea.Scan);
        var policyDecision=enterprisePolicy?.Evaluate(EnterpriseCapability.Diagnostics);
        if(policyDecision is {Allowed:false})throw new EnterprisePolicyException("WAID-POLICY-DIAGNOSTICS-BLOCKED",$"Diagnostics are blocked by {policyDecision.Source}.","Contact the organization policy administrator.");
        using var operation = operationContext is null ? null : logger.BeginWaidOperation(operationContext, "Scan");
        var started = timeProvider.GetUtcNow(); var session = new ScanSession(Guid.NewGuid(), started);
        var plan = BuildPlan(scanners); var completed = new ConcurrentDictionary<string, ScannerExecutionRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan) progress?.Report(new(item.Metadata.Id, item.Metadata.DisplayName, 0, plan.Count, ScannerExecutionStatus.Planned, "Planned", 0));

        var pending = new HashSet<string>(plan.Select(item => item.Metadata.Id), StringComparer.OrdinalIgnoreCase);
        var cancelled = false;
        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
            var ready = plan.Where(item => pending.Contains(item.Metadata.Id) && item.Metadata.Dependencies.All(dependency => !pending.Contains(dependency)))
                .Take(_policies.MaximumParallelism).ToArray();
            if (ready.Length == 0) throw new InvalidOperationException("Scanner dependency graph cannot make progress.");
            var tasks = ready.Select(item => ExecuteAsync(item, session.Id, isAdministrator, started, completed, progress, plan.Count, cancellationToken)).ToArray();
            var batch = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (var record in batch) { completed[record.ScannerId] = record; pending.Remove(record.ScannerId); }
            if (batch.Any(record => record.Status == ScannerExecutionStatus.Cancelled)) { cancelled = true; break; }
        }

        if (cancelled)
        {
            foreach (var item in plan.Where(item => pending.Contains(item.Metadata.Id)))
            {
                var now = timeProvider.GetUtcNow(); completed[item.Metadata.Id] = new(Guid.NewGuid(), session.Id, item.Metadata.Id, item.Metadata.DisplayName,
                    item.Metadata.Category, item.Metadata.Version.ToString(), ScannerExecutionStatus.Cancelled, now, now, 0, 0, "SCAN_CANCELLED", "Not started because the scan was cancelled.", new(0, 0), [], []);
                progress?.Report(new(item.Metadata.Id, item.Metadata.DisplayName, completed.Count, plan.Count, ScannerExecutionStatus.Cancelled, "Cancelled before execution", 0));
            }
        }

        var orderedRecords = plan.Select(item => completed[item.Metadata.Id]).ToArray();
        foreach (var record in orderedRecords) session.AddFindings(record.Findings);
        session.Complete(timeProvider.GetUtcNow());
        if (scanRuns is null) await repository.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        else await scanRuns.SaveAsync(session, orderedRecords, CancellationToken.None).ConfigureAwait(false);
        var partial = orderedRecords.Any(record => record.Status is not (ScannerExecutionStatus.Success or ScannerExecutionStatus.Unavailable));
        progress?.Report(new("scan", "Complete", orderedRecords.Count(record => record.Status is not ScannerExecutionStatus.Cancelled), plan.Count,
            cancelled ? ScannerExecutionStatus.Cancelled : ScannerExecutionStatus.Success, cancelled ? "Scan cancelled; completed results were saved." : partial ? "Scan completed with skipped or failed checks." : "Scan complete.", 100));
        if (cancelled) throw new OperationCanceledException(cancellationToken);
        return session;
    }

    private async Task<ScannerExecutionRecord> ExecuteAsync(ISystemScanner scanner, Guid sessionId, bool isAdministrator, DateTimeOffset scanStarted,
        IReadOnlyDictionary<string, ScannerExecutionRecord> completed, IProgress<ScanProgress>? progress, int total, CancellationToken token)
    {
        var metadata = scanner.Metadata.Validate(); var now = timeProvider.GetUtcNow();
        var dependencyFailure = metadata.Dependencies.FirstOrDefault(id => !completed.TryGetValue(id, out var dependency) || dependency.Status is not (ScannerExecutionStatus.Success or ScannerExecutionStatus.Unavailable));
        if (dependencyFailure is not null) return Skipped(scanner, sessionId, now, "SCANNER_DEPENDENCY", $"Dependency {dependencyFailure} did not complete successfully.", progress, completed.Count, total);
        var prerequisite = MissingPrerequisite(metadata, isAdministrator);
        if (prerequisite is not null) return Skipped(scanner, sessionId, now, "SCANNER_PREREQUISITE", prerequisite, progress, completed.Count, total);

        progress?.Report(new(metadata.Id, metadata.DisplayName, completed.Count, total, ScannerExecutionStatus.Running, "Running", 0));
        var policy = _policies.For(metadata); var cpuStart = CurrentCpu(); var memoryStart = GC.GetTotalMemory(false);
        var attempt = 0;
        while (true)
        {
            attempt++; using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(policy.Timeout);
            try
            {
                var scannerProgress = new Progress<ScannerStepProgress>(step => progress?.Report(new(metadata.Id, metadata.DisplayName, completed.Count, total, ScannerExecutionStatus.Running, step.Detail, Math.Clamp(step.Percentage, 0, 100))));
                var output = await scanner.ScanDetailedAsync(new(sessionId, isAdministrator, scanStarted, scannerProgress), timeout.Token).ConfigureAwait(false);
                output = ValidateAndSanitize(metadata, output);
                var status = output.Findings.Any(finding => finding.Code == "SCANNER_UNAVAILABLE") ? ScannerExecutionStatus.Unavailable : ScannerExecutionStatus.Success;
                return Complete(metadata, sessionId, now, status, attempt, null, status == ScannerExecutionStatus.Success ? "Completed" : "Unavailable on this system", output, cpuStart, memoryStart, progress, completed.Count, total);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return Complete(metadata, sessionId, now, ScannerExecutionStatus.Cancelled, attempt, "SCAN_CANCELLED", "Cancelled by the user.", new([], []), cpuStart, memoryStart, progress, completed.Count, total); }
            catch (OperationCanceledException)
            { return Failed(metadata, sessionId, now, ScannerExecutionStatus.TimedOut, attempt, "SCANNER_TIMED_OUT", "Timed out; remaining checks continued.", policy, null, cpuStart, memoryStart, progress, completed.Count, total); }
            catch (UnauthorizedAccessException exception)
            { logger.LogWarning(WaidEventIds.ScannerDegraded, exception, "Scanner {ScannerId} was denied permission", metadata.Id); return Failed(metadata, sessionId, now, ScannerExecutionStatus.PermissionDenied, attempt, "SCANNER_PERMISSION_DENIED", "Permission denied.", policy, null, cpuStart, memoryStart, progress, completed.Count, total); }
            catch (IOException exception) when (attempt <= policy.SafeRetryCount)
            { logger.LogWarning(WaidEventIds.ScannerDegraded, exception, "Read-only scanner {ScannerId} transiently failed; bounded retry {Attempt}", metadata.Id, attempt); }
            catch (Exception exception)
            { logger.LogError(WaidEventIds.ScannerDegraded, exception, "Scanner {ScannerId} failed", metadata.Id); return Failed(metadata, sessionId, now, ScannerExecutionStatus.Failed, attempt, "SCANNER_FAILED", "Failed; remaining checks continued.", policy, exception.GetType().Name, cpuStart, memoryStart, progress, completed.Count, total); }
        }
    }

    private ScannerExecutionRecord Complete(ScannerMetadata metadata, Guid sessionId, DateTimeOffset started, ScannerExecutionStatus status, int attempts,
        string? failureCode, string detail, ScannerOutput output, TimeSpan cpuStart, long memoryStart, IProgress<ScanProgress>? progress, int completed, int total)
    {
        var ended = timeProvider.GetUtcNow(); logger.LogInformation(WaidEventIds.ScannerCompleted, "Scanner {ScannerId} version {ScannerVersion} completed with {Status} in {DurationMs} ms", metadata.Id, metadata.Version, status, (long)(ended - started).TotalMilliseconds);
        progress?.Report(new(metadata.Id, metadata.DisplayName, completed + 1, total, status, detail, 100));
        return new(Guid.NewGuid(), sessionId, metadata.Id, metadata.DisplayName, metadata.Category, metadata.Version.ToString(), status, started, ended,
            Math.Max(0, (long)(ended - started).TotalMilliseconds), attempts, failureCode, detail,
            new(Math.Max(0, GC.GetTotalMemory(false) - memoryStart), Math.Max(0, (CurrentCpu() - cpuStart).TotalMilliseconds)), output.Observations, output.Findings);
    }

    private ScannerExecutionRecord Failed(ScannerMetadata metadata, Guid sessionId, DateTimeOffset started, ScannerExecutionStatus status, int attempts,
        string code, string detail, ScannerExecutionPolicy policy, string? exceptionType, TimeSpan cpuStart, long memoryStart, IProgress<ScanProgress>? progress, int completed, int total)
    {
        var finding = new DiagnosticFinding(metadata.Id, code, $"{metadata.DisplayName} did not complete", "This check did not produce a complete result. Other checks continued; unavailable data is not treated as healthy.", DiagnosticSeverity.Warning,
            evidence: new Dictionary<string, string> { ["status"] = status.ToString(), ["timeoutMilliseconds"] = ((long)policy.Timeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture), ["attempts"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture), ["exceptionType"] = exceptionType ?? "None" });
        return Complete(metadata, sessionId, started, status, attempts, code, detail, new([], [finding]), cpuStart, memoryStart, progress, completed, total);
    }

    private ScannerExecutionRecord Skipped(ISystemScanner scanner, Guid sessionId, DateTimeOffset now, string code, string detail, IProgress<ScanProgress>? progress, int completed, int total)
    {
        var metadata = scanner.Metadata; progress?.Report(new(metadata.Id, metadata.DisplayName, completed + 1, total, ScannerExecutionStatus.Skipped, detail, 100));
        return new(Guid.NewGuid(), sessionId, metadata.Id, metadata.DisplayName, metadata.Category, metadata.Version.ToString(), ScannerExecutionStatus.Skipped, now, now, 0, 0, code, detail, new(0, 0), [], []);
    }

    private ScannerOutput ValidateAndSanitize(ScannerMetadata metadata, ScannerOutput output)
    {
        ArgumentNullException.ThrowIfNull(output); if (output.Findings.Count > 10_000 || output.Observations.Count > 50_000) throw new InvalidDataException("Scanner output exceeds safety limits.");
        if (output.Findings.Any(finding => !string.Equals(finding.ScannerId, metadata.Id, StringComparison.OrdinalIgnoreCase) || finding.Evidence.Count > 100 || finding.Evidence.Any(item => item.Key.Length > 200 || item.Value.Length > 16_384))) throw new InvalidDataException("Scanner returned a finding with invalid provenance or evidence.");
        foreach (var observation in output.Observations)
            if (string.IsNullOrWhiteSpace(observation.Key) || observation.Key.Length > 200 || observation.Value.Length > 16_384 || string.IsNullOrWhiteSpace(observation.SourceReference) || observation.Attributes?.Count > 100 || observation.Attributes?.Any(item => item.Key.Length > 200 || item.Value.Length > 16_384) == true) throw new InvalidDataException("Scanner evidence is invalid.");
        return _sanitizer.Sanitize(metadata, output);
    }

    private static TimeSpan CurrentCpu() { using var process = Process.GetCurrentProcess(); return process.TotalProcessorTime; }

    private static string? MissingPrerequisite(ScannerMetadata metadata, bool administrator)
    {
        foreach (var prerequisite in metadata.Prerequisites)
        {
            if (prerequisite.Equals(ScannerPrerequisites.Windows, StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows()) return "Requires Windows.";
            if (prerequisite.Equals(ScannerPrerequisites.Administrator, StringComparison.OrdinalIgnoreCase) && !administrator) return "Requires administrator access.";
            if (prerequisite.Equals(ScannerPrerequisites.PowerShell, StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows()) return "Requires Windows PowerShell integration.";
        }
        return null;
    }

    private static IReadOnlyList<ISystemScanner> BuildPlan(IEnumerable<ISystemScanner> scanners)
    {
        var items = scanners.ToArray(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scanner in items) { var metadata = scanner.Metadata.Validate(); if (!string.Equals(scanner.Id, metadata.Id, StringComparison.OrdinalIgnoreCase) || !ids.Add(metadata.Id)) throw new InvalidOperationException("Scanner metadata contains a duplicate or mismatched ID."); }
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var byId = items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        void Visit(string id) { if (!byId.TryGetValue(id, out var scanner) || visited.Contains(id)) return; if (!visiting.Add(id)) throw new InvalidOperationException("Scanner dependency graph contains a cycle."); foreach (var dependency in scanner.Metadata.Dependencies) Visit(dependency); visiting.Remove(id); visited.Add(id); }
        foreach (var item in items) Visit(item.Id);
        return items.OrderBy(item => item.Metadata.Category, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class PassthroughSanitizer : IScanDataSanitizer { public ScannerOutput Sanitize(ScannerMetadata metadata, ScannerOutput output) => output; }
}

public sealed record ScanProgress(string ScannerId, string CurrentScanner, int CompletedScanners, int TotalScanners,
    ScannerExecutionStatus? Status = null, string? Detail = null, double ScannerPercentage = 0)
{
    public double Percentage => TotalScanners == 0 ? 100 : CompletedScanners * 100d / TotalScanners;
}
