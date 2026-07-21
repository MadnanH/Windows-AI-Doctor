using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Domain.Settings;
using WAID.Infrastructure.Configuration;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"waid-config-{Guid.NewGuid():N}");
    public ConfigurationServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task Precedence_is_default_machine_user_profile_session_policy()
    {
        var state = State(User(new(Theme: "Dark", ScanTimeoutSeconds: 180)), Profile(new(Theme: "Light", ScanTimeoutSeconds: 240)));
        var source = new Source(Machine(new(Theme: "Dark", ScanTimeoutSeconds: 150)), Policy(new(ScanTimeoutSeconds: 600), [SettingKeys.ScanTimeoutSeconds]));
        var service = Create(state, source);
        await service.SetSessionAsync(new(Theme: "System", ScanTimeoutSeconds: 300), EmptyFlags(), CancellationToken.None);

        var snapshot = await service.CreateSnapshotAsync(CancellationToken.None);

        Assert.Equal("System", snapshot.Settings.Theme);
        Assert.Equal(ConfigurationScope.Session, snapshot.Sources[SettingKeys.Theme]);
        Assert.Equal(600, snapshot.Settings.ScanTimeoutSeconds);
        Assert.Equal(ConfigurationScope.Policy, snapshot.Sources[SettingKeys.ScanTimeoutSeconds]);
        Assert.Contains(SettingKeys.ScanTimeoutSeconds, snapshot.LockedSettings);
    }

    [Fact]
    public async Task Policy_lock_cannot_be_bypassed_by_user_save()
    {
        var repository = new StateRepository(State(User(new(AllowTelemetry: true))));
        var service = Create(repository, new Source(null, Policy(new(AllowTelemetry: false), [SettingKeys.AllowTelemetry])));
        var saved = await service.SaveUserAsync(new ApplicationSettings { AllowTelemetry = true }, EmptyFlags(), CancellationToken.None);

        var snapshot = await service.CreateSnapshotAsync(CancellationToken.None);

        Assert.True(saved.Succeeded);
        Assert.False(snapshot.Settings.AllowTelemetry);
        Assert.Equal(ConfigurationScope.Policy, snapshot.Sources[SettingKeys.AllowTelemetry]);
        Assert.Contains(SettingKeys.AllowTelemetry, snapshot.LockedSettings);
    }

    [Fact]
    public async Task Experimental_flag_is_forced_off_without_explicit_global_opt_in()
    {
        var service = Create(State(User(new(), new Dictionary<string, bool> { [FeatureFlags.ExperimentalRepairPlanning] = true })), new Source(null, null));
        var snapshot = await service.CreateSnapshotAsync(CancellationToken.None);
        Assert.False(snapshot.IsEnabled(FeatureFlags.ExperimentalRepairPlanning));
        Assert.Equal(ConfigurationScope.SafetyDefault, snapshot.Flags[FeatureFlags.ExperimentalRepairPlanning].Source);
    }

    [Fact]
    public async Task Known_feature_flag_can_be_enabled_with_explicit_opt_in()
    {
        var service = Create(State(User(new(EnableExperimentalFeatures: true), new Dictionary<string, bool> { [FeatureFlags.ExperimentalRepairPlanning] = true })), new Source(null, null));
        Assert.True((await service.CreateSnapshotAsync(CancellationToken.None)).IsEnabled(FeatureFlags.ExperimentalRepairPlanning));
    }

    [Fact]
    public async Task Policy_feature_flag_overrides_user_and_profile_flags()
    {
        var enabled = new Dictionary<string, bool> { [FeatureFlags.ExperimentalRepairPlanning] = true };
        var policyFlags = new Dictionary<string, bool> { [FeatureFlags.ExperimentalRepairPlanning] = false };
        var state = State(User(new(EnableExperimentalFeatures: true), enabled), Profile(new(), enabled));
        var policy = new ConfigurationLayer(ConfigurationScope.Policy, "policy", new(), policyFlags, Array.Empty<string>());
        var snapshot = await Create(state, new Source(null, policy)).CreateSnapshotAsync(CancellationToken.None);
        Assert.False(snapshot.IsEnabled(FeatureFlags.ExperimentalRepairPlanning));
        Assert.Equal(ConfigurationScope.Policy, snapshot.Flags[FeatureFlags.ExperimentalRepairPlanning].Source);
    }

    [Fact]
    public async Task Invalid_policy_never_produces_an_unsafe_snapshot()
    {
        var invalid = new ConfigurationLayer(ConfigurationScope.Policy, "policy", new(), new Dictionary<string, bool> { ["unknown-unsafe"] = true }, Array.Empty<string>());
        var service = Create(State(User(new())), new Source(null, invalid));
        var failure = await Assert.ThrowsAsync<WaidConfigurationException>(() => service.CreateSnapshotAsync(CancellationToken.None));
        Assert.Equal("WAID-CONFIG-INVALID", failure.Code);
    }

    [Fact]
    public async Task Invalid_profile_is_rejected_without_changing_active_state()
    {
        var repository = new StateRepository(State(User(new(Theme: "Dark")))); var service = Create(repository, new Source(null, null));
        var path = Path.Combine(_root, "invalid.waid-profile.json"); await File.WriteAllTextAsync(path, "{\"version\":1,\"name\":\"Bad\",\"unknown\":true}");

        var result = await service.ImportProfileAsync(path, false, CancellationToken.None);

        Assert.False(result.Succeeded); Assert.Null(repository.Value.ActiveProfile); Assert.Equal("Dark", (await service.CreateSnapshotAsync(CancellationToken.None)).Settings.Theme);
    }

    [Fact]
    public async Task Experimental_profile_requires_acknowledgement_and_then_imports()
    {
        var repository = new StateRepository(State(User(new()))); var service = Create(repository, new Source(null, null));
        var path = Path.Combine(_root, "experimental.waid-profile.json");
        var layer = Profile(new(EnableExperimentalFeatures: true), new Dictionary<string, bool> { [FeatureFlags.ExperimentalRepairPlanning] = true });
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { version = 1, name = "Lab", layer }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal("WAID-PROFILE-EXPERIMENTAL", (await service.ImportProfileAsync(path, false, CancellationToken.None)).FailureCode);
        Assert.True((await service.ImportProfileAsync(path, true, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateSnapshotAsync(CancellationToken.None)).IsEnabled(FeatureFlags.ExperimentalRepairPlanning));
    }

    [Fact]
    public async Task Exported_profile_is_versioned_privacy_safe_and_importable()
    {
        var flags = new Dictionary<string, bool> { [FeatureFlags.AdvancedEventCorrelation] = true };
        var service = Create(State(User(new(Theme: "Dark"), flags)), new Source(null, null));
        var export = await service.ExportProfileAsync("Portable", string.Empty, CancellationToken.None);
        Assert.True(export.Succeeded); Assert.True(File.Exists(export.Path));
        var json = await File.ReadAllTextAsync(export.Path!);
        Assert.Contains("\"version\": 1", json); Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        var imported = Create(State(User(new())), new Source(null, null));
        Assert.True((await imported.ImportProfileAsync(export.Path!, false, CancellationToken.None)).Succeeded);
        Assert.Equal("Dark", (await imported.CreateSnapshotAsync(CancellationToken.None)).Settings.Theme);
    }

    [Fact]
    public async Task Operation_snapshot_is_immutable_across_later_session_changes()
    {
        var service = Create(State(User(new(Theme: "Dark"))), new Source(null, null));
        var first = await service.CreateSnapshotAsync(CancellationToken.None);
        await service.SetSessionAsync(new(Theme: "Light"), EmptyFlags(), CancellationToken.None);
        var second = await service.CreateSnapshotAsync(CancellationToken.None);
        Assert.Equal("Dark", first.Settings.Theme); Assert.Equal("Light", second.Settings.Theme);
    }

    [Fact]
    public async Task Schema_seven_legacy_settings_migrate_to_versioned_configuration_state()
    {
        var path = Path.Combine(_root, "legacy.db"); var database = new WaidDatabase($"Data Source={path};Pooling=False");
        await using (var connection = database.OpenConnection())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = LegacySevenSchema() + " INSERT INTO settings VALUES(1,'{\"theme\":\"Dark\",\"scanTimeoutSeconds\":300}','2026-01-01T00:00:00Z'); PRAGMA user_version=7;";
            await command.ExecuteNonQueryAsync();
        }
        await database.InitializeAsync(CancellationToken.None);
        var state = await new SqliteConfigurationStateRepository(database).GetAsync(CancellationToken.None);
        Assert.Equal(ConfigurationState.CurrentVersion, state.Version); Assert.Equal("Dark", state.User.Values.Theme); Assert.Equal(300, state.User.Values.ScanTimeoutSeconds);
        await using var verified = database.OpenConnection(); await using var versionCommand = verified.CreateCommand(); versionCommand.CommandText = "SELECT version FROM configuration_state WHERE id=1;"; Assert.Equal(2L, await versionCommand.ExecuteScalarAsync());
    }

    private ConfigurationService Create(ConfigurationState state, IConfigurationLayerSource source) => Create(new StateRepository(state), source);
    private ConfigurationService Create(IConfigurationStateRepository repository, IConfigurationLayerSource source) => new(repository, source, Path.Combine(_root, "Profiles"), TimeProvider.System, NullLogger<ConfigurationService>.Instance, new Audit());
    private static ConfigurationState State(ConfigurationLayer user, ConfigurationLayer? profile = null) => new(ConfigurationState.CurrentVersion, user, profile, DateTimeOffset.UtcNow);
    private static ConfigurationLayer User(ConfigurationValues values, IReadOnlyDictionary<string, bool>? flags = null) => new(ConfigurationScope.User, "user", values, flags ?? EmptyFlags(), Array.Empty<string>());
    private static ConfigurationLayer Profile(ConfigurationValues values, IReadOnlyDictionary<string, bool>? flags = null) => new(ConfigurationScope.Profile, "profile", values, flags ?? EmptyFlags(), Array.Empty<string>());
    private static ConfigurationLayer Machine(ConfigurationValues values) => new(ConfigurationScope.Machine, "machine", values, EmptyFlags(), Array.Empty<string>());
    private static ConfigurationLayer Policy(ConfigurationValues values, IReadOnlyList<string> locks) => new(ConfigurationScope.Policy, "policy", values, EmptyFlags(), locks);
    private static IReadOnlyDictionary<string, bool> EmptyFlags() => new Dictionary<string, bool>();

    private sealed class StateRepository(ConfigurationState value) : IConfigurationStateRepository
    { public ConfigurationState Value { get; private set; } = value; public Task<ConfigurationState> GetAsync(CancellationToken token) => Task.FromResult(Value); public Task SaveAsync(ConfigurationState state, CancellationToken token) { Value = state; return Task.CompletedTask; } }
    private sealed class Source(ConfigurationLayer? machine, ConfigurationLayer? policy) : IConfigurationLayerSource
    { public Task<ConfigurationLayer?> ReadMachineAsync(CancellationToken token) => Task.FromResult(machine); public Task<ConfigurationLayer?> ReadPolicyAsync(CancellationToken token) => Task.FromResult(policy); }
    private sealed class Audit : IAuditTrailService
    { public Task<AuditWriteResult> AppendAsync(AuditRecord record, CancellationToken token) => Task.FromResult(new AuditWriteResult(true, record.Id)); public Task<IReadOnlyList<AuditRecord>> SearchAsync(AuditQuery query, CancellationToken token) => Task.FromResult<IReadOnlyList<AuditRecord>>(Array.Empty<AuditRecord>()); public Task ApplyRetentionAsync(CancellationToken token) => Task.CompletedTask; }

    private static string LegacySevenSchema() => """
        CREATE TABLE scan_sessions(id TEXT PRIMARY KEY,started_utc TEXT NOT NULL,completed_utc TEXT NOT NULL); CREATE TABLE findings(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,scanner_id TEXT NOT NULL,code TEXT NOT NULL,title TEXT NOT NULL,description TEXT NOT NULL,severity INTEGER NOT NULL,repair_id TEXT NULL,evidence_json TEXT NOT NULL); CREATE TABLE settings(id INTEGER PRIMARY KEY,json TEXT NOT NULL,updated_utc TEXT NOT NULL); CREATE TABLE repair_history(transaction_id TEXT PRIMARY KEY,repair_id TEXT NOT NULL,status INTEGER NOT NULL,created_utc TEXT NOT NULL,completed_utc TEXT NULL,summary TEXT NULL,details TEXT NULL,backup_location TEXT NULL,restore_point_description TEXT NULL,events_json TEXT NOT NULL); CREATE TABLE diagnosis_reports(id TEXT PRIMARY KEY,scan_session_id TEXT NOT NULL,generated_utc TEXT NOT NULL,report_json TEXT NOT NULL); CREATE TABLE health_snapshots(id TEXT PRIMARY KEY,captured_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL); CREATE TABLE scan_schedule(id INTEGER PRIMARY KEY,schedule_json TEXT NOT NULL); CREATE TABLE repair_approvals(id TEXT PRIMARY KEY,requested_utc TEXT NOT NULL,approval_json TEXT NOT NULL); CREATE TABLE evidence(id TEXT PRIMARY KEY,scan_session_id TEXT NULL,source TEXT NOT NULL,captured_utc TEXT NOT NULL,evidence_json TEXT NOT NULL); CREATE TABLE rollback_records(id TEXT PRIMARY KEY,repair_transaction_id TEXT NOT NULL,created_utc TEXT NOT NULL,record_json TEXT NOT NULL); CREATE TABLE timeline_events(id TEXT PRIMARY KEY,occurred_utc TEXT NOT NULL,category TEXT NOT NULL,event_json TEXT NOT NULL); CREATE TABLE metrics(id TEXT PRIMARY KEY,captured_utc TEXT NOT NULL,metric_name TEXT NOT NULL,value REAL NOT NULL,unit TEXT NOT NULL,tags_json TEXT NOT NULL); CREATE TABLE chats(id TEXT PRIMARY KEY,created_utc TEXT NOT NULL,updated_utc TEXT NOT NULL,conversation_json TEXT NOT NULL); CREATE TABLE policies(id TEXT PRIMARY KEY,updated_utc TEXT NOT NULL,policy_json TEXT NOT NULL); CREATE TABLE plugins(id TEXT PRIMARY KEY,updated_utc TEXT NOT NULL,state_json TEXT NOT NULL); CREATE TABLE alerts(id TEXT PRIMARY KEY,created_utc TEXT NOT NULL,resolved_utc TEXT NULL,alert_json TEXT NOT NULL); CREATE TABLE reports(id TEXT PRIMARY KEY,created_utc TEXT NOT NULL,format TEXT NOT NULL,location TEXT NOT NULL,metadata_json TEXT NOT NULL); CREATE TABLE audit_events(id TEXT PRIMARY KEY,occurred_utc TEXT NOT NULL,actor TEXT NOT NULL,action TEXT NOT NULL,target TEXT NOT NULL,result TEXT NOT NULL,event_json TEXT NOT NULL); CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY,applied_utc TEXT NOT NULL,description TEXT NOT NULL);
        """;
}
