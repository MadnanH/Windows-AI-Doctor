using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using WAID.Application.Plugins;
namespace WAID.Infrastructure.Plugins;
public sealed record PluginManifest(string Id,string Name,string Version,string Publisher,string MinimumHostVersion,string EntryAssembly,string ApiVersion,IReadOnlyCollection<string>? Capabilities=null);
public enum PluginState{Loaded,Disabled,Rejected,Quarantined,Incompatible}
public sealed record PluginDiagnostic(string Id,string Name,string Version,PluginState State,string Detail,string? Path=null);
public sealed record PluginSecurityPolicy(IReadOnlyCollection<string> AllowedPublishers,bool RequireAuthenticodeSignature=false);
public sealed class PluginCatalog{private readonly List<PluginDiagnostic> _items=[];public IReadOnlyList<PluginDiagnostic> Items=>_items.AsReadOnly();internal void Replace(IEnumerable<PluginDiagnostic> items){_items.Clear();_items.AddRange(items);}}
public sealed class PluginLoader
{
    private readonly List<PluginLoadContext> _contexts=[];
    public IReadOnlyList<IWaidPlugin> Load(string directory,Version hostVersion)=>Load(directory,hostVersion,new PluginSecurityPolicy(["WAID Engineering"]),new PluginCatalog());
    public IReadOnlyList<IWaidPlugin> Load(string directory,Version hostVersion,PluginSecurityPolicy policy,PluginCatalog catalog)
    {
        Directory.CreateDirectory(directory);var disabled=ReadDisabled(Path.Combine(directory,"plugin-state.json"));var diagnostics=new List<PluginDiagnostic>();var plugins=new List<IWaidPlugin>();
        foreach(var manifestPath in Directory.EnumerateFiles(directory,"*.waid-plugin.json",SearchOption.TopDirectoryOnly))
        {
            PluginLoadContext? context=null;
            try
            {
                var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Manifest is empty.");Validate(manifest,hostVersion,policy);
                if(disabled.Contains(manifest.Id)){diagnostics.Add(new(manifest.Id,manifest.Name,manifest.Version,PluginState.Disabled,"Disabled by the user.",manifestPath));continue;}
                var root=Path.GetFullPath(directory)+Path.DirectorySeparatorChar;var assemblyPath=Path.GetFullPath(Path.Combine(directory,manifest.EntryAssembly));if(!assemblyPath.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(assemblyPath))throw new InvalidDataException("Entry assembly is missing or outside the plugin directory.");if(policy.RequireAuthenticodeSignature)VerifySignature(assemblyPath);
                context=new PluginLoadContext(assemblyPath);var assembly=context.LoadFromAssemblyPath(assemblyPath);var candidates=assembly.GetTypes().Where(t=>typeof(IWaidPlugin).IsAssignableFrom(t)&&!t.IsAbstract&&!t.IsInterface).ToArray();if(candidates.Length!=1)throw new InvalidDataException("A plugin must expose exactly one IWaidPlugin entry point.");var plugin=(IWaidPlugin)(Activator.CreateInstance(candidates[0])??throw new InvalidDataException("Plugin entry point could not be created."));if(!string.Equals(plugin.Metadata.Id,manifest.Id,StringComparison.Ordinal)||plugin.Metadata.Version!=Version.Parse(manifest.Version))throw new InvalidDataException("Manifest metadata does not match the assembly.");plugins.Add(plugin);_contexts.Add(context);context=null;diagnostics.Add(new(manifest.Id,manifest.Name,manifest.Version,PluginState.Loaded,"Loaded in an isolated dependency context.",assemblyPath));
            }
            catch(NotSupportedException exception){diagnostics.Add(new(Path.GetFileNameWithoutExtension(manifestPath),Path.GetFileName(manifestPath),"unknown",PluginState.Incompatible,exception.Message,manifestPath));}
            catch(Exception exception){diagnostics.Add(new(Path.GetFileNameWithoutExtension(manifestPath),Path.GetFileName(manifestPath),"unknown",PluginState.Quarantined,$"Quarantined after validation or load failure: {exception.GetType().Name}: {exception.Message}",manifestPath));}
            finally{context?.Unload();}
        }
        catalog.Replace(diagnostics);return plugins.AsReadOnly();
    }
    public static void SetDisabled(string directory,string pluginId,bool disabled){Directory.CreateDirectory(directory);var path=Path.Combine(directory,"plugin-state.json");var values=ReadDisabled(path);if(disabled)values.Add(pluginId);else values.Remove(pluginId);var temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(values.OrderBy(x=>x,StringComparer.Ordinal),new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);}
    private static HashSet<string> ReadDisabled(string path){try{return File.Exists(path)?JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path))??new(StringComparer.Ordinal):new(StringComparer.Ordinal);}catch{return new(StringComparer.Ordinal);}}
    private static void Validate(PluginManifest manifest,Version hostVersion,PluginSecurityPolicy policy){if(string.IsNullOrWhiteSpace(manifest.Id)||string.IsNullOrWhiteSpace(manifest.Name)||string.IsNullOrWhiteSpace(manifest.Publisher)||string.IsNullOrWhiteSpace(manifest.EntryAssembly))throw new InvalidDataException("Manifest required fields are missing.");if(manifest.ApiVersion!="1")throw new NotSupportedException($"Plugin API {manifest.ApiVersion} is unsupported; expected 1.");if(Version.Parse(manifest.MinimumHostVersion)>hostVersion)throw new NotSupportedException($"Requires WAID {manifest.MinimumHostVersion} or later.");_=Version.Parse(manifest.Version);if(!policy.AllowedPublishers.Contains(manifest.Publisher,StringComparer.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Publisher is not allow-listed.");}
    private static void VerifySignature(string path){if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Authenticode verification requires Windows.");try{using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));using var chain=new X509Chain();if(!chain.Build(certificate))throw new CryptographicException("Authenticode certificate chain is not trusted.");}catch(CryptographicException){throw;}catch(Exception exception){throw new CryptographicException("The plugin does not have a valid Authenticode signature.",exception);}}
}
internal sealed class PluginLoadContext(string pluginPath):AssemblyLoadContext(isCollectible:true){private readonly AssemblyDependencyResolver _resolver=new(pluginPath);protected override Assembly? Load(AssemblyName assemblyName){if(assemblyName.Name is "WAID.Application" or "WAID.Domain" or "Microsoft.Extensions.DependencyInjection.Abstractions")return null;var path=_resolver.ResolveAssemblyToPath(assemblyName);return path is null?null:LoadFromAssemblyPath(path);}}
