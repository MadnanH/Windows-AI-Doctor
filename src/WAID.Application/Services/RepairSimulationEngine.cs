using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public enum RepairEffectKind { File, Registry, Service, ScheduledTask, Policy, Command, Restart, StorageSpace, Prerequisite }
public enum RepairEffectCertainty { Exact, Estimated, Unknown }
public sealed record RepairPredictedEffect(int Order, RepairEffectKind Kind, string Target, string Before, string After, RepairEffectCertainty Certainty, string Rationale);
public sealed record RepairSimulationDefinition(IReadOnlyList<RepairPredictedEffect> Effects, IReadOnlyList<string> Assumptions, IReadOnlyList<string> Warnings, TimeSpan? EstimatedDuration, long? EstimatedSpaceBytes, bool? RestartRequired);
public interface IRepairSimulationDefinitionProvider { RepairSimulationDefinition DescribeSimulation(RepairPlan plan); }
public interface IRepairSimulationEngine
{
    string Version { get; }
    RepairSimulation Create(IRepairModule module, RepairPlan plan, IReadOnlyList<string> dependencies, IReadOnlyList<string> conflicts);
    bool IsCurrent(RepairSimulation simulation, IRepairModule module, RepairPlan currentPlan, IReadOnlyList<string> dependencies, IReadOnlyList<string> conflicts);
}

public sealed class DeterministicRepairSimulationEngine(TimeProvider timeProvider) : IRepairSimulationEngine
{
    public const string CurrentVersion = "repair-simulation-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string Version => CurrentVersion;

    public RepairSimulation Create(IRepairModule module, RepairPlan plan, IReadOnlyList<string> dependencies, IReadOnlyList<string> conflicts)
    {
        ArgumentNullException.ThrowIfNull(module); ArgumentNullException.ThrowIfNull(plan);
        var definition = Describe(module, plan, dependencies);
        var created = timeProvider.GetUtcNow();
        return new(plan.Description, definition.Effects.Select(FormatAction).ToArray(), plan.Resources.OrderBy(x => x.Kind).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray(), module.Policy.SafetyLevel, module.Policy.RequiresAdministrator, module.Policy.RequiresRestorePoint, module.Policy.RequiresBackup, module.Policy.SupportsRollback, dependencies.Order(StringComparer.OrdinalIgnoreCase).ToArray(), conflicts.Order(StringComparer.OrdinalIgnoreCase).ToArray(), Version, Fingerprint(module, plan, dependencies, conflicts, definition), created, created.AddMinutes(15), definition.Effects, definition.Assumptions, definition.Warnings, definition.EstimatedDuration, definition.EstimatedSpaceBytes, definition.RestartRequired);
    }

    public bool IsCurrent(RepairSimulation simulation, IRepairModule module, RepairPlan currentPlan, IReadOnlyList<string> dependencies, IReadOnlyList<string> conflicts)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!string.Equals(simulation.SimulationVersion, Version, StringComparison.Ordinal) || timeProvider.GetUtcNow() > simulation.ValidUntilUtc || string.IsNullOrWhiteSpace(simulation.Fingerprint) || simulation.Fingerprint.Length != 64) return false;
        var current = Fingerprint(module, currentPlan, dependencies, conflicts, Describe(module, currentPlan, dependencies));
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(simulation.Fingerprint), Convert.FromHexString(current)); }
        catch (FormatException) { return false; }
    }

    private static RepairSimulationDefinition Describe(IRepairModule module, RepairPlan plan, IReadOnlyList<string> dependencies)
    {
        RepairSimulationDefinition definition;
        if (module is IRepairSimulationDefinitionProvider provider) definition = provider.DescribeSimulation(plan);
        else
        {
            var effects = plan.Resources.OrderBy(x => x.Kind).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select((resource, index) => new RepairPredictedEffect(index + 1, resource.Kind == RepairResourceKind.RegistryKey ? RepairEffectKind.Registry : RepairEffectKind.File, Redact(resource.Path), "Current state; backed up when required.", "The repair may modify this target.", RepairEffectCertainty.Estimated, "The module declares the target but not its resulting value.")).ToList();
            effects.Add(new(effects.Count + 1, RepairEffectKind.Command, module.DisplayName, "Not executed.", plan.Description, RepairEffectCertainty.Unknown, "The exact Windows result depends on current system state."));
            definition = new(effects, ["Windows state may change after this preview."], ["Exact command output cannot be predicted without execution."], null, null, null);
        }
        var normalized = definition.Effects.Select((effect, index) => effect with { Order = index + 1, Target = Redact(effect.Target), Before = Redact(effect.Before), After = Redact(effect.After), Rationale = Redact(effect.Rationale) }).ToList();
        foreach (var dependency in dependencies.Order(StringComparer.OrdinalIgnoreCase)) normalized.Add(new(normalized.Count + 1, RepairEffectKind.Prerequisite, dependency, "Not verified by simulation.", "Must be satisfied before approval.", RepairEffectCertainty.Exact, "Declared repair dependency."));
        return definition with { Effects = normalized, Assumptions = definition.Assumptions.Select(Redact).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), Warnings = definition.Warnings.Select(Redact).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
    }

    private static string Fingerprint(IRepairModule module, RepairPlan plan, IReadOnlyList<string> dependencies, IReadOnlyList<string> conflicts, RepairSimulationDefinition definition)
    {
        var canonical = new { Version = CurrentVersion, module.Id, module.DisplayName, module.Description, Policy = module.Policy, Plan = new { plan.Description, Resources = plan.Resources.Select(x => new { x.Kind, Path = Redact(x.Path) }).OrderBy(x => x.Kind).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase) }, Dependencies = dependencies.Order(StringComparer.OrdinalIgnoreCase), Conflicts = conflicts.Order(StringComparer.OrdinalIgnoreCase), definition.Effects, definition.Assumptions, definition.Warnings, DurationTicks = definition.EstimatedDuration?.Ticks, definition.EstimatedSpaceBytes, definition.RestartRequired };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions))));
    }
    private static string FormatAction(RepairPredictedEffect effect) => $"{effect.Kind}: {effect.Target} ({effect.Certainty})";
    private static string Redact(string value) { if (string.IsNullOrWhiteSpace(value)) return value; var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); return string.IsNullOrWhiteSpace(profile) ? value : value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase); }
}