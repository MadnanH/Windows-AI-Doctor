using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;

namespace WAID.Application.Tests;

public sealed class RepairSimulationEngineTests
{
    [Fact]
    public void Dry_run_is_deterministic_and_does_not_execute_module()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));
        var engine = new DeterministicRepairSimulationEngine(time);
        var module = new SimulationModule();
        var first = engine.Create(module, module.Plan, ["storage-ready"], []);
        var second = engine.Create(module, new RepairPlan(module.Plan.Resources.Reverse().ToArray(), module.Plan.Description), ["storage-ready"], []);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Effects, second.Effects);
        Assert.Equal(0, module.ExecutionCount);
        Assert.All(first.Effects, effect => Assert.False(string.IsNullOrWhiteSpace(effect.Rationale)));
    }

    [Fact]
    public void Prerequisites_are_explicit_exact_effects()
    {
        var engine = new DeterministicRepairSimulationEngine(TimeProvider.System);
        var module = new SimulationModule();
        var simulation = engine.Create(module, module.Plan, ["network-online"], []);

        var prerequisite = Assert.Single(simulation.Effects, effect => effect.Kind == RepairEffectKind.Prerequisite);
        Assert.Equal("network-online", prerequisite.Target);
        Assert.Equal(RepairEffectCertainty.Exact, prerequisite.Certainty);
    }

    [Fact]
    public void Expired_simulation_is_not_current()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));
        var engine = new DeterministicRepairSimulationEngine(time);
        var module = new SimulationModule();
        var simulation = engine.Create(module, module.Plan, [], []);
        time.Advance(TimeSpan.FromMinutes(16));

        Assert.False(engine.IsCurrent(simulation, module, module.Plan, [], []));
        Assert.Equal(0, module.ExecutionCount);
    }

    [Fact]
    public void Definition_or_policy_change_invalidates_fingerprint()
    {
        var engine = new DeterministicRepairSimulationEngine(TimeProvider.System);
        var module = new SimulationModule();
        var simulation = engine.Create(module, module.Plan, [], []);
        var changed = new RepairPlan([new(RepairResourceKind.RegistryKey, @"HKLM\Software\Changed")], module.Plan.Description);

        Assert.False(engine.IsCurrent(simulation, module, changed, [], []));
    }

    private sealed class SimulationModule : IRepairModule
    {
        public string Id => "waid.simulation-test";
        public string DisplayName => "Simulation test";
        public string Description => "Describe changes without mutation.";
        public RepairPolicy Policy { get; } = new(SafetyLevel.Moderate);
        public RepairPlan Plan { get; } = new([new(RepairResourceKind.File, @"C:\Windows\test.dll"), new(RepairResourceKind.RegistryKey, @"HKLM\Software\WAID")], "Test deterministic effects.");
        public int ExecutionCount { get; private set; }
        public Task<RepairPlan> CreatePlanAsync(DiagnosticFinding? finding, CancellationToken cancellationToken) => Task.FromResult(Plan);
        public Task<RepairResult> ExecuteAsync(RepairExecutionContext context, CancellationToken cancellationToken) { ExecutionCount++; return Task.FromResult(RepairResult.Success("executed")); }
    }

    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;
        public override DateTimeOffset GetUtcNow() => _current;
        public void Advance(TimeSpan amount) => _current += amount;
    }
}