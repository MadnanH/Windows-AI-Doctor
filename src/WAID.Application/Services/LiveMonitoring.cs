using Microsoft.Extensions.Logging;

namespace WAID.Application.Services;

public enum LiveMonitoringState { Disabled, Starting, Running, Paused, PausedBatterySaver, PausedHighLoad, PausedByPolicy, Faulted }
public enum LiveSignalSeverity { Normal, Advisory, Warning, Critical, Unavailable }
public enum MonitoringGapReason { ApplicationRestart, Suspended, BudgetExceeded, CollectorFailure, SchedulingDelay }

public sealed record LiveMonitoringOptions(
    bool Enabled,
    TimeSpan BaseInterval,
    TimeSpan MaximumInterval,
    HashSet<string> EnabledSignals,
    bool PauseOnBatterySaver = true,
    bool PauseOnHighLoad = true,
    double HighLoadThresholdPercent = 80,
    TimeSpan CollectionBudget = default,
    int RetentionDays = 30,
    int MaximumStoredSamples = 50_000)
{
    public LiveMonitoringOptions Validate()
    {
        if (BaseInterval < TimeSpan.FromSeconds(15) || BaseInterval > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(BaseInterval), "The monitoring interval must be between 15 seconds and one hour.");
        if (MaximumInterval < BaseInterval || MaximumInterval > TimeSpan.FromHours(4)) throw new ArgumentOutOfRangeException(nameof(MaximumInterval));
        if (HighLoadThresholdPercent is < 50 or > 100) throw new ArgumentOutOfRangeException(nameof(HighLoadThresholdPercent));
        if (EffectiveCollectionBudget < TimeSpan.FromMilliseconds(100) || EffectiveCollectionBudget > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(CollectionBudget));
        if (RetentionDays is < 1 or > 365 || MaximumStoredSamples is < 100 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(RetentionDays));
        return this;
    }
    public TimeSpan EffectiveCollectionBudget => CollectionBudget == default ? TimeSpan.FromSeconds(2) : CollectionBudget;
    public static LiveMonitoringOptions Default { get; } = new(true, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), new HashSet<string>(["cpu", "memory", "storage"], StringComparer.OrdinalIgnoreCase));
}

public sealed record LiveSignalSample(Guid Id, Guid SessionId, string SignalId, DateTimeOffset CapturedAtUtc, double? Value, string Unit, LiveSignalSeverity Severity, string Source, TimeSpan CollectionDuration, string? Detail = null);
public sealed record MonitoringGap(Guid Id, Guid SessionId, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, MonitoringGapReason Reason, string Detail);
public sealed record CollectorFailure(Guid Id, Guid SessionId, string CollectorId, DateTimeOffset OccurredAtUtc, string Code, string SafeMessage);
public sealed record MonitoringRetentionState(DateTimeOffset EvaluatedAtUtc, DateTimeOffset RetainAfterUtc, int MaximumSamples, int DeletedSamples, int DeletedSessions);
public sealed record LiveMonitoringSession(Guid Id, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc, LiveMonitoringState State, LiveMonitoringOptions Options, string StopReason);
public sealed record LiveMonitoringSnapshot(LiveMonitoringSession Session, IReadOnlyList<LiveSignalSample> Samples, IReadOnlyList<MonitoringGap> Gaps, IReadOnlyList<CollectorFailure> Failures, MonitoringRetentionState? Retention);
public sealed record LiveCollectorReading(string SignalId, double? Value, string Unit, string Source, string? Detail = null);
public sealed record LiveHealthAlert(string SignalId, LiveSignalSeverity Severity, DateTimeOffset RaisedAtUtc, string Message);
public sealed record LiveMonitoringResourceUsage(TimeSpan LastCollectionDuration, TimeSpan Budget, int CollectedSignals, int FailedCollectors, TimeSpan CurrentInterval, int StableCycles);

public sealed class LiveMonitoringException(string code, string message, string recoveryAction, Exception? inner = null) : InvalidOperationException(message, inner)
{
    public string Code { get; } = code;
    public string RecoveryAction { get; } = recoveryAction;
}

