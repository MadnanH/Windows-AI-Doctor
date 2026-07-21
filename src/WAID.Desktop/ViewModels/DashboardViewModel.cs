using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows.Input;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
namespace WAID.Desktop.ViewModels;
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ScanOrchestrator _orchestrator; private readonly AsyncCommand _scanCommand; private readonly RelayCommand _cancelCommand; private CancellationTokenSource? _scanCancellation; private string _status="Ready to check your PC"; private double _progress; private bool _isScanning;
    public DashboardViewModel(ScanOrchestrator orchestrator) { _orchestrator=orchestrator; _scanCommand=new AsyncCommand(RunScanAsync,()=>!IsScanning); _cancelCommand=new RelayCommand(CancelScan,()=>IsScanning); }
    public ObservableCollection<DiagnosticFinding> Findings { get; }=[]; public ICommand ScanCommand => _scanCommand; public ICommand CancelCommand => _cancelCommand;
    public string Status { get=>_status; private set=>Set(ref _status,value); } public double Progress { get=>_progress; private set=>Set(ref _progress,value); }
    public bool IsScanning { get=>_isScanning; private set { if(Set(ref _isScanning,value)){_scanCommand.NotifyCanExecuteChanged();_cancelCommand.NotifyCanExecuteChanged();} } }
    private async Task RunScanAsync() { _scanCancellation?.Dispose(); _scanCancellation=new(); IsScanning=true; Findings.Clear(); Progress=0; try { var progress=new Progress<ScanProgress>(x=>{Status=x.CurrentScanner;Progress=x.Percentage;}); using var identity=WindowsIdentity.GetCurrent(); var admin=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); var session=await _orchestrator.RunAsync(admin,progress,_scanCancellation.Token); foreach(var finding in session.Findings) Findings.Add(finding); Status=session.Findings.Count==0?"Your PC looks healthy":$"Found {session.Findings.Count} item(s) to review"; Progress=100; } catch(OperationCanceledException){Status="Scan cancelled";} catch(Exception ex){Status=$"Scan stopped: {ex.Message}";} finally{IsScanning=false;} }
    private void CancelScan()=>_scanCancellation?.Cancel();
}
