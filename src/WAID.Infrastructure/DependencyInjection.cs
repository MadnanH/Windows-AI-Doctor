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
using WAID.Diagnosis;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.KnowledgeBase;
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
            .AddSingleton<IDiagnosisRepository,SqliteDiagnosisRepository>()
            .AddSingleton<IRepairHistoryRepository,SqliteRepairHistoryRepository>()
            .AddSingleton<IDiagnosticsExportService>(provider => new DiagnosticsExportService(
                dataDirectory,
                provider.GetRequiredService<IScanRepository>(),
                provider.GetRequiredService<IDiagnosisRepository>(),
                provider.GetRequiredService<IRepairHistoryRepository>(),
                provider.GetRequiredService<TimeProvider>()))
            .AddSingleton<IPowerShellRunner,PowerShellRunner>()
            .AddSingleton<IAdministratorService,AdministratorService>()
            .AddSingleton<IRestorePointManager,RestorePointManager>()
            .AddSingleton<IBackupManager>(provider => new BackupManager(
                Path.Combine(dataDirectory, "Backups"),
                provider.GetRequiredService<IPowerShellRunner>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupManager>>()))
            .AddSingleton<IRollbackManager,RollbackManager>()
            .AddSingleton<ISystemScanner,OperatingSystemScanner>()
            .AddSingleton<ISystemScanner,WindowsEventViewerScanner>()
            .AddSingleton<ISystemScanner,ReliabilityMonitorScanner>()
            .AddSingleton<ISystemScanner,InstalledDriversScanner>()
            .AddSingleton<ISystemScanner,InstalledSoftwareScanner>()
            .AddSingleton<ISystemScanner,WindowsUpdateScanner>()
            .AddSingleton<ISystemScanner,RunningServicesScanner>()
            .AddSingleton<ISystemScanner,StartupApplicationsScanner>()
            .AddSingleton<ISystemScanner,RegistryHealthScanner>()
            .AddSingleton<ISystemScanner,WindowsDefenderScanner>()
            .AddSingleton<ISystemScanner,NetworkConfigurationScanner>()
            .AddSingleton<ISystemScanner,StorageHealthScanner>()
            .AddSingleton<ISystemScanner,SmartScanner>()
            .AddSingleton<ISystemScanner,MemoryScanner>()
            .AddSingleton<ISystemScanner,CpuScanner>()
            .AddSingleton<ISystemScanner,GpuScanner>()
            .AddSingleton<ISystemScanner,BsodMinidumpScanner>()
            .AddSingleton<IRepairModule,DismRepairModule>()
            .AddSingleton<IRepairModule,SfcRepairModule>()
            .AddSingleton<IRepairModule,WindowsUpdateResetModule>()
            .AddSingleton<IRepairModule,DnsResetModule>()
            .AddSingleton<IRepairModule,WinsockResetModule>()
            .AddSingleton<IRepairModule,TcpIpResetModule>()
            .AddSingleton<DiagnosticKnowledgeBase>()
            .AddSingleton<RuleEngine>()
            .AddSingleton<EventCorrelationEngine>()
            .AddSingleton<CorrelationScanner>()
            .AddSingleton<ConfidenceEngine>()
            .AddSingleton<RecommendationEngine>()
            .AddSingleton<ExplanationEngine>()
            .AddSingleton<RootCauseAnalyzer>()
            .AddSingleton<HealthScoreEngine>()
            .AddSingleton<AIReportBuilder>()
            .AddSingleton<DiagnosisEngine>()
            .AddSingleton<IAiAnalyzer,OfflineDiagnosisAnalyzer>()
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
