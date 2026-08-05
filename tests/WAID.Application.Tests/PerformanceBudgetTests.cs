using Microsoft.Extensions.Logging.Abstractions;
using WAID.Application.Services;

namespace WAID.Application.Tests;

[Trait("Category", "Performance")]
public sealed class PerformanceBudgetTests
{
    [Fact]
    public void Default_catalog_defines_every_required_budget()
    {
        var catalog = new PerformanceBudgetCatalog();
        Assert.Equal(Enum.GetValues<PerformanceArea>().Length, catalog.All.Count);
        Assert.All(catalog.All, budget => Assert.True(budget.Limit > 0));
    }

    [Fact]
    public void Catalog_rejects_an_incomplete_budget_set() =>
        Assert.Throws<InvalidOperationException>(() => new PerformanceBudgetCatalog([new(PerformanceArea.Scan, 1, "ms", "test")]));

    [Fact]
    public void Telemetry_records_bounded_non_sensitive_measurements()
    {
        var telemetry = new PerformanceTelemetry(new PerformanceBudgetCatalog(), TimeProvider.System, NullLogger<PerformanceTelemetry>.Instance);
        for (var index = 0; index < 600; index++) telemetry.Measure($"operation-{index}", PerformanceArea.Database).Dispose();
        var snapshot = telemetry.Snapshot();
        Assert.Equal(512, snapshot.Count);
        Assert.DoesNotContain(snapshot, item => item.Operation.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot, item => Assert.True(item.Value >= 0));
    }

    [Fact]
    public void Telemetry_rejects_unbounded_operation_names()
    {
        var telemetry = new PerformanceTelemetry(new PerformanceBudgetCatalog(), TimeProvider.System, NullLogger<PerformanceTelemetry>.Instance);
        Assert.Throws<ArgumentException>(() => telemetry.Measure(new string('x', 101), PerformanceArea.Scan));
        Assert.Throws<ArgumentException>(() => telemetry.Measure("report C:\\Users\\person", PerformanceArea.Report));
        Assert.Throws<ArgumentException>(() => telemetry.Measure("cpu", PerformanceArea.Cpu));
    }

    [Fact]
    public void Gauge_observations_use_the_declared_budget_unit()
    {
        var telemetry = new PerformanceTelemetry(new PerformanceBudgetCatalog(), TimeProvider.System, NullLogger<PerformanceTelemetry>.Instance);
        telemetry.Observe("desktop.working-set", PerformanceArea.Memory, 349);
        telemetry.Observe("monitoring.cpu", PerformanceArea.Cpu, 6);
        var observations = telemetry.Snapshot();
        Assert.True(observations[0].WithinBudget);
        Assert.Equal("MiB", observations[0].Unit);
        Assert.False(observations[1].WithinBudget);
        Assert.Equal("percent", observations[1].Unit);
    }

    [Fact]
    public void Downsampling_rejects_invalid_limit_and_preserves_peak()
    {
        var engine = new PerformanceAggregationEngine();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Downsample([], 0));
        var points = Enumerable.Range(0, 250_000).Select(index => Rollup(index, index == 123_456 ? 100 : 5)).ToArray();
        var result = engine.Downsample(points, 500);
        Assert.Equal(500, result.Count);
        Assert.Contains(result, item => item.Maximum == 100);
    }

    [Fact]
    public void Repeated_large_downsampling_has_bounded_managed_memory_growth()
    {
        var engine = new PerformanceAggregationEngine();
        var points = Enumerable.Range(0, 100_000).Select(index => Rollup(index, index % 100)).ToArray();
        _ = engine.Downsample(points, 500);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        for (var iteration = 0; iteration < 100; iteration++) Assert.Equal(500, engine.Downsample(points, 500).Count);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.True(GC.GetTotalMemory(true) - before < 16 * 1024 * 1024, "Managed memory growth exceeded the 16 MiB soak-test allowance.");
    }

    private static PerformanceMetricRollup Rollup(int index, double value)
    {
        var start = DateTimeOffset.UnixEpoch.AddMinutes(index);
        return new(Guid.NewGuid(), PerformanceMetricKind.CpuUtilization, MetricResolution.Hourly, start, start.AddHours(1), value, value, value, value, 1, 100, "percent", MetricQuality.Measured, PerformanceAggregationEngine.Version);
    }
}
