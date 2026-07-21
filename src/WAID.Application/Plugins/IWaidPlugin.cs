using Microsoft.Extensions.DependencyInjection;
namespace WAID.Application.Plugins;
public interface IWaidPlugin { PluginMetadata Metadata { get; } void ConfigureServices(IServiceCollection services); }
public sealed record PluginMetadata(string Id, string Name, Version Version, string Publisher, Version MinimumHostVersion);
