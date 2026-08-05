using Microsoft.Extensions.DependencyInjection;
namespace WAID.Application.Plugins;
public interface IWaidPlugin { PluginMetadata Metadata { get; } void ConfigureServices(IServiceCollection services); }
public sealed record PluginMetadata(string Id,string Name,Version Version,string Publisher,Version MinimumHostVersion);
public enum PluginCapability { Scanner,ReportContributor,KnowledgeProvider,RepairModule }
public enum PluginPermission { SystemRead,EnvironmentRead,EventLogRead,NetworkProbe,ReportWrite,KnowledgeRead,RepairPlan }
public enum PluginLifecycleState { Discovered,Certified,Loaded,Disabled,Quarantined,Incompatible,RestartRequired }
public sealed record PluginDependency(string Id,string MinimumVersion,bool Optional=false);
public sealed record PluginSdkDescriptor(string ApiVersion,IReadOnlySet<PluginCapability> Capabilities,IReadOnlySet<PluginPermission> Permissions,IReadOnlyList<PluginDependency> Dependencies);
public interface IPluginReportContributor{string Id{get;}Task<IReadOnlyDictionary<string,string>>BuildSectionAsync(CancellationToken token);}
public interface IPluginKnowledgeProvider{string Id{get;}Task<IReadOnlyCollection<string>>GetArticleIdsAsync(CancellationToken token);}
public interface IPluginServiceRegistry
{
 void AddScanner<T>()where T:class,Abstractions.ISystemScanner;
 void AddReportContributor<T>()where T:class,IPluginReportContributor;
 void AddKnowledgeProvider<T>()where T:class,IPluginKnowledgeProvider;
 void AddRepairModule<T>()where T:class,Abstractions.IRepairModule;
}
public interface IWaidPluginV2:IWaidPlugin{PluginSdkDescriptor Sdk{get;}void Configure(IPluginServiceRegistry services);}
