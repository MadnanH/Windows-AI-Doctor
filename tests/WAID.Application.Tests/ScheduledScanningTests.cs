using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;

namespace WAID.Application.Tests;

public sealed class ScheduledScanningTests
{
    [Fact]
    public void Recurrence_supports_daily_weekly_monthly_and_custom()
    {
        var zone=TimeZoneInfo.Utc;var after=new DateTimeOffset(2026,1,30,10,0,0,TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026,1,31,9,0,0,TimeSpan.Zero),ScheduledScanService.CalculateNextRun(Schedule(ScheduleFrequency.Daily),after,zone));
        Assert.Equal(DayOfWeek.Monday,ScheduledScanService.CalculateNextRun(Schedule(ScheduleFrequency.Weekly) with{WeeklyDay=DayOfWeek.Monday},after,zone).DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026,1,31,9,0,0,TimeSpan.Zero),ScheduledScanService.CalculateNextRun(Schedule(ScheduleFrequency.Monthly) with{MonthlyDay=31},after,zone));
        Assert.Equal(after.AddMinutes(30),ScheduledScanService.CalculateNextRun(Schedule(ScheduleFrequency.Custom) with{CustomInterval=TimeSpan.FromMinutes(30)},after,zone));
    }

    [Fact]
    public void Dst_gap_moves_invalid_local_time_forward()
    {
        var daylight=TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,2,0,0),3,2,DayOfWeek.Sunday);
        var standard=TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,2,0,0),11,1,DayOfWeek.Sunday);
        var zone=TimeZoneInfo.CreateCustomTimeZone("WAID-Test",TimeSpan.FromHours(-5),"WAID-Test","WAID-Test", "WAID-Test DST",[TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020,1,1),new DateTime(2030,12,31),TimeSpan.FromHours(1),daylight,standard)]);
        var next=ScheduledScanService.CalculateNextRun(Schedule(ScheduleFrequency.Daily) with{DailyTime=new TimeOnly(2,30)},new DateTimeOffset(2026,3,8,5,0,0,TimeSpan.Zero),zone);
        Assert.Equal(3,TimeZoneInfo.ConvertTime(next,zone).Hour);
    }

    [Fact]
    public async Task Missed_run_after_sleep_executes_once_and_records_next_run()
    {
        var now=new DateTimeOffset(2026,5,2,12,0,0,TimeSpan.Zero);var repo=new Repository(Schedule(ScheduleFrequency.Daily) with{NextRunUtc=now.AddHours(-3)});var scanner=new Scanner();var service=Create(repo,new Conditions(),scanner,new FixedTime(now));
        var result=await service.RunIfDueAsync(CancellationToken.None);
        Assert.Equal(ScheduledScanOutcome.Completed,result.Outcome);Assert.Equal(1,scanner.Count);Assert.True(repo.Schedule.NextRunUtc>now);Assert.Contains(repo.History,x=>x.Outcome==ScheduledScanOutcome.Completed);
    }

    [Theory]
    [InlineData(false,true,true,10,ScheduledScanOutcome.DeferredPower)]
    [InlineData(true,false,true,10,ScheduledScanOutcome.DeferredIdle)]
    [InlineData(true,true,false,10,ScheduledScanOutcome.DeferredNetwork)]
    [InlineData(true,true,true,90,ScheduledScanOutcome.DeferredLoad)]
    public async Task Policies_defer_with_typed_auditable_reason(bool plugged,bool idle,bool network,double load,ScheduledScanOutcome expected)
    {
        var now=new DateTimeOffset(2026,5,2,12,0,0,TimeSpan.Zero);var schedule=Schedule(ScheduleFrequency.Daily) with{NextRunUtc=now.AddMinutes(-1),OnlyWhenPluggedIn=true,OnlyWhenIdle=true,RequireNetwork=true,MaximumLoadPercent=80};var repo=new Repository(schedule);var service=Create(repo,new Conditions(plugged,idle,network,load),new Scanner(),new FixedTime(now));
        var result=await service.RunIfDueAsync(CancellationToken.None);
        Assert.Equal(expected,result.Outcome);Assert.Equal(now.AddMinutes(15),repo.Schedule.DeferredUntilUtc);Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public async Task Concurrent_evaluation_prevents_duplicate_scan()
    {
        var now=new DateTimeOffset(2026,5,2,12,0,0,TimeSpan.Zero);var repo=new Repository(Schedule(ScheduleFrequency.Daily) with{NextRunUtc=now.AddMinutes(-1)});var scanner=new Scanner(block:true);var service=Create(repo,new Conditions(),scanner,new FixedTime(now));
        var first=service.RunIfDueAsync(CancellationToken.None);await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));var second=await service.RunIfDueAsync(CancellationToken.None);scanner.Release.TrySetResult();var completed=await first;
        Assert.Equal(ScheduledScanOutcome.OverlapPrevented,second.Outcome);Assert.Equal(ScheduledScanOutcome.Completed,completed.Outcome);Assert.Equal(1,scanner.Count);
    }

    [Fact]
    public void Invalid_policy_and_maintenance_window_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(()=>(Schedule(ScheduleFrequency.Daily) with{MaximumLoadPercent=101}).Validate());
        Assert.Throws<InvalidOperationException>(()=>Schedule(ScheduleFrequency.Maintenance).Validate());
    }

    private static ScanSchedule Schedule(ScheduleFrequency frequency)=>new(true,frequency,TimeSpan.FromHours(1),DayOfWeek.Sunday,new TimeOnly(9,0),false,false);
    private static ScheduledScanService Create(Repository repo,Conditions conditions,Scanner scanner,TimeProvider time){var orchestrator=new ScanOrchestrator([scanner],new ScanRepository(),time,NullLogger<ScanOrchestrator>.Instance);return new(new ScanCoordinator(orchestrator),repo,conditions,time,NullLogger<ScheduledScanService>.Instance);}
    private sealed class FixedTime(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class Repository(ScanSchedule schedule):IScanScheduleRepository{public ScanSchedule Schedule{get;private set;}=schedule;public List<ScheduledScanHistory> History{get;}=[];public Task<ScanSchedule>GetAsync(CancellationToken t)=>Task.FromResult(Schedule);public Task SaveAsync(ScanSchedule s,CancellationToken t){Schedule=s;return Task.CompletedTask;}public Task SaveHistoryAsync(ScheduledScanHistory h,CancellationToken t){History.Add(h);return Task.CompletedTask;}public Task<IReadOnlyList<ScheduledScanHistory>>GetHistoryAsync(int c,CancellationToken t)=>Task.FromResult<IReadOnlyList<ScheduledScanHistory>>(History);}
    private sealed class Conditions(bool plugged=true,bool idle=true,bool network=true,double load=10):ISystemConditionService{public bool IsBatterySaverEnabled()=>false;public bool IsPluggedIn()=>plugged;public bool IsSystemIdle()=>idle;public bool IsNetworkAvailable()=>network;public double GetSystemLoadPercent()=>load;}
    private sealed class Scanner(bool block=false):ISystemScanner{public int Count;public TaskCompletionSource Started{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public string Id=>"scheduler-test";public string DisplayName=>"Scheduler test";public async Task<IReadOnlyCollection<DiagnosticFinding>>ScanAsync(ScanContext context,CancellationToken token){Interlocked.Increment(ref Count);Started.TrySetResult();if(block)await Release.Task.WaitAsync(token);return [];}}
    private sealed class ScanRepository:IScanRepository{public Task SaveAsync(ScanSession s,CancellationToken t)=>Task.CompletedTask;public Task<IReadOnlyList<ScanSession>>GetRecentAsync(int c,CancellationToken t)=>Task.FromResult<IReadOnlyList<ScanSession>>([]);}
}
