namespace WAID.Application.Abstractions;

public enum NetworkLayer { LocalStack, Adapter, LanGateway, Dns, Internet, Proxy, Vpn, WiFi, Firewall, Application }
public enum NetworkTestStatus { Passed, Failed, TimedOut, Skipped, Unavailable }
public sealed record NetworkAdapterSnapshot(string Id, string Name, string Type, string Status, IReadOnlyList<string> Addresses, IReadOnlyList<string> DnsServers, string? Gateway, string? WiFiProfile, int? SignalPercent);
public sealed record NetworkRouteSnapshot(string Destination, string NextHop, int Metric, string InterfaceId);
public sealed record NetworkProxySnapshot(bool Enabled, string Mode, string? ProxyHost, string? BypassSummary);
public sealed record NetworkTestResult(string Id, NetworkLayer Layer, string Name, NetworkTestStatus Status, TimeSpan Duration, double? LatencyMilliseconds, double? PacketLossPercent, string Detail, string SourceReference);
public sealed record NetworkSnapshot(DateTimeOffset CollectedAtUtc, IReadOnlyList<NetworkAdapterSnapshot> Adapters, IReadOnlyList<NetworkRouteSnapshot> Routes, NetworkProxySnapshot Proxy, bool VpnActive, string FirewallState, IReadOnlyList<string> RequiredServiceFailures, IReadOnlyList<NetworkTestResult> Tests, IReadOnlyList<string> Limitations);
public sealed record NetworkEvidence(string Signal, string Value, string SourceReference, DateTimeOffset ObservedAtUtc);
public sealed record NetworkFinding(NetworkLayer Layer, string Code, string Title, string Explanation, string Severity, double Confidence, IReadOnlyList<NetworkEvidence> Evidence, string? RepairId, bool ResetSupported);
public sealed record NetworkHealthReport(Guid Id, DateTimeOffset GeneratedAtUtc, NetworkSnapshot Snapshot, IReadOnlyList<NetworkFinding> Findings, IReadOnlyList<string> Topology, IReadOnlyList<string> Limitations);
public sealed record NetworkProbeOptions(string? DnsName = null, Uri? HttpEndpoint = null, int TimeoutMilliseconds = 3000)
{
    public void Validate() { if (TimeoutMilliseconds is < 250 or > 15000) throw new ArgumentOutOfRangeException(nameof(TimeoutMilliseconds)); if (HttpEndpoint is not null && HttpEndpoint.Scheme is not ("http" or "https")) throw new ArgumentException("Only HTTP or HTTPS probe endpoints are supported.", nameof(HttpEndpoint)); }
}
public interface INetworkEvidenceProvider { Task<NetworkSnapshot> CollectAsync(NetworkProbeOptions options, CancellationToken cancellationToken); }
public interface INetworkHealthRepository { Task<NetworkHealthReport?> GetLatestAsync(CancellationToken cancellationToken); Task SaveAsync(NetworkHealthReport report, CancellationToken cancellationToken); }
public interface INetworkDiagnosticCenter { Task<NetworkHealthReport> AnalyzeAsync(NetworkProbeOptions options, CancellationToken cancellationToken); Task<string> ExportAsync(NetworkHealthReport report, CancellationToken cancellationToken); }
