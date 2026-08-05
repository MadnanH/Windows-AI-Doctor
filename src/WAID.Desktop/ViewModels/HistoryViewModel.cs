using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Repairs;
using WAID.Diagnosis;

namespace WAID.Desktop.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
 private readonly IScanRepository _scans;private readonly IRepairHistoryRepository _repairs;private readonly IDiagnosisRepository _diagnoses;private readonly IRepairOutcomeRepository _outcomes;private readonly IRepairOutcomeRecorder _recorder;private readonly IRepairOutcomeExportService _exporter;private string _status="Select Refresh to load saved activity.";private string _repairFilter="";private string _outcomeFilter="All";
 public HistoryViewModel(IScanRepository scans,IRepairHistoryRepository repairs,IDiagnosisRepository diagnoses,IRepairOutcomeRepository outcomes,IRepairOutcomeRecorder recorder,IRepairOutcomeExportService exporter){_scans=scans;_repairs=repairs;_diagnoses=diagnoses;_outcomes=outcomes;_recorder=recorder;_exporter=exporter;RefreshCommand=new AsyncCommand(RefreshAsync);ExportCommand=new AsyncCommand(ExportAsync);}
 public ObservableCollection<ScanHistoryItem> Scans{get;}=[];public ObservableCollection<RepairHistoryEntry> Repairs{get;}=[];public ObservableCollection<DiagnosisExplanation> Explanations{get;}=[];public ObservableCollection<RepairAuditEntry> RepairAudit{get;}=[];public ObservableCollection<RepairOutcomeAggregate> OutcomeAggregates{get;}=[];
 public IReadOnlyList<string> OutcomeFilters{get;}=["All",..Enum.GetNames<RepairOutcomeClass>()];public ICommand RefreshCommand{get;}public ICommand ExportCommand{get;}public string RepairFilter{get=>_repairFilter;set=>Set(ref _repairFilter,value);}public string OutcomeFilter{get=>_outcomeFilter;set=>Set(ref _outcomeFilter,value);}public string Status{get=>_status;private set=>Set(ref _status,value);}
 private RepairAuditQuery Query(){RepairOutcomeClass? outcome=Enum.TryParse<RepairOutcomeClass>(OutcomeFilter,out var parsed)&&OutcomeFilter!="All"?parsed:null;return new(string.IsNullOrWhiteSpace(RepairFilter)?null:RepairFilter.Trim(),Outcome:outcome,Limit:500);}
 private async Task RefreshAsync(){try{var scans=await _scans.GetRecentAsync(25,CancellationToken.None);var repairs=await _repairs.GetRecentAsync(100,CancellationToken.None);var diagnosis=await _diagnoses.GetLatestAsync(CancellationToken.None);var audit=await _outcomes.QueryAsync(Query(),CancellationToken.None);var aggregates=await _recorder.RebuildAggregatesAsync(CancellationToken.None);Scans.Clear();foreach(var scan in scans)Scans.Add(new(scan.Id,scan.StartedAtUtc,scan.CompletedAtUtc,scan.Findings.Count));Repairs.Clear();foreach(var repair in repairs)Repairs.Add(repair);Explanations.Clear();if(diagnosis is not null)foreach(var explanation in diagnosis.Explanations)Explanations.Add(explanation);RepairAudit.Clear();foreach(var entry in audit)RepairAudit.Add(entry);OutcomeAggregates.Clear();foreach(var item in aggregates)OutcomeAggregates.Add(item);Status=$"Loaded {Scans.Count} scan(s), {Repairs.Count} transaction(s), and {RepairAudit.Count} immutable repair audit event(s).";}catch(Exception exception){Status=$"History could not be loaded ({exception.GetType().Name}). Check database health and Logs & Audit.";}}
 private async Task ExportAsync(){try{var path=await _exporter.ExportAsync(Query(),CancellationToken.None);Status=$"Redacted repair audit exported to {path}";}catch(Exception exception){Status=$"Repair audit export failed safely ({exception.GetType().Name}). Check Logs & Audit.";}}
}
public sealed record ScanHistoryItem(Guid Id,DateTimeOffset StartedAtUtc,DateTimeOffset? CompletedAtUtc,int FindingCount);