public interface ILiveMonitoringPolicy { bool IsMonitoringAllowed(); }
public sealed class AllowLiveMonitoringPolicy : ILiveMonitoringPolicy { public bool IsMonitoringAllowed() => true; }

public interface ILiveSignalCollector
{
    string Id { get; }
    IReadOnlySet<string> SignalIds { get; }
    Task<IReadOnlyList<LiveCollectorReading>> CollectAsync(CancellationToken token);
}

public interface ILiveMonitoringRepository
{
    Task RecoverInterruptedSessionsAsync(DateTimeOffset now, CancellationToken token);
    Task SaveSessionAsync(LiveMonitoringSession session, CancellationToken token);
    Task SaveCycleAsync(IReadOnlyList<LiveSignalSample> samples, IReadOnlyList<MonitoringGap> gaps, IReadOnlyList<CollectorFailure> failures, CancellationToken token);
    Task<MonitoringRetentionState> ApplyRetentionAsync(DateTimeOffset retainAfterUtc, int maximumSamples, CancellationToken token);
    Task<LiveMonitoringSnapshot?> GetLatestAsync(int sampleCount, CancellationToken token);
}

public sealed class LiveSignalAggregator
{
    public LiveSignalSeverity Classify(string signalId, double? value)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return LiveSignalSeverity.Unavailable;
        return signalId.ToLowerInvariant() switch
        {
            "cpu" or "memory" when value >= 95 => LiveSignalSeverity.Critical,
            "cpu" or "memory" when value >= 85 => LiveSignalSeverity.Warning,
            "cpu" or "memory" when value >= 70 => LiveSignalSeverity.Advisory,
            "storage" when value <= 5 => LiveSignalSeverity.Critical,
            "storage" when value <= 10 => LiveSignalSeverity.Warning,
            "storage" when value <= 20 => LiveSignalSeverity.Advisory,
            _ => LiveSignalSeverity.Normal
        };
    }
}

public sealed class LiveAlertEvaluator
{
    public IReadOnlyList<LiveHealthAlert> Evaluate(IReadOnlyList<LiveSignalSample> samples) => samples.Where(item => item.Severity >= LiveSignalSeverity.Warning && item.Severity != LiveSignalSeverity.Unavailable).Select(item => new LiveHealthAlert(item.SignalId, item.Severity, item.CapturedAtUtc, $"{item.SignalId} crossed the {item.Severity} monitoring threshold; verify the source measurement.")).ToArray();
}

