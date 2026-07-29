using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Diagnosis;

namespace WAID.Desktop.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    private readonly IScanRepository _scans;
    private readonly IRepairHistoryRepository _repairs;
    private readonly IDiagnosisRepository _diagnoses;
    private readonly AsyncCommand _refreshCommand;
    private string _status = "Select Refresh to load saved activity.";

    public HistoryViewModel(IScanRepository scans, IRepairHistoryRepository repairs, IDiagnosisRepository diagnoses)
    {
        _scans = scans;
        _repairs = repairs;
        _diagnoses = diagnoses;
        _refreshCommand = new AsyncCommand(RefreshAsync);
    }

    public ObservableCollection<ScanHistoryItem> Scans { get; } = [];
    public ObservableCollection<RepairHistoryEntry> Repairs { get; } = [];
    public ObservableCollection<DiagnosisExplanation> Explanations { get; } = [];
    public ICommand RefreshCommand => _refreshCommand;
    public string Status { get => _status; private set => Set(ref _status, value); }

    private async Task RefreshAsync()
    {
        try
        {
            var scans = await _scans.GetRecentAsync(25, CancellationToken.None);
            var repairs = await _repairs.GetRecentAsync(25, CancellationToken.None);
            var diagnosis = await _diagnoses.GetLatestAsync(CancellationToken.None);
            Scans.Clear();
            foreach (var scan in scans) Scans.Add(new(scan.Id, scan.StartedAtUtc, scan.CompletedAtUtc, scan.Findings.Count));
            Repairs.Clear();
            foreach (var repair in repairs) Repairs.Add(repair);
            Explanations.Clear();
            if (diagnosis is not null)
                foreach (var explanation in diagnosis.Explanations) Explanations.Add(explanation);
            Status = $"Loaded {Scans.Count} scan(s) and {Repairs.Count} repair transaction(s).";
        }
        catch (Exception exception) { Status = $"History could not be loaded: {exception.Message}"; }
    }
}

public sealed record ScanHistoryItem(Guid Id, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, int FindingCount);
