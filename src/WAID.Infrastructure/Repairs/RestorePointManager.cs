using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Repairs;

public sealed class RestorePointManager(
    IPowerShellRunner powerShell,
    ILogger<RestorePointManager> logger) : IRestorePointManager
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var result = await powerShell.RunAsync(
            "[bool](Get-Command -Name Checkpoint-Computer -ErrorAction SilentlyContinue)",
            new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
        var available = result.Succeeded && result.Output.Any(value => bool.TryParse(value, out var parsed) && parsed);
        logger.LogInformation("System Restore Point availability: {Available}", available);
        return available;
    }

    public async Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        logger.LogInformation("Creating System Restore Point: {Description}", description);
        var result = await powerShell.RunAsync(
            "param($Description) Checkpoint-Computer -Description $Description -RestorePointType MODIFY_SETTINGS -ErrorAction Stop",
            new Dictionary<string, object?> { ["Description"] = description }, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? new(true, description)
            : new(false, description, string.Join(Environment.NewLine, result.Errors));
    }
}
