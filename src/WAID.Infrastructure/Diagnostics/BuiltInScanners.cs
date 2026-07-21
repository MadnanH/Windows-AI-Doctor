using System.Runtime.InteropServices;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
namespace WAID.Infrastructure.Diagnostics;
public sealed class DiskSpaceScanner : ISystemScanner
{
    public string Id => "waid.disk-space"; public string DisplayName => "Storage health";
    public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken token)
    {
        var findings=new List<DiagnosticFinding>();
        foreach(var drive in DriveInfo.GetDrives().Where(x=>x.IsReady && x.DriveType==DriveType.Fixed)) { token.ThrowIfCancellationRequested(); var ratio=(double)drive.AvailableFreeSpace/drive.TotalSize; if(ratio<0.1) findings.Add(new(Id,"STORAGE_LOW",$"Low space on {drive.Name}",$"Only {ratio:P0} free space remains.",ratio<0.05?DiagnosticSeverity.Critical:DiagnosticSeverity.Warning,"waid.cleanup",new Dictionary<string,string>{{"freeBytes",drive.AvailableFreeSpace.ToString()},{"totalBytes",drive.TotalSize.ToString()}})); }
        return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>(findings);
    }
}
public sealed class OperatingSystemScanner : ISystemScanner
{
    public string Id=>"waid.os"; public string DisplayName=>"Windows compatibility";
    public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context,CancellationToken token) { var findings=new List<DiagnosticFinding>(); if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) findings.Add(new(Id,"OS_UNSUPPORTED","Unsupported operating system","WAID repairs require Windows.",DiagnosticSeverity.Critical)); return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>(findings); }
}
