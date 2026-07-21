using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WAID.Application.Abstractions;
using WAID.Diagnosis;
using WAID.Infrastructure;
using WAID.Application.Services;

namespace WAID.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task Infrastructure_registers_every_production_scanner_and_workflow_service()
    {
        var root = Path.Combine(Path.GetTempPath(), $"waid-di-{Guid.NewGuid():N}");
        try
        {
            var services = new ServiceCollection().AddWaidInfrastructure(root);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var scanners = provider.GetServices<ISystemScanner>().Select(scanner => scanner.Id).ToHashSet(StringComparer.Ordinal);
            string[] expected = ["waid.os", "waid.event-viewer", "waid.reliability", "waid.drivers", "waid.software", "waid.windows-update", "waid.services", "waid.startup", "waid.registry-health", "waid.defender", "waid.network", "waid.storage-health", "waid.smart", "waid.memory", "waid.cpu", "waid.gpu", "waid.bsod", "waid.battery"];

            Assert.Equal(expected.Length, scanners.Count);
            Assert.All(expected, id => Assert.Contains(id, scanners));
            Assert.NotNull(provider.GetRequiredService<DiagnosisEngine>());
            Assert.NotNull(provider.GetRequiredService<IDiagnosisRepository>());
            Assert.NotNull(provider.GetRequiredService<IDiagnosticsExportService>());
            Assert.NotNull(provider.GetRequiredService<ScanCoordinator>());
            Assert.NotNull(provider.GetRequiredService<RepairExecutor>());
            Assert.NotNull(provider.GetRequiredService<BackgroundHealthMonitoringService>());
            Assert.NotNull(provider.GetRequiredService<ScheduledScanService>());
            Assert.NotNull(provider.GetRequiredService<IDiagnosticReportExporter>());
            Assert.NotNull(provider.GetRequiredService<IPdfReportExporter>());
        }
        finally
        {
            Log.CloseAndFlush();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
