using System.Collections.Frozen;

namespace WAID.Domain.Settings;

public enum ConfigurationScope { Default, Machine, User, Profile, Session, Policy, SafetyDefault }

public static class SettingKeys
{
    public const string RunScansAtStartup = nameof(ApplicationSettings.RunScansAtStartup);
    public const string EnableAiAnalysis = nameof(ApplicationSettings.EnableAiAnalysis);
    public const string AllowTelemetry = nameof(ApplicationSettings.AllowTelemetry);
    public const string AiProvider = nameof(ApplicationSettings.AiProvider);
    public const string Theme = nameof(ApplicationSettings.Theme);
    public const string ScanTimeoutSeconds = nameof(ApplicationSettings.ScanTimeoutSeconds);
    public const string EnableExperimentalFeatures = nameof(ApplicationSettings.EnableExperimentalFeatures);
    public static readonly IReadOnlySet<string> All = new[]
        { RunScansAtStartup, EnableAiAnalysis, AllowTelemetry, AiProvider, Theme, ScanTimeoutSeconds, EnableExperimentalFeatures }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}

public static class FeatureFlags
{
    public const string AdvancedEventCorrelation = "advanced-event-correlation";
    public const string ExperimentalRepairPlanning = "experimental-repair-planning";
    public const string CloudAiProvider = "cloud-ai-provider";
    public static readonly IReadOnlyDictionary<string, FeatureFlagDefinition> Catalog =
        new Dictionary<string, FeatureFlagDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [AdvancedEventCorrelation] = new(AdvancedEventCorrelation, "Advanced event correlation", false, false, "Uses additional local correlation rules."),
            [ExperimentalRepairPlanning] = new(ExperimentalRepairPlanning, "Experimental repair planning", false, true, "May change recommendation ordering; repairs still require approval."),
            [CloudAiProvider] = new(CloudAiProvider, "Cloud AI provider", false, true, "Reserved for a future privacy-reviewed provider; no network implementation exists.")
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}

public sealed record FeatureFlagDefinition(string Key, string DisplayName, bool DefaultEnabled, bool Experimental, string Warning);

public sealed record ConfigurationValues(
    bool? RunScansAtStartup = null,
    bool? EnableAiAnalysis = null,
    bool? AllowTelemetry = null,
    string? AiProvider = null,
    string? Theme = null,
    int? ScanTimeoutSeconds = null,
    bool? EnableExperimentalFeatures = null)
{
    public ConfigurationValues Validate()
    {
        if (ScanTimeoutSeconds is < 10 or > 3600) throw new InvalidOperationException("Scan timeout must be between 10 and 3600 seconds.");
        if (AiProvider is not null && string.IsNullOrWhiteSpace(AiProvider)) throw new InvalidOperationException("AI provider cannot be blank.");
        if (Theme is not null && Theme is not ("System" or "Light" or "Dark")) throw new InvalidOperationException("Theme must be System, Light, or Dark.");
        return this;
    }

    public static ConfigurationValues From(ApplicationSettings settings) => new(settings.RunScansAtStartup, settings.EnableAiAnalysis,
        settings.AllowTelemetry, settings.AiProvider, settings.Theme, settings.ScanTimeoutSeconds, settings.EnableExperimentalFeatures);
}

public sealed record ConfigurationLayer(ConfigurationScope Scope, string Source, ConfigurationValues Values,
    IReadOnlyDictionary<string, bool> Flags, IReadOnlyList<string> LockedSettings)
{
    public ConfigurationLayer Validate()
    {
        if (string.IsNullOrWhiteSpace(Source) || Source.Length > 200) throw new InvalidOperationException("Configuration source is invalid.");
        Values.Validate();
        if (Flags.Keys.Any(key => !FeatureFlags.Catalog.ContainsKey(key))) throw new InvalidOperationException("Configuration contains an unknown feature flag.");
        if (LockedSettings.Any(key => !SettingKeys.All.Contains(key))) throw new InvalidOperationException("Configuration contains an unknown policy lock.");
        if (Scope != ConfigurationScope.Policy && LockedSettings.Count > 0) throw new InvalidOperationException("Only policy configuration can lock settings.");
        foreach (var key in LockedSettings) if (!HasValue(Values, key)) throw new InvalidOperationException($"Policy lock {key} must provide an enforced value.");
        return this;
    }

    private static bool HasValue(ConfigurationValues values, string key) => key.ToUpperInvariant() switch
    {
        "RUNSCANSATSTARTUP" => values.RunScansAtStartup.HasValue,
        "ENABLEAIANALYSIS" => values.EnableAiAnalysis.HasValue,
        "ALLOWTELEMETRY" => values.AllowTelemetry.HasValue,
        "AIPROVIDER" => values.AiProvider is not null,
        "THEME" => values.Theme is not null,
        "SCANTIMEOUTSECONDS" => values.ScanTimeoutSeconds.HasValue,
        "ENABLEEXPERIMENTALFEATURES" => values.EnableExperimentalFeatures.HasValue,
        _ => false
    };
}

public sealed record ConfigurationState(int Version, ConfigurationLayer User, ConfigurationLayer? ActiveProfile, DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentVersion = 2;
    public ConfigurationState Validate()
    {
        if (Version != CurrentVersion) throw new InvalidOperationException($"Configuration state version {Version} is unsupported.");
        if (User.Scope != ConfigurationScope.User || ActiveProfile is { Scope: not ConfigurationScope.Profile }) throw new InvalidOperationException("Configuration state scopes are invalid.");
        User.Validate(); ActiveProfile?.Validate(); return this;
    }
}

public sealed record EffectiveFeatureFlag(string Key, bool Enabled, ConfigurationScope Source, bool Experimental, string Warning);
public sealed record ConfigurationSnapshot(ApplicationSettings Settings, IReadOnlyDictionary<string, ConfigurationScope> Sources,
    IReadOnlySet<string> LockedSettings, IReadOnlyDictionary<string, EffectiveFeatureFlag> Flags, string? ActiveProfile,
    DateTimeOffset CreatedAtUtc)
{
    public bool IsEnabled(string key) => Flags.TryGetValue(key, out var flag) && flag.Enabled;
}
