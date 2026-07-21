using System.Runtime.InteropServices;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
namespace WAID.Infrastructure.Diagnostics;
public sealed class OperatingSystemScanner : ISystemScanner
{
    public string Id=>"waid.os"; public string DisplayName=>"Windows compatibility";
    public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context,CancellationToken token) { var findings=new List<DiagnosticFinding>(); if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) findings.Add(new(Id,"OS_UNSUPPORTED","Unsupported operating system","WAID repairs require Windows.",DiagnosticSeverity.Critical)); return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>(findings); }
}
