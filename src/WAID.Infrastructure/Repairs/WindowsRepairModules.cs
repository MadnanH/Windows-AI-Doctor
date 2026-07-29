using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Repairs;

public abstract class PowerShellRepairModule(IPowerShellRunner powerShell) : IRepairModule, IRepairSimulationDefinitionProvider
{
    protected IPowerShellRunner PowerShell { get; } = powerShell;
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract RepairPolicy Policy { get; }
    protected abstract string Script { get; }
    protected abstract IReadOnlyCollection<RepairResource> Resources { get; }
    protected virtual bool RestartRequired => false;

    public Task<RepairPlan> CreatePlanAsync(DiagnosticFinding? finding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RepairPlan(Resources, Description));
    }

    public virtual RepairSimulationDefinition DescribeSimulation(RepairPlan plan)
    {
        var effects = plan.Resources
            .OrderBy(resource => resource.Kind)
            .ThenBy(resource => resource.Path, StringComparer.OrdinalIgnoreCase)
            .Select((resource, index) => new RepairPredictedEffect(
                index + 1,
                resource.Kind == RepairResourceKind.RegistryKey ? RepairEffectKind.Registry : RepairEffectKind.File,
                resource.Path,
                "Current state; backup is required when supported.",
                "The declared Windows resource may be changed by the repair command.",
                RepairEffectCertainty.Estimated,
                "The target is exact; the resulting value depends on Windows state."))
            .ToList();
        effects.Add(new(effects.Count + 1, RepairEffectKind.Command, DisplayName, "Not executed.", Description, RepairEffectCertainty.Unknown, "Exit code and exact changes are only available after execution."));
        effects.Add(new(effects.Count + 1, RepairEffectKind.Restart, "Windows", "Current session continues.", RestartRequired ? "A restart is required after successful execution." : "No restart is expected by this module.", RepairEffectCertainty.Exact, "Declared by the registered repair definition."));
        return new(effects, ["The preview uses the current registered repair definition and does not query or mutate protected state."], RestartRequired ? ["Save work before execution because a restart is expected."] : [], TimeSpan.FromMinutes(5), null, RestartRequired);
    }
    public async Task<RepairResult> ExecuteAsync(RepairExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await PowerShell.RunAsync(
            Script,
            new Dictionary<string, object?> { ["TransactionId"] = context.TransactionId.ToString("N") },
            cancellationToken).ConfigureAwait(false);
        var actions = result.Output.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return result.Succeeded
            ? RepairResult.Success($"{DisplayName} completed successfully.", RestartRequired, actions)
            : RepairResult.Failure(
                $"{DisplayName} failed.",
                string.Join(Environment.NewLine, result.Errors),
                actions: actions);
    }
}

public sealed class DismRepairModule(IPowerShellRunner powerShell) : PowerShellRepairModule(powerShell)
{
    public override string Id => "waid.dism";
    public override string DisplayName => "DISM component store repair";
    public override string Description => "Checks and repairs the Windows component store using DISM RestoreHealth.";
    public override RepairPolicy Policy { get; } = new(SafetyLevel.High, SupportsRollback: false);
    protected override IReadOnlyCollection<RepairResource> Resources => [];
    protected override string Script => """
        param($TransactionId)
        & dism.exe /Online /Cleanup-Image /RestoreHealth
        if ($LASTEXITCODE -ne 0) { throw "DISM failed with exit code $LASTEXITCODE" }
        "DISM repaired the Windows component store."
        """;
}

public sealed class SfcRepairModule(IPowerShellRunner powerShell) : PowerShellRepairModule(powerShell)
{
    public override string Id => "waid.sfc";
    public override string DisplayName => "System File Checker repair";
    public override string Description => "Verifies protected Windows files and replaces damaged copies.";
    public override RepairPolicy Policy { get; } = new(SafetyLevel.High, SupportsRollback: false);
    protected override IReadOnlyCollection<RepairResource> Resources => [];
    protected override string Script => """
        param($TransactionId)
        & sfc.exe /scannow
        if ($LASTEXITCODE -notin 0,1,2) { throw "SFC failed with exit code $LASTEXITCODE" }
        "System File Checker completed with exit code $LASTEXITCODE."
        """;
}

