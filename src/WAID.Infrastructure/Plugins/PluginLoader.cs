using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using WAID.Application.Plugins;

namespace WAID.Infrastructure.Plugins;

public sealed record PluginManifest(string Id,string Name,string Version,string Publisher,string MinimumHostVersion,string EntryAssembly,string ApiVersion,IReadOnlyCollection<string>? Capabilities=null,IReadOnlyCollection<string>? Permissions=null,IReadOnlyCollection<PluginDependency>? Dependencies=null,string? AssemblySha256=null);
public enum PluginState{Loaded,Disabled,Rejected,Quarantined,Incompatible,CertificationFailed,RestartRequired}
public sealed record PluginCertificationCheck(string Code,bool Passed,string Detail);
public sealed record PluginCertificationResult(bool Certified,string ManifestPath,string AssemblyPath,string AssemblySha256,string SignatureStatus,IReadOnlyList<PluginCertificationCheck> Checks);
public sealed record PluginDiagnostic(string Id,string Name,string Version,PluginState State,string Detail,string? Path=null,IReadOnlyCollection<string>? Capabilities=null,IReadOnlyCollection<string>? Permissions=null,string SignatureStatus="Not evaluated",bool Certified=false,IReadOnlyList<PluginCertificationCheck>? Checks=null);
public sealed record PluginSecurityPolicy(IReadOnlyCollection<string> AllowedPublishers,bool RequireAuthenticodeSignature=false,IReadOnlySet<PluginPermission>? AllowedPermissions=null)
{
 public IReadOnlySet<PluginPermission> EffectivePermissions=>AllowedPermissions??new HashSet<PluginPermission>(Enum.GetValues<PluginPermission>());
}
public sealed class PluginCatalog
{
 private readonly List<PluginDiagnostic> _items=[];public IReadOnlyList<PluginDiagnostic>Items=>_items.AsReadOnly();
 internal void Replace(IEnumerable<PluginDiagnostic>items){_items.Clear();_items.AddRange(items);}
 internal void RecordRegistrationFailure(PluginMetadata metadata,Exception exception)=>_items.Add(new(metadata.Id,metadata.Name,metadata.Version.ToString(),PluginState.Quarantined,$"Service registration failed: {exception.GetType().Name}."));
 public PluginDiagnostic? Find(string id)=>_items.FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
}

