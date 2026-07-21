using System.Collections.ObjectModel;using System.Windows.Input;using WAID.Application.Abstractions;using WAID.Infrastructure.Diagnostics;
namespace WAID.Desktop.ViewModels;
public sealed class BootHealthViewModel:ViewModelBase
{
    private readonly IStartupBootAnalyzer _analyzer;private readonly IBootHealthRepository _repository;private readonly AsyncCommand _analyze;private bool _initialized;
    public BootHealthViewModel(IStartupBootAnalyzer analyzer,IBootHealthRepository repository){_analyzer=analyzer;_repository=repository;_analyze=new(AnalyzeAsync);}
    public ObservableCollection<StartupRecommendation> Recommendations{get;}=[];public ObservableCollection<StartupEntry> Entries{get;}=[];public ObservableCollection<BootMeasurement> Boots{get;}=[];public ObservableCollection<StartupChange> History{get;}=[];public ICommand AnalyzeCommand=>_analyze;public string Status{get;private set;}="Select Analyze startup to collect current evidence.";
    public async Task InitializeAsync(){if(_initialized)return;_initialized=true;var report=await _repository.GetLatestAsync(CancellationToken.None);if(report is not null){Populate(report);Status=$"Loaded boot analysis from {report.GeneratedAtUtc.LocalDateTime:g}.";Changed(nameof(Status));}}
    private async Task AnalyzeAsync(){Status="Collecting startup sources and boot evidence…";Changed(nameof(Status));try{var report=await _analyzer.AnalyzeAsync(CancellationToken.None);Populate(report);Status=$"Analyzed {report.Entries.Count} startup entries and {report.BootMeasurements.Count} boot measurement(s). No entries were changed.";}catch(StartupDiagnosticsException e){Status=$"Boot analysis unavailable ({e.Code}): {e.Message}";}catch(Exception e){Status=$"Boot analysis could not complete: {e.Message}";}Changed(nameof(Status));}
    private void Populate(BootHealthReport report){Entries.Clear();Recommendations.Clear();Boots.Clear();History.Clear();foreach(var x in report.Entries.OrderBy(x=>x.Source).ThenBy(x=>x.Name))Entries.Add(x);foreach(var x in report.Recommendations.OrderByDescending(x=>x.Impact))Recommendations.Add(x);foreach(var x in report.BootMeasurements.OrderByDescending(x=>x.BootedAtUtc))Boots.Add(x);foreach(var x in report.Changes.OrderByDescending(x=>x.DetectedAtUtc))History.Add(x);}
    private void Changed(string name)=>Notify(name);
}
