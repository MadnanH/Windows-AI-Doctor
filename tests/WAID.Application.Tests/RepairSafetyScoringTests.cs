using WAID.Application.Services;
using WAID.Domain.Repairs;

namespace WAID.Application.Tests;

public sealed class RepairSafetyScoringTests
{
    private readonly DeterministicRepairSafetyScorer _scorer = new();

    [Fact]
    public void Identical_inputs_produce_identical_explainable_score()
    {
        var simulation = Simulation(SafetyLevel.Moderate, supportsRollback: true);
        var first = _scorer.Evaluate("waid.test", simulation, RepairSafetyPolicy.Default);
        var second = _scorer.Evaluate("waid.test", simulation, RepairSafetyPolicy.Default);
        Assert.Equal(first.Score, second.Score); Assert.Equal(first.RequiredApproval, second.RequiredApproval); Assert.Equal(first.Factors, second.Factors); Assert.Equal(first.Score, first.Factors.Sum(x => x.WeightedRisk)); Assert.All(first.Factors, x => Assert.False(string.IsNullOrWhiteSpace(x.Explanation)));
    }

    [Fact]
    public void Maximum_score_boundary_is_inclusive_and_one_point_lower_blocks()
    {
        var simulation = Simulation(SafetyLevel.Moderate, supportsRollback: true); var score = _scorer.Evaluate("waid.test", simulation, RepairSafetyPolicy.Default).Score;
        var allowed = _scorer.Evaluate("waid.test", simulation, Policy(maximumScore: score));
        var blocked = _scorer.Evaluate("waid.test", simulation, Policy(maximumScore: score - 1));
        Assert.NotEqual(RepairPolicyResult.Blocked, allowed.PolicyResult); Assert.Equal(RepairPolicyResult.Blocked, blocked.PolicyResult);
    }

    [Fact]
    public void Restrictive_policy_confirmation_takes_precedence()
    {
        var result = _scorer.Evaluate("waid.test", Simulation(SafetyLevel.Low, supportsRollback: true), Policy(minimum: RepairApprovalType.ExplicitHighRisk));
        Assert.Equal(RepairApprovalType.ExplicitHighRisk, result.RequiredApproval); Assert.Equal(RepairPolicyResult.AllowedWithRestrictions, result.PolicyResult); Assert.Contains(result.PolicyRequirements, x => x.Contains("raises confirmation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Policy_cannot_weaken_high_risk_baseline()
    {
        var result = _scorer.Evaluate("waid.test", Simulation(SafetyLevel.High, supportsRollback: false), Policy(minimum: RepairApprovalType.Explicit));
        Assert.Equal(RepairApprovalType.ExplicitHighRisk, result.RequiredApproval); Assert.True(result.Score >= 0); Assert.Contains("cannot weaken", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocked_identifier_and_required_reversibility_fail_closed()
    {
        var policy = new RepairSafetyPolicy("test-policy", 100, SafetyLevel.Critical, RepairApprovalType.Explicit, new HashSet<string>(["waid.test"], StringComparer.OrdinalIgnoreCase), true);
        var result = _scorer.Evaluate("waid.test", Simulation(SafetyLevel.High, supportsRollback: false), policy);
        Assert.Equal(RepairPolicyResult.Blocked, result.PolicyResult); Assert.Contains(result.PolicyRequirements, x => x.Contains("blocked", StringComparison.OrdinalIgnoreCase)); Assert.Contains(result.PolicyRequirements, x => x.Contains("rollback", StringComparison.OrdinalIgnoreCase));
    }

    private static RepairSafetyPolicy Policy(int maximumScore = 100, RepairApprovalType minimum = RepairApprovalType.Explicit) => new("test-policy", maximumScore, SafetyLevel.Critical, minimum, new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
    private static RepairSimulation Simulation(SafetyLevel risk, bool supportsRollback)
    {
        var now = DateTimeOffset.UtcNow;
        return new("test", ["Registry change"], [new(RepairResourceKind.RegistryKey, @"HKLM\Software\WAID")], risk, true, true, supportsRollback, supportsRollback, [], [], DeterministicRepairSimulationEngine.CurrentVersion, new string('A', 64), now, now.AddMinutes(15), [new(1, RepairEffectKind.Registry, @"HKLM\Software\WAID", "Current", "May change", RepairEffectCertainty.Estimated, "Declared registry target."), new(2, RepairEffectKind.Command, "test", "Not run", "Unknown result", RepairEffectCertainty.Unknown, "Runtime result.")], ["Windows state may change."], ["Review before execution."], TimeSpan.FromMinutes(5), null, true);
    }
}