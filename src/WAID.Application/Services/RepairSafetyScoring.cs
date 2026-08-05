using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public enum RepairApprovalType { Explicit = 0, ExplicitWithRiskAcknowledgement = 1, ExplicitHighRisk = 2 }
public enum RepairPolicyResult { Allowed, AllowedWithRestrictions, Blocked }
public enum RepairSafetyFactorKind { Impact, Reversibility, Privilege, Downtime, DataRisk, Uncertainty, ValidationStrength, PolicyRequirements }
public sealed record RepairSafetyFactor(RepairSafetyFactorKind Kind, int RawRisk, int WeightPercent, int WeightedRisk, string Explanation);
public sealed record RepairSafetyAssessment(string ScoringVersion, string PolicyVersion, int Score, SafetyLevel RiskLevel, RepairApprovalType RequiredApproval, RepairPolicyResult PolicyResult, IReadOnlyList<RepairSafetyFactor> Factors, IReadOnlyList<string> PolicyRequirements, IReadOnlyList<string> Exceptions, string Explanation);
public sealed record RepairSafetyPolicy(string Version, int MaximumScore, SafetyLevel MaximumSafetyLevel, RepairApprovalType MinimumApproval, IReadOnlySet<string> BlockedRepairIds, bool RequireRollbackSupportForHighRisk)
{
    public static RepairSafetyPolicy Default { get; } = new("repair-policy-1.0", 100, SafetyLevel.Critical, RepairApprovalType.Explicit, new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
    public RepairSafetyPolicy Validate() { if (string.IsNullOrWhiteSpace(Version) || Version.Length > 100) throw new InvalidOperationException("A valid repair policy version is required."); if (MaximumScore is < 0 or > 100) throw new InvalidOperationException("Maximum repair score must be between 0 and 100."); ArgumentNullException.ThrowIfNull(BlockedRepairIds); return this; }
}
public interface IRepairSafetyScorer { string Version { get; } RepairSafetyAssessment Evaluate(string repairId, RepairSimulation simulation, RepairSafetyPolicy policy); }

public sealed class DeterministicRepairSafetyScorer : IRepairSafetyScorer
{
    public const string CurrentVersion = "repair-safety-score-1.0";
    public string Version => CurrentVersion;
    public RepairSafetyAssessment Evaluate(string repairId, RepairSimulation simulation, RepairSafetyPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repairId); ArgumentNullException.ThrowIfNull(simulation); policy.Validate();
        var effects = simulation.Effects ?? [];
        var unknown = effects.Count == 0 ? 100 : (int)Math.Round(effects.Count(x => x.Certainty == RepairEffectCertainty.Unknown) * 100d / effects.Count);
        var destructive = effects.Count(x => x.Kind is RepairEffectKind.File or RepairEffectKind.Registry or RepairEffectKind.Service or RepairEffectKind.ScheduledTask or RepairEffectKind.Policy);
        var impact = Math.Clamp((int)simulation.Risk * 25 + destructive * 10, 0, 100);
        var reversibility = simulation.SupportsRollback && simulation.RequiresBackup ? 10 : simulation.SupportsRollback ? 35 : 100;
        var privilege = simulation.RequiresAdministrator ? 80 : 10;
        var downtime = simulation.PredictedRestartRequired == true ? 90 : simulation.EstimatedDuration is null ? 60 : Math.Clamp((int)simulation.EstimatedDuration.Value.TotalMinutes * 3, 5, 80);
        var dataRisk = Math.Clamp(effects.Count(x => x.Kind is RepairEffectKind.File or RepairEffectKind.Registry or RepairEffectKind.Policy) * 20, 0, 100);
        var validationRisk = simulation.SupportsRollback && simulation.RequiresBackup ? 20 : 70;
        var factors = new[] { Factor(RepairSafetyFactorKind.Impact, impact, 25, "Scope and declared safety level of predicted changes."), Factor(RepairSafetyFactorKind.Reversibility, reversibility, 15, "Declared backup and rollback capability; validation still occurs at execution."), Factor(RepairSafetyFactorKind.Privilege, privilege, 10, "Administrator access increases operating-system impact."), Factor(RepairSafetyFactorKind.Downtime, downtime, 10, "Estimated duration and restart requirement."), Factor(RepairSafetyFactorKind.DataRisk, dataRisk, 15, "File, registry, and policy targets that may change persistent state."), Factor(RepairSafetyFactorKind.Uncertainty, unknown, 15, "Share of predicted effects whose result is unknown until execution."), Factor(RepairSafetyFactorKind.ValidationStrength, validationRisk, 10, "Strength of declared rollback and post-execution validation support.") }.ToList();
        var score = Math.Clamp(factors.Sum(x => x.WeightedRisk), 0, 100);
        var baseline = score >= 70 || simulation.Risk >= SafetyLevel.High ? RepairApprovalType.ExplicitHighRisk : score >= 35 || simulation.Risk >= SafetyLevel.Moderate ? RepairApprovalType.ExplicitWithRiskAcknowledgement : RepairApprovalType.Explicit;
        var required = (RepairApprovalType)Math.Max((int)baseline, (int)policy.MinimumApproval);
        var requirements = new List<string>(); var exceptions = new List<string>(); var blocked = false;
        if (policy.BlockedRepairIds.Contains(repairId)) { blocked = true; requirements.Add("Repair identifier is blocked by organizational policy."); }
        if (score > policy.MaximumScore) { blocked = true; requirements.Add($"Safety score {score} exceeds policy maximum {policy.MaximumScore}."); }
        if (simulation.Risk > policy.MaximumSafetyLevel) { blocked = true; requirements.Add($"Declared safety level {simulation.Risk} exceeds policy maximum {policy.MaximumSafetyLevel}."); }
        if (policy.RequireRollbackSupportForHighRisk && (score >= 70 || simulation.Risk >= SafetyLevel.High) && !simulation.SupportsRollback) { blocked = true; requirements.Add("Policy requires rollback support for high-risk repairs."); }
        if (required > baseline) requirements.Add($"Policy raises confirmation from {baseline} to {required}.");
        if (unknown > 0) exceptions.Add($"{unknown}% of effects remain unknown until execution; this increases rather than reduces risk.");
        factors.Add(Factor(RepairSafetyFactorKind.PolicyRequirements, blocked ? 100 : requirements.Count > 0 ? 50 : 0, 0, "Policy requirements are restrictive gates and never subtract from the engineering risk score."));
        var result = blocked ? RepairPolicyResult.Blocked : requirements.Count > 0 ? RepairPolicyResult.AllowedWithRestrictions : RepairPolicyResult.Allowed;
        var explanation = blocked ? $"Blocked by {policy.Version}. Deterministic score {score}/100 requires {required}." : $"Deterministic score {score}/100 requires {required}; policy {policy.Version} cannot weaken this gate.";
        return new(CurrentVersion, policy.Version, score, simulation.Risk, required, result, factors, requirements, exceptions, explanation);
    }
    private static RepairSafetyFactor Factor(RepairSafetyFactorKind kind, int raw, int weight, string explanation) => new(kind, raw, weight, (int)Math.Round(raw * weight / 100d), explanation);
}