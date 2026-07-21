using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Abstractions;
using WAID.Infrastructure.Diagnostics;

namespace WAID.Desktop.ViewModels;

public sealed class NetworkHealthViewModel : ViewModelBase
{
    private readonly INetworkDiagnosticCenter _center; private readonly INetworkHealthRepository _repository; private readonly AsyncCommand _run; private readonly AsyncCommand _export; private readonly RelayCommand _cancel;
    private CancellationTokenSource? _cancellation; private bool _initialized; private string _dnsName = ""; private string _httpEndpoint = ""; private string _status = "Run diagnostics to inspect each network layer.";
    public NetworkHealthViewModel(INetworkDiagnosticCenter center, INetworkHealthRepository repository) { _center = center; _repository = repository; _run = new(RunAsync); _export = new(ExportAsync, () => Report is not null); _cancel = new(() => _cancellation?.Cancel(), () => _cancellation is not null); }
    public ObservableCollection<NetworkTestResult> Tests { get; } = []; public ObservableCollection<NetworkFinding> Findings { get; } = []; public ObservableCollection<string> Topology { get; } = [];
    public NetworkHealthReport? Report { get; private set; } public string DnsName { get => _dnsName; set => Set(ref _dnsName, value); } public string HttpEndpoint { get => _httpEndpoint; set => Set(ref _httpEndpoint, value); } public string Status { get => _status; private set => Set(ref _status, value); }
    public ICommand RunCommand => _run; public ICommand CancelCommand => _cancel; public ICommand ExportCommand => _export;
    public async Task InitializeAsync() { if (_initialized) return; _initialized = true; var report = await _repository.GetLatestAsync(CancellationToken.None); if (report is not null) Fill(report); }
    private async Task RunAsync()
    {
        _cancellation = new(); _cancel.NotifyCanExecuteChanged(); Status = "Running bounded network tests...";
        try { var endpoint = string.IsNullOrWhiteSpace(HttpEndpoint) ? null : new Uri(HttpEndpoint, UriKind.Absolute); Fill(await _center.AnalyzeAsync(new(string.IsNullOrWhiteSpace(DnsName) ? null : DnsName, endpoint), _cancellation.Token)); Status = $"Completed {Tests.Count} tests with {Findings.Count} evidence-backed finding(s). No repair was executed."; }
        catch (OperationCanceledException) { Status = "Network diagnostics cancelled. No repair was executed."; }
        catch (Exception exception) when (exception is NetworkDiagnosticException or ArgumentException or UriFormatException) { Status = $"Network diagnostics unavailable: {exception.Message}"; }
        finally { _cancellation.Dispose(); _cancellation = null; _cancel.NotifyCanExecuteChanged(); }
    }
    private async Task ExportAsync() { if (Report is null) return; try { Status = $"Privacy-safe network report exported to {await _center.ExportAsync(Report, CancellationToken.None)}"; } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Status = $"Report export failed: {exception.Message}"; } }
    private void Fill(NetworkHealthReport report) { Report = report; Tests.Clear(); Findings.Clear(); Topology.Clear(); foreach (var item in report.Snapshot.Tests) Tests.Add(item); foreach (var item in report.Findings) Findings.Add(item); foreach (var item in report.Topology) Topology.Add(item); _export.NotifyCanExecuteChanged(); Notify(nameof(Report)); }
}
