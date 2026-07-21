using WAID.Application.Abstractions;
using WAID.Infrastructure.Diagnostics;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Tests;

public sealed class WindowsScannerTests
{
    [Theory]
    [InlineData("event-viewer")]
    [InlineData("reliability")]
    [InlineData("drivers")]
    [InlineData("software")]
    [InlineData("windows-update")]
    [InlineData("services")]
    [InlineData("startup")]
    [InlineData("registry")]
    [InlineData("defender")]
    [InlineData("network")]
    [InlineData("storage")]
    [InlineData("smart")]
    [InlineData("memory")]
    [InlineData("cpu")]
    [InlineData("gpu")]
    [InlineData("bsod")]
    public async Task Scanner_handles_empty_result(string name)
    {
        var scanner = Create(name, new FakePowerShellRunner("[]"));
        var findings = await scanner.ScanAsync(new(Guid.NewGuid(), true, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Empty(findings);
        Assert.False(string.IsNullOrWhiteSpace(scanner.Id));
        Assert.False(string.IsNullOrWhiteSpace(scanner.DisplayName));
    }

    [Fact]
    public async Task Scanner_normalizes_structured_evidence()
    {
        const string json = "{\"code\":\"SMART_WARNING\",\"title\":\"Disk warning\",\"description\":\"Health warning\",\"severity\":\"Critical\",\"evidence\":{\"category\":\"Storage\"}}";
        var scanner = new SmartScanner(new FakePowerShellRunner(json));
        var finding = Assert.Single(await scanner.ScanAsync(new(Guid.NewGuid(), true, DateTimeOffset.UtcNow), CancellationToken.None));
        Assert.Equal("SMART_WARNING", finding.Code);
        Assert.Equal(WAID.Domain.Diagnostics.DiagnosticSeverity.Critical, finding.Severity);
        Assert.Equal("Storage", finding.Evidence["category"]);
    }

    private static ISystemScanner Create(string name, IPowerShellRunner runner) => name switch
    {
        "event-viewer" => new WindowsEventViewerScanner(runner), "reliability" => new ReliabilityMonitorScanner(runner),
        "drivers" => new InstalledDriversScanner(runner), "software" => new InstalledSoftwareScanner(runner),
        "windows-update" => new WindowsUpdateScanner(runner), "services" => new RunningServicesScanner(runner),
        "startup" => new StartupApplicationsScanner(runner), "registry" => new RegistryHealthScanner(runner),
        "defender" => new WindowsDefenderScanner(runner), "network" => new NetworkConfigurationScanner(runner),
        "storage" => new StorageHealthScanner(runner), "smart" => new SmartScanner(runner),
        "memory" => new MemoryScanner(runner), "cpu" => new CpuScanner(runner), "gpu" => new GpuScanner(runner),
        "bsod" => new BsodMinidumpScanner(runner), _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private sealed class FakePowerShellRunner(string json) : IPowerShellRunner
    {
        public Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken token) =>
            Task.FromResult(new PowerShellResult([json], []));
    }
}
