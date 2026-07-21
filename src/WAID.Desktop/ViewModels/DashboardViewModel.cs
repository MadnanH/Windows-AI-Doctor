using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows.Input;
using WAID.Application.Services;
using WAID.Domain.Diagnostics;
namespace WAID.Desktop.ViewModels;
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ScanOrchestrator _orchestrator; private string _status="Ready to check your PC"; private double _progress; private bool _isScanning;
    public DashboardViewModel(ScanOrchestrator orchestrator) { _orchestrator=orchestrator; ScanCommand=new AsyncCommand(RunScanAsync,()=>!IsScanning); }
    public ObservableCollection<DiagnosticFinding> Findings { get; }=[]; public ICommand ScanCommand { get; }
    public string Status { get=>_status; private set=>Set(ref _status,value); } public double Progress { get=>_progress; private set=>Set(ref _progress,value); }
    public bool IsScanning { get=>_isScanning; private set=>Set(ref _isScanning,value); }
    private async Task RunScanAsync() { IsScanning=true; Findings.Clear(); try { var progress=new Progress<ScanProgress>(x=>{Status=x.CurrentScanner;Progress=x.Percentage;}); using var identity=WindowsIdentity.GetCurrent(); var admin=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); var session=await _orchestrator.RunAsync(admin,progress,CancellationToken.None); foreach(var finding in session.Findings) Findings.Add(finding); Status=session.Findings.Count==0?"Your PC looks healthy":$"Found {session.Findings.Count} item(s) to review"; Progress=100; } catch(Exception ex){Status=$"Scan stopped: {ex.Message}";} finally{IsScanning=false;} }
}
