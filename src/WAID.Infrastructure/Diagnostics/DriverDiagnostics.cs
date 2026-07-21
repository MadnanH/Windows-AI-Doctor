using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Diagnostics;

public sealed class WindowsDriverInventoryProvider(IPowerShellRunner powerShell, IAdministratorService administrator) : IDriverInventoryProvider
{
    private const string Script = """
        $arch=[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        $signed=@{}; Get-CimInstance Win32_PnPSignedDriver -ErrorAction Stop | ForEach-Object {$signed[$_.DeviceID]=$_}
        $devices=@(Get-CimInstance Win32_PnPEntity -ErrorAction Stop | ForEach-Object {
          $d=$_; $s=$signed[$d.DeviceID]; $path=$s.DriverProviderName
          [pscustomobject]@{deviceId=$d.DeviceID;name=$d.Name;class=$d.PNPClass;manufacturer=$d.Manufacturer;provider=$s.DriverProviderName;version=$s.DriverVersion;driverDate=$s.DriverDate;infName=$s.InfName;binaryPath=$null;present=$d.Present;enabled=($d.ConfigManagerErrorCode -ne 22);problemCode=[int]$d.ConfigManagerErrorCode;signed=[bool]$s.IsSigned;signatureStatus=if($s.IsSigned){'Valid'}else{'Unsigned'};hardwareId=(@($d.HardwareID)[0]);architecture=$arch}
        })
        $events=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-30);Id=219,4101,7000,7001,7026,20001,20003} -ErrorAction SilentlyContinue | Select-Object -First 200 | ForEach-Object {[pscustomobject]@{time=$_.TimeCreated.ToUniversalTime().ToString('O');id=$_.Id;provider=$_.ProviderName;message=$_.Message}})
        [pscustomobject]@{architecture=$arch;devices=$devices;events=$events} | ConvertTo-Json -Depth 6 -Compress
        """;

    public async Task<DriverInventorySnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var result = await powerShell.RunAsync(Script, new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) throw new DriverDiagnosticsException("WAID-DRV-COLLECT", "Windows driver inventory could not be read.", string.Join("; ", result.Errors));
        var payload = JsonSerializer.Deserialize<Payload>(string.Join(Environment.NewLine, result.Output), JsonOptions)
            ?? throw new DriverDiagnosticsException("WAID-DRV-DATA", "Windows returned an empty driver inventory.", "No payload was returned.");
        var now = DateTimeOffset.UtcNow;
        var drivers = (payload.Devices ?? []).Where(item => !string.IsNullOrWhiteSpace(item.DeviceId) && !string.IsNullOrWhiteSpace(item.Name)).Take(10000)
            .Select(item => new DriverInventoryItem(Hash(item.DeviceId!), Clean(item.Name), Clean(item.Class), CleanOrNull(item.Manufacturer), CleanOrNull(item.Provider),
                CleanOrNull(item.Version), ParseDate(item.DriverDate), CleanOrNull(item.InfName), null, item.Present, item.Enabled, Math.Clamp(item.ProblemCode, 0, 100),
                item.Signed, Clean(item.SignatureStatus), HashOrNull(item.HardwareId), CleanOrNull(item.Architecture))).ToArray();
        var events = (payload.Events ?? []).Where(item => item.Id is 219 or 4101 or 7000 or 7001 or 7026 or 20001 or 20003).Take(200)
            .Select(item => new DriverEventEvidence(ParseDate(item.Time) ?? now, item.Id, Clean(item.Provider), MatchDevice(drivers, item.Message), Clean(item.Message, 500))).ToArray();
        return new(now, Clean(payload.Architecture), drivers, events, administrator.IsAdministrator(),
            administrator.IsAdministrator() ? [] : ["Standard-user collection may omit protected driver files and signature details."]);
    }

    private static string MatchDevice(IEnumerable<DriverInventoryItem> drivers, string? message) => drivers.FirstOrDefault(item => !string.IsNullOrWhiteSpace(message) && message.Contains(item.DeviceName, StringComparison.OrdinalIgnoreCase))?.DeviceKey ?? string.Empty;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20];
    private static string? HashOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : Hash(value);
    private static string Clean(string? value, int max = 200) { var clean=(value ?? string.Empty).Replace(Environment.UserName, "[user]", StringComparison.OrdinalIgnoreCase).Trim(); return clean.Length <= max ? clean : clean[..max]; }
    private static string? CleanOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : Clean(value);
    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record Payload(string? Architecture, DeviceDto[]? Devices, EventDto[]? Events);
    private sealed record DeviceDto(string? DeviceId,string? Name,string? Class,string? Manufacturer,string? Provider,string? Version,string? DriverDate,string? InfName,string? BinaryPath,bool Present,bool Enabled,int ProblemCode,bool Signed,string? SignatureStatus,string? HardwareId,string? Architecture);
    private sealed record EventDto(string? Time,int Id,string? Provider,string? Message);
}

