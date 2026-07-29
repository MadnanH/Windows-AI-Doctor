using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Services;
using WAID.Health;
namespace WAID.Desktop.ViewModels;
public sealed class PredictiveHealthViewModel : ViewModelBase
{
    private readonly PredictiveHealthEngine _engine; private readonly IPredictiveHealthRepository _reports; private readonly IHealthSnapshotRepository _snapshots; private string _status="Load saved health history to evaluate whether a responsible prediction can be made."; private bool _initialized;
    public PredictiveHealthViewModel(PredictiveHealthEngine engine,IPredictiveHealthRepository reports,IHealthSnapshotRepository snapshots){_engine=engine;_reports=reports;_snapshots=snapshots;AnalyzeCommand=new AsyncCommand(AnalyzeAsync);}
    public ObservableCollection<HealthPrediction> Predictions{get;}=[]; public ICommand AnalyzeCommand{get;} public string Status{get=>_status;private set=>Set(ref _status,value);}
    public async Task InitializeAsync(){if(_initialized)return;_initialized=true;var latest=await _reports.GetLatestAsync(CancellationToken.None);if(latest is not null)Show(latest);}
    private async Task AnalyzeAsync(){Status="Analyzing local health history with the transparent statistical model…";var snapshots=await _snapshots.GetRecentAsync(90,CancellationToken.None);Show(await _engine.AnalyzeAsync(snapshots,CancellationToken.None));}
    private void Show(PredictiveHealthReport report){Predictions.Clear();foreach(var prediction in report.Predictions)Predictions.Add(prediction);var emerging=report.Predictions.Count(item=>item.State==PredictionState.EmergingRisk);var insufficient=report.Predictions.Count(item=>item.State==PredictionState.InsufficientHistory);Status=$"Model {report.ModelVersion}: {emerging} emerging risk(s); {insufficient} area(s) need more history. Estimates are not failure guarantees.";}
}
