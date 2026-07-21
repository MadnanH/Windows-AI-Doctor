using Microsoft.Extensions.DependencyInjection;
using WAID.Application.Abstractions;
using WAID.Application.Plugins;
using WAID.Domain.Diagnostics;
namespace WAID.Plugin.Sample;
public sealed class SamplePlugin : IWaidPlugin
{
    public PluginMetadata Metadata { get; }=new("com.waid.sample","WAID Sample Scanner",new(1,0,0),"WAID Engineering",new(1,0,0));
    public void ConfigureServices(IServiceCollection services)=>services.AddSingleton<ISystemScanner,EnvironmentScanner>();
}
public sealed class EnvironmentScanner : ISystemScanner
{
    public string Id=>"sample.environment"; public string DisplayName=>"Environment variables";
    public ScannerMetadata Metadata=>new(Id,DisplayName,"Checks whether PATH entries reference available folders.","Environment",new Version(1,0,0),[],[]);
    public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([
                new(Id, "ENV_PATH_MISSING", "PATH is not configured", "Windows cannot reliably locate command-line tools because PATH is empty.", DiagnosticSeverity.Warning)
            ]);

        var unavailable = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !Directory.Exists(Environment.ExpandEnvironmentVariables(entry.Trim('"'))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unavailable.Length == 0)
            return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([]);

        return Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([
            new(Id, "ENV_PATH_INVALID", "PATH contains unavailable folders",
                $"{unavailable.Length} PATH entr{(unavailable.Length == 1 ? "y is" : "ies are")} unavailable.",
                DiagnosticSeverity.Information,
                evidence: new Dictionary<string, string> { ["unavailableCount"] = unavailable.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        ]);
    }
}
