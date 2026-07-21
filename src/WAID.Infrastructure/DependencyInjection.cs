using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Extensions.Logging;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Application.Plugins;
using WAID.Application.Services;
using WAID.Diagnosis;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.Infrastructure.Ai;
using WAID.Infrastructure.Diagnostics;
using WAID.Infrastructure.Persistence;
using WAID.Infrastructure.Plugins;
using WAID.Infrastructure.PowerShell;
using WAID.Infrastructure.Repairs;
using WAID.KnowledgeBase;

namespace WAID.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWaidInfrastructure(this IServiceCollection services, string dataDirectory) =>
        services.AddWaidInfrastructure(WaidHostOptions.CreateDesktopDefaults(dataDirectory));

    public static IServiceCollection AddWaidInfrastructure(this IServiceCollection services, WaidHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(services); options.Validate();
        var modules = new WaidModuleCatalog();
        services.AddSingleton(options).AddSingleton(modules).AddWaidLogging(options, modules).AddWaidPersistence(options, modules)
            .AddWaidWindowsPlatform(options, modules).AddWaidDiagnostics(options, modules).AddWaidRepairs(modules)
            .AddWaidOfflineDiagnosis(modules).AddWaidContinuousOperations(modules);
        return services;
    }

    public static IServiceCollection AddWaidPlugins(this IServiceCollection services, string pluginDirectory, Version hostVersion) =>
        services.AddWaidPlugins(new PluginSecurityPolicy(["WAID Engineering"]), pluginDirectory, hostVersion);

    public static IServiceCollection AddWaidPlugins(this IServiceCollection services, PluginSecurityPolicy policy, string pluginDirectory, Version hostVersion)
    {
        var catalog = new PluginCatalog();
        foreach (IWaidPlugin plugin in new PluginLoader().Load(pluginDirectory, hostVersion, policy, catalog))
            try { plugin.ConfigureServices(services); }
            catch (Exception exception) { catalog.RecordRegistrationFailure(plugin.Metadata, exception); }
        services.AddSingleton(catalog);
        return services;
    }

    private static IServiceCollection AddWaidLogging(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        services.AddLogging().AddSingleton<ILoggerProvider>(_ =>
        {
            Directory.CreateDirectory(Path.Combine(options.DataDirectory, "logs"));
            var logger = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext()
                .WriteTo.File(Path.Combine(options.DataDirectory, "logs", "waid-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14).CreateLogger();
            return new SerilogLoggerProvider(logger, dispose: true);
        });
        modules.Add("logging", "Structured logging"); return services;
    }

    private static IServiceCollection AddWaidPersistence(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        Directory.CreateDirectory(options.DataDirectory); var db = new WaidDatabase($"Data Source={Path.Combine(options.DataDirectory, "waid.db")};Foreign Keys=True"); db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        services.AddSingleton(db).AddSingleton<TimeProvider>(TimeProvider.System).AddSingleton<IScanRepository, SqliteScanRepository>()
            .AddSingleton<ISettingsRepository, SqliteSettingsRepository>().AddSingleton<IDiagnosisRepository, SqliteDiagnosisRepository>()
            .AddSingleton<IRepairHistoryRepository, SqliteRepairHistoryRepository>().AddSingleton<IHealthSnapshotRepository, SqliteHealthSnapshotRepository>()
            .AddSingleton<IScanScheduleRepository, SqliteScanScheduleRepository>().AddSingleton<IRepairApprovalRepository, SqliteRepairApprovalRepository>();
        modules.Add("persistence", "SQLite persistence"); return services;
    }

    private static IServiceCollection AddWaidWindowsPlatform(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        services.AddSingleton<IPowerShellRunner, PowerShellRunner>().AddSingleton<IAdministratorService, AdministratorService>()
            .AddSingleton<IRestorePointManager, RestorePointManager>().AddSingleton<IBackupManager>(provider => new BackupManager(Path.Combine(options.DataDirectory, "Backups"), provider.GetRequiredService<IPowerShellRunner>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupManager>>()))
            .AddSingleton<IRollbackManager, RollbackManager>().AddSingleton<ISystemConditionService, WindowsSystemConditionService>()
            .AddSingleton<IStartupLaunchService>(_ => new WindowsStartupLaunchService(options.ApplicationExecutablePath));
        modules.Add("windows", "Windows platform adapters"); return services;
    }

    private static IServiceCollection AddWaidDiagnostics(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        services.AddSingleton<IDiagnosticsExportService>(provider => new DiagnosticsExportService(options.DataDirectory, provider.GetRequiredService<IScanRepository>(), provider.GetRequiredService<IDiagnosisRepository>(), provider.GetRequiredService<IRepairHistoryRepository>(), provider.GetRequiredService<TimeProvider>()))
            .AddSingleton<IDiagnosticReportExporter>(_ => new DiagnosticReportExporter(Path.Combine(options.DataDirectory, "Reports")))
            .AddSingleton<IPdfReportExporter>(_ => new PdfDiagnosticReportExporter(Path.Combine(options.DataDirectory, "Reports")))
            .AddSingleton<ISystemScanner, OperatingSystemScanner>().AddSingleton<ISystemScanner, WindowsEventViewerScanner>()
            .AddSingleton<ISystemScanner, ReliabilityMonitorScanner>().AddSingleton<ISystemScanner, InstalledDriversScanner>()
            .AddSingleton<ISystemScanner, InstalledSoftwareScanner>().AddSingleton<ISystemScanner, WindowsUpdateScanner>()
            .AddSingleton<ISystemScanner, RunningServicesScanner>().AddSingleton<ISystemScanner, StartupApplicationsScanner>()
            .AddSingleton<ISystemScanner, RegistryHealthScanner>().AddSingleton<ISystemScanner, WindowsDefenderScanner>()
            .AddSingleton<ISystemScanner, NetworkConfigurationScanner>().AddSingleton<ISystemScanner, StorageHealthScanner>()
            .AddSingleton<ISystemScanner, SmartScanner>().AddSingleton<ISystemScanner, MemoryScanner>().AddSingleton<ISystemScanner, CpuScanner>()
            .AddSingleton<ISystemScanner, GpuScanner>().AddSingleton<ISystemScanner, BsodMinidumpScanner>().AddSingleton<ISystemScanner, BatteryHealthScanner>();
        modules.Add("diagnostics", "Windows diagnostics"); return services;
    }

    private static IServiceCollection AddWaidRepairs(this IServiceCollection services, WaidModuleCatalog modules)
    {
        services.AddSingleton<IRepairModule, DismRepairModule>().AddSingleton<IRepairModule, SfcRepairModule>()
            .AddSingleton<IRepairModule, WindowsUpdateResetModule>().AddSingleton<IRepairModule, DnsResetModule>()
            .AddSingleton<IRepairModule, WinsockResetModule>().AddSingleton<IRepairModule, TcpIpResetModule>()
            .AddSingleton<RepairRegistry>().AddSingleton<RepairExecutor>().AddSingleton<RepairQueue>().AddSingleton<RepairHistory>();
        modules.Add("repairs", "Safe repair workflow"); return services;
    }

    private static IServiceCollection AddWaidOfflineDiagnosis(this IServiceCollection services, WaidModuleCatalog modules)
    {
        services.AddSingleton<DiagnosticKnowledgeBase>().AddSingleton<RuleEngine>().AddSingleton<EventCorrelationEngine>().AddSingleton<CorrelationScanner>()
            .AddSingleton<ConfidenceEngine>().AddSingleton<RecommendationEngine>().AddSingleton<ExplanationEngine>().AddSingleton<RootCauseAnalyzer>()
            .AddSingleton<HealthScoreEngine>().AddSingleton<AIReportBuilder>().AddSingleton<DiagnosisEngine>().AddSingleton<IAiAnalyzer, OfflineDiagnosisAnalyzer>();
        modules.Add("diagnosis", "Offline diagnosis"); return services;
    }

    private static IServiceCollection AddWaidContinuousOperations(this IServiceCollection services, WaidModuleCatalog modules)
    {
        services.AddSingleton(new ScannerPolicyRegistry(new ScannerExecutionPolicy(TimeSpan.FromSeconds(45), 1), new Dictionary<string, ScannerExecutionPolicy>(StringComparer.OrdinalIgnoreCase) { ["waid.windows-update"] = new(TimeSpan.FromSeconds(90)), ["waid.reliability"] = new(TimeSpan.FromSeconds(90)), ["waid.bsod"] = new(TimeSpan.FromSeconds(60)) }))
            .AddSingleton<ScanOrchestrator>().AddSingleton<ScanCoordinator>().AddSingleton<BackgroundHealthMonitoringService>()
            .AddSingleton<ScheduledScanService>().AddSingleton<ScheduledScanLoopService>().AddSingleton<EvidenceCollector>()
            .AddSingleton<RepairPrioritizationEngine>().AddSingleton<RepairApprovalWorkflow>().AddSingleton<MinidumpAnalyzer>();
        modules.Add("operations", "Monitoring and scheduling"); return services;
    }
}