public sealed class LiveMonitoringService(
    IEnumerable<ILiveSignalCollector> collectors,
    ILiveMonitoringRepository repository,
    ISystemConditionService conditions,
    LiveSignalAggregator aggregator,
    LiveAlertEvaluator alertEvaluator,
    ILiveMonitoringPolicy policy,
    TimeProvider time,
    ILogger<LiveMonitoringService> logger,
    AlertManager? alertManager = null) : IAsyncDisposable
{
    private readonly IReadOnlyList<ILiveSignalCollector> _collectors = collectors.ToArray();
    private CancellationTokenSource? _cancellation; private Task? _worker; private LiveMonitoringOptions _options = LiveMonitoringOptions.Default;
    private LiveMonitoringSession? _session; private DateTimeOffset? _lastCycleUtc; private int _stableCycles; private IReadOnlyDictionary<string, LiveSignalSeverity> _lastSeverities = new Dictionary<string, LiveSignalSeverity>();
    public LiveMonitoringState State { get; private set; } = LiveMonitoringState.Disabled;
    public IReadOnlyList<LiveHealthAlert> ActiveAlerts { get; private set; } = [];
    public LiveMonitoringResourceUsage ResourceUsage { get; private set; } = new(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, 0, TimeSpan.FromMinutes(1), 0);
    public event EventHandler? Updated;

    public async Task StartAsync(LiveMonitoringOptions options, CancellationToken token = default)
    {
        options.Validate();
        if (!options.Enabled) { await StopAsync("Disabled by user").ConfigureAwait(false); return; }
        if (_worker is { IsCompleted: false }) return;
        await repository.RecoverInterruptedSessionsAsync(time.GetUtcNow(), token).ConfigureAwait(false);
        _options = options; _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); State = LiveMonitoringState.Starting;
        _session = new(Guid.NewGuid(), time.GetUtcNow(), null, State, options, string.Empty); await repository.SaveSessionAsync(_session, token).ConfigureAwait(false);
        _worker = RunAsync(_cancellation.Token); Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Pause() { if (State is LiveMonitoringState.Running or LiveMonitoringState.Starting) { State = LiveMonitoringState.Paused; Updated?.Invoke(this, EventArgs.Empty); } }
    public void Resume() { if (State == LiveMonitoringState.Paused) { State = LiveMonitoringState.Running; Updated?.Invoke(this, EventArgs.Empty); } }

    public async Task StopAsync(string reason = "Stopped by user")
    {
        if (_cancellation is not null) { await _cancellation.CancelAsync().ConfigureAwait(false); try { if (_worker is not null) await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { } _cancellation.Dispose(); }
        if (_session is not null) { _session = _session with { EndedAtUtc = time.GetUtcNow(), State = LiveMonitoringState.Disabled, StopReason = reason }; await repository.SaveSessionAsync(_session, CancellationToken.None).ConfigureAwait(false); }
        _cancellation = null; _worker = null; _session = null; ActiveAlerts = []; State = LiveMonitoringState.Disabled; Updated?.Invoke(this, EventArgs.Empty);
    }

    public async Task RunCycleAsync(CancellationToken token)
    {
        if (_session is null) throw new LiveMonitoringException("WAID-MON-NOT-STARTED", "Monitoring has not been started.", "Start monitoring before requesting a collection cycle.");
        var now = time.GetUtcNow(); var gaps = new List<MonitoringGap>();
        if (_lastCycleUtc is { } previous && now - previous > _options.MaximumInterval + _options.BaseInterval)
            gaps.Add(new(Guid.NewGuid(), _session.Id, previous, now, MonitoringGapReason.SchedulingDelay, "Collection resumed after a sleep or scheduling discontinuity."));
        _lastCycleUtc = now;
        if (!policy.IsMonitoringAllowed()) { State = LiveMonitoringState.PausedByPolicy; gaps.Add(Gap(now, MonitoringGapReason.Suspended, "Paused because monitoring is disabled by policy.")); await repository.SaveCycleAsync([], gaps, [], token).ConfigureAwait(false); return; }
        if (_options.PauseOnBatterySaver && conditions.IsBatterySaverEnabled()) { State = LiveMonitoringState.PausedBatterySaver; gaps.Add(Gap(now, MonitoringGapReason.Suspended, "Paused while Windows battery saver is active.")); await repository.SaveCycleAsync([], gaps, [], token).ConfigureAwait(false); return; }
        if (_options.PauseOnHighLoad && conditions.GetSystemLoadPercent() >= _options.HighLoadThresholdPercent) { State = LiveMonitoringState.PausedHighLoad; gaps.Add(Gap(now, MonitoringGapReason.BudgetExceeded, "Paused to avoid adding work during high system load.")); await repository.SaveCycleAsync([], gaps, [], token).ConfigureAwait(false); return; }
        if (State == LiveMonitoringState.Paused) return;
        State = LiveMonitoringState.Running; var started = time.GetTimestamp(); var samples = new List<LiveSignalSample>(); var failures = new List<CollectorFailure>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(_options.EffectiveCollectionBudget);
        foreach (var collector in _collectors.Where(item => item.SignalIds.Any(_options.EnabledSignals.Contains)))
        {
            var collectorStarted = time.GetTimestamp();
            try
            {
                var readings = await collector.CollectAsync(budget.Token).ConfigureAwait(false);
                foreach (var reading in readings.Where(item => _options.EnabledSignals.Contains(item.SignalId)).Take(16 - samples.Count))
                    samples.Add(new(Guid.NewGuid(), _session.Id, reading.SignalId, now, Valid(reading.Value), reading.Unit, aggregator.Classify(reading.SignalId, Valid(reading.Value)), reading.Source, time.GetElapsedTime(collectorStarted), reading.Detail));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { gaps.Add(Gap(now, MonitoringGapReason.BudgetExceeded, $"Collector {collector.Id} exceeded the cycle budget.")); break; }
            catch (Exception exception)
            {
                failures.Add(new(Guid.NewGuid(), _session.Id, collector.Id, now, "WAID-MON-COLLECTOR", "The collector could not read this signal."));
                logger.LogWarning("Live monitoring collector {CollectorId} failed with {FailureType}", collector.Id, exception.GetType().Name);
            }
            if (samples.Count >= 16) break;
        }
        var duration = time.GetElapsedTime(started); var current = samples.ToDictionary(item => item.SignalId, item => item.Severity, StringComparer.OrdinalIgnoreCase);
        _stableCycles = Same(current, _lastSeverities) ? Math.Min(_stableCycles + 1, 8) : 0; _lastSeverities = current;
        ActiveAlerts = alertEvaluator.Evaluate(samples);
        if (alertManager is not null)
            foreach (var alert in ActiveAlerts)
            {
                var sample = samples.First(item => string.Equals(item.SignalId, alert.SignalId, StringComparison.OrdinalIgnoreCase));
                var severity = alert.Severity == LiveSignalSeverity.Critical ? AlertSeverity.Critical : AlertSeverity.Warning;
                var category = alert.SignalId.Equals("storage", StringComparison.OrdinalIgnoreCase) ? AlertCategory.Storage : AlertCategory.Performance;
                await alertManager.RaiseAsync(new($"live:{alert.SignalId}", $"live-{alert.SignalId}", "live-alert-1.0", category, severity, $"{alert.SignalId} needs attention", alert.Message, "Review monitoring evidence", "waid://live-monitoring", new("live-monitoring", sample.Id.ToString(), sample.CapturedAtUtc, new Dictionary<string,string>{{"signal",sample.SignalId},{"severity",sample.Severity.ToString()},{"value",sample.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"Unavailable"},{"unit",sample.Unit},{"source",sample.Source}})), token).ConfigureAwait(false);
            }
        ResourceUsage = new(duration, _options.EffectiveCollectionBudget, samples.Count, failures.Count, AdaptiveInterval(), _stableCycles);
        await repository.SaveCycleAsync(samples, gaps, failures, token).ConfigureAwait(false);
        await repository.ApplyRetentionAsync(now.AddDays(-_options.RetentionDays), _options.MaximumStoredSamples, token).ConfigureAwait(false);
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(CancellationToken token)
    {
        logger.LogInformation("Live monitoring session {SessionId} started", _session?.Id);
        while (!token.IsCancellationRequested)
        {
            try { await RunCycleAsync(token).ConfigureAwait(false); await Task.Delay(AdaptiveInterval(), time, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception exception) { State = LiveMonitoringState.Faulted; logger.LogError("Live monitoring cycle failed with {FailureType}", exception.GetType().Name); await Task.Delay(_options.MaximumInterval, time, token).ConfigureAwait(false); }
        }
        logger.LogInformation("Live monitoring stopped");
    }
    private MonitoringGap Gap(DateTimeOffset now, MonitoringGapReason reason, string detail) => new(Guid.NewGuid(), _session!.Id, now, now, reason, detail);
    private TimeSpan AdaptiveInterval() { var ticks = Math.Min(_options.MaximumInterval.Ticks, _options.BaseInterval.Ticks * (1L << Math.Min(_stableCycles / 2, 3))); return TimeSpan.FromTicks(ticks); }
    private static double? Valid(double? value) => value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value) ? null : value;
    private static bool Same(IReadOnlyDictionary<string, LiveSignalSeverity> left, IReadOnlyDictionary<string, LiveSignalSeverity> right) => left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out var value) && value == item.Value);
    public async ValueTask DisposeAsync() => await StopAsync("Application shutdown").ConfigureAwait(false);
}