public sealed class WindowsUpdateResetModule(IPowerShellRunner powerShell) : PowerShellRepairModule(powerShell)
{
    public override string Id => "waid.windows-update-reset";
    public override string DisplayName => "Windows Update reset";
    public override string Description => "Stops update services, rotates update caches, and restarts the services.";
    public override RepairPolicy Policy { get; } = new(SafetyLevel.Critical);
    protected override bool RestartRequired => true;
    protected override IReadOnlyCollection<RepairResource> Resources =>
    [
        new(RepairResourceKind.RegistryKey, @"HKLM\SYSTEM\CurrentControlSet\Services\wuauserv"),
        new(RepairResourceKind.RegistryKey, @"HKLM\SYSTEM\CurrentControlSet\Services\BITS"),
        new(RepairResourceKind.RegistryKey, @"HKLM\SYSTEM\CurrentControlSet\Services\CryptSvc"),
        new(RepairResourceKind.Directory, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution")),
        new(RepairResourceKind.Directory, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "catroot2"))
    ];
    protected override string Script => """
        param($TransactionId)
        $services = 'bits','wuauserv','cryptsvc','msiserver'
        $softwareDistribution = Join-Path $env:SystemRoot 'SoftwareDistribution'
        $catroot = Join-Path $env:SystemRoot 'System32\catroot2'
        $suffix = ".waid-$TransactionId"
        try {
            foreach ($service in $services) { Stop-Service -Name $service -Force -ErrorAction Stop }
            if (Test-Path -LiteralPath $softwareDistribution) { Rename-Item -LiteralPath $softwareDistribution -NewName ((Split-Path $softwareDistribution -Leaf) + $suffix) -ErrorAction Stop }
            if (Test-Path -LiteralPath $catroot) { Rename-Item -LiteralPath $catroot -NewName ((Split-Path $catroot -Leaf) + $suffix) -ErrorAction Stop }
            "Windows Update cache directories were rotated."
        }
        finally {
            foreach ($service in $services) { Start-Service -Name $service -ErrorAction Continue }
        }
        """;
}

public sealed class DnsResetModule(IPowerShellRunner powerShell) : PowerShellRepairModule(powerShell)
{
    public override string Id => "waid.dns-reset";
    public override string DisplayName => "DNS reset";
    public override string Description => "Flushes the Windows DNS resolver cache and refreshes DNS registration.";
    public override RepairPolicy Policy { get; } = new(SafetyLevel.Low, RequiresBackup: false, SupportsRollback: false);
    protected override IReadOnlyCollection<RepairResource> Resources => [];
    protected override string Script => """
        param($TransactionId)
        & ipconfig.exe /flushdns
        if ($LASTEXITCODE -ne 0) { throw "DNS cache flush failed with exit code $LASTEXITCODE" }
        & ipconfig.exe /registerdns
        if ($LASTEXITCODE -ne 0) { throw "DNS registration failed with exit code $LASTEXITCODE" }
        "DNS cache and registration were reset."
        """;
}

public sealed class WinsockResetModule(IPowerShellRunner powerShell) : PowerShellRepairModule(powerShell)
{
    public override string Id => "waid.winsock-reset";
    public override string DisplayName => "Winsock reset";
    public override string Description => "Resets the Windows Winsock catalog to a clean state.";
    public override RepairPolicy Policy { get; } = new(SafetyLevel.High);
    protected override bool RestartRequired => true;
    protected override IReadOnlyCollection<RepairResource> Resources =>
    [
        new(RepairResourceKind.RegistryKey, @"HKLM\SYSTEM\CurrentControlSet\Services\WinSock2")
    ];
    protected override string Script => """
        param($TransactionId)
        & netsh.exe winsock reset
        if ($LASTEXITCODE -ne 0) { throw "Winsock reset failed with exit code $LASTEXITCODE" }
        "Winsock catalog was reset."
        """;
}

public sealed class TcpIpResetModule(IPowerShellRunner powerShell) : PowerShellRepairModule(powerShell)
{
    public override string Id => "waid.tcpip-reset";
    public override string DisplayName => "TCP/IP reset";
    public override string Description => "Resets the Windows TCP/IP stack configuration.";
    public override RepairPolicy Policy { get; } = new(SafetyLevel.Critical);
    protected override bool RestartRequired => true;
    protected override IReadOnlyCollection<RepairResource> Resources =>
    [
        new(RepairResourceKind.RegistryKey, @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"),
        new(RepairResourceKind.RegistryKey, @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters")
    ];
    protected override string Script => """
        param($TransactionId)
        & netsh.exe int ip reset
        if ($LASTEXITCODE -ne 0) { throw "TCP/IP reset failed with exit code $LASTEXITCODE" }
        "TCP/IP stack was reset."
        """;
}
