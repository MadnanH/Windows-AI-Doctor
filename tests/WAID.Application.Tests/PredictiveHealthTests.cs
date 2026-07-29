using WAID.Health;

namespace WAID.Application.Tests;

public sealed class PredictiveHealthTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly TransparentTrendPredictor _model = new();

    [Fact]
    public void Sustained_storage_wear_trend_is_explainable_and_versioned()
    {
        var prediction = _model.Predict(PredictiveRiskKind.StorageWear, Series(10, index => 10 + index), TimeSpan.FromDays(14));
        Assert.Equal(PredictionState.EmergingRisk, prediction.State);
        Assert.Equal(TransparentTrendPredictor.Version, prediction.ModelVersion);
        Assert.Contains(prediction.Features, item => item.Name == "slopePerDay" && item.FeatureVersion == "features-v1");
        Assert.NotEmpty(prediction.Evidence);
        Assert.Contains("not a failure prediction", prediction.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_history_never_manufactures_a_prediction()
    {
        var prediction = _model.Predict(PredictiveRiskKind.CrashRate, Series(3, index => index), TimeSpan.FromDays(7));
        Assert.Equal(PredictionState.InsufficientHistory, prediction.State);
        Assert.Equal(PredictionValidationOutcome.NotEnoughData, prediction.ValidationOutcome);
        Assert.Equal(0, prediction.RiskRange.MostLikely);
    }

    [Fact]
    public void Alternating_noise_is_suppressed_to_guard_against_false_positives()
    {
        var prediction = _model.Predict(PredictiveRiskKind.ThermalTrend, Series(10, index => index % 2 == 0 ? 45 : 54), TimeSpan.FromDays(14));
        Assert.Equal(PredictionState.Suppressed, prediction.State);
        Assert.Contains("suppressed", prediction.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Abrupt_regime_drift_is_not_presented_as_a_stable_trend()
    {
        var values = new[] { 1d, 1, 1, 1, 20, 20, 20, 20 };
        var prediction = _model.Predict(PredictiveRiskKind.UpdateFailures, Series(values.Length, index => values[index]), TimeSpan.FromDays(14));
        Assert.Equal(PredictionState.Suppressed, prediction.State);
    }

    [Fact]
    public void Linear_history_passes_holdout_backtesting()
    {
        var prediction = _model.Predict(PredictiveRiskKind.PerformanceDecline, Series(8, index => 2 + index), TimeSpan.FromDays(14));
        Assert.Equal(PredictionValidationOutcome.Passed, prediction.ValidationOutcome);
        Assert.True(prediction.ConfidenceLower <= prediction.RiskRange.MostLikely);
        Assert.True(prediction.ConfidenceUpper >= prediction.RiskRange.MostLikely);
    }

    [Theory]
    [InlineData(PredictiveRiskKind.StorageWear)]
    [InlineData(PredictiveRiskKind.CrashRate)]
    [InlineData(PredictiveRiskKind.ThermalTrend)]
    [InlineData(PredictiveRiskKind.MemoryInstability)]
    [InlineData(PredictiveRiskKind.UpdateFailures)]
    [InlineData(PredictiveRiskKind.PerformanceDecline)]
    public void Every_risk_kind_returns_a_bounded_explainable_result(PredictiveRiskKind kind)
    {
        var prediction = _model.Predict(kind, Series(7, index => index * .75), TimeSpan.FromDays(14));
        Assert.False(string.IsNullOrWhiteSpace(prediction.Explanation));
        Assert.False(string.IsNullOrWhiteSpace(prediction.MonitoringRecommendation));
        Assert.InRange(prediction.RiskRange.Minimum, 0, 100);
        Assert.InRange(prediction.RiskRange.Maximum, 0, 100);
        Assert.Equal(TransparentTrendPredictor.Version, prediction.ModelVersion);
    }

    private static IReadOnlyList<PredictiveObservation> Series(int count, Func<int, double> value) =>
        Enumerable.Range(0, count).Select(index => new PredictiveObservation(Start.AddDays(index), value(index), $"snapshot:{index}")).ToArray();
}
