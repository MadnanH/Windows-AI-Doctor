namespace WAID.Domain.Settings;

public sealed record ApplicationSettings
{
    public const int CurrentVersion = 2;
    public int Version { get; init; } = CurrentVersion;
    public bool RunScansAtStartup { get; init; }
    public bool EnableAiAnalysis { get; init; }
    public bool AllowTelemetry { get; init; }
    public string AiProvider { get; init; } = "None";
    public string Theme { get; init; } = "System";
    public int ScanTimeoutSeconds { get; init; } = 120;
    public bool EnableExperimentalFeatures { get; init; }

    public ApplicationSettings Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidOperationException($"Settings version {Version} is unsupported.");
        if (ScanTimeoutSeconds is < 10 or > 3600)
            throw new InvalidOperationException("Scan timeout must be between 10 and 3600 seconds.");
        if (string.IsNullOrWhiteSpace(AiProvider))
            throw new InvalidOperationException("AI provider is required.");
        if (Theme is not ("System" or "Light" or "Dark"))
            throw new InvalidOperationException("Theme must be System, Light, or Dark.");
        return this;
    }
}
