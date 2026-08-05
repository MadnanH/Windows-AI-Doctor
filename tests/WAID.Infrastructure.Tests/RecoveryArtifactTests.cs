using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;
using WAID.Infrastructure.Repairs;

namespace WAID.Infrastructure.Tests;

public sealed class RecoveryArtifactTests
{
    [Fact]
    public async Task Backup_manifest_and_items_are_hashed_and_rollback_is_verified()
    {
        await WithWorkspace(async root => { var source = Path.Combine(root, "settings.ini"); await File.WriteAllTextAsync(source, "original"); var manager = Manager(root); var snapshot = await manager.CreateAsync(Guid.NewGuid(), [new(RepairResourceKind.File, source)], CancellationToken.None); Assert.True(snapshot.IsValidated); Assert.Equal(64, snapshot.ManifestSha256.Length); Assert.Equal(64, Assert.Single(snapshot.Items).Sha256.Length); await File.WriteAllTextAsync(source, "changed"); var result = await new RollbackManager(new PowerShell(true), NullLogger<RollbackManager>.Instance).RollbackAsync(snapshot, CancellationToken.None); Assert.True(result.Succeeded); Assert.True(result.Verified); Assert.Equal("original", await File.ReadAllTextAsync(source)); });
    }

    [Fact]
    public async Task Tampered_artifact_is_rejected_before_restore()
    {
        await WithWorkspace(async root => { var source = Path.Combine(root, "settings.ini"); await File.WriteAllTextAsync(source, "original"); var snapshot = await Manager(root).CreateAsync(Guid.NewGuid(), [new(RepairResourceKind.File, source)], CancellationToken.None); await File.WriteAllTextAsync(Assert.Single(snapshot.Items).BackupPath, "tampered"); await File.WriteAllTextAsync(source, "changed"); var result = await new RollbackManager(new PowerShell(true), NullLogger<RollbackManager>.Instance).RollbackAsync(snapshot, CancellationToken.None); Assert.False(result.Succeeded); Assert.False(result.Verified); Assert.Equal("changed", await File.ReadAllTextAsync(source)); });
    }

    [Fact]
    public async Task Unverified_access_protection_makes_backup_unusable()
    {
        await WithWorkspace(async root => { var source = Path.Combine(root, "settings.ini"); await File.WriteAllTextAsync(source, "value"); var snapshot = await Manager(root, powerShell: new PowerShell(false)).CreateAsync(Guid.NewGuid(), [new(RepairResourceKind.File, source)], CancellationToken.None); Assert.False(snapshot.IsValidated); Assert.Equal(RecoveryCapabilityLevel.None, snapshot.Capability); Assert.Equal("WAID-BACKUP-VALIDATION", snapshot.ValidationFailureCode); });
    }

    [Fact]
    public async Task Insufficient_disk_space_fails_before_resource_copy()
    {
        await WithWorkspace(async root => { var source = Path.Combine(root, "settings.ini"); await File.WriteAllTextAsync(source, "value"); var snapshot = await Manager(root, storage: new Storage(false)).CreateAsync(Guid.NewGuid(), [new(RepairResourceKind.File, source)], CancellationToken.None); Assert.False(snapshot.IsValidated); Assert.Empty(snapshot.Items); Assert.Equal("WAID-BACKUP-SPACE", snapshot.ValidationFailureCode); });
    }

    [Fact]
    public async Task Repeated_creation_for_same_transaction_is_idempotent()
    {
        await WithWorkspace(async root => { var source = Path.Combine(root, "settings.ini"); await File.WriteAllTextAsync(source, "value"); var manager = Manager(root); var id = Guid.NewGuid(); var first = await manager.CreateAsync(id, [new(RepairResourceKind.File, source)], CancellationToken.None); var second = await manager.CreateAsync(id, [new(RepairResourceKind.File, source)], CancellationToken.None); Assert.Equal(first.ManifestSha256, second.ManifestSha256); Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc); Assert.True(second.IsValidated); });
    }

    [Fact]
    public async Task Recovery_workflow_never_rolls_back_without_explicit_confirmation()
    {
        var workflow = new RecoveryWorkflow(null!, null!, null!, TimeProvider.System, NullLogger<RecoveryWorkflow>.Instance);
        var result = await workflow.RollbackAsync(Guid.NewGuid(), false, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.False(result.Verified); Assert.Contains("confirmation", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static BackupManager Manager(string root, IPowerShellRunner? powerShell = null, IRecoveryStorageProbe? storage = null) => new(Path.Combine(root, "backups"), powerShell ?? new PowerShell(true), NullLogger<BackupManager>.Instance, storage: storage ?? new Storage(true));
    private static async Task WithWorkspace(Func<string, Task> test) { var root = Path.Combine(Path.GetTempPath(), $"waid-recovery-{Guid.NewGuid():N}"); Directory.CreateDirectory(root); try { await test(root); } finally { if (Directory.Exists(root)) Directory.Delete(root, true); } }
    private sealed class Storage(bool available) : IRecoveryStorageProbe { public bool HasAvailableSpace(string path, long requiredBytes) => available; }
    private sealed class PowerShell(bool succeeds) : IPowerShellRunner { public Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken token) => Task.FromResult(succeeds ? new PowerShellResult([], []) : new PowerShellResult([], ["denied"])); }
}