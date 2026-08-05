using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Repairs;
using WAID.Domain.Settings;

namespace WAID.Infrastructure.Configuration;

public sealed class LocalConfigurationLayerSource(string machinePath, string policyPath) : IConfigurationLayerSource
{
    private static readonly JsonSerializerOptions Options = CreateOptions(false);
    public Task<ConfigurationLayer?> ReadMachineAsync(CancellationToken token) => ReadAsync(machinePath, ConfigurationScope.Machine, token);
    public Task<ConfigurationLayer?> ReadPolicyAsync(CancellationToken token) => ReadAsync(policyPath, ConfigurationScope.Policy, token);

    private static async Task<ConfigurationLayer?> ReadAsync(string path, ConfigurationScope expected, CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path); if (info.Length > 1024 * 1024) throw new InvalidDataException("Configuration file exceeds the one-megabyte limit.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<LayerDocument>(stream, Options, token).ConfigureAwait(false) ?? throw new InvalidDataException("Configuration file is empty.");
        if (document.Version != 1 || document.Layer.Scope != expected) throw new InvalidDataException($"{expected} configuration version or scope is invalid.");
        return document.Layer.Validate();
    }

    private sealed record LayerDocument(int Version, ConfigurationLayer Layer);
    private static JsonSerializerOptions CreateOptions(bool writeIndented) { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = writeIndented }; options.Converters.Add(new JsonStringEnumConverter()); return options; }
}

