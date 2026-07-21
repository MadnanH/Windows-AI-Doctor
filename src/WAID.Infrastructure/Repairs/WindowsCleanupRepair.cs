using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;
using WAID.Infrastructure.PowerShell;
namespace WAID.Infrastructure.Repairs;
public sealed class WindowsCleanupRepair(IPowerShellRunner runner) : IRepairAction
{
    public string Id=>"waid.cleanup"; public string DisplayName=>"Clean Windows temporary files"; public bool RequiresAdministrator=>false;
    public async Task<RepairResult> ExecuteAsync(DiagnosticFinding finding,CancellationToken token) { var result=await runner.RunAsync("Get-ChildItem -LiteralPath $env:TEMP -Force -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer } | Remove-Item -Force -ErrorAction SilentlyContinue",new Dictionary<string,object?>(),token); return result.Succeeded?RepairResult.Success("Temporary files were cleaned."):RepairResult.Failure("Cleanup could not complete.",string.Join(Environment.NewLine,result.Errors)); }
}
