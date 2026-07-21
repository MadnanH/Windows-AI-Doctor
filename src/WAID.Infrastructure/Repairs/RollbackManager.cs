using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Repairs;

public sealed class RollbackManager(
    IPowerShellRunner powerShell,
    ILogger<RollbackManager> logger) : IRollbackManager
{
    public async Task<RollbackResult> RollbackAsync(BackupSnapshot snapshot, CancellationToken cancellationToken)
    {
        var actions = new List<string>();
        var errors = new List<string>();
        foreach (var item in snapshot.Items.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                logger.LogWarning("Rolling back {ResourceKind} {ResourcePath}", item.Resource.Kind, item.Resource.Path);
                switch (item.Resource.Kind)
                {
                    case RepairResourceKind.File:
                        var parent = Path.GetDirectoryName(item.Resource.Path);
                        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                        File.Copy(item.BackupPath, item.Resource.Path, true);
                        break;
                    case RepairResourceKind.Directory:
                        CopyDirectory(item.BackupPath, item.Resource.Path);
                        break;
                    case RepairResourceKind.RegistryKey:
                        var import = await powerShell.RunAsync(
                            "param($BackupPath) & reg.exe import $BackupPath; if ($LASTEXITCODE -ne 0) { throw \"reg.exe import failed with exit code $LASTEXITCODE\" }",
                            new Dictionary<string, object?> { ["BackupPath"] = item.BackupPath }, cancellationToken).ConfigureAwait(false);
                        if (!import.Succeeded) throw new InvalidOperationException(string.Join(Environment.NewLine, import.Errors));
                        break;
                }
                actions.Add($"Restored {item.Resource.Path}");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Rollback failed for {ResourcePath}", item.Resource.Path);
                errors.Add($"{item.Resource.Path}: {exception.Message}");
            }
        }
        return new(errors.Count == 0, actions.AsReadOnly(), errors.AsReadOnly());
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
