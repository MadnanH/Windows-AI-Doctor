using System.IO.Compression;
using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.Health;
using WAID.Infrastructure.Diagnostics;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class DiagnosticsExportTests
{
    [Fact]
    public async Task Export_contains_required_diagnostic_data_without_database_or_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"waid-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        var database = new WaidDatabase($"Data Source={Path.Combine(root, "waid.db")};Foreign Keys=True;Pooling=False");
        try
        {
            await database.InitializeAsync(CancellationToken.None);
            var scans = new SqliteScanRepository(database);
            var diagnoses = new SqliteDiagnosisRepository(database);
            var repairs = new SqliteRepairHistoryRepository(database);
            var session = new ScanSession(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-1));
            session.AddFindings([new("waid.cpu", "CPU_HIGH", "High CPU", "CPU usage is high.", DiagnosticSeverity.Warning)]);
            session.Complete(DateTimeOffset.UtcNow);
            await scans.SaveAsync(session, CancellationToken.None);
            await diagnoses.SaveAsync(session.Id, new AIReport(DateTimeOffset.UtcNow, "Review CPU use.", new HealthScore(100, 100, 100, 100, 88, 100, 100, 99), [], [], session.Findings), CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root, "logs", "waid-test.log"), "structured test log");
            var service = new DiagnosticsExportService(root, scans, diagnoses, repairs, TimeProvider.System);

            var package = await service.ExportAsync(CancellationToken.None);

            Assert.True(File.Exists(package));
            using var archive = ZipFile.OpenRead(package);
            var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("scan-data.json", names);
            Assert.Contains("diagnosis.json", names);
            Assert.Contains("repair-history.json", names);
            Assert.Contains("system-information.json", names);
            Assert.Contains("logs/waid-test.log", names);
            Assert.DoesNotContain("waid.db", names);
            Assert.DoesNotContain("settings.json", names);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
