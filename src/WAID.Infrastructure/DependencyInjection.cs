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
        return services.AddSingleton(db).AddSingleton<TimeProvider>(TimeProvider.System).AddSingleton<IScanRepository,SqliteScanRepository>().AddSingleton<ISettingsRepository,SqliteSettingsRepository>().AddSingleton<IPowerShellRunner,PowerShellRunner>().AddSingleton<ISystemScanner,DiskSpaceScanner>().AddSingleton<ISystemScanner,OperatingSystemScanner>().AddSingleton<IRepairAction,WindowsCleanupRepair>().AddSingleton<IAiAnalyzer,RulesBasedAiAnalyzer>().AddSingleton<ScanOrchestrator>();
    }
    public static IServiceCollection AddWaidPlugins(this IServiceCollection services,string pluginDirectory,Version hostVersion)
    {
        foreach(IWaidPlugin plugin in new PluginLoader().Load(pluginDirectory,hostVersion)) plugin.ConfigureServices(services);
        return services;
    }
}