public sealed class DriverConflictAnalyzer(IDriverInventoryProvider provider, IDriverHealthRepository repository, ILogger<DriverConflictAnalyzer> logger) : IDriverConflictAnalyzer
{
    public async Task<DriverHealthReport> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var snapshot = await provider.CollectAsync(cancellationToken).ConfigureAwait(false);
        var previous = await repository.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        var changes = FindChanges(previous, snapshot);
        var findings = Evaluate(snapshot, changes);
        var report = new DriverHealthReport(Guid.NewGuid(), snapshot.CollectedAtUtc, snapshot.Drivers, findings, changes, snapshot.Limitations);
        await repository.SaveAsync(snapshot, report, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Driver analysis {ReportId} completed with {DriverCount} drivers, {FindingCount} findings and {ChangeCount} changes", report.Id, report.Inventory.Count, report.Findings.Count, report.Changes.Count);
        return report;
    }

    public static IReadOnlyList<DriverHealthFinding> Evaluate(DriverInventorySnapshot snapshot, IReadOnlyList<DriverChange>? changes = null)
    {
        var findings = new List<DriverHealthFinding>(); var now=snapshot.CollectedAtUtc;
        foreach (var d in snapshot.Drivers)
        {
            if (d.ProblemCode != 0)
            {
                Add(d, d.ProblemCode == 22 ? DriverFindingKind.Disabled : DriverFindingKind.ProblemCode, d.ProblemCode == 22 ? "Device is disabled" : "Windows reports a driver problem", $"Device Manager problem code {d.ProblemCode} is active.", d.ProblemCode is 10 or 31 or 39 or 43 ? "Critical" : "Warning", .96, "Review the device in Device Manager and use the hardware vendor's supported driver.", [Ev("problemCode", d.ProblemCode.ToString(), "SetupAPI/CIM", now)]);
                if(d.ProblemCode is 10 or 31 or 39 or 43) Add(d,DriverFindingKind.Failed,"Driver failed to start","Windows reports a problem code that normally means the device or its driver could not start or load correctly.","Critical",.94,"Review the linked Device Manager status and Windows events before installing a vendor-supported replacement.",[Ev("problemCode",d.ProblemCode.ToString(),"SetupAPI/CIM",now)]);
            }
            if (!d.SignatureValid && !string.Equals(d.Provider, "Microsoft", StringComparison.OrdinalIgnoreCase)) Add(d, DriverFindingKind.Unsigned, "Unsigned third-party driver", "Windows did not report a valid signature for this third-party driver. Signature availability can vary with permissions.", "Warning", snapshot.IsAdministrator ? .9 : .72, "Verify the package with the device manufacturer before replacing it.", [Ev("signature", d.SignatureStatus, "Win32_PnPSignedDriver", now)]);
            if (!d.IsPresent && !string.Equals(d.Provider, "Microsoft", StringComparison.OrdinalIgnoreCase) && d.DriverDateUtc < now.AddYears(-2)) Add(d, DriverFindingKind.Orphaned, "Old non-present driver package", "A non-Microsoft driver is not attached to a present device and is more than two years old. This is informational because disconnected devices are common.", "Information", .65, "Keep it unless the associated hardware is permanently removed; do not use registry cleaners.", [Ev("present", "false", "SetupAPI/CIM", now), Ev("driverDate", d.DriverDateUtc?.ToString("O") ?? "unknown", "Win32_PnPSignedDriver", now)]);
            if (!string.IsNullOrWhiteSpace(d.Architecture) && !string.Equals(d.Architecture, snapshot.OperatingSystemArchitecture, StringComparison.OrdinalIgnoreCase)) Add(d, DriverFindingKind.Incompatible, "Driver architecture mismatch", "The reported driver architecture does not match Windows.", "Critical", .92, "Install the correct architecture package from the hardware manufacturer.", [Ev("driverArchitecture", d.Architecture!, "driver inventory", now), Ev("osArchitecture", snapshot.OperatingSystemArchitecture, "operating system", now)]);
        }
        foreach (var group in snapshot.Drivers.Where(d => d.IsPresent && !string.IsNullOrWhiteSpace(d.HardwareKey)).GroupBy(d => d.HardwareKey!).Where(g => g.Select(x => x.DeviceKey).Distinct().Count() > 1))
            foreach (var d in group) Add(d, DriverFindingKind.Duplicate, "Possible duplicate active device", "Multiple present device records share the same privacy-safe hardware identity. Docking and virtual devices can legitimately do this.", "Information", .6, "Inspect the device tree before taking action; do not remove either entry automatically.", [Ev("matchingPresentDevices", group.Count().ToString(), "SetupAPI/CIM", now)]);
        foreach (var e in snapshot.Events.Where(e => e.EventId is 219 or 7026)) { var d=snapshot.Drivers.FirstOrDefault(x=>x.DeviceKey==e.DeviceKey); if(d is not null) Add(d, DriverFindingKind.LoadFailure, "Recent driver load failure", "Windows recorded a driver load failure linked to this device.", "Warning", .9, "Review the event and vendor driver release notes before changing the driver.", [Ev("eventId", e.EventId.ToString(), $"EventLog:{e.Provider}", e.OccurredAtUtc), Ev("eventSummary", e.Summary, $"EventLog:{e.Provider}", e.OccurredAtUtc)]); }
        foreach (var e in snapshot.Events.Where(e => e.EventId==4101)) { var d=snapshot.Drivers.FirstOrDefault(x=>x.DeviceKey==e.DeviceKey); if(d is not null) Add(d,DriverFindingKind.Failed,"Driver recovered after a crash","Windows recorded a display-driver timeout linked to this device. One event does not prove the driver is defective.","Warning",.82,"Correlate repeated events with application crashes and vendor release notes.",[Ev("eventId","4101",$"EventLog:{e.Provider}",e.OccurredAtUtc),Ev("eventSummary",e.Summary,$"EventLog:{e.Provider}",e.OccurredAtUtc)]); }
        foreach (var e in snapshot.Events.Where(e => e.EventId is 20001 or 20003)) { var d=snapshot.Drivers.FirstOrDefault(x=>x.DeviceKey==e.DeviceKey); if(d is not null) Add(d,DriverFindingKind.RecentlyChanged,"Recent driver installation event","Windows recorded a driver installation or update event linked to this device. This is informational unless symptoms began at the same time.","Information",.78,"Compare the event time with the first occurrence of symptoms before considering rollback.",[Ev("eventId",e.EventId.ToString(),$"EventLog:{e.Provider}",e.OccurredAtUtc)]); }
        foreach (var c in changes ?? []) if (c.ChangeType == "VersionChanged") { var d=snapshot.Drivers.First(x=>x.DeviceKey==c.DeviceKey); Add(d, DriverFindingKind.RecentlyChanged, "Driver version recently changed", "The driver version differs from the previous WAID inventory. This is not a fault by itself.", "Information", .98, "Correlate the change with new symptoms or crashes before considering rollback.", [Ev("previousVersion", c.PreviousValue, "WAID previous snapshot", c.DetectedAtUtc), Ev("currentVersion", c.CurrentValue, "WAID current snapshot", c.DetectedAtUtc)]); }
        return findings.GroupBy(f=>new{f.DeviceKey,f.Kind}).Select(g=>g.OrderByDescending(f=>f.Confidence).First()).ToArray();

        void Add(DriverInventoryItem d,DriverFindingKind kind,string title,string explanation,string severity,double confidence,string action,IReadOnlyList<DriverEvidence> evidence) => findings.Add(new(HashId(d.DeviceKey,kind),d.DeviceKey,d.DeviceName,kind,title,explanation,severity,confidence,evidence,action,kind is DriverFindingKind.Disabled or DriverFindingKind.Incompatible));
    }

    private static IReadOnlyList<DriverChange> FindChanges(DriverHealthReport? previous, DriverInventorySnapshot current)
    {
        if(previous is null) return [];
        var old=previous.Inventory.ToDictionary(x=>x.DeviceKey,StringComparer.OrdinalIgnoreCase); var changes=new List<DriverChange>();
        foreach(var item in current.Drivers) if(old.TryGetValue(item.DeviceKey,out var prior) && !string.Equals(prior.DriverVersion,item.DriverVersion,StringComparison.OrdinalIgnoreCase)) changes.Add(new(item.DeviceKey,item.DeviceName,"VersionChanged",prior.DriverVersion??"unknown",item.DriverVersion??"unknown",current.CollectedAtUtc));
        return changes;
    }
    private static DriverEvidence Ev(string signal,string value,string source,DateTimeOffset time)=>new(signal,value,source,time);
    private static string HashId(string key,DriverFindingKind kind)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{kind}")))[..24];
}

public sealed class DriverDiagnosticsException(string code,string message,string detail) : InvalidOperationException(message) { public string Code { get; }=code; public string Detail { get; }=detail; }
