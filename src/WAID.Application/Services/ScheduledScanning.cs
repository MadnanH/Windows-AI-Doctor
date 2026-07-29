using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public sealed class ScheduledScanService(
    ScanCoordinator scans,
    IScanScheduleRepository repository,
    ISystemConditionService conditions,
    TimeProvider timeProvider,
    ILogger<ScheduledScanService> logger,
    IAuditTrailService? auditTrail = null)
{
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);
    private int _startupConsumed;

    public static bool IsDue(ScanSchedule schedule, DateTimeOffset now) =>
        schedule.Enabled && !schedule.Paused &&
        (!schedule.DeferredUntilUtc.HasValue || schedule.DeferredUntilUtc <= now) &&
        (!schedule.NextRunUtc.HasValue
            ? !schedule.LastRunUtc.HasValue || now - schedule.LastRunUtc.Value >= schedule.Validate().Interval
            : schedule.NextRunUtc <= now);

    public static DateTimeOffset CalculateNextRun(ScanSchedule schedule, DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        schedule.Validate();
        if (schedule.Frequency == ScheduleFrequency.Custom)
            return afterUtc + schedule.CustomInterval;
        if (schedule.Frequency is ScheduleFrequency.Startup or ScheduleFrequency.Idle)
            return afterUtc + TimeSpan.FromMinutes(15);

        var localAfter = TimeZoneInfo.ConvertTime(afterUtc, timeZone);
        DateTime local;
        if (schedule.Frequency == ScheduleFrequency.Weekly)
        {
            var days = ((int)schedule.WeeklyDay - (int)localAfter.DayOfWeek + 7) % 7;
            local = localAfter.Date.AddDays(days).Add(schedule.DailyTime.ToTimeSpan());
            if (local <= localAfter.DateTime) local = local.AddDays(7);
        }
        else if (schedule.Frequency == ScheduleFrequency.Monthly)
        {
            var day = Math.Min(schedule.MonthlyDay, DateTime.DaysInMonth(localAfter.Year, localAfter.Month));
            local = new DateTime(localAfter.Year, localAfter.Month, day).Add(schedule.DailyTime.ToTimeSpan());
            if (local <= localAfter.DateTime)
            {
                var nextMonth = localAfter.Date.AddMonths(1);
                day = Math.Min(schedule.MonthlyDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                local = new DateTime(nextMonth.Year, nextMonth.Month, day).Add(schedule.DailyTime.ToTimeSpan());
            }
        }
        else
        {
            var time = schedule.Frequency == ScheduleFrequency.Maintenance
                ? schedule.MaintenanceStart!.Value
                : schedule.DailyTime;
            local = localAfter.Date.Add(time.ToTimeSpan());
            if (local <= localAfter.DateTime) local = local.AddDays(1);
        }

        if (timeZone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public Task<ScheduledScanHistory> RunIfDueAsync(CancellationToken token) => EvaluateAsync(false, token);
    public Task<ScheduledScanHistory> RunNowAsync(CancellationToken token) => EvaluateAsync(true, token);

    private async Task<ScheduledScanHistory> EvaluateAsync(bool userRequested, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var schedule = (await repository.GetAsync(token).ConfigureAwait(false)).Validate();
        if (!await _evaluationGate.WaitAsync(0, token).ConfigureAwait(false))
            return await RecordAsync(schedule, now, ScheduledScanOutcome.OverlapPrevented, "Another scheduler evaluation is active.", null, null, token).ConfigureAwait(false);

        try
        {
            var next = schedule.NextRunUtc ?? (schedule.LastRunUtc.HasValue ? CalculateNextRun(schedule, schedule.LastRunUtc.Value, TimeZoneInfo.Local) : now);
            schedule = schedule with { NextRunUtc = next };
            if (!userRequested)
            {
                if (!schedule.Enabled) return await RecordAsync(schedule, now, ScheduledScanOutcome.Disabled, "The schedule is disabled.", null, null, token).ConfigureAwait(false);
                if (schedule.Paused) return await RecordAsync(schedule, now, ScheduledScanOutcome.Paused, "The schedule is paused.", null, null, token).ConfigureAwait(false);
                if (!IsDue(schedule, now)) return await RecordAsync(schedule, now, ScheduledScanOutcome.NotDue, "The next scheduled time has not arrived.", null, null, token).ConfigureAwait(false);
                if (schedule.Frequency == ScheduleFrequency.Startup && Interlocked.Exchange(ref _startupConsumed, 1) != 0) return await DeferAsync(schedule, now, ScheduledScanOutcome.NotDue, "The startup schedule already ran in this application session.", token).ConfigureAwait(false);
            }
            if (schedule.OnlyWhenPluggedIn && !conditions.IsPluggedIn()) return await DeferAsync(schedule, now, ScheduledScanOutcome.DeferredPower, "Waiting for AC power.", token).ConfigureAwait(false);
            if ((schedule.OnlyWhenIdle || schedule.Frequency == ScheduleFrequency.Idle) && !conditions.IsSystemIdle()) return await DeferAsync(schedule, now, ScheduledScanOutcome.DeferredIdle, "Waiting for the system to become idle.", token).ConfigureAwait(false);
            if (schedule.RequireNetwork && !conditions.IsNetworkAvailable()) return await DeferAsync(schedule, now, ScheduledScanOutcome.DeferredNetwork, "Waiting for network availability.", token).ConfigureAwait(false);
            var load = conditions.GetSystemLoadPercent();
            if (!double.IsFinite(load) || load < 0 || load > 100) return await DeferAsync(schedule, now, ScheduledScanOutcome.DeferredLoad, "System load data is unavailable or invalid.", token).ConfigureAwait(false);
            if (load > schedule.MaximumLoadPercent) return await DeferAsync(schedule, now, ScheduledScanOutcome.DeferredLoad, $"System load is above the configured {schedule.MaximumLoadPercent:0}% limit.", token).ConfigureAwait(false);
            if (schedule.Frequency == ScheduleFrequency.Maintenance && !IsInsideWindow(schedule, now, TimeZoneInfo.Local)) return await DeferAsync(schedule, now, ScheduledScanOutcome.DeferredWindow, "Outside the configured maintenance window.", token).ConfigureAwait(false);
            if (scans.IsScanRunning) return await DeferAsync(schedule, now, ScheduledScanOutcome.OverlapPrevented, "Another scan is already active.", token).ConfigureAwait(false);

            var started = timeProvider.GetUtcNow();
            try
            {
                var session = await scans.TryRunAsync(false, null, token).ConfigureAwait(false);
                if (session is null) return await DeferAsync(schedule, now, ScheduledScanOutcome.OverlapPrevented, "Another scan acquired the execution slot.", token).ConfigureAwait(false);
                var completed = timeProvider.GetUtcNow();
                var nextRun = CalculateNextRun(schedule, completed, TimeZoneInfo.Local);
                schedule = schedule with { LastRunUtc = completed, NextRunUtc = nextRun, DeferredUntilUtc = null };
                await repository.SaveAsync(schedule, token).ConfigureAwait(false);
                logger.LogInformation("Scheduled scan {SessionId} completed; next run {NextRunUtc}", session.Id, nextRun);
                return await RecordAsync(schedule, now, ScheduledScanOutcome.Completed, "Scheduled scan completed.", started, session.Id, token, completed).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await RecordAsync(schedule, now, ScheduledScanOutcome.Cancelled, "Scheduled scan was cancelled.", started, null, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled scan failed with {FailureType}", exception.GetType().Name);
                schedule = schedule with { DeferredUntilUtc = now.AddMinutes(15) };
                await repository.SaveAsync(schedule, token).ConfigureAwait(false);
                return await RecordAsync(schedule, now, ScheduledScanOutcome.Failed, $"Scheduled scan failed ({exception.GetType().Name}); retry deferred for 15 minutes.", started, null, token).ConfigureAwait(false);
            }
        }
        finally { _evaluationGate.Release(); }
    }

    private async Task<ScheduledScanHistory> DeferAsync(ScanSchedule schedule, DateTimeOffset now, ScheduledScanOutcome outcome, string reason, CancellationToken token)
    {
        var deferred = schedule with { DeferredUntilUtc = now.AddMinutes(15) };
        await repository.SaveAsync(deferred, token).ConfigureAwait(false);
        logger.LogInformation("Scheduled scan deferred: {Outcome}; {Reason}", outcome, reason);
        return await RecordAsync(deferred, now, outcome, reason, null, null, token).ConfigureAwait(false);
    }

    private async Task<ScheduledScanHistory> RecordAsync(ScanSchedule schedule, DateTimeOffset evaluated, ScheduledScanOutcome outcome, string reason, DateTimeOffset? started, Guid? sessionId, CancellationToken token, DateTimeOffset? completed = null)
    {
        var history = new ScheduledScanHistory(Guid.NewGuid(), evaluated, started, completed, outcome, reason, sessionId, schedule.PolicySource, schedule.NextRunUtc);
        if (outcome is not (ScheduledScanOutcome.NotDue or ScheduledScanOutcome.Disabled or ScheduledScanOutcome.Paused))
            await repository.SaveHistoryAsync(history, token).ConfigureAwait(false);
        if (auditTrail is not null && outcome is ScheduledScanOutcome.Completed or ScheduledScanOutcome.OverlapPrevented or ScheduledScanOutcome.Failed or ScheduledScanOutcome.Cancelled)
        {
            var result = outcome switch { ScheduledScanOutcome.Completed => AuditResult.Succeeded, ScheduledScanOutcome.Cancelled => AuditResult.Cancelled, ScheduledScanOutcome.Failed => AuditResult.Failed, _ => AuditResult.Rejected };
            try { await auditTrail.AppendAsync(new(history.Id, evaluated, AuditActor.Scheduler, "ScheduledScan", "SystemScan", result, SafetyLevel.Low, false, false, history.Id, history.Id, reason), token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning("Scheduled scan audit write failed with {FailureType}", exception.GetType().Name); }
        }
        return history;
    }

    private static bool IsInsideWindow(ScanSchedule schedule, DateTimeOffset utc, TimeZoneInfo zone)
    {
        var time = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);
        var start = schedule.MaintenanceStart!.Value;
        var end = schedule.MaintenanceEnd!.Value;
        return start < end ? time >= start && time < end : time >= start || time < end;
    }
}

public sealed class ScheduledScanLoopService(ScheduledScanService scheduledScans, TimeProvider timeProvider, ILogger<ScheduledScanLoopService> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    public void Start() { if (_worker is { IsCompleted: false }) return; _cancellation = new(); _worker = RunAsync(_cancellation.Token); }
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await scheduledScans.RunIfDueAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Scheduled scan evaluation failed with {FailureType}", exception.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_cancellation is null) return;
        await _cancellation.CancelAsync().ConfigureAwait(false);
        try { if (_worker is not null) await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cancellation.Dispose(); _cancellation = null; _worker = null;
    }
}
