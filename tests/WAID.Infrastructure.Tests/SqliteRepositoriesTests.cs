using WAID.Domain.Diagnostics;
using WAID.Domain.Settings;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class SqliteRepositoriesTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"waid-tests-{Guid.NewGuid():N}.db");
    private WaidDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = new WaidDatabase($"Data Source={_databasePath};Foreign Keys=True;Pooling=False");
        await _database.InitializeAsync(CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Scan_round_trip_preserves_sessions_findings_and_evidence()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        var session = new ScanSession(Guid.NewGuid(), started);
        var finding = new DiagnosticFinding(
            "storage", "STORAGE_LOW", "Low storage", "Less than ten percent remains.",
            DiagnosticSeverity.Warning, "waid.cleanup",
            new Dictionary<string, string> { ["freeBytes"] = "1024" });
        session.AddFindings([finding]);
        session.Complete(started.AddSeconds(5));
        var repository = new SqliteScanRepository(_database);

        await repository.SaveAsync(session, CancellationToken.None);
        var loaded = Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None));

        Assert.Equal(session.Id, loaded.Id);
        Assert.Equal(session.CompletedAtUtc, loaded.CompletedAtUtc);
        var loadedFinding = Assert.Single(loaded.Findings);
        Assert.Equal(finding.Id, loadedFinding.Id);
        Assert.Equal("1024", loadedFinding.Evidence["freeBytes"]);
    }

    [Fact]
    public async Task Settings_round_trip_validated_values()
    {
        var repository = new SqliteSettingsRepository(_database);
        var settings = new ApplicationSettings
        {
            EnableAiAnalysis = true,
            Theme = "Dark",
            ScanTimeoutSeconds = 300
        };

        await repository.SaveAsync(settings, CancellationToken.None);
        var loaded = await repository.GetAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
    }
}
