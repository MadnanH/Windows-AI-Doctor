using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Repairs;

public sealed class RollbackManager(IPowerShellRunner powerShell, ILogger<RollbackManager> logger, IRecoveryArtifactRepository? artifacts = null, TimeProvider? clock = null) : IRollbackManager
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public async Task<RollbackResult> RollbackAsync(BackupSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ExpiresAtUtc != default && snapshot.ExpiresAtUtc <= _clock.GetUtcNow()) return new(false, [], ["Recovery artifact has expired."], false, "Expired artifacts cannot be used for rollback.");
        if (!await BackupManager.ValidateAsync(snapshot, cancellationToken).ConfigureAwait(false)) return new(false, [], ["Recovery artifact integrity validation failed."], false, "Rollback was blocked before mutation because the manifest or item hash was invalid.");
        var actions = new List<string>(); var errors = new List<string>();
        foreach (var item in snapshot.Items.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                logger.LogWarning("Rolling back {ResourceKind} for a validated recovery artifact", item.Resource.Kind);
                switch (item.Resource.Kind)
                {
                    case RepairResourceKind.File:
                        var parent = Path.GetDirectoryName(item.Resource.Path); if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent); File.Copy(item.BackupPath, item.Resource.Path, true); break;
                    case RepairResourceKind.Directory: CopyDirectory(item.BackupPath, item.Resource.Path); break;
                    case RepairResourceKind.RegistryKey:
                        var import = await powerShell.RunAsync("param($BackupPath) & reg.exe import $BackupPath; if ($LASTEXITCODE -ne 0) { throw \"Registry import failed.\" }", new Dictionary<string, object?> { ["BackupPath"] = item.BackupPath }, cancellationToken).ConfigureAwait(false);
                        if (!import.Succeeded) throw new InvalidOperationException("Registry provider did not confirm import."); break;
                    default: throw new InvalidOperationException("Unsupported recovery resource kind.");
                }
                if (item.Resource.Kind is RepairResourceKind.File or RepairResourceKind.Directory)
                {
                    var restored = HashPath(item.Resource.Path);
                    if (!string.Equals(restored.Hash, item.Sha256, StringComparison.OrdinalIgnoreCase) || restored.Length != item.Length) throw new InvalidDataException("Post-restore hash verification failed.");
                }
                actions.Add($"Restored and verified {item.Resource.Kind} resource.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogError("Rollback failed for {ResourceKind} with {FailureType}", item.Resource.Kind, exception.GetType().Name); errors.Add($"{item.Resource.Kind} rollback failed verification."); }
        }
        var verified = errors.Count == 0 && actions.Count == snapshot.Items.Count;
        await StoreOutcomeAsync(snapshot, verified, cancellationToken).ConfigureAwait(false);
        return new(verified, actions.AsReadOnly(), errors.AsReadOnly(), verified, verified ? "Every artifact was validated before restore and every supported restore action was verified." : "One or more restore actions failed verification.");
    }
    private async Task StoreOutcomeAsync(BackupSnapshot snapshot, bool verified, CancellationToken token) { if (artifacts is null) return; var existing = await artifacts.GetRecentAsync(500, token).ConfigureAwait(false); var record = existing.FirstOrDefault(x => string.Equals(x.Location, snapshot.Location, StringComparison.OrdinalIgnoreCase)); if (record is null) return; await artifacts.SaveAsync(record with { State = verified ? RecoveryArtifactState.RolledBack : RecoveryArtifactState.RollbackFailed, Capability = verified ? RecoveryCapabilityLevel.VerifiedRollback : RecoveryCapabilityLevel.None, RolledBackAtUtc = _clock.GetUtcNow(), RollbackDetail = verified ? "Rollback verified." : "Rollback verification failed." }, token).ConfigureAwait(false); }
    private static (string Hash, long Length) HashPath(string path) { if (File.Exists(path)) { using var stream = File.OpenRead(path); return (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)), stream.Length); } using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256); long length = 0; foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)) { hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(path, file).Replace('\\', '/'))); using var stream = File.OpenRead(file); var buffer = new byte[81920]; int read; while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { hash.AppendData(buffer, 0, read); length += read; } } return (Convert.ToHexString(hash.GetHashAndReset()), length); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true); foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory))); }
}