public sealed class PluginCertificationService
{
 private static readonly Regex IdPattern=new("^[a-z0-9]+(?:[.-][a-z0-9]+){2,}$",RegexOptions.CultureInvariant);
 public PluginCertificationResult Evaluate(string manifestPath,Version hostVersion,PluginSecurityPolicy policy)
 {
  var checks=new List<PluginCertificationCheck>();PluginManifest manifest;string assemblyPath="";
  try{manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Manifest is empty.");}
  catch(Exception e){checks.Add(new("manifest.json",false,$"Manifest parsing failed: {e.GetType().Name}."));return new(false,manifestPath,"","","Not evaluated",checks);}
  Check(checks,"manifest.id",IdPattern.IsMatch(manifest.Id??""),"ID must use lower-case reverse-domain notation.");
  Check(checks,"manifest.name",!string.IsNullOrWhiteSpace(manifest.Name)&&manifest.Name.Length<=120,"Name is required and limited to 120 characters.");
  var version=Parse(manifest.Version);Check(checks,"manifest.version",version is not null,"Version must be a valid System.Version.");
  var minimum=Parse(manifest.MinimumHostVersion);Check(checks,"host.compatibility",minimum is not null&&minimum<=hostVersion,$"Requires host {manifest.MinimumHostVersion}; current host is {hostVersion}.");
  Check(checks,"sdk.api",manifest.ApiVersion is "1" or "2","Supported API versions are 1 and 2.");
  Check(checks,"publisher.allowlist",policy.AllowedPublishers.Contains(manifest.Publisher,StringComparer.OrdinalIgnoreCase),"Publisher must be allow-listed.");
  var root=Path.GetFullPath(Path.GetDirectoryName(manifestPath)!)+Path.DirectorySeparatorChar;
  try{assemblyPath=Path.GetFullPath(Path.Combine(root,manifest.EntryAssembly??""));Check(checks,"assembly.containment",assemblyPath.StartsWith(root,StringComparison.OrdinalIgnoreCase),"Entry assembly must remain inside the plugin package.");}
  catch(Exception){Check(checks,"assembly.containment",false,"Entry assembly path is invalid.");}
  Check(checks,"assembly.exists",File.Exists(assemblyPath),"Entry assembly must exist.");
  var capabilities=ParseSet<PluginCapability>(manifest.Capabilities,checks,"capability");
  var permissions=ParseSet<PluginPermission>(manifest.Permissions,checks,"permission");
  if(manifest.ApiVersion=="2"){Check(checks,"sdk.capabilities",capabilities.Count>0,"API v2 requires at least one capability.");Check(checks,"sdk.permissions",permissions.Count>0,"API v2 requires explicit permissions.");}
  foreach(var permission in permissions)Check(checks,$"policy.permission.{permission}",policy.EffectivePermissions.Contains(permission),$"Permission {permission} must be allowed by host policy.");
  if(capabilities.Contains(PluginCapability.Scanner))Check(checks,"permission.scanner",permissions.Overlaps([PluginPermission.SystemRead,PluginPermission.EnvironmentRead,PluginPermission.EventLogRead,PluginPermission.NetworkProbe]),"Scanner capability requires a read permission.");
  if(capabilities.Contains(PluginCapability.ReportContributor))Check(checks,"permission.report",permissions.Contains(PluginPermission.ReportWrite),"Report contributors require ReportWrite.");
  if(capabilities.Contains(PluginCapability.KnowledgeProvider))Check(checks,"permission.knowledge",permissions.Contains(PluginPermission.KnowledgeRead),"Knowledge providers require KnowledgeRead.");
  if(capabilities.Contains(PluginCapability.RepairModule))Check(checks,"permission.repair",permissions.Contains(PluginPermission.RepairPlan),"Optional repair modules require RepairPlan and still use the host repair executor.");
  foreach(var dependency in manifest.Dependencies??[]){Check(checks,"dependency.id",IdPattern.IsMatch(dependency.Id),$"Dependency ID {dependency.Id} is invalid.");Check(checks,"dependency.version",Parse(dependency.MinimumVersion)is not null,$"Dependency {dependency.Id} version is invalid.");}
  var hash=File.Exists(assemblyPath)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))):"";
  if(!string.IsNullOrWhiteSpace(manifest.AssemblySha256))Check(checks,"assembly.sha256",hash.Equals(manifest.AssemblySha256,StringComparison.OrdinalIgnoreCase),"Assembly SHA-256 must match the manifest.");
  var signature=policy.RequireAuthenticodeSignature?VerifySignature(assemblyPath,checks):"Optional policy not enabled";
  return new(checks.All(x=>x.Passed),manifestPath,assemblyPath,hash,signature,checks);
 }
 private static HashSet<T> ParseSet<T>(IReadOnlyCollection<string>? values,List<PluginCertificationCheck> checks,string kind)where T:struct,Enum{var result=new HashSet<T>();foreach(var value in values??[]){if(Enum.TryParse<T>(value,true,out var parsed))result.Add(parsed);else Check(checks,$"{kind}.known",false,$"Unknown {kind}: {value}.");}return result;}
 private static Version? Parse(string? value)=>Version.TryParse(value,out var parsed)?parsed:null;
 private static void Check(List<PluginCertificationCheck> checks,string code,bool pass,string detail)=>checks.Add(new(code,pass,detail));
 private static string VerifySignature(string path,List<PluginCertificationCheck>checks){if(!OperatingSystem.IsWindows()){Check(checks,"signature.platform",false,"Authenticode verification requires Windows.");return"Unavailable";}try{using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));using var chain=new X509Chain();var valid=chain.Build(certificate);Check(checks,"signature.trust",valid,"Authenticode chain must be trusted.");return valid?$"Trusted: {certificate.Subject}":"Untrusted";}catch(Exception e){Check(checks,"signature.trust",false,$"Authenticode validation failed: {e.GetType().Name}.");return"Missing or invalid";}}
}