public sealed class ConfigurationService(
    IConfigurationStateRepository repository,
    IConfigurationLayerSource sources,
    string profileDirectory,
    TimeProvider timeProvider,
    ILogger<ConfigurationService> logger,
    IAuditTrailService auditTrail,
    IEnterprisePolicyService? enterprisePolicy=null) : IConfigurationService
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConfigurationLayer? _session;

    public async Task<ConfigurationSnapshot> CreateSnapshotAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await repository.GetAsync(token).ConfigureAwait(false);
            var layers = new List<ConfigurationLayer> { DefaultLayer() };
            var machine = await sources.ReadMachineAsync(token).ConfigureAwait(false); if (machine is not null) layers.Add(machine.Validate());
            layers.Add(state.User.Validate()); if (state.ActiveProfile is not null) layers.Add(state.ActiveProfile.Validate());
            if (_session is not null) layers.Add(_session.Validate());
            var policy = await sources.ReadPolicyAsync(token).ConfigureAwait(false); if (policy is not null) layers.Add(policy.Validate());
            return Resolve(layers, state.ActiveProfile?.Source, timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogError("Configuration snapshot failed safely with {FailureType}", exception.GetType().Name);
            throw new WaidConfigurationException("WAID-CONFIG-INVALID", "Configuration could not be validated. Unsafe features remain disabled.", "Correct or remove the invalid machine, profile, or policy configuration.", exception);
        }
        finally { _gate.Release(); }
    }

    public async Task<ConfigurationResult> SaveUserAsync(ApplicationSettings settings, IReadOnlyDictionary<string, bool> flags, CancellationToken token)
    {
        try { settings.Validate(); ValidateFlags(flags); }
        catch (InvalidOperationException) { return ConfigurationResult.Failure("WAID-CONFIG-USER", "Settings contain an invalid value and were not saved."); }
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await repository.GetAsync(token).ConfigureAwait(false);
            var layer = new ConfigurationLayer(ConfigurationScope.User, "Local user", ConfigurationValues.From(settings), Copy(flags), EmptyLocks()).Validate();
            await repository.SaveAsync(state with { Version = ConfigurationState.CurrentVersion, User = layer, UpdatedAtUtc = timeProvider.GetUtcNow() }, token).ConfigureAwait(false);
            await AuditAsync("ConfigurationSave", "Local user settings", AuditResult.Succeeded, token).ConfigureAwait(false);
            return ConfigurationResult.Success("User settings saved. Policy-controlled values remain enforced.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        { logger.LogWarning("Saving user configuration failed with {FailureType}", exception.GetType().Name); return ConfigurationResult.Failure("WAID-CONFIG-SAVE", "User settings could not be saved. Existing settings remain active."); }
        finally { _gate.Release(); }
    }

    public async Task<ConfigurationResult> SetSessionAsync(ConfigurationValues values, IReadOnlyDictionary<string, bool> flags, CancellationToken token)
    {
        try { values.Validate(); ValidateFlags(flags); }
        catch (InvalidOperationException) { return ConfigurationResult.Failure("WAID-CONFIG-SESSION", "Session settings are invalid and were not applied."); }
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { _session = new(ConfigurationScope.Session, "Current application session", values, Copy(flags), EmptyLocks()); await AuditAsync("SessionConfiguration", "Current session", AuditResult.Succeeded, token).ConfigureAwait(false); return ConfigurationResult.Success("Session settings applied until WAID closes."); }
        finally { _gate.Release(); }
    }

    public async Task<ConfigurationResult> ResetUserAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var reset = new ConfigurationState(ConfigurationState.CurrentVersion, new(ConfigurationScope.User, "Local user", new(), EmptyFlags(), EmptyLocks()), null, timeProvider.GetUtcNow());
            await repository.SaveAsync(reset, token).ConfigureAwait(false); _session = null;
            await AuditAsync("ConfigurationReset", "User settings and profile", AuditResult.Succeeded, token).ConfigureAwait(false);
            return ConfigurationResult.Success("User settings, active profile, and session overrides were reset. Machine policy was not changed.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        { logger.LogWarning("Configuration reset failed with {FailureType}", exception.GetType().Name); return ConfigurationResult.Failure("WAID-CONFIG-RESET", "Settings could not be reset. Existing configuration remains active."); }
        finally { _gate.Release(); }
    }

    public async Task<ConfigurationResult> ImportProfileAsync(string path, bool experimentalChangesAcknowledged, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path)) return ConfigurationResult.Failure("WAID-PROFILE-PATH", "Select an existing WAID profile file.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var info = new FileInfo(path); if (info.Length > 1024 * 1024) return ConfigurationResult.Failure("WAID-PROFILE-SIZE", "Profile files cannot exceed one megabyte.");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(stream, Options, token).ConfigureAwait(false);
            if (document is null || document.Version != 1 || string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 80 || document.Layer.Scope != ConfigurationScope.Profile)
                return ConfigurationResult.Failure("WAID-PROFILE-INVALID", "The profile version, name, or scope is invalid.");
            document.Layer.Validate();
            var enablesExperimental = document.Layer.Values.EnableExperimentalFeatures == true || document.Layer.Flags.Any(item => item.Value && FeatureFlags.Catalog[item.Key].Experimental);
            if (enablesExperimental && !experimentalChangesAcknowledged) return ConfigurationResult.Failure("WAID-PROFILE-EXPERIMENTAL", "Acknowledge the experimental warning before importing this profile.");
            var state = await repository.GetAsync(token).ConfigureAwait(false);
            var profile = document.Layer with { Source = document.Name };
            await repository.SaveAsync(state with { ActiveProfile = profile, UpdatedAtUtc = timeProvider.GetUtcNow() }, token).ConfigureAwait(false);
            await AuditAsync("ProfileImport", document.Name, AuditResult.Succeeded, token).ConfigureAwait(false);
            return ConfigurationResult.Success($"Profile '{document.Name}' imported. Policy values still take precedence.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { logger.LogWarning("Profile import rejected with {FailureType}", exception.GetType().Name); return ConfigurationResult.Failure("WAID-PROFILE-INVALID", "The profile is invalid or cannot be read. No settings were changed."); }
        finally { _gate.Release(); }
    }

    public async Task<ConfigurationResult> ExportProfileAsync(string name, string directory, CancellationToken token)
    {
        var exportDecision=enterprisePolicy?.Evaluate(EnterpriseCapability.Exports);
        if(exportDecision is {Allowed:false})return ConfigurationResult.Failure("WAID-POLICY-EXPORT-BLOCKED",$"Profile export is blocked by {exportDecision.Source}.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return ConfigurationResult.Failure("WAID-PROFILE-NAME", "Enter a valid profile name of 80 characters or fewer.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await repository.GetAsync(token).ConfigureAwait(false); Directory.CreateDirectory(directory.Length == 0 ? profileDirectory : directory);
            var root = Path.GetFullPath(directory.Length == 0 ? profileDirectory : directory); var path = Path.Combine(root, $"{name}.waid-profile.json");
            var layer = new ConfigurationLayer(ConfigurationScope.Profile, name, state.User.Values, Copy(state.User.Flags), EmptyLocks());
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ProfileDocument(1, name, layer), Options), token).ConfigureAwait(false);
            await AuditAsync("ProfileExport", name, AuditResult.Succeeded, token).ConfigureAwait(false);
            return ConfigurationResult.Success("Profile exported. It contains settings and feature choices, but no secrets or system evidence.", path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { logger.LogWarning("Profile export failed with {FailureType}", exception.GetType().Name); return ConfigurationResult.Failure("WAID-PROFILE-EXPORT", "The profile could not be exported. Check the destination folder."); }
        finally { _gate.Release(); }
    }

    private static ConfigurationSnapshot Resolve(IReadOnlyList<ConfigurationLayer> layers, string? profile, DateTimeOffset now)
    {
        var settings = new ApplicationSettings(); var settingSources = SettingKeys.All.ToDictionary(key => key, _ => ConfigurationScope.Default, StringComparer.OrdinalIgnoreCase);
        var flags = FeatureFlags.Catalog.Values.ToDictionary(item => item.Key, item => new EffectiveFeatureFlag(item.Key, item.DefaultEnabled, ConfigurationScope.Default, item.Experimental, item.Warning), StringComparer.OrdinalIgnoreCase);
        var locks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers)
        {
            settings = Apply(settings, layer.Values, settingSources, layer.Scope);
            foreach (var flag in layer.Flags) { var definition = FeatureFlags.Catalog[flag.Key]; flags[flag.Key] = new(flag.Key, flag.Value, layer.Scope, definition.Experimental, definition.Warning); }
            if (layer.Scope == ConfigurationScope.Policy) locks.UnionWith(layer.LockedSettings);
        }
        settings.Validate();
        if (!settings.EnableExperimentalFeatures)
            foreach (var key in flags.Where(item => item.Value.Experimental && item.Value.Enabled).Select(item => item.Key).ToArray()) flags[key] = flags[key] with { Enabled = false, Source = ConfigurationScope.SafetyDefault };
        return new(settings, settingSources.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), locks.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            flags.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), profile, now);
    }

    private static ApplicationSettings Apply(ApplicationSettings current, ConfigurationValues values, IDictionary<string, ConfigurationScope> sources, ConfigurationScope scope)
    {
        if (values.RunScansAtStartup is { } startup) { current = current with { RunScansAtStartup = startup }; sources[SettingKeys.RunScansAtStartup] = scope; }
        if (values.EnableAiAnalysis is { } ai) { current = current with { EnableAiAnalysis = ai }; sources[SettingKeys.EnableAiAnalysis] = scope; }
        if (values.AllowTelemetry is { } telemetry) { current = current with { AllowTelemetry = telemetry }; sources[SettingKeys.AllowTelemetry] = scope; }
        if (values.AiProvider is { } provider) { current = current with { AiProvider = provider }; sources[SettingKeys.AiProvider] = scope; }
        if (values.Theme is { } theme) { current = current with { Theme = theme }; sources[SettingKeys.Theme] = scope; }
        if (values.ScanTimeoutSeconds is { } timeout) { current = current with { ScanTimeoutSeconds = timeout }; sources[SettingKeys.ScanTimeoutSeconds] = scope; }
        if (values.EnableExperimentalFeatures is { } experimental) { current = current with { EnableExperimentalFeatures = experimental }; sources[SettingKeys.EnableExperimentalFeatures] = scope; }
        return current;
    }

    private static ConfigurationLayer DefaultLayer() => new(ConfigurationScope.Default, "Built-in safe defaults", ConfigurationValues.From(new ApplicationSettings()), EmptyFlags(), EmptyLocks());
    private static void ValidateFlags(IReadOnlyDictionary<string, bool> flags) { if (flags.Keys.Any(key => !FeatureFlags.Catalog.ContainsKey(key))) throw new InvalidOperationException("Unknown feature flag."); }
    private static IReadOnlyDictionary<string, bool> Copy(IReadOnlyDictionary<string, bool> values) => new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(values, StringComparer.OrdinalIgnoreCase));
    private static IReadOnlyDictionary<string, bool> EmptyFlags() => new Dictionary<string, bool>();
    private static IReadOnlyList<string> EmptyLocks() => Array.Empty<string>();
    private async Task AuditAsync(string action, string target, AuditResult result, CancellationToken token)
    {
        var write = await auditTrail.AppendAsync(new(Guid.NewGuid(), timeProvider.GetUtcNow(), AuditActor.User, action, target, result, SafetyLevel.Low, false, false, Guid.NewGuid(), Guid.NewGuid(), "Configuration state changed."), token).ConfigureAwait(false);
        if (!write.Succeeded) logger.LogWarning("Configuration audit could not be stored: {FailureCode}", write.FailureCode);
    }
    private sealed record ProfileDocument(int Version, string Name, ConfigurationLayer Layer);
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }; options.Converters.Add(new JsonStringEnumConverter()); return options; }
}

public sealed class WaidConfigurationException(string code, string userMessage, string recoveryAction, Exception? inner = null) : InvalidOperationException(userMessage, inner)
{
    public string Code { get; } = code; public string RecoveryAction { get; } = recoveryAction;
}
