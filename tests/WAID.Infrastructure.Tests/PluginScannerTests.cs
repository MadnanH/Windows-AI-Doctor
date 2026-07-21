using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Plugin.Sample;

namespace WAID.Infrastructure.Tests;

public sealed class PluginScannerTests
{
    [Fact]
    public async Task Sample_environment_scanner_performs_a_real_path_check()
    {
        var scanner = new EnvironmentScanner();

        var findings = await scanner.ScanAsync(new ScanContext(Guid.NewGuid(), false, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.All(findings, finding =>
        {
            Assert.Equal(scanner.Id, finding.ScannerId);
            Assert.StartsWith("ENV_PATH_", finding.Code, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(finding.Description));
        });
    }
}
