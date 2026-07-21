using WAID.Domain.Settings;

namespace WAID.Application.Abstractions;

public sealed record ConfigurationResult(bool Succeeded, string? FailureCode, string Message, string? Path = null)
{
    public static ConfigurationResult Success(string message, string? path = null) => new(true, null, message, path);
    public static ConfigurationResult Failure(string code, string message) => new(false, code, message);
}

public interface IConfigurationStateRepository
{
    Task<ConfigurationState> GetAsync(CancellationToken token);
    Task SaveAsync(ConfigurationState state, CancellationToken token);
}

public interface IConfigurationLayerSource
{
    Task<ConfigurationLayer?> ReadMachineAsync(CancellationToken token);
    Task<ConfigurationLayer?> ReadPolicyAsync(CancellationToken token);
}

public interface IConfigurationService
{
    Task<ConfigurationSnapshot> CreateSnapshotAsync(CancellationToken token);
    Task<ConfigurationResult> SaveUserAsync(ApplicationSettings settings, IReadOnlyDictionary<string, bool> flags, CancellationToken token);
    Task<ConfigurationResult> SetSessionAsync(ConfigurationValues values, IReadOnlyDictionary<string, bool> flags, CancellationToken token);
    Task<ConfigurationResult> ResetUserAsync(CancellationToken token);
    Task<ConfigurationResult> ImportProfileAsync(string path, bool experimentalChangesAcknowledged, CancellationToken token);
    Task<ConfigurationResult> ExportProfileAsync(string name, string directory, CancellationToken token);
}
