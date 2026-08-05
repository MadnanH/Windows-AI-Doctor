using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using WAID.Application.Services;

namespace WAID.Desktop.ViewModels;

public sealed record CaseReviewItem(string Name, string Preview);

public sealed class CaseExchangeViewModel : ViewModelBase
{
    private readonly IRemoteCaseExchangeService _exchange;
    private bool _includeScans=true,_includeDiagnosis=true,_includeTimeline=true,_includeRepairHistory=true,_includeLogs=true,_includeSystemSummary=true,_includeNotes=true,_maximumRedaction;
    private string _password=string.Empty,_notes=string.Empty,_importPath=string.Empty,_status="Choose content and preview redaction before exporting.",_preview="No preview generated.",_reviewBanner="No package is open.";
    public CaseExchangeViewModel(IRemoteCaseExchangeService exchange){_exchange=exchange;PreviewCommand=new RelayCommand(BuildPreview);ExportCommand=new AsyncCommand(ExportAsync);ImportCommand=new AsyncCommand(ImportAsync);}
    public ICommand PreviewCommand{get;} public ICommand ExportCommand{get;} public ICommand ImportCommand{get;}
    public ObservableCollection<CaseReviewItem> ReviewItems{get;}=[];
    public bool IncludeScans{get=>_includeScans;set=>Set(ref _includeScans,value);} public bool IncludeDiagnosis{get=>_includeDiagnosis;set=>Set(ref _includeDiagnosis,value);} public bool IncludeTimeline{get=>_includeTimeline;set=>Set(ref _includeTimeline,value);} public bool IncludeRepairHistory{get=>_includeRepairHistory;set=>Set(ref _includeRepairHistory,value);} public bool IncludeLogs{get=>_includeLogs;set=>Set(ref _includeLogs,value);} public bool IncludeSystemSummary{get=>_includeSystemSummary;set=>Set(ref _includeSystemSummary,value);} public bool IncludeNotes{get=>_includeNotes;set=>Set(ref _includeNotes,value);} public bool MaximumRedaction{get=>_maximumRedaction;set=>Set(ref _maximumRedaction,value);}
    public string Password{private get=>_password;set=>Set(ref _password,value);} public string Notes{get=>_notes;set=>Set(ref _notes,value);} public string ImportPath{get=>_importPath;set=>Set(ref _importPath,value);} public string Status{get=>_status;private set=>Set(ref _status,value);} public string Preview{get=>_preview;private set=>Set(ref _preview,value);} public string ReviewBanner{get=>_reviewBanner;private set=>Set(ref _reviewBanner,value);}
    private CaseExportRequest Request()=>new(Content(),MaximumRedaction?CaseRedactionProfile.Maximum:CaseRedactionProfile.Standard,Password,Notes);
    private CasePackageContent Content(){var value=CasePackageContent.None;if(IncludeScans)value|=CasePackageContent.Scans;if(IncludeDiagnosis)value|=CasePackageContent.Diagnosis;if(IncludeTimeline)value|=CasePackageContent.Timeline;if(IncludeRepairHistory)value|=CasePackageContent.RepairHistory;if(IncludeLogs)value|=CasePackageContent.SanitizedLogs;if(IncludeSystemSummary)value|=CasePackageContent.SystemSummary;if(IncludeNotes)value|=CasePackageContent.Notes;return value;}
    private void BuildPreview(){try{var preview=_exchange.Preview(Request());Preview=$"Included: {string.Join(", ",preview.Included)}\n\nExcluded: {string.Join(", ",preview.Excluded)}\n\n{preview.RedactionSummary}\nEncryption required: {preview.Encrypted}. Import is review-only: {preview.ReviewOnlyImport}.";Status="Preview ready. Confirm the selection and export when ready.";}catch(CaseExchangeException ex){Status=$"{ex.Message} {ex.RecoveryAction}";}}
    private async Task ExportAsync(){try{BuildPreview();var path=await _exchange.ExportAsync(Request(),CancellationToken.None);Password=string.Empty;Status=$"Encrypted case package exported to {path}. Share its password separately.";}catch(CaseExchangeException ex){Status=$"Export blocked [{ex.Code}]: {ex.Message} {ex.RecoveryAction}";}catch(Exception){Status="Export failed safely. Review Logs & Audit and retry.";}}
    private async Task ImportAsync(){try{ReviewItems.Clear();var review=await _exchange.ImportForReviewAsync(ImportPath,Password,CancellationToken.None);foreach(var item in review.Documents.OrderBy(x=>x.Key,StringComparer.Ordinal)){var text=JsonSerializer.Serialize(item.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});ReviewItems.Add(new(item.Key,text.Length<=8000?text:text[..8000]+"\n[Preview truncated]"));}ReviewBanner=review.ReviewBanner;Password=string.Empty;Status=$"Package integrity verified. Loaded {ReviewItems.Count} documents into temporary review memory; nothing was imported into WAID.";}catch(CaseExchangeException ex){ReviewBanner="IMPORT BLOCKED - no package content was loaded.";Status=$"Import blocked [{ex.Code}]: {ex.Message} {ex.RecoveryAction}";}catch(Exception){ReviewBanner="IMPORT BLOCKED - no package content was loaded.";Status="Import failed safely. Review Logs & Audit and retry.";}}
}
