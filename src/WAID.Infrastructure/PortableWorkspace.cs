using WAID.Application.Services;
using WAID.Infrastructure.Configuration;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace WAID.Infrastructure;

public enum WaidStorageMode{Installed,Portable}
public sealed record WorkspaceStatus(WaidStorageMode Mode,string RootPath,bool IsWritable,bool IsReadOnly,string DisplayName,string? Warning);
public interface IWaidWorkspaceContext:IDisposable{WorkspaceStatus Status{get;}WaidHostOptions CreateHostOptions();void CleanPortableWorkspace(bool explicitlyApproved);}
public sealed class WaidWorkspaceException(string code,string message,string recoveryAction,Exception? inner=null):InvalidOperationException(message,inner){public string Code{get;}=code;public string RecoveryAction{get;}=recoveryAction;}

public sealed class WaidWorkspaceContext: IWaidWorkspaceContext
{
 private const string Marker=".waid-portable-workspace";private static readonly ConcurrentDictionary<string,byte> ActiveWorkspaces=new(StringComparer.OrdinalIgnoreCase);private readonly Mutex? _mutex;private readonly string? _lockPath;private bool _ownsMutex;
 private WaidWorkspaceContext(WorkspaceStatus status,Mutex? mutex,bool ownsMutex,string? lockPath=null){Status=status;_mutex=mutex;_ownsMutex=ownsMutex;_lockPath=lockPath;}
 public WorkspaceStatus Status{get;}
 public static WaidWorkspaceContext Resolve(string[] args,string executableDirectory)
 {
  ArgumentNullException.ThrowIfNull(args);var portable=args.Any(x=>x.Equals("--portable",StringComparison.OrdinalIgnoreCase))||File.Exists(Path.Combine(executableDirectory,"waid.portable"));
  var enterprisePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Windows AI Doctor","enterprise-policy.json");
  if(portable&&!EnterprisePolicyBootstrap.IsAllowed(enterprisePath,EnterpriseCapability.PortableMode))throw new WaidWorkspaceException("WAID-POLICY-PORTABLE-BLOCKED","Portable mode is blocked by organization policy.","Use the installed application or contact the organization policy administrator.");
  if(!portable){var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Windows AI Doctor");return new(new(WaidStorageMode.Installed,Path.GetFullPath(root),true,false,"Installed workspace",null),null,false);}
  var index=Array.FindIndex(args,x=>x.Equals("--workspace",StringComparison.OrdinalIgnoreCase));var requested=index>=0&&index+1<args.Length?args[index+1]:Path.Combine(executableDirectory,"WAID-Workspace");
  if(string.IsNullOrWhiteSpace(requested))throw new WaidWorkspaceException("WAID-PORTABLE-WORKSPACE","A portable workspace was not selected.","Launch with --portable --workspace <folder>.");
  var rootPath=Path.GetFullPath(requested);try{Directory.CreateDirectory(rootPath);}catch(Exception e)when(e is UnauthorizedAccessException or IOException){throw new WaidWorkspaceException("WAID-PORTABLE-READONLY","The selected portable workspace cannot be created or written.","Choose writable local or removable media and verify permissions.",e);}ProbeWritable(rootPath);var marker=Path.Combine(rootPath,Marker);if(!File.Exists(marker))File.WriteAllText(marker,"WAID portable workspace. Data may contain private diagnostics.");
  if(!ActiveWorkspaces.TryAdd(rootPath,0))throw new WaidWorkspaceException("WAID-PORTABLE-IN-USE","This portable workspace is already open.","Close the other WAID instance or choose a different workspace.");var name="WAID-PORTABLE-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rootPath.ToUpperInvariant())))[..24];var mutex=new Mutex(false,name);bool owns;try{owns=mutex.WaitOne(0,false);}catch(AbandonedMutexException){owns=true;}if(!owns){ActiveWorkspaces.TryRemove(rootPath,out _);mutex.Dispose();throw new WaidWorkspaceException("WAID-PORTABLE-IN-USE","This portable workspace is already open.","Close the other WAID instance or choose a different workspace.");}
  return new(new(WaidStorageMode.Portable,rootPath,true,false,"Portable workspace",null),mutex,true,rootPath);
 }
 public WaidHostOptions CreateHostOptions()=>Status.Mode==WaidStorageMode.Portable?WaidHostOptions.CreatePortableDefaults(Status.RootPath):WaidHostOptions.CreateDesktopDefaults(Status.RootPath);
 public void CleanPortableWorkspace(bool explicitlyApproved){if(Status.Mode!=WaidStorageMode.Portable||!explicitlyApproved)throw new WaidWorkspaceException("WAID-PORTABLE-CLEAN-DENIED","Portable cleanup requires explicit approval.","Confirm cleanup from the portable workspace.");var marker=Path.Combine(Status.RootPath,Marker);if(!File.Exists(marker))throw new WaidWorkspaceException("WAID-PORTABLE-MARKER","The selected folder is not a WAID portable workspace.","Choose the original portable workspace.");foreach(var entry in Directory.EnumerateFileSystemEntries(Status.RootPath)){if(Path.GetFileName(entry).Equals(Marker,StringComparison.OrdinalIgnoreCase))continue;if(Directory.Exists(entry))Directory.Delete(entry,true);else File.Delete(entry);}}
 public void Dispose(){if(_ownsMutex){_mutex?.ReleaseMutex();_ownsMutex=false;}if(_lockPath is not null)ActiveWorkspaces.TryRemove(_lockPath,out _);_mutex?.Dispose();}
 private static void ProbeWritable(string root){var probe=Path.Combine(root,$".waid-write-{Guid.NewGuid():N}.tmp");try{using(var stream=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.WriteThrough)){stream.WriteByte(0);}File.Delete(probe);}catch(Exception e)when(e is UnauthorizedAccessException or IOException){throw new WaidWorkspaceException("WAID-PORTABLE-READONLY","The selected portable workspace is read-only or unavailable.","Choose writable local or removable media and verify permissions.",e);}}
}
