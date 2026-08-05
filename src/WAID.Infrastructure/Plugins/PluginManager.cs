using System.Text.Json;
using WAID.Application.Abstractions;
using WAID.Application.Plugins;
using WAID.Application.Services;
using WAID.Domain.Repairs;
using WAID.Infrastructure.Diagnostics;

namespace WAID.Infrastructure.Plugins;

public sealed record PluginInventoryRecord(string Id,string Name,string Version,PluginState State,bool Certified,string SignatureStatus,IReadOnlyCollection<string>Capabilities,IReadOnlyCollection<string>Permissions,string Detail,string? Path,DateTimeOffset UpdatedAtUtc);
public interface IPluginInventoryRepository{Task SaveAsync(PluginInventoryRecord record,CancellationToken token);Task<IReadOnlyList<PluginInventoryRecord>>GetAllAsync(CancellationToken token);}
public sealed record PluginInstallPreview(PluginManifest Manifest,PluginCertificationResult Certification,string SourcePath);
public sealed record PluginManagementResult(bool Succeeded,string Code,string Detail,bool RestartRequired);

public sealed class PluginManager(string directory,Version hostVersion,PluginSecurityPolicy policy,PluginCatalog catalog,IPluginInventoryRepository inventory,IAuditTrailService audit,TimeProvider time,IEnterprisePolicyService? enterprisePolicy=null)
{
 public bool CanInstallOrEnable=>enterprisePolicy?.Evaluate(EnterpriseCapability.Plugins).Allowed??true;
 public PluginInstallPreview PreviewInstall(string manifestPath)
 {
  var full=Path.GetFullPath(manifestPath);if(!File.Exists(full)||!full.EndsWith(".waid-plugin.json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Select a .waid-plugin.json manifest.");
  var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(full),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Manifest is empty.");
  return new(manifest,new PluginCertificationService().Evaluate(full,hostVersion,policy),full);
 }
 public async Task<PluginManagementResult>InstallAsync(string manifestPath,bool permissionsApproved,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();EnsureAllowed();var preview=PreviewInstall(manifestPath);if(!preview.Certification.Certified)return await Result(preview,"PLUGIN_CERTIFICATION_FAILED","Plugin failed certification and was not installed: "+string.Join(" ",preview.Certification.Checks.Where(x=>!x.Passed).Select(x=>x.Detail)),false,AuditResult.Rejected,token);
  if((preview.Manifest.Permissions?.Count??0)>0&&!permissionsApproved)return await Result(preview,"PLUGIN_PERMISSION_APPROVAL_REQUIRED","Review and explicitly approve the declared permissions.",false,AuditResult.Rejected,token);
  Directory.CreateDirectory(directory);var targetManifest=Path.Combine(directory,$"{preview.Manifest.Id}.waid-plugin.json");var targetAssembly=Path.Combine(directory,Path.GetFileName(preview.Certification.AssemblyPath));
  if(File.Exists(targetManifest)||File.Exists(targetAssembly))return await Result(preview,"PLUGIN_ALREADY_INSTALLED","A plugin with this ID or assembly is already installed.",false,AuditResult.Rejected,token);
  var manifestTemp=targetManifest+".tmp";var assemblyTemp=targetAssembly+".tmp";
  try{File.Copy(preview.Certification.AssemblyPath,assemblyTemp,false);File.Copy(preview.SourcePath,manifestTemp,false);File.Move(assemblyTemp,targetAssembly);File.Move(manifestTemp,targetManifest);var diagnostic=new PluginDiagnostic(preview.Manifest.Id,preview.Manifest.Name,preview.Manifest.Version,PluginState.RestartRequired,"Installed after certification; restart WAID to load.",targetManifest,preview.Manifest.Capabilities,preview.Manifest.Permissions,preview.Certification.SignatureStatus,true,preview.Certification.Checks);catalog.Replace(catalog.Items.Where(x=>!x.Id.Equals(preview.Manifest.Id,StringComparison.OrdinalIgnoreCase)).Append(diagnostic));return await Result(preview,"PLUGIN_INSTALLED",diagnostic.Detail,true,AuditResult.Succeeded,token);}
  catch{TryDelete(assemblyTemp);TryDelete(manifestTemp);TryDelete(targetAssembly);TryDelete(targetManifest);throw;}
 }
 public async Task<PluginManagementResult>SetEnabledAsync(string pluginId,bool enabled,CancellationToken token)
 {
  if(enabled)EnsureAllowed();var item=catalog.Find(pluginId)??throw new InvalidOperationException("Plugin inventory item was not found.");PluginLoader.SetDisabled(directory,pluginId,!enabled);var state=PluginState.RestartRequired;var detail=enabled?"Enable requested; restart WAID to recertify and load.":"Disabled; restart WAID to unload registered services.";var updated=item with{State=state,Detail=detail};catalog.Replace(catalog.Items.Where(x=>!x.Id.Equals(pluginId,StringComparison.OrdinalIgnoreCase)).Append(updated));await Save(updated,token);await Audit(pluginId,enabled?"PluginEnable":"PluginDisable",AuditResult.Succeeded,detail,token);return new(true,enabled?"PLUGIN_ENABLE_PENDING":"PLUGIN_DISABLED",detail,true);
 }
 public Task<PluginManagementResult>QuarantineAsync(string pluginId,CancellationToken token)=>SetQuarantined(pluginId,token);
 public async Task SynchronizeAsync(CancellationToken token){foreach(var item in catalog.Items)await Save(item,token);}
 private async Task<PluginManagementResult>SetQuarantined(string id,CancellationToken token){var item=catalog.Find(id)??throw new InvalidOperationException("Plugin inventory item was not found.");PluginLoader.SetDisabled(directory,id,true);var updated=item with{State=PluginState.Quarantined,Detail="Quarantined by the user; restart required. Logs remain available in inventory."};catalog.Replace(catalog.Items.Where(x=>!x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)).Append(updated));await Save(updated,token);await Audit(id,"PluginQuarantine",AuditResult.Succeeded,updated.Detail,token);return new(true,"PLUGIN_QUARANTINED",updated.Detail,true);}
 private async Task<PluginManagementResult>Result(PluginInstallPreview preview,string code,string detail,bool restart,AuditResult result,CancellationToken token){var m=preview.Manifest;var state=result==AuditResult.Succeeded?PluginState.RestartRequired:preview.Certification.Certified?PluginState.Rejected:PluginState.CertificationFailed;var diagnostic=new PluginDiagnostic(m.Id,m.Name,m.Version,state,detail,preview.SourcePath,m.Capabilities,m.Permissions,preview.Certification.SignatureStatus,preview.Certification.Certified,preview.Certification.Checks);catalog.Replace(catalog.Items.Where(x=>!x.Id.Equals(m.Id,StringComparison.OrdinalIgnoreCase)).Append(diagnostic));await Save(diagnostic,token);await Audit(m.Id,"PluginInstall",result,detail,token);return new(result==AuditResult.Succeeded,code,detail,restart);}
 private Task Save(PluginDiagnostic x,CancellationToken token)=>inventory.SaveAsync(new(x.Id,x.Name,x.Version,x.State,x.Certified,x.SignatureStatus,x.Capabilities??[],x.Permissions??[],x.Detail,x.Path,time.GetUtcNow()),token);
 private async Task Audit(string target,string action,AuditResult result,string detail,CancellationToken token){await audit.AppendAsync(new(Guid.NewGuid(),time.GetUtcNow(),AuditActor.User,action,target,result,SafetyLevel.High,false,false,Guid.NewGuid(),Guid.NewGuid(),detail),token);}
 private void EnsureAllowed(){var decision=enterprisePolicy?.Evaluate(EnterpriseCapability.Plugins);if(decision is {Allowed:false})throw new EnterprisePolicyException("WAID-POLICY-PLUGINS-BLOCKED",$"Plugin changes are blocked by {decision.Source}.","Contact the organization policy administrator.");}
 private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
