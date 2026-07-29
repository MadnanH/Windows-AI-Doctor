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

public sealed class RepairPrioritizationEngine
{
    public const string RankingVersion="deterministic-v2";
    private readonly RepairRegistry _registry;private readonly IRepairRecommendationRepository? _repository;private readonly TimeProvider _time;
    public RepairPrioritizationEngine(RepairRegistry registry,IRepairRecommendationRepository? repository=null,TimeProvider? time=null){_registry=registry;_repository=repository;_time=time??TimeProvider.System;}
    public IReadOnlyCollection<PrioritizedRepair> Prioritize(AIReport report)=>Evaluate(report,RepairRankingContext.Default).Ranked;
    public RepairRankingRun Evaluate(AIReport report,RepairRankingContext context)
    {
        ArgumentNullException.ThrowIfNull(report);ArgumentNullException.ThrowIfNull(context);var ranked=new List<PrioritizedRepair>();var decisions=new List<RepairRecommendationDecision>();
        var causes=report.RootCauses.Where(x=>x.Recommendation.RepairId is not null).GroupBy(x=>x.Recommendation.RepairId!,StringComparer.OrdinalIgnoreCase).Select(x=>x.OrderByDescending(y=>y.Confidence).ThenBy(y=>y.Id,StringComparer.Ordinal).First());
        foreach(var cause in causes.OrderBy(x=>x.Recommendation.RepairId,StringComparer.Ordinal))
        {
            var id=cause.Recommendation.RepairId!;if(!_registry.TryGet(id,out var module)||module is null)continue;var policy=module.Policy;var prereq=Prerequisites(policy);var missing=prereq.Where(x=>!context.AvailablePrerequisites.Contains(x)).ToArray();var conflicts=context.Conflicts.TryGetValue(id,out var found)?found:[];var blocked=context.BlockedRepairIds.Contains(id)||policy.SafetyLevel>context.MaximumRisk;var status=blocked?RepairCandidateStatus.BlockedByPolicy:conflicts.Count>0?RepairCandidateStatus.Conflict:missing.Length>0?RepairCandidateStatus.PrerequisiteMissing:RepairCandidateStatus.Eligible;
            var evidence=Math.Min(100,cause.SupportingEvidence.Count*20+report.Correlations.Where(c=>c.Findings.Any(cause.SupportingEvidence.Contains)).Select(c=>c.Strength).DefaultIfEmpty(0).Max());var baseBenefit=Math.Clamp((cause.Confidence+evidence+(cause.Severity==DiagnosticSeverity.Critical?100:60))/3,0,100);var riskPenalty=(int)policy.SafetyLevel*8;var reversible=policy.SupportsRollback?10:0;var downtime=Downtime(id);var downtimePenalty=downtime>context.MaximumDowntimeMinutes?15:downtime/10;var conflictPenalty=conflicts.Count*20;var prerequisitePenalty=missing.Length*25;var policyPenalty=blocked?100:0;var feedback=context.Feedback.TryGetValue(id,out var aggregate)?aggregate.Adjustment:0;var score=Math.Clamp(baseBenefit-riskPenalty+reversible-downtimePenalty-conflictPenalty-prerequisitePenalty-policyPenalty+feedback,0,100);var factors=new RepairRankingFactors(evidence,baseBenefit,riskPenalty,reversible,downtimePenalty,0,conflictPenalty,prerequisitePenalty,policyPenalty,feedback,score);var reason=status switch{RepairCandidateStatus.BlockedByPolicy=>"Rejected by active repair policy.",RepairCandidateStatus.Conflict=>"Rejected because a conflicting repair or system condition is present.",RepairCandidateStatus.PrerequisiteMissing=>"Rejected until all declared prerequisites are available.",_=>"Eligible registered repair ranked from evidence, benefit, risk, reversibility, downtime, and bounded outcome feedback."};var auto=status==RepairCandidateStatus.Eligible&&policy.SafetyLevel==SafetyLevel.Low;
            decisions.Add(new(id,status,reason,factors,auto,conflicts,missing));if(status!=RepairCandidateStatus.Eligible)continue;ranked.Add(new(id,cause.Recommendation.Title,DependencyOrder(id),score,policy.SafetyLevel,policy.RequiresAdministrator,Restart(id),policy.SupportsRollback,cause.Confidence,evidence){RankingFactors=factors,CandidateStatus=status,RankingExplanation=reason,Prerequisites=prereq,Conflicts=conflicts,EstimatedDowntimeMinutes=downtime,AutoSelectable=auto,RankingVersion=RankingVersion});
        }
        var ordered=ranked.OrderBy(x=>x.Order).ThenByDescending(x=>x.ExpectedBenefit).ThenBy(x=>x.RepairId,StringComparer.Ordinal).ToArray();return new(Guid.NewGuid(),_time.GetUtcNow(),RankingVersion,ordered,decisions.OrderBy(x=>x.RepairId,StringComparer.Ordinal).ToArray(),context.Feedback);
    }
    public async Task<RepairRankingRun> EvaluateAsync(AIReport report,RepairRankingContext context,CancellationToken token){var run=Evaluate(report,context);if(_repository is not null)await _repository.SaveAsync(run,token).ConfigureAwait(false);return run;}
    private static IReadOnlyList<string> Prerequisites(RepairPolicy p){var items=new List<string>();if(p.RequiresAdministrator)items.Add("administrator");if(p.RequiresRestorePoint)items.Add("restore-point");if(p.RequiresBackup)items.Add("backup");return items;}
    private static int Downtime(string id)=>id switch{"waid.windows-update-reset"=>30,"waid.dism"=>45,"waid.sfc"=>30,"waid.winsock-reset" or "waid.tcpip-reset"=>10,_=>5};
    private static bool Restart(string id)=>id is "waid.windows-update-reset" or "waid.winsock-reset" or "waid.tcpip-reset";
    private static int DependencyOrder(string id)=>id switch{"waid.dism"=>10,"waid.sfc"=>20,"waid.dns-reset"=>30,"waid.winsock-reset"=>40,"waid.tcpip-reset"=>50,_=>25};
}

public sealed class RepairApprovalWorkflow(IRepairApprovalRepository repository, TimeProvider timeProvider, IAuditTrailService? auditTrail=null, IOperationContextAccessor? operationContext=null)
{
    public async Task<RepairApproval> RecordAsync(PrioritizedRepair repair, string evidenceSummary,
        IReadOnlyCollection<string> plannedActions, bool approved, bool riskAcknowledged, CancellationToken token)
    {
        if (approved && repair.RiskLevel >= SafetyLevel.Moderate && !riskAcknowledged)
            throw new InvalidOperationException("Medium and high-risk repairs require explicit risk acknowledgement.");
        var now = timeProvider.GetUtcNow();
        var record = new RepairApproval(Guid.NewGuid(), repair.RepairId, now, approved ? now : null, approved, evidenceSummary, plannedActions);
        await repository.SaveAsync(record, token).ConfigureAwait(false);
        if(auditTrail is not null){var context=operationContext?.Current;await auditTrail.AppendAsync(new(Guid.NewGuid(),now,AuditActor.User,"RepairApproval",repair.RepairId,approved?AuditResult.Approved:AuditResult.Rejected,repair.RiskLevel,repair.RequiresAdministrator,repair.SupportsRollback,context?.CorrelationId??record.Id,context?.OperationId??record.Id,$"Approval recorded; risk acknowledgement: {riskAcknowledged}."),token).ConfigureAwait(false);}
        return record;
    }
}
