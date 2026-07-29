using WAID.Application.Services;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class ScheduledScanPersistenceTests
{
    [Fact]
    public async Task Schedule_and_history_round_trip_with_policy_metadata()
    {
        var path=Path.Combine(Path.GetTempPath(),$"waid-schedule-{Guid.NewGuid():N}.db");
        try
        {
            var database=new WaidDatabase($"Data Source={path};Pooling=False");await database.InitializeAsync(CancellationToken.None);var repository=new SqliteScanScheduleRepository(database);var now=DateTimeOffset.UtcNow;
            var schedule=new ScanSchedule(true,ScheduleFrequency.Monthly,TimeSpan.FromHours(1),DayOfWeek.Monday,new TimeOnly(8,0),true,true,null,28,now.AddDays(1),null,true,65,"OrganizationPolicy");
            await repository.SaveAsync(schedule,CancellationToken.None);var history=new ScheduledScanHistory(Guid.NewGuid(),now,now,now,ScheduledScanOutcome.Completed,"Completed",Guid.NewGuid(),schedule.PolicySource,schedule.NextRunUtc);await repository.SaveHistoryAsync(history,CancellationToken.None);
            Assert.Equal(schedule,await repository.GetAsync(CancellationToken.None));Assert.Equal(history,Assert.Single(await repository.GetHistoryAsync(10,CancellationToken.None)));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
    }
}
