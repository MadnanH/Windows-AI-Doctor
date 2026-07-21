using System.Text.Json;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Repairs;

public sealed class BackupManager(
    string backupRoot,
    IPowerShellRunner powerShell,
    ILogger<BackupManager> logger) : IBackupManager
{
    public async Task<BackupSnapshot> CreateAsync(
        Guid transactionId,
        IReadOnlyCollection<RepairResource> resources,
        CancellationToken cancellationToken)
    {
        var location = Path.Combine(backupRoot, transactionId.ToString("N"));
        Directory.CreateDirectory(location);
        var items = new List<BackupItem>();
        var warnings = new List<string>();

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backupPath = Path.Combine(location, $"{items.Count:D3}-{Sanitize(Path.GetFileName(resource.Path))}");
            logger.LogInformation("Backing up {ResourceKind} {ResourcePath}", resource.Kind, resource.Path);
            switch (resource.Kind)
            {
                case RepairResourceKind.File when File.Exists(resource.Path):
                    File.Copy(resource.Path, backupPath, true);
                    items.Add(new(resource, backupPath));
                    break;
                case RepairResourceKind.Directory when Directory.Exists(resource.Path):
                    CopyDirectory(resource.Path, backupPath);
                    items.Add(new(resource, backupPath));
                    break;
                case RepairResourceKind.RegistryKey:
                    backupPath += ".reg";
                    var export = await powerShell.RunAsync(
                        "param($Key,$Destination) & reg.exe export $Key $Destination /y; if ($LASTEXITCODE -ne 0) { throw \"reg.exe export failed with exit code $LASTEXITCODE\" }",
                        new Dictionary<string, object?> { ["Key"] = resource.Path, ["Destination"] = backupPath },
                        cancellationToken).ConfigureAwait(false);
                    if (export.Succeeded) items.Add(new(resource, backupPath));
                    else warnings.Add($"Could not export registry key {resource.Path}: {string.Join("; ", export.Errors)}");
                    break;
                default:
                    warnings.Add($"Resource was not present: {resource.Path}");
                    break;
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(location, "backup-manifest.json"),
            JsonSerializer.Serialize(new { transactionId, createdAtUtc = DateTimeOffset.UtcNow, items, warnings }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
        return new(location, items.AsReadOnly(), warnings.AsReadOnly());
    }

    private static string Sanitize(string value)
    {
        var safe = string.IsNullOrWhiteSpace(value) ? "resource" : value;
        foreach (var character in Path.GetInvalidFileNameChars()) safe = safe.Replace(character, '_');
        return safe;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
