using System.Windows.Input;
using WAID.Application.Abstractions;
using WAID.Domain.Settings;

namespace WAID.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsRepository _repository;
    private readonly IDatabaseMaintenanceService _database;
    private bool _startup, _ai, _telemetry, _restoreApproved;
    private string _theme = "System", _status = "", _databaseState = "Checking…", _databaseDetail = "", _backupLocation = "", _migrationStatus = "", _restorePath = "";

    public SettingsViewModel(ISettingsRepository repository, IDatabaseMaintenanceService database)
    {
        _repository = repository; _database = database;
        SaveCommand = new AsyncCommand(SaveAsync);
        RefreshDatabaseCommand = new AsyncCommand(RefreshDatabaseAsync);
        BackupDatabaseCommand = new AsyncCommand(BackupDatabaseAsync);
        RestoreDatabaseCommand = new AsyncCommand(RestoreDatabaseAsync);
        _ = LoadAsync();
    }

    public bool RunScansAtStartup { get => _startup; set => Set(ref _startup, value); }
    public bool EnableAiAnalysis { get => _ai; set => Set(ref _ai, value); }
    public bool AllowTelemetry { get => _telemetry; set => Set(ref _telemetry, value); }
    public bool RestoreApproved { get => _restoreApproved; set => Set(ref _restoreApproved, value); }
    public string Theme { get => _theme; set => Set(ref _theme, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string DatabaseState { get => _databaseState; private set => Set(ref _databaseState, value); }
    public string DatabaseDetail { get => _databaseDetail; private set => Set(ref _databaseDetail, value); }
    public string BackupLocation { get => _backupLocation; private set => Set(ref _backupLocation, value); }
    public string MigrationStatus { get => _migrationStatus; private set => Set(ref _migrationStatus, value); }
    public string RestorePath { get => _restorePath; set => Set(ref _restorePath, value); }
    public ICommand SaveCommand { get; }
    public ICommand RefreshDatabaseCommand { get; }
    public ICommand BackupDatabaseCommand { get; }
    public ICommand RestoreDatabaseCommand { get; }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await _repository.GetAsync(CancellationToken.None);
            RunScansAtStartup = settings.RunScansAtStartup; EnableAiAnalysis = settings.EnableAiAnalysis; AllowTelemetry = settings.AllowTelemetry; Theme = settings.Theme;
        }
        catch (Exception exception) { Status = $"Could not load settings ({exception.GetType().Name}). Try again or open Logs & Audit."; }
        await RefreshDatabaseAsync();
    }

    private async Task SaveAsync()
    {
        try { await _repository.SaveAsync(new ApplicationSettings { RunScansAtStartup = RunScansAtStartup, EnableAiAnalysis = EnableAiAnalysis, AllowTelemetry = AllowTelemetry, Theme = Theme }, CancellationToken.None); Status = "Settings saved."; }
        catch (Exception exception) { Status = $"Could not save settings ({exception.GetType().Name}). Check database health."; }
    }

    private async Task RefreshDatabaseAsync()
    {
        var health = await _database.CheckHealthAsync(CancellationToken.None);
        DatabaseState = health.State.ToString(); DatabaseDetail = health.Detail; BackupLocation = health.BackupLocation;
        MigrationStatus = $"Schema {health.SchemaVersion}/{health.SupportedSchemaVersion}; journal {health.JournalMode}. {health.LastMigration}";
    }

    private async Task BackupDatabaseAsync()
    {
        var result = await _database.CreateBackupAsync(CancellationToken.None);
        Status = result.Succeeded ? $"{result.Message} Location: {result.Path}" : $"{result.Message} Reference: {result.FailureCode}";
        if (result.Succeeded) RestorePath = result.Path ?? string.Empty;
        await RefreshDatabaseAsync();
    }

    private async Task RestoreDatabaseAsync()
    {
        var result = await _database.RestoreAsync(RestorePath, RestoreApproved, CancellationToken.None);
        Status = result.Succeeded ? result.Message : $"{result.Message} Reference: {result.FailureCode}";
        RestoreApproved = false;
        await RefreshDatabaseAsync();
    }
}
