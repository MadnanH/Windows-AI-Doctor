using WAID.Application.Services;
using WAID.Health;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class PredictiveHealthPersistenceTests
{
    [Fact]
    public async Task Predictive_report_round_trip_preserves_model_features_and_uncertainty()
    {
        var path=Path.Combine(Path.GetTempPath(),$"waid-predict-{Guid.NewGuid():N}.db");
        try
        {
            var database=new WaidDatabase($"Data Source={path};Pooling=False"); await database.InitializeAsync(CancellationToken.None);
            var prediction=new TransparentTrendPredictor().Predict(PredictiveRiskKind.StorageWear,Enumerable.Range(0,7).Select(index=>new PredictiveObservation(DateTimeOffset.UtcNow.AddDays(index-7),10+index,$"snapshot:{index}")).ToArray(),TimeSpan.FromDays(14));
            var report=new PredictiveHealthReport(Guid.NewGuid(),DateTimeOffset.UtcNow,TransparentTrendPredictor.Version,[prediction]);
            var repository=new SqlitePredictiveHealthRepository(database); await repository.SaveAsync(report,CancellationToken.None); var loaded=await repository.GetLatestAsync(CancellationToken.None);
            Assert.NotNull(loaded); Assert.Equal(report.Id,loaded.Id); Assert.Equal("features-v1",loaded.Predictions[0].Features[0].FeatureVersion); Assert.Equal(report.Predictions[0].RiskRange,loaded.Predictions[0].RiskRange);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
    }
}
