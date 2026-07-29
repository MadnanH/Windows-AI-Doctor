using Microsoft.Extensions.DependencyInjection;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Infrastructure.Diagnostics;
using WAID.Infrastructure.Persistence;
using WAID.Infrastructure.Plugins;

namespace WAID.Infrastructure;

public sealed record WaidHostOptions(
    int ConfigurationVersion,
    string DataDirectory,
    string PluginDirectory,
    string ApplicationExecutablePath,
    Version HostVersion,
    IReadOnlyCollection<string> AllowedPluginPublishers,
    bool RequireSignedPlugins = false,
    int TechnicalLogRetentionDays = 14,
    int AuditRetentionDays = 365,
    string? MachineConfigurationPath = null,
    string? PolicyConfigurationPath = null)
{
    public const int CurrentConfigurationVersion = 1;

    public WaidHostOptions Validate()
    {
        if (ConfigurationVersion != CurrentConfigurationVersion)
            throw new WaidStartupException("WAID-CONFIG-VERSION", $"Configuration version {ConfigurationVersion} is unsupported. Expected version {CurrentConfigurationVersion}.", "Use a configuration created for this WAID version.");
        ValidateAbsoluteDirectory(DataDirectory, nameof(DataDirectory));
        ValidateAbsoluteDirectory(PluginDirectory, nameof(PluginDirectory));
        if (string.IsNullOrWhiteSpace(ApplicationExecutablePath) || !Path.IsPathFullyQualified(ApplicationExecutablePath))
            throw new WaidStartupException("WAID-CONFIG-EXECUTABLE", "The application executable path is missing or invalid.", "Repair or reinstall the application.");
        if (HostVersion.Major < 1) throw new WaidStartupException("WAID-CONFIG-HOST-VERSION", "The plugin host version is invalid.", "Repair or reinstall the application.");
        if (AllowedPluginPublishers.Count == 0 || AllowedPluginPublishers.Any(string.IsNullOrWhiteSpace))
            throw new WaidStartupException("WAID-CONFIG-PUBLISHERS", "At least one valid plugin publisher must be allowed.", "Restore the default plugin security policy.");
        if (TechnicalLogRetentionDays is < 1 or > 90 || AuditRetentionDays is < 30 or > 3650)
            throw new WaidStartupException("WAID-CONFIG-RETENTION", "Log or audit retention is outside the supported range.", "Restore the default retention policy.");
        if (MachineConfigurationPath is not null && !Path.IsPathFullyQualified(MachineConfigurationPath) || PolicyConfigurationPath is not null && !Path.IsPathFullyQualified(PolicyConfigurationPath))
            throw new WaidStartupException("WAID-CONFIG-LAYER-PATH", "Machine and policy configuration paths must be absolute.", "Restore the default configuration paths.");
        return this;
    }

    public static WaidHostOptions CreateDesktopDefaults(string dataDirectory) => new(
        CurrentConfigurationVersion,
        Path.GetFullPath(dataDirectory),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Plugins")),
        Path.GetFullPath(Environment.ProcessPath ?? throw new WaidStartupException("WAID-CONFIG-EXECUTABLE", "The executable path is unavailable.", "Restart or repair the application.")),
        new Version(1, 0, 0),
        ["WAID Engineering"], MachineConfigurationPath: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Windows AI Doctor", "machine-settings.json"),
        PolicyConfigurationPath: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Windows AI Doctor", "policy-settings.json"));

    private static void ValidateAbsoluteDirectory(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new WaidStartupException("WAID-CONFIG-DIRECTORY", $"{name} must be an absolute directory.", "Restore the default application configuration.");
    }
}

public sealed class WaidStartupException(string code, string userMessage, string recoveryAction, Exception? innerException = null)
    : InvalidOperationException(userMessage, innerException)
{
    public string Code { get; } = code;
    public string UserMessage { get; } = userMessage;
    public string RecoveryAction { get; } = recoveryAction;
}

public enum WaidModuleState { Configured, Degraded }
public sealed record WaidModuleStatus(string Id, string DisplayName, WaidModuleState State, string Detail);
public sealed class WaidModuleCatalog
{
    private readonly List<WaidModuleStatus> _items = [];
    public IReadOnlyList<WaidModuleStatus> Items => _items.AsReadOnly();
    internal void Add(string id, string name) => _items.Add(new(id, name, WaidModuleState.Configured, "Registration validated."));
    internal void Degrade(string id, string detail) { var index = _items.FindIndex(item => item.Id == id); if (index >= 0) _items[index] = _items[index] with { State = WaidModuleState.Degraded, Detail = detail }; }
}

public static class WaidServiceRegistrationValidator
{
    private static readonly Type[] RequiredSingletonContracts =
    [
        typeof(WaidHostOptions), typeof(WaidModuleCatalog), typeof(WaidDatabase), typeof(TimeProvider),
        typeof(IScanRepository), typeof(IScanRunRepository), typeof(IScanDataSanitizer), typeof(ISettingsRepository), typeof(IDiagnosisRepository), typeof(IRepairHistoryRepository),
        typeof(IHealthSnapshotRepository), typeof(IScanScheduleRepository), typeof(IRepairApprovalRepository),
        typeof(IDiagnosticsExportService), typeof(IAdministratorService), typeof(IRestorePointManager), typeof(IBackupManager),
        typeof(IRollbackManager), typeof(IDiagnosticReportExporter), typeof(IPdfReportExporter), typeof(ScanCoordinator),
        typeof(RepairExecutor), typeof(BackgroundHealthMonitoringService), typeof(LiveMonitoringService), typeof(ILiveMonitoringRepository), typeof(IReliabilityTimelineRepository), typeof(ReliabilityTimelineProjector), typeof(IPerformanceHistoryRepository), typeof(PerformanceHistoryService), typeof(IDigitalTwinRepository), typeof(DigitalTwinService), typeof(ScheduledScanService), typeof(PluginCatalog),
        typeof(IAuditTrailService), typeof(ILocalDiagnosticsService), typeof(IOperationContextAccessor), typeof(IDatabaseMaintenanceService),
        typeof(IConfigurationStateRepository), typeof(IConfigurationLayerSource), typeof(IConfigurationService)
    ];

    public static ServiceProvider BuildValidatedWaidServiceProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var contract in RequiredSingletonContracts)
        {
            var registrations = services.Where(item => item.ServiceType == contract).ToArray();
            if (registrations.Length == 0)
                throw new WaidStartupException("WAID-DI-MISSING", $"Required service {contract.Name} is not registered.", "Repair the application installation or restore its default configuration.");
            if (registrations.Length > 1)
                throw new WaidStartupException("WAID-DI-DUPLICATE", $"Required service {contract.Name} is registered more than once.", "Disable conflicting extensions and restart WAID.");
            if (registrations[0].Lifetime != ServiceLifetime.Singleton)
                throw new WaidStartupException("WAID-DI-LIFETIME", $"Required service {contract.Name} must be a singleton.", "Repair the application installation.");
        }

        try
        {
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            _ = provider.GetRequiredService<WaidHostOptions>().Validate();
            foreach (var contract in RequiredSingletonContracts) _ = provider.GetRequiredService(contract);
            var scanners = provider.GetServices<ISystemScanner>().ToArray();
            if (scanners.Length == 0 || scanners.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != scanners.Length)
                throw new WaidStartupException("WAID-DI-SCANNERS", "Scanner registrations are missing or contain duplicate IDs.", "Disable conflicting plugins and restart WAID.");
            foreach (var scanner in scanners)
            {
                var metadata = scanner.Metadata.Validate();
                if (!string.Equals(scanner.Id, metadata.Id, StringComparison.OrdinalIgnoreCase)) throw new WaidStartupException("WAID-DI-SCANNER-METADATA", $"Scanner {scanner.Id} metadata does not match its registration ID.", "Disable the invalid plugin and restart WAID.");
            }
            var repairs = provider.GetServices<IRepairModule>().ToArray();
            if (repairs.Length == 0 || repairs.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != repairs.Length)
                throw new WaidStartupException("WAID-DI-REPAIRS", "Repair registrations are missing or contain duplicate IDs.", "Disable conflicting plugins and restart WAID.");
            return provider;
        }
        catch (WaidStartupException) { throw; }
        catch (Exception exception)
        {
            throw new WaidStartupException("WAID-DI-INVALID", "Application services could not be initialized safely.", "Disable recently installed plugins or repair the application.", exception);
        }
    }
}
