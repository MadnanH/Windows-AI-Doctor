using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;
using WAID.Infrastructure.Repairs;

namespace WAID.Infrastructure.Tests;

public sealed class RepairInfrastructureTests
{
    [Fact]
    public async Task File_backup_can_be_rolled_back()
    {
        var root = Path.Combine(Path.GetTempPath(), $"waid-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "settings.ini");
            await File.WriteAllTextAsync(source, "original");
            var powerShell = new FakePowerShellRunner();
            var backup = new BackupManager(Path.Combine(root, "backups"), powerShell, NullLogger<BackupManager>.Instance);
            var rollback = new RollbackManager(powerShell, NullLogger<RollbackManager>.Instance);

            var snapshot = await backup.CreateAsync(
                Guid.NewGuid(), [new(RepairResourceKind.File, source)], CancellationToken.None);
            await File.WriteAllTextAsync(source, "modified");
            var result = await rollback.RollbackAsync(snapshot, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("original", await File.ReadAllTextAsync(source));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void All_built_in_repairs_require_administrator_and_restore_point()
    {
        var powerShell = new FakePowerShellRunner();
        IRepairModule[] modules =
        [
            new DismRepairModule(powerShell), new SfcRepairModule(powerShell),
            new WindowsUpdateResetModule(powerShell), new DnsResetModule(powerShell),
            new WinsockResetModule(powerShell), new TcpIpResetModule(powerShell)
        ];

        Assert.All(modules, module =>
        {
            Assert.True(module.Policy.RequiresAdministrator);
            Assert.True(module.Policy.RequiresRestorePoint);
        });
        Assert.Equal(6, modules.Select(module => module.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task PowerShell_repair_returns_detailed_actions()
    {
        var powerShell = new FakePowerShellRunner
        {
            Result = new PowerShellResult(["Winsock catalog was reset."], [])
        };
        var module = new WinsockResetModule(powerShell);
        var plan = await module.CreatePlanAsync(null, CancellationToken.None);

        var result = await module.ExecuteAsync(
            new(Guid.NewGuid(), null, plan, "C:\\backup"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.RestartRequired);
        Assert.Contains("Winsock catalog was reset.", result.Actions);
    }

    private sealed class FakePowerShellRunner : IPowerShellRunner
    {
        public PowerShellResult Result { get; set; } = new([], []);
        public Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken token) =>
            Task.FromResult(Result);
    }
}
