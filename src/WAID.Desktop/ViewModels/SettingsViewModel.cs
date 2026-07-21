using System.Windows.Input;
using WAID.Application.Abstractions;
using WAID.Domain.Settings;
namespace WAID.Desktop.ViewModels;
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsRepository _repository; private bool _startup,_ai,_telemetry; private string _theme="System",_status="";
    public SettingsViewModel(ISettingsRepository repository) { _repository=repository; SaveCommand=new AsyncCommand(SaveAsync); _=LoadAsync(); }
    public bool RunScansAtStartup{get=>_startup;set=>Set(ref _startup,value);} public bool EnableAiAnalysis{get=>_ai;set=>Set(ref _ai,value);} public bool AllowTelemetry{get=>_telemetry;set=>Set(ref _telemetry,value);} public string Theme{get=>_theme;set=>Set(ref _theme,value);} public string Status{get=>_status;private set=>Set(ref _status,value);} public ICommand SaveCommand{get;}
    private async Task LoadAsync(){var s=await _repository.GetAsync(CancellationToken.None);RunScansAtStartup=s.RunScansAtStartup;EnableAiAnalysis=s.EnableAiAnalysis;AllowTelemetry=s.AllowTelemetry;Theme=s.Theme;}
    private async Task SaveAsync(){await _repository.SaveAsync(new ApplicationSettings{RunScansAtStartup=RunScansAtStartup,EnableAiAnalysis=EnableAiAnalysis,AllowTelemetry=AllowTelemetry,Theme=Theme},CancellationToken.None);Status="Settings saved";}
}
