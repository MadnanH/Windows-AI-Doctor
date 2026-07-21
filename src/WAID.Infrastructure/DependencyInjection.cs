using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Infrastructure.Ai;
using WAID.Infrastructure.Diagnostics;
using WAID.Infrastructure.Persistence;
using WAID.Infrastructure.PowerShell;
using WAID.Infrastructure.Repairs;
using WAID.Infrastructure.Plugins;
using WAID.Application.Plugins;
namespace WAID.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddWaidInfrastructure(this IServiceCollection services,string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory); var db=new WaidDatabase($"Data Source={Path.Combine(dataDirectory,"waid.db")};Foreign Keys=True"); db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        Log.Logger=new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext().WriteTo.File(Path.Combine(dataDirectory,"logs","waid-.log"),rollingInterval:RollingInterval.Day,retainedFileCountLimit:14).CreateLogger();
        return services
            .AddLogging(builder => builder.AddSerilog(dispose: false))
            .AddSingleton(db)
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .AddSingleton<IScanRepository,SqliteScanRepository>()
            .AddSingleton<ISettingsRepository,SqliteSettingsRepository>()
            .AddSingleton<IRepairHistoryRepository,SqliteRepairHistoryRepository>()
            .AddSingleton<IPowerShellRunner,PowerShellRunner>()
            .AddSingleton<IAdministratorService,AdministratorService>()
            .AddSingleton<IRestorePointManager,RestorePointManager>()
            .AddSingleton<IBackupManager>(provider => new BackupManager(
                Path.Combine(dataDirectory, "Backups"),
                provider.GetRequiredService<IPowerShellRunner>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupManager>>()))
            .AddSingleton<IRollbackManager,RollbackManager>()
            .AddSingleton<ISystemScanner,DiskSpaceScanner>()
            .AddSingleton<ISystemScanner,OperatingSystemScanner>()
            .AddSingleton<IRepairModule,DismRepairModule>()
            .AddSingleton<IRepairModule,SfcRepairModule>()
            .AddSingleton<IRepairModule,WindowsUpdateResetModule>()
            .AddSingleton<IRepairModule,DnsResetModule>()
            .AddSingleton<IRepairModule,WinsockResetModule>()
            .AddSingleton<IRepairModule,TcpIpResetModule>()
            .AddSingleton<IAiAnalyzer,RulesBasedAiAnalyzer>()
            .AddSingleton<ScanOrchestrator>()
            .AddSingleton<RepairRegistry>()
            .AddSingleton<RepairExecutor>()
            .AddSingleton<RepairQueue>()
            .AddSingleton<RepairHistory>();
    }
    public static IServiceCollection AddWaidPlugins(this IServiceCollection services,string pluginDirectory,Version hostVersion)
    {
        foreach(IWaidPlugin plugin in new PluginLoader().Load(pluginDirectory,hostVersion)) plugin.ConfigureServices(services);
        return services;
    }
}
