using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Abstractions;
using WAID.Infrastructure.Diagnostics;

namespace WAID.Desktop.ViewModels;

public sealed class DriverHealthViewModel(IDriverConflictAnalyzer analyzer, IDriverHealthRepository repository) : ViewModelBase
{
    private readonly AsyncCommand _analyzeCommand = new(() => Task.CompletedTask);
    private string _status = "Select Analyze drivers to collect current Windows evidence.";
    private string _filter = "All";
    private bool _busy;
    private bool _initialized;
    private DriverHealthReport? _report;

    public ObservableCollection<DriverHealthFinding> Findings { get; }=[];
    public ObservableCollection<DriverInventoryItem> Devices { get; }=[];
    public ObservableCollection<DriverChange> History { get; }=[];
    public IReadOnlyList<string> Filters { get; }=["All","Critical","Warning","Information"];
    public ICommand AnalyzeCommand => _analyzeCommand;
    public string Status { get=>_status; private set=>Set(ref _status,value); }
    public string Filter { get=>_filter; set { if(Set(ref _filter,value)) ApplyFilter(); } }
    public bool IsBusy { get=>_busy; private set=>Set(ref _busy,value); }

    public async Task InitializeAsync()
    {
        if(_initialized)return; _initialized=true; _analyzeCommand.SetExecute(AnalyzeAsync);
        _report=await repository.GetLatestAsync(CancellationToken.None);
        if(_report is not null){ Populate(); Status=$"Loaded driver analysis from {_report.GeneratedAtUtc.LocalDateTime:g}."; }
    }

    private async Task AnalyzeAsync()
    {
        IsBusy=true; Status="Collecting device, signature, and Windows event evidence…";
        try { _report=await analyzer.AnalyzeAsync(CancellationToken.None); Populate(); Status=$"Analyzed {_report.Inventory.Count} drivers; {_report.Findings.Count} conservative finding(s). No drivers were changed or downloaded."; }
        catch(DriverDiagnosticsException exception){ Status=$"Driver analysis unavailable ({exception.Code}): {exception.Message}"; }
        catch(Exception exception){ Status=$"Driver analysis could not complete: {exception.Message}"; }
        finally { IsBusy=false; }
    }

    private void Populate(){ Devices.Clear(); History.Clear(); foreach(var item in _report!.Inventory.OrderBy(x=>x.DeviceClass).ThenBy(x=>x.DeviceName)) Devices.Add(item); foreach(var item in _report.Changes.OrderByDescending(x=>x.DetectedAtUtc)) History.Add(item); ApplyFilter(); }
    private void ApplyFilter(){ Findings.Clear(); if(_report is null)return; foreach(var item in _report.Findings.Where(x=>Filter=="All"||x.Severity==Filter).OrderByDescending(x=>SeverityRank(x.Severity)).ThenByDescending(x=>x.Confidence)) Findings.Add(item); }
    private static int SeverityRank(string value)=>value switch{"Critical"=>3,"Warning"=>2,_=>1};
}
