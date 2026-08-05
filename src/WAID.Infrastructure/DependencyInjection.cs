using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Extensions.Logging;
using Microsoft.Extensions.Logging;
using Serilog.Formatting.Json;
using WAID.Application.Abstractions;
using WAID.Application.Plugins;
using WAID.Application.Services;
using WAID.Diagnosis;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.Infrastructure.Ai;
using WAID.Infrastructure.Configuration;
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
            try { if(plugin is IWaidPluginV2 v2)v2.Configure(new ControlledPluginServiceRegistry(services,v2.Sdk));else plugin.ConfigureServices(services); }
            catch (Exception exception) { catalog.RecordRegistrationFailure(plugin.Metadata, exception); }
        services.AddSingleton(catalog);
        return services;
    }

    private static IServiceCollection AddWaidLogging(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        services.AddLogging().AddSingleton<ILoggerProvider>(_ =>
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(options.DataDirectory, "logs"));
                var logger = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext()
                    .WriteTo.File(new JsonFormatter(renderMessage: true), Path.Combine(options.DataDirectory, "logs", "waid-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: options.TechnicalLogRetentionDays).CreateLogger();
                return new SerilogLoggerProvider(logger, dispose: true);
            }
            catch (IOException) { modules.Degrade("logging", "Local log storage is unavailable; WAID continues without a file sink."); return new SerilogLoggerProvider(new LoggerConfiguration().CreateLogger(), dispose: true); }
            catch (UnauthorizedAccessException) { modules.Degrade("logging", "Local log storage permission was denied; WAID continues without a file sink."); return new SerilogLoggerProvider(new LoggerConfiguration().CreateLogger(), dispose: true); }
        });
        modules.Add("logging", "Structured logging"); return services;
    }

    private static IServiceCollection AddWaidPersistence(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        Directory.CreateDirectory(options.DataDirectory); var db = new WaidDatabase($"Data Source={Path.Combine(options.DataDirectory, "waid.db")};Foreign Keys=True"); db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        services.AddSingleton(db).AddSingleton<TimeProvider>(TimeProvider.System).AddSingleton<SqliteScanRepository>()
            .AddSingleton<IScanRepository>(provider => provider.GetRequiredService<SqliteScanRepository>())
            .AddSingleton<IScanRunRepository>(provider => provider.GetRequiredService<SqliteScanRepository>())
            .AddSingleton<ISettingsRepository, SqliteSettingsRepository>().AddSingleton<IDiagnosisRepository, SqliteDiagnosisRepository>()
            .AddSingleton<IRepairHistoryRepository, SqliteRepairHistoryRepository>().AddSingleton<IHealthSnapshotRepository, SqliteHealthSnapshotRepository>()
            .AddSingleton<IScanScheduleRepository, SqliteScanScheduleRepository>().AddSingleton<IRepairApprovalRepository, SqliteRepairApprovalRepository>()
            .AddSingleton<IRepairRecommendationRepository, SqliteRepairRecommendationRepository>()
            .AddSingleton<IPredictiveHealthRepository, SqlitePredictiveHealthRepository>()
            .AddSingleton<ILiveMonitoringRepository, SqliteLiveMonitoringRepository>()
            .AddSingleton<IReliabilityTimelineRepository, SqliteReliabilityTimelineRepository>()
            .AddSingleton<IReliabilityTimelineSource, SqliteReliabilityTimelineSource>()
            .AddSingleton<IPerformanceHistoryRepository, SqlitePerformanceHistoryRepository>()
            .AddSingleton<IDigitalTwinRepository, SqliteDigitalTwinRepository>()
            .AddSingleton<IAlertRepository, SqliteAlertRepository>()
            .AddSingleton<IRepairOrchestrationRepository, SqliteRepairOrchestrationRepository>()
            .AddSingleton<IRepairOutcomeRepository, SqliteRepairOutcomeRepository>()
            .AddSingleton<IPluginInventoryRepository, SqlitePluginInventoryRepository>()
            .AddSingleton<IEnterprisePolicyRepository, SqliteEnterprisePolicyRepository>()
            .AddSingleton<ITechnicianWorkspaceRepository, SqliteTechnicianWorkspaceRepository>()
            .AddSingleton<IRepairOutcomeExportService>(provider=>new RepairOutcomeExportService(provider.GetRequiredService<IRepairOutcomeRepository>(),Path.Combine(options.DataDirectory,"Reports"),provider.GetRequiredService<TimeProvider>()))
            .AddSingleton<IRecoveryArtifactRepository, SqliteRecoveryArtifactRepository>();
        services.AddSingleton<IDriverHealthRepository, SqliteDriverHealthRepository>();
        services.AddSingleton<IBootHealthRepository, SqliteBootHealthRepository>();
        services.AddSingleton<IWindowsUpdateHealthRepository, SqliteWindowsUpdateHealthRepository>();
        services.AddSingleton<IStorageHealthRepository, SqliteStorageHealthRepository>();
        services.AddSingleton<ISecurityPostureRepository, SqliteSecurityPostureRepository>();
        services.AddSingleton<INetworkHealthRepository, SqliteNetworkHealthRepository>();
        services.AddSingleton<IEvidenceGraphRepository, SqliteEvidenceGraphRepository>();
        services.AddSingleton<IChatConversationRepository, SqliteChatConversationRepository>();
        services.AddSingleton<IOperationContextAccessor,OperationContextAccessor>()
            .AddSingleton<IAuditTrailService>(_=>new LocalAuditTrailService(Path.Combine(options.DataDirectory,"Audit"),options.AuditRetentionDays,TimeProvider.System))
            .AddSingleton<ILocalDiagnosticsService>(provider=>new LocalDiagnosticsService(Path.Combine(options.DataDirectory,"logs"),Path.Combine(options.DataDirectory,"Exports"),provider.GetRequiredService<IAuditTrailService>()))
            .AddSingleton<IDatabaseMaintenanceService>(provider => new DatabaseMaintenanceService(db, Path.Combine(options.DataDirectory, "Backups", "Database"), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<DatabaseMaintenanceService>>(), provider.GetRequiredService<IAuditTrailService>()))
            .AddSingleton<IEnterprisePolicyProvider, BuiltInEnterprisePolicyProvider>()
            .AddSingleton<IEnterprisePolicyProvider>(_=>new JsonEnterprisePolicyProvider(options.EnterprisePolicyPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Windows AI Doctor","enterprise-policy.json")))
            .AddSingleton<IEnterprisePolicyService, EnterprisePolicyService>()
            .AddSingleton<IConfigurationStateRepository, SqliteConfigurationStateRepository>()
            .AddSingleton<IConfigurationLayerSource>(_ => new LocalConfigurationLayerSource(
                options.MachineConfigurationPath ?? Path.Combine(options.DataDirectory, "Configuration", "machine-settings.json"),
                options.PolicyConfigurationPath ?? Path.Combine(options.DataDirectory, "Configuration", "policy-settings.json")))
            .AddSingleton<IConfigurationService>(provider => new ConfigurationService(provider.GetRequiredService<IConfigurationStateRepository>(), provider.GetRequiredService<IConfigurationLayerSource>(),
                Path.Combine(options.DataDirectory, "Profiles"), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<ConfigurationService>>(), provider.GetRequiredService<IAuditTrailService>(),provider.GetRequiredService<IEnterprisePolicyService>()));
        modules.Add("persistence", "SQLite persistence"); return services;
    }

    private static IServiceCollection AddWaidWindowsPlatform(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        services.AddSingleton<IPowerShellRunner, PowerShellRunner>().AddSingleton<IAdministratorService, AdministratorService>()
            .AddSingleton<IRecoveryStorageProbe, WindowsRecoveryStorageProbe>().AddSingleton<IRestorePointManager, RestorePointManager>().AddSingleton<IBackupManager>(provider => new BackupManager(Path.Combine(options.DataDirectory, "Backups"), provider.GetRequiredService<IPowerShellRunner>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupManager>>(), provider.GetRequiredService<IRecoveryArtifactRepository>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IRecoveryStorageProbe>()))
            .AddSingleton<IRollbackManager, RollbackManager>().AddSingleton<IRecoveryWorkflow, RecoveryWorkflow>().AddSingleton<IRecoveryRetentionService>(provider => new RecoveryRetentionService(Path.Combine(options.DataDirectory, "Backups"), provider.GetRequiredService<IRecoveryArtifactRepository>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RecoveryRetentionService>>())).AddSingleton<ISystemConditionService, WindowsSystemConditionService>()
            .AddSingleton<ILiveSignalCollector, WindowsLiveSignalCollector>()
            .AddSingleton<IPerformanceMetricCollector, WindowsPerformanceMetricCollector>()
            .AddSingleton<IStartupLaunchService>(_ => new WindowsStartupLaunchService(options.ApplicationExecutablePath));
        modules.Add("windows", "Windows platform adapters"); return services;
    }

    private static IServiceCollection AddWaidDiagnostics(this IServiceCollection services, WaidHostOptions options, WaidModuleCatalog modules)
    {
        services.AddSingleton<IKnowledgeRetrievalService>(_=>new OfflineKnowledgeRetrievalService(Path.Combine(options.DataDirectory,"Knowledge","index.json"),TimeProvider.System));
        services.AddSingleton<IDiagnosticsExportService>(provider => new DiagnosticsExportService(options.DataDirectory, provider.GetRequiredService<IScanRepository>(), provider.GetRequiredService<IDiagnosisRepository>(), provider.GetRequiredService<IRepairHistoryRepository>(), provider.GetRequiredService<TimeProvider>(),provider.GetRequiredService<IEnterprisePolicyService>()))
            .AddSingleton<IDiagnosticReportExporter>(_ => new DiagnosticReportExporter(Path.Combine(options.DataDirectory, "Reports")))
            .AddSingleton<IPdfReportExporter>(_ => new PdfDiagnosticReportExporter(Path.Combine(options.DataDirectory, "Reports")))
            .AddSingleton<ISystemScanner, OperatingSystemScanner>().AddSingleton<ISystemScanner, WindowsEventViewerScanner>()
            .AddSingleton<ISystemScanner, ReliabilityMonitorScanner>().AddSingleton<ISystemScanner, InstalledDriversScanner>()
            .AddSingleton<ISystemScanner, InstalledSoftwareScanner>().AddSingleton<ISystemScanner, WindowsUpdateScanner>()
            .AddSingleton<ISystemScanner, RunningServicesScanner>().AddSingleton<ISystemScanner, StartupApplicationsScanner>()
            .AddSingleton<ISystemScanner, RegistryHealthScanner>().AddSingleton<ISystemScanner, WindowsDefenderScanner>()
            .AddSingleton<ISystemScanner, NetworkConfigurationScanner>().AddSingleton<ISystemScanner, StorageHealthScanner>()
            .AddSingleton<ISystemScanner, SmartScanner>().AddSingleton<ISystemScanner, MemoryScanner>().AddSingleton<ISystemScanner, CpuScanner>()
            .AddSingleton<ISystemScanner, GpuScanner>().AddSingleton<ISystemScanner, BsodMinidumpScanner>().AddSingleton<ISystemScanner, BatteryHealthScanner>()
            .AddSingleton<IScanDataSanitizer, ScanDataSanitizer>().AddSingleton<IDriverInventoryProvider, WindowsDriverInventoryProvider>()
            .AddSingleton<IDriverConflictAnalyzer, DriverConflictAnalyzer>().AddSingleton<IStartupInventoryProvider, WindowsStartupInventoryProvider>()
            .AddSingleton<IStartupBootAnalyzer, StartupBootAnalyzer>().AddSingleton<IStartupActionPlanner, StartupActionPlanner>()
            .AddSingleton<IWindowsUpdateEvidenceProvider, WindowsUpdateEvidenceProvider>().AddSingleton<IWindowsUpdateIntelligence, WindowsUpdateIntelligence>()
            .AddSingleton<IUpdateRepairPlanner, UpdateRepairPlanner>();
        services.AddSingleton<IStorageEvidenceProvider, WindowsStorageEvidenceProvider>().AddSingleton<ICleanupEstimator, SafeCleanupEstimator>().AddSingleton<ILargeFolderAnalyzer, LargeFolderAnalyzer>().AddSingleton<IStorageHealthCenter, StorageHealthCenter>();
        services.AddSingleton<ISecurityPostureProvider, WindowsSecurityPostureProvider>().AddSingleton<ISecurityPostureAnalyzer, SecurityPostureAnalyzer>();
        services.AddSingleton<INetworkEvidenceProvider, WindowsNetworkEvidenceProvider>().AddSingleton<INetworkDiagnosticCenter>(provider => new NetworkDiagnosticCenter(provider.GetRequiredService<INetworkEvidenceProvider>(), provider.GetRequiredService<INetworkHealthRepository>(), provider.GetRequiredService<ILogger<NetworkDiagnosticCenter>>(), Path.Combine(options.DataDirectory, "Reports")));
        services.AddSingleton(ChatProviderPolicy.Default).AddSingleton<IChatEvidenceRetriever, WaidChatEvidenceRetriever>().AddSingleton<IChatPromptBuilder, GroundedChatPromptBuilder>().AddSingleton<IChatSafetyService, ChatSafetyService>().AddSingleton<IChatProvider, OfflineChatProvider>().AddSingleton<IChatAssistant, ChatAssistant>();
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
        services.AddSingleton<DiagnosticKnowledgeBase>().AddSingleton<RuleEngine>().AddSingleton<EventCorrelationEngine>().AddSingleton<EvidenceAggregationEngine>().AddSingleton<CorrelationScanner>()
            .AddSingleton<ConfidenceEngine>().AddSingleton<RecommendationEngine>().AddSingleton<ExplanationEngine>().AddSingleton<RootCauseAnalyzer>()
            .AddSingleton<HealthScoreEngine>().AddSingleton<AIReportBuilder>().AddSingleton<DiagnosisEngine>().AddSingleton<IAiAnalyzer, OfflineDiagnosisAnalyzer>();
        modules.Add("diagnosis", "Offline diagnosis"); return services;
    }

    private static IServiceCollection AddWaidContinuousOperations(this IServiceCollection services, WaidModuleCatalog modules)
    {
        services.AddSingleton(new ScannerPolicyRegistry(new ScannerExecutionPolicy(TimeSpan.FromSeconds(45), 1), new Dictionary<string, ScannerExecutionPolicy>(StringComparer.OrdinalIgnoreCase) { ["waid.windows-update"] = new(TimeSpan.FromSeconds(90)), ["waid.reliability"] = new(TimeSpan.FromSeconds(90)), ["waid.bsod"] = new(TimeSpan.FromSeconds(60)) }))
            .AddSingleton<ScanOrchestrator>().AddSingleton<ScanCoordinator>().AddSingleton<BackgroundHealthMonitoringService>()
            .AddSingleton<ScheduledScanService>().AddSingleton<ScheduledScanLoopService>().AddSingleton<EvidenceCollector>()
            .AddSingleton<RepairPrioritizationEngine>().AddSingleton<RepairApprovalWorkflow>().AddSingleton<MinidumpAnalyzer>()
            .AddSingleton<IPredictiveHealthModel, TransparentTrendPredictor>().AddSingleton<PredictiveHealthEngine>()
            .AddSingleton<IRepairOutcomeRecorder, RepairOutcomeRecorder>()
            .AddSingleton<TechnicianDashboardService>()
            .AddSingleton(RepairOrchestrationOptions.Default).AddSingleton(RepairSafetyPolicy.Default).AddSingleton<IRepairSafetyScorer, DeterministicRepairSafetyScorer>().AddSingleton<IRepairSimulationEngine, DeterministicRepairSimulationEngine>().AddSingleton<IRepairValidator, DefaultRepairValidator>().AddSingleton<IRepairDependencyCatalog, EmptyRepairDependencyCatalog>().AddSingleton<RepairOrchestrator>()
            .AddSingleton<IAlertDeliveryChannel, InAppAlertChannel>().AddSingleton<IAlertPolicy, AllowConfiguredAlertPolicy>().AddSingleton<AlertManager>()
            .AddSingleton<LiveSignalAggregator>().AddSingleton<LiveAlertEvaluator>().AddSingleton<ILiveMonitoringPolicy, EnterpriseLiveMonitoringPolicy>().AddSingleton<LiveMonitoringService>()
            .AddSingleton<ReliabilityTimelineProjector>().AddSingleton<ReliabilityTimelineExporter>()
            .AddSingleton<PerformanceAggregationEngine>().AddSingleton<PerformanceHistoryService>()
            .AddSingleton<ISystemSnapshotComponentProvider>(_=>new EnvironmentSnapshotComponentProvider("hardware"))
            .AddSingleton<ISystemSnapshotComponentProvider>(_=>new EnvironmentSnapshotComponentProvider("os"))
            .AddSingleton<ISystemSnapshotComponentProvider>(p=>new SqliteSnapshotComponentProvider(p.GetRequiredService<WaidDatabase>(),"drivers","component-1.0",new Dictionary<string,string>{{"latest","SELECT COALESCE(MAX(generated_utc),'Unavailable') FROM driver_analysis_runs"},{"runs","SELECT COUNT(*) FROM driver_analysis_runs"}}))
            .AddSingleton<ISystemSnapshotComponentProvider>(p=>new SqliteSnapshotComponentProvider(p.GetRequiredService<WaidDatabase>(),"services","component-1.0",new Dictionary<string,string>{{"findings","SELECT COUNT(*) FROM findings WHERE scanner_id LIKE '%service%'"}}))
            .AddSingleton<ISystemSnapshotComponentProvider>(p=>new SqliteSnapshotComponentProvider(p.GetRequiredService<WaidDatabase>(),"startup","component-1.0",new Dictionary<string,string>{{"runs","SELECT COUNT(*) FROM boot_analysis_runs"}}))
            .AddSingleton<ISystemSnapshotComponentProvider>(p=>new SqliteSnapshotComponentProvider(p.GetRequiredService<WaidDatabase>(),"update","component-1.0",new Dictionary<string,string>{{"runs","SELECT COUNT(*) FROM windows_update_analysis_runs"}}))
            .AddSingleton<ISystemSnapshotComponentProvider>(p=>new SqliteSnapshotComponentProvider(p.GetRequiredService<WaidDatabase>(),"security","component-1.0",new Dictionary<string,string>{{"runs","SELECT COUNT(*) FROM security_posture_runs"}}))
            .AddSingleton<ISystemSnapshotComponentProvider>(p=>new SqliteSnapshotComponentProvider(p.GetRequiredService<WaidDatabase>(),"storage","component-1.0",new Dictionary<string,string>{{"runs","SELECT COUNT(*) FROM storage_health_runs"}}))
            .AddSingleton<ISystemSnapshotComponentProvider>(p=>new SqliteSnapshotComponentProvider(p.GetRequiredService<WaidDatabase>(),"configuration","component-1.0",new Dictionary<string,string>{{"version","SELECT COALESCE(MAX(version),0) FROM configuration_state"}}))
            .AddSingleton<DigitalTwinService>();
        modules.Add("operations", "Monitoring and scheduling"); return services;
    }
}
