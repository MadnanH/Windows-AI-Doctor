using System.Windows.Input;
using Microsoft.UI.Xaml;
using WAID.Application.Abstractions;
using WAID.Domain.Settings;

namespace WAID.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configuration;
    private readonly IDatabaseMaintenanceService _database;
    private bool _startup, _ai, _telemetry, _experimental, _advancedCorrelation, _experimentalRepairPlanning, _cloudAi, _restoreApproved, _profileExperimentalApproved;
    private string _theme = "System", _status = "", _databaseState = "Checking…", _databaseDetail = "", _backupLocation = "", _migrationStatus = "", _restorePath = "", _searchText = "", _policyStatus = "No policy locks", _activeProfile = "None", _profilePath = "", _profileName = "My WAID Profile", _profileDirectory = "";
    private int _scanTimeoutSeconds = 120;
    private IReadOnlySet<string> _locks = new HashSet<string>();
    private Visibility _generalVisibility = Visibility.Visible, _privacyVisibility = Visibility.Visible, _experimentalVisibility = Visibility.Visible, _profilesVisibility = Visibility.Visible, _databaseVisibility = Visibility.Visible;

    public SettingsViewModel(IConfigurationService configuration, IDatabaseMaintenanceService database)
    {
        _configuration = configuration; _database = database;
        SaveCommand = new AsyncCommand(SaveAsync); ResetCommand = new AsyncCommand(ResetAsync); RefreshDatabaseCommand = new AsyncCommand(RefreshDatabaseAsync);
        BackupDatabaseCommand = new AsyncCommand(BackupDatabaseAsync); RestoreDatabaseCommand = new AsyncCommand(RestoreDatabaseAsync);
        ImportProfileCommand = new AsyncCommand(ImportProfileAsync); ExportProfileCommand = new AsyncCommand(ExportProfileAsync);
        _ = LoadAsync();
    }

    public bool RunScansAtStartup { get => _startup; set => Set(ref _startup, value); }
    public bool EnableAiAnalysis { get => _ai; set => Set(ref _ai, value); }
    public bool AllowTelemetry { get => _telemetry; set => Set(ref _telemetry, value); }
    public bool EnableExperimentalFeatures { get => _experimental; set => Set(ref _experimental, value); }
    public bool AdvancedEventCorrelation { get => _advancedCorrelation; set => Set(ref _advancedCorrelation, value); }
    public bool ExperimentalRepairPlanning { get => _experimentalRepairPlanning; set => Set(ref _experimentalRepairPlanning, value); }
    public bool CloudAiProvider { get => _cloudAi; set => Set(ref _cloudAi, value); }
    public bool RestoreApproved { get => _restoreApproved; set => Set(ref _restoreApproved, value); }
    public bool ProfileExperimentalApproved { get => _profileExperimentalApproved; set => Set(ref _profileExperimentalApproved, value); }
    public string Theme { get => _theme; set => Set(ref _theme, value); }
    public int ScanTimeoutSeconds { get => _scanTimeoutSeconds; set => Set(ref _scanTimeoutSeconds, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string DatabaseState { get => _databaseState; private set => Set(ref _databaseState, value); }
    public string DatabaseDetail { get => _databaseDetail; private set => Set(ref _databaseDetail, value); }
    public string BackupLocation { get => _backupLocation; private set => Set(ref _backupLocation, value); }
    public string MigrationStatus { get => _migrationStatus; private set => Set(ref _migrationStatus, value); }
    public string RestorePath { get => _restorePath; set => Set(ref _restorePath, value); }
    public string PolicyStatus { get => _policyStatus; private set => Set(ref _policyStatus, value); }
    public string ActiveProfile { get => _activeProfile; private set => Set(ref _activeProfile, value); }
    public string ProfilePath { get => _profilePath; set => Set(ref _profilePath, value); }
    public string ProfileName { get => _profileName; set => Set(ref _profileName, value); }
    public string ProfileDirectory { get => _profileDirectory; set => Set(ref _profileDirectory, value); }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) ApplySearch(); } }
    public bool CanEditStartup => !_locks.Contains(SettingKeys.RunScansAtStartup);
    public bool CanEditAi => !_locks.Contains(SettingKeys.EnableAiAnalysis);
    public bool CanEditTelemetry => !_locks.Contains(SettingKeys.AllowTelemetry);
    public bool CanEditTheme => !_locks.Contains(SettingKeys.Theme);
    public bool CanEditTimeout => !_locks.Contains(SettingKeys.ScanTimeoutSeconds);
    public bool CanEditExperimental => !_locks.Contains(SettingKeys.EnableExperimentalFeatures);
    public Visibility GeneralVisibility { get => _generalVisibility; private set => Set(ref _generalVisibility, value); }
    public Visibility PrivacyVisibility { get => _privacyVisibility; private set => Set(ref _privacyVisibility, value); }
    public Visibility ExperimentalVisibility { get => _experimentalVisibility; private set => Set(ref _experimentalVisibility, value); }
    public Visibility ProfilesVisibility { get => _profilesVisibility; private set => Set(ref _profilesVisibility, value); }
    public Visibility DatabaseVisibility { get => _databaseVisibility; private set => Set(ref _databaseVisibility, value); }
    public ICommand SaveCommand { get; } public ICommand ResetCommand { get; } public ICommand RefreshDatabaseCommand { get; }
    public ICommand BackupDatabaseCommand { get; } public ICommand RestoreDatabaseCommand { get; } public ICommand ImportProfileCommand { get; } public ICommand ExportProfileCommand { get; }

    private async Task LoadAsync()
    {
        try { Apply(await _configuration.CreateSnapshotAsync(CancellationToken.None)); }
        catch (Exception exception) { Status = $"Configuration is invalid ({exception.GetType().Name}). Unsafe features remain disabled. Review Logs & Audit."; }
        await RefreshDatabaseAsync();
    }

    private void Apply(ConfigurationSnapshot snapshot)
    {
        var settings = snapshot.Settings; RunScansAtStartup = settings.RunScansAtStartup; EnableAiAnalysis = settings.EnableAiAnalysis; AllowTelemetry = settings.AllowTelemetry;
        Theme = settings.Theme; ScanTimeoutSeconds = settings.ScanTimeoutSeconds; EnableExperimentalFeatures = settings.EnableExperimentalFeatures;
        AdvancedEventCorrelation = snapshot.IsEnabled(FeatureFlags.AdvancedEventCorrelation); ExperimentalRepairPlanning = snapshot.IsEnabled(FeatureFlags.ExperimentalRepairPlanning); CloudAiProvider = snapshot.IsEnabled(FeatureFlags.CloudAiProvider);
        _locks = snapshot.LockedSettings; ActiveProfile = snapshot.ActiveProfile ?? "None"; PolicyStatus = _locks.Count == 0 ? "No policy locks" : $"Managed by policy: {string.Join(", ", _locks.Order())}";
        Notify(nameof(CanEditStartup)); Notify(nameof(CanEditAi)); Notify(nameof(CanEditTelemetry)); Notify(nameof(CanEditTheme)); Notify(nameof(CanEditTimeout)); Notify(nameof(CanEditExperimental));
    }

    private async Task SaveAsync()
    {
        var settings = new ApplicationSettings { RunScansAtStartup = RunScansAtStartup, EnableAiAnalysis = EnableAiAnalysis, AllowTelemetry = AllowTelemetry, Theme = Theme, ScanTimeoutSeconds = ScanTimeoutSeconds, EnableExperimentalFeatures = EnableExperimentalFeatures };
        var flags = new Dictionary<string, bool> { [FeatureFlags.AdvancedEventCorrelation] = AdvancedEventCorrelation, [FeatureFlags.ExperimentalRepairPlanning] = ExperimentalRepairPlanning, [FeatureFlags.CloudAiProvider] = CloudAiProvider };
        var result = await _configuration.SaveUserAsync(settings, flags, CancellationToken.None); Status = ResultText(result);
        if (result.Succeeded) Apply(await _configuration.CreateSnapshotAsync(CancellationToken.None));
    }

    private async Task ResetAsync() { var result = await _configuration.ResetUserAsync(CancellationToken.None); Status = ResultText(result); if (result.Succeeded) Apply(await _configuration.CreateSnapshotAsync(CancellationToken.None)); }
    private async Task ImportProfileAsync() { var result = await _configuration.ImportProfileAsync(ProfilePath, ProfileExperimentalApproved, CancellationToken.None); Status = ResultText(result); ProfileExperimentalApproved = false; if (result.Succeeded) Apply(await _configuration.CreateSnapshotAsync(CancellationToken.None)); }
    private async Task ExportProfileAsync() { var result = await _configuration.ExportProfileAsync(ProfileName, ProfileDirectory, CancellationToken.None); Status = ResultText(result); if (result.Succeeded) ProfilePath = result.Path ?? string.Empty; }
    private async Task RefreshDatabaseAsync() { var health = await _database.CheckHealthAsync(CancellationToken.None); DatabaseState = health.State.ToString(); DatabaseDetail = health.Detail; BackupLocation = health.BackupLocation; MigrationStatus = $"Schema {health.SchemaVersion}/{health.SupportedSchemaVersion}; journal {health.JournalMode}. {health.LastMigration}"; }
    private async Task BackupDatabaseAsync() { var result = await _database.CreateBackupAsync(CancellationToken.None); Status = result.Succeeded ? $"{result.Message} Location: {result.Path}" : $"{result.Message} Reference: {result.FailureCode}"; if (result.Succeeded) RestorePath = result.Path ?? string.Empty; await RefreshDatabaseAsync(); }
    private async Task RestoreDatabaseAsync() { var result = await _database.RestoreAsync(RestorePath, RestoreApproved, CancellationToken.None); Status = result.Succeeded ? result.Message : $"{result.Message} Reference: {result.FailureCode}"; RestoreApproved = false; await RefreshDatabaseAsync(); }
    private static string ResultText(ConfigurationResult result) => result.Succeeded ? result.Path is null ? result.Message : $"{result.Message} Location: {result.Path}" : $"{result.Message} Reference: {result.FailureCode}";

    private void ApplySearch()
    {
        static Visibility Match(string query, string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        var query = SearchText.Trim(); GeneralVisibility = Match(query, "general startup theme timeout scan local AI"); PrivacyVisibility = Match(query, "privacy telemetry diagnostics");
        ExperimentalVisibility = Match(query, "experimental feature flags event correlation repair cloud AI warning"); ProfilesVisibility = Match(query, "profiles import export reset policy lock"); DatabaseVisibility = Match(query, "database health backup restore migration recovery");
    }
}
