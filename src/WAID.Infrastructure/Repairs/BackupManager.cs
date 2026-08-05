using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Repairs;

public sealed class BackupManager(string backupRoot, IPowerShellRunner powerShell, ILogger<BackupManager> logger, IRecoveryArtifactRepository? artifacts = null, TimeProvider? clock = null, IRecoveryStorageProbe? storage = null) : IBackupManager
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly IRecoveryStorageProbe _storage = storage ?? new WindowsRecoveryStorageProbe();

    public async Task<BackupSnapshot> CreateAsync(Guid transactionId, IReadOnlyCollection<RepairResource> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var root = Path.GetFullPath(backupRoot);
        var location = Path.GetFullPath(Path.Combine(root, transactionId.ToString("N")));
        if (!location.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Backup location escaped the configured root.");
        Directory.CreateDirectory(location);
        var persisted = Path.Combine(location, "snapshot.json");
        if (File.Exists(persisted))
        {
            var existing = JsonSerializer.Deserialize<BackupSnapshot>(await File.ReadAllTextAsync(persisted, cancellationToken).ConfigureAwait(false), Json);
            if (existing is not null && await ValidateAsync(existing, cancellationToken).ConfigureAwait(false)) return existing;
            return existing is null ? Invalid(location, "WAID-BACKUP-METADATA", "Existing backup metadata is invalid.") : existing with { IsValidated = false, Capability = RecoveryCapabilityLevel.None, ValidationFailureCode = "WAID-BACKUP-INTEGRITY" };
        }

        var warnings = new List<string>();
        var estimated = EstimateBytes(resources);
        if (!_storage.HasAvailableSpace(location, estimated + 10L * 1024 * 1024)) { var invalid = Invalid(location, "WAID-BACKUP-SPACE", "Insufficient free space for the required backup.", _clock.GetUtcNow()); if (artifacts is not null) await artifacts.SaveAsync(ToRecord(transactionId, invalid), cancellationToken).ConfigureAwait(false); return invalid; }
        var protection = await RestrictAccessAsync(location, cancellationToken).ConfigureAwait(false);
        if (protection != RecoveryArtifactProtection.LocalAccessRestricted) warnings.Add("Backup directory access restrictions could not be verified.");
        var items = new List<BackupItem>();
        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backupPath = Path.Combine(location, $"{items.Count:D3}-{Sanitize(Path.GetFileName(resource.Path))}");
            logger.LogInformation("Backing up repair resource {ResourceKind} for transaction {TransactionId}", resource.Kind, transactionId);
            try
            {
                switch (resource.Kind)
                {
                    case RepairResourceKind.File when File.Exists(resource.Path): File.Copy(resource.Path, backupPath, true); break;
                    case RepairResourceKind.Directory when Directory.Exists(resource.Path): CopyDirectory(resource.Path, backupPath); break;
                    case RepairResourceKind.RegistryKey:
                        backupPath += ".reg";
                        var export = await powerShell.RunAsync("param($Key,$Destination) & reg.exe export $Key $Destination /y; if ($LASTEXITCODE -ne 0) { throw \"Registry export failed.\" }", new Dictionary<string, object?> { ["Key"] = resource.Path, ["Destination"] = backupPath }, cancellationToken).ConfigureAwait(false);
                        if (!export.Succeeded) { warnings.Add("A required registry resource could not be exported."); continue; }
                        break;
                    default: warnings.Add("A required resource was unavailable during backup."); continue;
                }
                var digest = HashPath(backupPath); items.Add(new(resource, backupPath, digest.Hash, digest.Length));
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning("Backup resource failed with {FailureType}", exception.GetType().Name); warnings.Add($"A required {resource.Kind} resource could not be backed up."); }
        }
        var created = _clock.GetUtcNow();
        var manifestPath = Path.Combine(location, "backup-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new BackupManifest("recovery-artifact-1.0", transactionId, created, created.AddDays(30), items), Json), cancellationToken).ConfigureAwait(false);
        var manifestHash = HashFile(manifestPath);
        var valid = protection == RecoveryArtifactProtection.LocalAccessRestricted && items.Count == resources.Count && warnings.Count == 0 && items.All(x => x.Sha256.Length == 64);
        var snapshot = new BackupSnapshot(location, items.AsReadOnly(), warnings.AsReadOnly(), manifestHash, created, created.AddDays(30), protection, valid ? RecoveryCapabilityLevel.ResourceBackup : RecoveryCapabilityLevel.None, valid, valid ? null : "WAID-BACKUP-VALIDATION");
        await File.WriteAllTextAsync(persisted, JsonSerializer.Serialize(snapshot, Json), cancellationToken).ConfigureAwait(false);
        if (artifacts is not null) await artifacts.SaveAsync(ToRecord(transactionId, snapshot), cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public static async Task<bool> ValidateAsync(BackupSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!snapshot.IsValidated || snapshot.ManifestSha256.Length != 64 || !File.Exists(Path.Combine(snapshot.Location, "backup-manifest.json"))) return false;
        var manifestPath = Path.Combine(snapshot.Location, "backup-manifest.json");
        if (!string.Equals(HashFile(manifestPath), snapshot.ManifestSha256, StringComparison.OrdinalIgnoreCase)) return false;
        BackupManifest? manifest; try { manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, token).ConfigureAwait(false), Json); } catch (JsonException) { return false; }
        if (manifest is null || manifest.SchemaVersion != "recovery-artifact-1.0" || manifest.CreatedAtUtc != snapshot.CreatedAtUtc || manifest.ExpiresAtUtc != snapshot.ExpiresAtUtc || !manifest.Items.SequenceEqual(snapshot.Items)) return false;
        foreach (var item in snapshot.Items) { token.ThrowIfCancellationRequested(); if (!File.Exists(item.BackupPath) && !Directory.Exists(item.BackupPath)) return false; var digest = HashPath(item.BackupPath); if (!string.Equals(digest.Hash, item.Sha256, StringComparison.OrdinalIgnoreCase) || digest.Length != item.Length) return false; }
        await Task.CompletedTask; return true;
    }

    private sealed record BackupManifest(string SchemaVersion, Guid TransactionId, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, IReadOnlyCollection<BackupItem> Items);
    private RecoveryArtifactRecord ToRecord(Guid transactionId, BackupSnapshot snapshot) => new(Guid.NewGuid(), transactionId, snapshot.Location, snapshot.ManifestSha256, snapshot.CreatedAtUtc, snapshot.ExpiresAtUtc, snapshot.Protection, snapshot.Capability, snapshot.IsValidated ? RecoveryArtifactState.Valid : RecoveryArtifactState.Invalid, _clock.GetUtcNow(), snapshot.IsValidated ? "Manifest, item hashes, access protection, completeness, and space checks passed." : snapshot.ValidationFailureCode ?? "Validation failed.");
    private static BackupSnapshot Invalid(string location, string code, string warning, DateTimeOffset? created = null) { var time = created ?? DateTimeOffset.UtcNow; return new(location, [], [warning], CreatedAtUtc: time, ExpiresAtUtc: time.AddDays(30), ValidationFailureCode: code); }
    private async Task<RecoveryArtifactProtection> RestrictAccessAsync(string location, CancellationToken token) { var result = await powerShell.RunAsync("param($Path) & icacls.exe $Path /inheritance:r /grant:r \"${env:USERNAME}:(OI)(CI)F\" \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" /Q; if ($LASTEXITCODE -ne 0) { throw \"Access restriction failed.\" }", new Dictionary<string, object?> { ["Path"] = location }, token).ConfigureAwait(false); return result.Succeeded ? RecoveryArtifactProtection.LocalAccessRestricted : RecoveryArtifactProtection.Unknown; }
    private static long EstimateBytes(IEnumerable<RepairResource> resources) { long total = 0; foreach (var resource in resources) try { if (resource.Kind == RepairResourceKind.File && File.Exists(resource.Path)) total = checked(total + new FileInfo(resource.Path).Length); else if (resource.Kind == RepairResourceKind.Directory && Directory.Exists(resource.Path)) foreach (var file in Directory.EnumerateFiles(resource.Path, "*", SearchOption.AllDirectories)) total = checked(total + new FileInfo(file).Length); else if (resource.Kind == RepairResourceKind.RegistryKey) total = checked(total + 1024 * 1024); } catch { return long.MaxValue / 2; } return total; }
    private static (string Hash, long Length) HashPath(string path) { if (File.Exists(path)) return (HashFile(path), new FileInfo(path).Length); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long length = 0; foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)) { var relative = Path.GetRelativePath(path, file).Replace('\\', '/'); hash.AppendData(Encoding.UTF8.GetBytes(relative)); using var stream = File.OpenRead(file); var buffer = new byte[81920]; int read; while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { hash.AppendData(buffer, 0, read); length += read; } } return (Convert.ToHexString(hash.GetHashAndReset()), length); }
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string Sanitize(string value) { var safe = string.IsNullOrWhiteSpace(value) ? "resource" : value; foreach (var character in Path.GetInvalidFileNameChars()) safe = safe.Replace(character, '_'); return safe; }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true); foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory))); }
}