using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.KnowledgeBase;

namespace WAID.Diagnosis.Tests;

public sealed class DiagnosticEngineTests
{
    [Theory]
    [InlineData("ssd-failure")]
    [InlineData("component-store")]
    [InlineData("system-files")]
    [InlineData("update-cache")]
    [InlineData("driver-instability")]
    [InlineData("memory-pressure")]
    [InlineData("defender-disabled")]
    [InlineData("network-stack")]
    [InlineData("dns-cache")]
    [InlineData("service-failure")]
    [InlineData("startup-load")]
    [InlineData("gpu-instability")]
    public void Embedded_knowledge_contains_expected_rule(string id) =>
        Assert.NotNull(new DiagnosticKnowledgeBase().Find(id));

    [Theory]
    [InlineData("waid.hardware", HealthCategory.Hardware)]
    [InlineData("waid.event-viewer", HealthCategory.Windows)]
    [InlineData("waid.drivers", HealthCategory.Drivers)]
    [InlineData("waid.defender", HealthCategory.Security)]
    [InlineData("waid.memory", HealthCategory.Performance)]
    [InlineData("waid.smart", HealthCategory.Storage)]
    [InlineData("waid.network", HealthCategory.Network)]
    public void Health_engine_categorizes_scanner_findings(string scannerId, HealthCategory category) =>
        Assert.Equal(category, HealthScoreEngine.Categorize(Finding(scannerId, "TEST")));

    [Theory]
    [InlineData(DiagnosticSeverity.Information, 97)]
    [InlineData(DiagnosticSeverity.Warning, 88)]
    [InlineData(DiagnosticSeverity.Critical, 70)]
    public void Health_engine_applies_severity_penalty(DiagnosticSeverity severity, int expected)
    {
        var score = new HealthScoreEngine().Calculate([Finding("waid.smart", "TEST", severity)]);
        Assert.Equal(expected, score.Storage);
    }

    [Fact]
    public void Event_41_smart_and_ntfs_correlate_as_storage_failure()
    {
        var correlations = new EventCorrelationEngine().Correlate([
            Finding("events", "EVENT_41"), Finding("smart", "SMART_WARNING"), Finding("events", "NTFS_ERROR")]);
        var correlation = Assert.Single(correlations);
        Assert.Equal("power-storage", correlation.CorrelationId);
        Assert.Equal(94, correlation.Strength);
    }

    [Fact]
    public async Task Diagnosis_correlates_component_store_and_update_failures()
    {
        var report = await CreateEngine().DiagnoseAsync([
            Finding("servicing", "CBS_CORRUPTION", DiagnosticSeverity.Critical),
            Finding("updates", "UPDATE_EVENT_20"), Finding("updates", "UPDATE_EVENT_25")], CancellationToken.None);
        var cause = Assert.Single(report.RootCauses.Where(item => item.Id == "component-store"));
        Assert.True(cause.Confidence >= 97);
        Assert.Equal("waid.dism", cause.Recommendation.RepairId);
        Assert.Contains("component store", cause.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_evidence_produces_healthy_offline_report()
    {
        var report = await CreateEngine().DiagnoseAsync([], CancellationToken.None);
        Assert.Equal(100, report.Health.Overall);
        Assert.Empty(report.RootCauses);
        Assert.Contains("No health issues", report.Summary);
    }

    [Fact]
    public async Task Unknown_findings_remain_visible_without_inventing_a_cause()
    {
        var finding = Finding("custom", "UNKNOWN");
        var report = await CreateEngine().DiagnoseAsync([finding], CancellationToken.None);
        Assert.Empty(report.RootCauses);
        Assert.Contains(finding, report.Findings);
    }

    [Fact]
    public async Task Cancelled_diagnosis_stops_before_analysis()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateEngine().DiagnoseAsync([], cancellation.Token));
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(75, 75)]
    [InlineData(90, 90)]
    [InlineData(98, 98)]
    public void Confidence_engine_honors_base_confidence_without_extra_evidence(int baseConfidence, int expected)
    {
        var rule = new KnowledgeRule("test", "Cause", "Explanation", DiagnosticSeverity.Warning, 1, null, baseConfidence, ["A"], []);
        var match = new RuleMatch(rule, [Finding("test", "A")]);
        Assert.Equal(expected, new ConfidenceEngine().Calculate(match, []));
    }

    private static DiagnosisEngine CreateEngine()
    {
        var confidence = new ConfidenceEngine();
        var recommendations = new RecommendationEngine();
        var explanations = new ExplanationEngine();
        return new(new DiagnosticKnowledgeBase(), new RuleEngine(), new EventCorrelationEngine(),
            new RootCauseAnalyzer(confidence, recommendations, explanations),
            new HealthScoreEngine(), new AIReportBuilder(TimeProvider.System));
    }

    private static DiagnosticFinding Finding(
        string scanner, string code, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        new(scanner, code, code.Replace('_', ' '), $"Evidence for {code}", severity);
}
