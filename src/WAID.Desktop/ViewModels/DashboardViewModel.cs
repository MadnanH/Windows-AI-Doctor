using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows.Input;
using WAID.Application.Services;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;
namespace WAID.Desktop.ViewModels;
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ScanCoordinator _scans; private readonly IDiagnosisRepository _diagnoses; private readonly RepairQueue _repairQueue; private readonly IDiagnosticsExportService _exportService; private readonly AsyncCommand _scanCommand; private readonly AsyncCommand _exportCommand; private readonly RelayCommand _cancelCommand; private CancellationTokenSource? _scanCancellation; private string _status="Ready to check your PC"; private double _progress; private bool _isScanning; private bool _isRepairing; private bool _partial;
    public DashboardViewModel(ScanCoordinator scans,RepairRegistry repairRegistry,RepairQueue repairQueue,IDiagnosticsExportService exportService,IDiagnosisRepository diagnoses) { _scans=scans; _diagnoses=diagnoses; _repairQueue=repairQueue; _exportService=exportService; _scanCommand=new AsyncCommand(RunScanAsync,()=>!IsScanning&&!IsRepairing); _exportCommand=new AsyncCommand(ExportAsync,()=>!IsScanning&&!IsRepairing); _cancelCommand=new RelayCommand(CancelScan,()=>IsScanning); AvailableRepairs=repairRegistry.All.OrderBy(module=>module.DisplayName).Select(module=>new RepairOption(module.Id,module.DisplayName,module.Description,module.Policy.SafetyLevel)).ToArray(); }
    public ObservableCollection<DiagnosticFinding> Findings { get; }=[]; public ObservableCollection<WAID.Diagnosis.DiagnosisExplanation> Explanations { get; }=[]; public ObservableCollection<ScannerStatusItem> ScannerPlan { get; }=[]; public ICommand ScanCommand => _scanCommand; public ICommand CancelCommand => _cancelCommand; public ICommand ExportCommand => _exportCommand;
    public IReadOnlyCollection<RepairOption> AvailableRepairs { get; }
    public string Status { get=>_status; private set=>Set(ref _status,value); } public double Progress { get=>_progress; private set=>Set(ref _progress,value); }
    public bool IsScanning { get=>_isScanning; private set { if(Set(ref _isScanning,value)){_scanCommand.NotifyCanExecuteChanged();_exportCommand.NotifyCanExecuteChanged();_cancelCommand.NotifyCanExecuteChanged();} } }
    public bool IsRepairing { get=>_isRepairing; private set { if(Set(ref _isRepairing,value)){_scanCommand.NotifyCanExecuteChanged();_exportCommand.NotifyCanExecuteChanged();} } }
    public async Task LoadExplanationsAsync() { var report=await _diagnoses.GetLatestAsync(CancellationToken.None); Explanations.Clear(); if(report is not null) foreach(var explanation in report.Explanations) Explanations.Add(explanation); }
    private async Task RunScanAsync() { _scanCancellation?.Dispose(); _scanCancellation=new(); IsScanning=true; Findings.Clear(); ScannerPlan.Clear(); _partial=false; Progress=0; try { var progress=new Progress<ScanProgress>(UpdateProgress); using var identity=WindowsIdentity.GetCurrent(); var admin=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); var session=await _scans.TryRunAsync(admin,progress,_scanCancellation.Token); if(session is null){Status="A scan is already running";return;} foreach(var finding in session.Findings) Findings.Add(finding); Status=_partial?$"Scan completed partially. Found {session.Findings.Count} item(s); review skipped or failed checks.":session.Findings.Count==0?"Your PC looks healthy":$"Found {session.Findings.Count} item(s) to review"; Progress=100; } catch(OperationCanceledException){Status="Scan cancelled. Completed scanner results were saved; unfinished checks are marked cancelled.";} catch(Exception ex){Status=$"Scan stopped safely ({ex.GetType().Name}). Review Logs & Audit.";} finally{IsScanning=false;} }
    private void UpdateProgress(ScanProgress progress)
    {
        Status=progress.Detail??progress.CurrentScanner;Progress=progress.Percentage;
        if(progress.Status is ScannerExecutionStatus.Failed or ScannerExecutionStatus.TimedOut or ScannerExecutionStatus.PermissionDenied or ScannerExecutionStatus.Skipped)_partial=true;
        if(progress.ScannerId=="scan")return;var item=new ScannerStatusItem(progress.ScannerId,progress.CurrentScanner,progress.Status?.ToString()??"Planned",progress.Detail??string.Empty,progress.ScannerPercentage);
        var index=-1;for(var i=0;i<ScannerPlan.Count;i++)if(ScannerPlan[i].Id.Equals(progress.ScannerId,StringComparison.OrdinalIgnoreCase)){index=i;break;}if(index<0)ScannerPlan.Add(item);else ScannerPlan[index]=item;
    }
    private void CancelScan()=>_scanCancellation?.Cancel();
    private async Task ExportAsync()
    {
        try { Status=$"Diagnostics exported to {await _exportService.ExportAsync(CancellationToken.None)}"; }
        catch(Exception ex) { Status=$"Diagnostics export failed: {ex.Message}"; }
    }
    public async Task RunRepairAsync(string repairId,bool userConfirmed,bool riskAcknowledged=false)
    {
        if(IsRepairing) return; IsRepairing=true;
        try { var orchestration=await _repairQueue.EnqueueAsync(repairId,null,userConfirmed,riskAcknowledged,CancellationToken.None); Status=orchestration.Outcome; }
        catch(Exception ex){Status=$"Repair stopped: {ex.Message}";}
        finally{IsRepairing=false;}
    }
}
public sealed record RepairOption(string Id,string DisplayName,string Description,SafetyLevel SafetyLevel);
public sealed record ScannerStatusItem(string Id,string Name,string Status,string Detail,double Percentage);
