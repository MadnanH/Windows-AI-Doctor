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
    public Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context,CancellationToken token)=>Task.FromResult<IReadOnlyCollection<DiagnosticFinding>>([]);
}
