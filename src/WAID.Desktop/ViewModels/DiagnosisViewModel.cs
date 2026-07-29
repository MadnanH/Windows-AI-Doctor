using System.Windows.Input;
using WAID.Application.Abstractions;
using WAID.Diagnosis;

namespace WAID.Desktop.ViewModels;

public sealed class DiagnosisViewModel : ViewModelBase
{
    private readonly DiagnosisEngine _engine;
    private readonly IScanRepository _repository;
    private readonly IDiagnosisRepository _diagnosisRepository;
    private readonly AsyncCommand _analyzeCommand;
    private AIReport? _report;
    private string _status = "Run a system scan, then analyze the evidence offline.";

    public DiagnosisViewModel(DiagnosisEngine engine, IScanRepository repository, IDiagnosisRepository diagnosisRepository)
    {
        _engine = engine;
        _repository = repository;
        _diagnosisRepository = diagnosisRepository;
        _analyzeCommand = new AsyncCommand(AnalyzeAsync);
    }

    public ICommand AnalyzeCommand => _analyzeCommand;
    public AIReport? Report { get => _report; private set { if(Set(ref _report,value)) Notify(nameof(RecommendedRepairs)); } }
    public IReadOnlyCollection<RootCause> RecommendedRepairs => Report?.RootCauses.Where(cause => !string.IsNullOrWhiteSpace(cause.Recommendation.RepairId)).ToArray() ?? [];
    public string Status { get => _status; private set => Set(ref _status, value); }

    private async Task AnalyzeAsync()
    {
        try
        {
            Status = "Correlating diagnostic evidence...";
            var sessions = await _repository.GetRecentAsync(1, CancellationToken.None);
            if (sessions.Count == 0)
            {
                Status = "No completed scan is available. Run a system scan first.";
                return;
            }
            var previous = await _diagnosisRepository.GetLatestAsync(CancellationToken.None);
            Report = await _engine.DiagnoseAsync(sessions[0].Findings, previous, CancellationToken.None);
            await _diagnosisRepository.SaveAsync(sessions[0].Id, Report, CancellationToken.None);
            Status = Report.Summary;
        }
        catch (Exception exception)
        {
            Status = $"Diagnosis could not complete: {exception.Message}";
        }
    }

    public async Task LoadLatestAsync()
    {
        if (Report is not null) return;
        try
        {
            Report = await _diagnosisRepository.GetLatestAsync(CancellationToken.None);
            if (Report is not null) Status = Report.Summary;
        }
        catch (Exception exception) { Status = $"Saved diagnosis could not be loaded: {exception.Message}"; }
    }
}
