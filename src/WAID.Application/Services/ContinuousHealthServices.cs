using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public sealed class ScanCoordinator(ScanOrchestrator orchestrator)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsScanRunning => _gate.CurrentCount == 0;
    public async Task<ScanSession?> TryRunAsync(bool administrator, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token).ConfigureAwait(false)) return null;
        try { return await orchestrator.RunAsync(administrator, progress, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}

public sealed class BackgroundHealthMonitoringService(
    ScanCoordinator scans, DiagnosisEngine diagnosis, IHealthSnapshotRepository snapshots,
    ISystemConditionService conditions, TimeProvider timeProvider,
    ILogger<BackgroundHealthMonitoringService> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    public MonitoringState State { get; private set; } = MonitoringState.Stopped;
    public HealthSnapshot? Latest { get; private set; }
    public event EventHandler? Updated;

    public void Start(MonitoringOptions options)
    {
        options.Validate();
        if (_worker is { IsCompleted: false }) return;
        _cancellation = new CancellationTokenSource();
        State = MonitoringState.Starting;
        _worker = RunAsync(options, _cancellation.Token);
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        if (_cancellation is null) return;
        await _cancellation.CancelAsync().ConfigureAwait(false);
        try { if (_worker is not null) await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cancellation.Dispose(); _cancellation = null; _worker = null; State = MonitoringState.Stopped;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public async Task<HealthSnapshot?> RefreshAsync(CancellationToken token)
    {
        var session = await scans.TryRunAsync(false, null, token).ConfigureAwait(false);
        if (session is null) return null;
        var report = await diagnosis.DiagnoseAsync(session.Findings, token).ConfigureAwait(false);
        Latest = new(Guid.NewGuid(), timeProvider.GetUtcNow(), report.Health, session.Findings, State);
        await snapshots.SaveAsync(Latest, token).ConfigureAwait(false);
        Updated?.Invoke(this, EventArgs.Empty);
        return Latest;
    }

    private async Task RunAsync(MonitoringOptions options, CancellationToken token)
    {
        logger.LogInformation("Health monitoring started with interval {Interval}", options.Interval);
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (options.PauseOnBatterySaver && conditions.IsBatterySaverEnabled()) State = MonitoringState.PausedBatterySaver;
                else if (options.PauseOnHighLoad && conditions.GetSystemLoadPercent() >= 80) State = MonitoringState.PausedHighLoad;
                else { State = MonitoringState.Running; await RefreshAsync(token).ConfigureAwait(false); }
                Updated?.Invoke(this, EventArgs.Empty);
                await Task.Delay(State == MonitoringState.Running ? options.Interval : TimeSpan.FromMinutes(1), timeProvider, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception exception) { State = MonitoringState.Faulted; logger.LogError(exception, "Background health monitoring cycle failed"); Updated?.Invoke(this, EventArgs.Empty); await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, token).ConfigureAwait(false); }
        }
        logger.LogInformation("Health monitoring stopped");
    }
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

public sealed class ScheduledScanService(ScanCoordinator scans, IScanScheduleRepository repository,
    ISystemConditionService conditions, TimeProvider timeProvider, ILogger<ScheduledScanService> logger)
{
    public static bool IsDue(ScanSchedule schedule, DateTimeOffset now) => schedule.Enabled &&
        (!schedule.LastRunUtc.HasValue || now - schedule.LastRunUtc.Value >= schedule.Validate().Interval);

    public async Task<bool> RunIfDueAsync(CancellationToken token)
    {
        var schedule = await repository.GetAsync(token).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        if (!IsDue(schedule, now) || schedule.OnlyWhenPluggedIn && !conditions.IsPluggedIn() || schedule.OnlyWhenIdle && !conditions.IsSystemIdle()) return false;
        var session = await scans.TryRunAsync(false, null, token).ConfigureAwait(false);
        if (session is null) { logger.LogInformation("Scheduled scan skipped because another scan is active"); return false; }
        await repository.SaveAsync(schedule with { LastRunUtc = now }, token).ConfigureAwait(false);
        logger.LogInformation("Scheduled scan {SessionId} completed", session.Id);
        return true;
    }
}

public sealed class ScheduledScanLoopService(ScheduledScanService scheduledScans, TimeProvider timeProvider,
    ILogger<ScheduledScanLoopService> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _cancellation; private Task? _worker;
    public void Start(){if(_worker is{IsCompleted:false})return;_cancellation=new();_worker=RunAsync(_cancellation.Token);}
    private async Task RunAsync(CancellationToken token){while(!token.IsCancellationRequested){try{await scheduledScans.RunIfDueAsync(token).ConfigureAwait(false);await Task.Delay(TimeSpan.FromMinutes(1),timeProvider,token).ConfigureAwait(false);}catch(OperationCanceledException)when(token.IsCancellationRequested){break;}catch(Exception exception){logger.LogError(exception,"Scheduled scan check failed");await Task.Delay(TimeSpan.FromMinutes(1),timeProvider,token).ConfigureAwait(false);}}}
    public async ValueTask DisposeAsync(){if(_cancellation is null)return;await _cancellation.CancelAsync().ConfigureAwait(false);try{if(_worker is not null)await _worker.ConfigureAwait(false);}catch(OperationCanceledException){} _cancellation.Dispose();_cancellation=null;_worker=null;}
}

public sealed class EvidenceCollector(TimeProvider timeProvider)
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    { "eventId", "provider", "category", "timeCreated", "errorCode", "resultCode", "hResult", "health", "state", "load", "freeBytes", "totalBytes", "createdUtc", "fileName", "count", "driverVersion", "antivirusEnabled", "realTimeEnabled" };
    private static readonly Regex SecretPattern = new("(?i)(password|token|secret|product.?key|authorization|cookie)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public IReadOnlyCollection<CollectedEvidence> Collect(IReadOnlyCollection<DiagnosticFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings.Select(finding => new CollectedEvidence(finding.ScannerId, finding.Code, EvidenceTime(finding),
            finding.Evidence.Where(item => AllowedKeys.Contains(item.Key) && !SecretPattern.IsMatch(item.Key) && !SecretPattern.IsMatch(item.Value))
                .ToDictionary(item => item.Key, item => Redact(item.Value), StringComparer.OrdinalIgnoreCase))).ToArray();
    }
    private DateTimeOffset EvidenceTime(DiagnosticFinding finding) => finding.Evidence.TryGetValue("timeCreated", out var value) && DateTimeOffset.TryParse(value, out var parsed) ? parsed : timeProvider.GetUtcNow();
    private static string Redact(string value)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? value : value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RepairPrioritizationEngine(RepairRegistry registry)
{
    public IReadOnlyCollection<PrioritizedRepair> Prioritize(AIReport report)
    {
        var unique = report.RootCauses.Where(cause => cause.Recommendation.RepairId is not null)
            .GroupBy(cause => cause.Recommendation.RepairId!, StringComparer.OrdinalIgnoreCase).Select(group => group.OrderByDescending(x => x.Confidence).First());
        return unique.Select(cause =>
        {
            registry.TryGet(cause.Recommendation.RepairId!, out var module);
            var risk = module?.Policy.SafetyLevel ?? SafetyLevel.Critical;
            var evidence = Math.Min(100, cause.SupportingEvidence.Count * 20 + report.Correlations.Where(c => c.Findings.Any(cause.SupportingEvidence.Contains)).Select(c => c.Strength).DefaultIfEmpty(0).Max());
            var impact = Math.Clamp((cause.Confidence + evidence + (cause.Severity == DiagnosticSeverity.Critical ? 100 : 60) - (int)risk * 10) / 3, 0, 100);
            var restart = cause.Recommendation.RepairId is "waid.windows-update-reset" or "waid.winsock-reset" or "waid.tcpip-reset";
            return new PrioritizedRepair(module?.Id ?? cause.Recommendation.RepairId!, cause.Recommendation.Title, DependencyOrder(cause.Recommendation.RepairId!), impact, risk, module?.Policy.RequiresAdministrator ?? true, restart, module?.Policy.SupportsRollback ?? false, cause.Confidence, evidence);
        }).OrderBy(item => item.Order).ThenByDescending(item => item.ExpectedBenefit).ToArray();
    }
    private static int DependencyOrder(string id) => id switch { "waid.dism" => 10, "waid.sfc" => 20, "waid.dns-reset" => 30, "waid.winsock-reset" => 40, "waid.tcpip-reset" => 50, _ => 25 };
}

public sealed class RepairApprovalWorkflow(IRepairApprovalRepository repository, TimeProvider timeProvider)
{
    public async Task<RepairApproval> RecordAsync(PrioritizedRepair repair, string evidenceSummary,
        IReadOnlyCollection<string> plannedActions, bool approved, bool riskAcknowledged, CancellationToken token)
    {
        if (approved && repair.RiskLevel >= SafetyLevel.Moderate && !riskAcknowledged)
            throw new InvalidOperationException("Medium and high-risk repairs require explicit risk acknowledgement.");
        var now = timeProvider.GetUtcNow();
        var record = new RepairApproval(Guid.NewGuid(), repair.RepairId, now, approved ? now : null, approved, evidenceSummary, plannedActions);
        await repository.SaveAsync(record, token).ConfigureAwait(false);
        return record;
    }
}