public sealed class PluginLoader
{
 private readonly List<PluginLoadContext>_contexts=[];public IReadOnlyList<IWaidPlugin>Load(string directory,Version hostVersion)=>Load(directory,hostVersion,new PluginSecurityPolicy(["WAID Engineering"]),new PluginCatalog());
 public IReadOnlyList<IWaidPlugin>Load(string directory,Version hostVersion,PluginSecurityPolicy policy,PluginCatalog catalog)
 {
  Directory.CreateDirectory(directory);var disabled=ReadDisabled(Path.Combine(directory,"plugin-state.json"));var diagnostics=new List<PluginDiagnostic>();var plugins=new List<IWaidPlugin>();var manifests=new Dictionary<string,PluginManifest>(StringComparer.OrdinalIgnoreCase);
  foreach(var path in Directory.EnumerateFiles(directory,"*.waid-plugin.json"))try{var m=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web));if(m is not null)manifests[m.Id]=m;}catch{}
  foreach(var manifestPath in Directory.EnumerateFiles(directory,"*.waid-plugin.json",SearchOption.TopDirectoryOnly))
  {
   PluginLoadContext? context=null;try
   {
    var result=new PluginCertificationService().Evaluate(manifestPath,hostVersion,policy);var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Manifest is empty.");
    var caps=manifest.Capabilities??[];var permissions=manifest.Permissions??[];
    if(!result.Certified){var incompatible=result.Checks.Any(x=>!x.Passed&&(x.Code is "host.compatibility" or "sdk.api"));diagnostics.Add(new(manifest.Id,manifest.Name,manifest.Version,incompatible?PluginState.Incompatible:PluginState.CertificationFailed,"Blocked by certification: "+string.Join(" ",result.Checks.Where(x=>!x.Passed).Select(x=>x.Detail)),manifestPath,caps,permissions,result.SignatureStatus,false,result.Checks));continue;}
    var missing=(manifest.Dependencies??[]).Where(d=>!d.Optional&&(!manifests.TryGetValue(d.Id,out var found)||Version.Parse(found.Version)<Version.Parse(d.MinimumVersion))).ToArray();if(missing.Length>0){diagnostics.Add(new(manifest.Id,manifest.Name,manifest.Version,PluginState.Incompatible,$"Missing compatible dependencies: {string.Join(", ",missing.Select(x=>x.Id))}.",manifestPath,caps,permissions,result.SignatureStatus,false,result.Checks));continue;}
    if(disabled.Contains(manifest.Id)){diagnostics.Add(new(manifest.Id,manifest.Name,manifest.Version,PluginState.Disabled,"Disabled by the user; restart required after changing state.",manifestPath,caps,permissions,result.SignatureStatus,true,result.Checks));continue;}
    context=new PluginLoadContext(result.AssemblyPath);var assembly=context.LoadFromAssemblyPath(result.AssemblyPath);var candidates=assembly.GetTypes().Where(t=>typeof(IWaidPlugin).IsAssignableFrom(t)&&!t.IsAbstract&&!t.IsInterface).ToArray();if(candidates.Length!=1)throw new InvalidDataException("A plugin must expose exactly one IWaidPlugin entry point.");
    var plugin=(IWaidPlugin)(Activator.CreateInstance(candidates[0])??throw new InvalidDataException("Plugin entry point could not be created."));if(!string.Equals(plugin.Metadata.Id,manifest.Id,StringComparison.Ordinal)||plugin.Metadata.Version!=Version.Parse(manifest.Version))throw new InvalidDataException("Manifest metadata does not match the assembly.");
    if(manifest.ApiVersion=="2"){if(plugin is not IWaidPluginV2 v2)throw new InvalidDataException("API v2 manifest requires IWaidPluginV2.");var declaredCaps=Parse<PluginCapability>(caps);var declaredPermissions=Parse<PluginPermission>(permissions);if(!declaredCaps.SetEquals(v2.Sdk.Capabilities)||!declaredPermissions.SetEquals(v2.Sdk.Permissions))throw new InvalidDataException("Assembly SDK permissions or capabilities do not match the certified manifest.");}
    plugins.Add(plugin);_contexts.Add(context);context=null;diagnostics.Add(new(manifest.Id,manifest.Name,manifest.Version,PluginState.Loaded,"Certified and loaded in an isolated collectible dependency context.",result.AssemblyPath,caps,permissions,result.SignatureStatus,true,result.Checks));
   }catch(Exception e){diagnostics.Add(new(Path.GetFileNameWithoutExtension(manifestPath),Path.GetFileName(manifestPath),"unknown",PluginState.Quarantined,$"Quarantined after isolated validation or load failure: {e.GetType().Name}: {e.Message}",manifestPath));}finally{context?.Unload();}
  }catalog.Replace(diagnostics);return plugins.AsReadOnly();
 }
 public int ReleaseLoadContexts(){var count=_contexts.Count;foreach(var context in _contexts)context.Unload();_contexts.Clear();return count;}
 public static void SetDisabled(string directory,string pluginId,bool disabled){Directory.CreateDirectory(directory);var path=Path.Combine(directory,"plugin-state.json");var values=ReadDisabled(path);if(disabled)values.Add(pluginId);else values.Remove(pluginId);var temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(values.OrderBy(x=>x,StringComparer.Ordinal),new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);}
 private static HashSet<string>ReadDisabled(string path){try{return File.Exists(path)?JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path))??new(StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);}catch{return new(StringComparer.OrdinalIgnoreCase);}}
 private static HashSet<T>Parse<T>(IEnumerable<string>values)where T:struct,Enum=>values.Select(x=>Enum.Parse<T>(x,true)).ToHashSet();
}
public sealed class ControlledPluginServiceRegistry(IServiceCollection services,PluginSdkDescriptor sdk):IPluginServiceRegistry
{
 public void AddScanner<T>()where T:class,WAID.Application.Abstractions.ISystemScanner{Require(PluginCapability.Scanner);services.AddSingleton<WAID.Application.Abstractions.ISystemScanner,T>();}
 public void AddReportContributor<T>()where T:class,IPluginReportContributor{Require(PluginCapability.ReportContributor);services.AddSingleton<IPluginReportContributor,T>();}
 public void AddKnowledgeProvider<T>()where T:class,IPluginKnowledgeProvider{Require(PluginCapability.KnowledgeProvider);services.AddSingleton<IPluginKnowledgeProvider,T>();}
 public void AddRepairModule<T>()where T:class,WAID.Application.Abstractions.IRepairModule{Require(PluginCapability.RepairModule);if(!sdk.Permissions.Contains(PluginPermission.RepairPlan))throw new UnauthorizedAccessException("RepairPlan permission is required.");services.AddSingleton<WAID.Application.Abstractions.IRepairModule,T>();}
 private void Require(PluginCapability capability){if(!sdk.Capabilities.Contains(capability))throw new UnauthorizedAccessException($"Plugin did not declare {capability}.");}
}
internal sealed class PluginLoadContext(string pluginPath):AssemblyLoadContext(isCollectible:true){private readonly AssemblyDependencyResolver _resolver=new(pluginPath);protected override Assembly?Load(AssemblyName assemblyName){if(assemblyName.Name is "WAID.Application" or "WAID.Domain" or "Microsoft.Extensions.DependencyInjection.Abstractions")return null;var path=_resolver.ResolveAssemblyToPath(assemblyName);return path is null?null:LoadFromAssemblyPath(path);}}
