using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Abstractions;

namespace WAID.Desktop.ViewModels;

public sealed class AuditViewModel(IAuditTrailService auditTrail, ILocalDiagnosticsService diagnostics) : ViewModelBase
{
    private string _searchText = string.Empty;
    private string _status = "Select Refresh to load sanitized local diagnostics.";
    public ObservableCollection<TechnicalLogEntry> Logs { get; } = [];
    public ObservableCollection<AuditRecord> AuditRecords { get; } = [];
    public string SearchText { get => _searchText; set => Set(ref _searchText, value ?? string.Empty); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public ICommand RefreshCommand => new AsyncCommand(RefreshAsync);
    public ICommand ExportCommand => new AsyncCommand(ExportAsync);

    private async Task RefreshAsync()
    {
        try
        {
            var logs = await diagnostics.SearchLogsAsync(new(SearchText, MaximumRecords: 300), CancellationToken.None);
            var audit = await auditTrail.SearchAsync(new(SearchText, MaximumRecords: 300), CancellationToken.None);
            Logs.Clear(); foreach (var item in logs) Logs.Add(item);
            AuditRecords.Clear(); foreach (var item in audit) AuditRecords.Add(item);
            Status = $"Loaded {Logs.Count} technical log event(s) and {AuditRecords.Count} audit event(s).";
        }
        catch (Exception exception) { Status = $"Local diagnostics could not be loaded: {exception.GetType().Name}."; }
    }

    private async Task ExportAsync()
    {
        try { var path = await diagnostics.ExportSanitizedAsync(new(SearchText, MaximumRecords: 1000), new(SearchText, MaximumRecords: 1000), CancellationToken.None); Status = $"Sanitized support logs exported to {path}"; }
        catch (Exception exception) { Status = $"Sanitized export failed: {exception.GetType().Name}."; }
    }
}
