using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WAID.Application.Services;

public enum PerformanceArea { Startup, Memory, Cpu, Scan, Database, Report, Monitoring, UiResponsiveness }
public sealed record PerformanceBudget(PerformanceArea Area, double Limit, string Unit, string Rationale)
{
    public PerformanceBudget Validate() => Limit <= 0 || string.IsNullOrWhiteSpace(Unit) || string.IsNullOrWhiteSpace(Rationale)
        ? throw new ArgumentException("A positive performance limit, unit, and rationale are required.") : this;
}
public sealed record PerformanceObservation(string Operation, PerformanceArea Area, double Value, string Unit, bool WithinBudget, DateTimeOffset ObservedAtUtc);
public interface IPerformanceTelemetry
{
    IDisposable Measure(string operation, PerformanceArea area);
    void Observe(string operation, PerformanceArea area, double value);
    IReadOnlyList<PerformanceObservation> Snapshot();
}

public sealed class PerformanceBudgetCatalog
{
    private readonly IReadOnlyDictionary<PerformanceArea, PerformanceBudget> _budgets;
    public PerformanceBudgetCatalog(IEnumerable<PerformanceBudget>? budgets = null)
    {
        var configured = (budgets ?? Defaults).Select(x => x.Validate()).ToArray();
        _budgets = configured.ToDictionary(x => x.Area);
        if (Enum.GetValues<PerformanceArea>().Any(area => !_budgets.ContainsKey(area))) throw new InvalidOperationException("Every performance area must have a budget.");
    }
    public PerformanceBudget this[PerformanceArea area] => _budgets[area];
    public IReadOnlyCollection<PerformanceBudget> All => _budgets.Values.ToArray();
    public static IReadOnlyList<PerformanceBudget> Defaults { get; } =
    [
        new(PerformanceArea.Startup, 4_000, "ms", "Interactive shell activation on supported release hardware."),
        new(PerformanceArea.Memory, 350, "MiB", "Steady-state working-set guard for the desktop process."),
        new(PerformanceArea.Cpu, 5, "percent", "Average idle-monitoring CPU utilization."),
        new(PerformanceArea.Scan, 120_000, "ms", "Complete read-only scan excluding provider timeouts."),
        new(PerformanceArea.Database, 250, "ms", "P95 indexed interactive query latency."),
        new(PerformanceArea.Report, 5_000, "ms", "Local report generation for a representative large case."),
        new(PerformanceArea.Monitoring, 2_000, "ms", "One background monitoring collection cycle."),
        new(PerformanceArea.UiResponsiveness, 100, "ms", "Maximum synchronous UI-thread work per interaction.")
    ];
}

public sealed class PerformanceTelemetry(PerformanceBudgetCatalog catalog, TimeProvider time, ILogger<PerformanceTelemetry> logger) : IPerformanceTelemetry
{
    private const int Capacity = 512;
    private static readonly ActivitySource Activities = new("WAID.Performance", "1.0");
    private readonly ConcurrentQueue<PerformanceObservation> _observations = new();
    public IDisposable Measure(string operation, PerformanceArea area)
    {
        ValidateOperation(operation);
        if (area is PerformanceArea.Memory or PerformanceArea.Cpu) throw new ArgumentException("Memory and CPU budgets require a gauge observation.", nameof(area));
        return new Scope(this, operation, area, Stopwatch.StartNew(), Activities.StartActivity(operation, ActivityKind.Internal));
    }
    public void Observe(string operation, PerformanceArea area, double value)
    {
        ValidateOperation(operation);
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Record(operation, area, value, catalog[area].Unit, null);
    }
    public IReadOnlyList<PerformanceObservation> Snapshot() => _observations.ToArray();
    private void Record(string operation, PerformanceArea area, TimeSpan elapsed, Activity? activity)
        => Record(operation, area, elapsed.TotalMilliseconds, "ms", activity);
    private void Record(string operation, PerformanceArea area, double value, string unit, Activity? activity)
    {
        var budget = catalog[area];
        if (!string.Equals(unit, budget.Unit, StringComparison.Ordinal)) throw new InvalidOperationException("The observation unit does not match its budget.");
        var observation = new PerformanceObservation(operation, area, value, unit, value <= budget.Limit, time.GetUtcNow());
        _observations.Enqueue(observation);
        while (_observations.Count > Capacity) _observations.TryDequeue(out _);
        activity?.SetTag("waid.performance.area", area.ToString());
        activity?.SetTag("waid.performance.elapsed_ms", value);
        activity?.SetTag("waid.performance.within_budget", observation.WithinBudget);
        if (observation.WithinBudget) logger.LogDebug("Performance observation {Operation} was {Value:F1} {Unit}", operation, value, unit);
        else logger.LogWarning("Performance budget exceeded for {Operation}: {Value:F1} {Unit} (budget {Budget:F1} {Unit})", operation, value, unit, budget.Limit, unit);
    }
    private static void ValidateOperation(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 100 || operation.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            throw new ArgumentException("A bounded, identifier-only operation name is required.", nameof(operation));
    }
    private sealed class Scope(PerformanceTelemetry owner, string operation, PerformanceArea area, Stopwatch watch, Activity? activity) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; watch.Stop(); owner.Record(operation, area, watch.Elapsed, activity); activity?.Dispose(); }
    }
}
