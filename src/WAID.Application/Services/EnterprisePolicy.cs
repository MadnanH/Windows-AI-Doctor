using System.Collections.Frozen;
namespace WAID.Application.Services;
public enum EnterpriseCapability{CloudServices,AiFeatures,Repairs,Exports,Plugins,Monitoring,PortableMode,Diagnostics}
public enum EnterprisePolicyState{Active,FailClosed,RolledBack}
public sealed record EnterprisePolicyRule(bool Allowed,bool Locked,string Source,string Reason);
public sealed record EnterpriseRetentionPolicy(int DiagnosticDays,int AuditDays,int MonitoringDays)
{
 public EnterpriseRetentionPolicy Validate(){if(DiagnosticDays is<1 or>365||AuditDays is<30 or>3650||MonitoringDays is<1 or>365)throw new InvalidOperationException("Enterprise retention values are outside supported bounds.");return this;}
 public static EnterpriseRetentionPolicy Default{get;}=new(14,365,30);
}
public sealed record EnterprisePolicyDocument(int Version,string Id,string Source,DateTimeOffset UpdatedAtUtc,IReadOnlyDictionary<EnterpriseCapability,bool>Rules,EnterpriseRetentionPolicy?Retention=null)
{
 public const int CurrentVersion=1;
 public EnterprisePolicyDocument Validate(){if(Version!=CurrentVersion)throw new InvalidOperationException($"Enterprise policy version {Version} is unsupported.");if(string.IsNullOrWhiteSpace(Id)||Id.Length>100||string.IsNullOrWhiteSpace(Source)||Source.Length>200)throw new InvalidOperationException("Enterprise policy identity or source is invalid.");if(Rules.Keys.Any(x=>!Enum.IsDefined(x)))throw new InvalidOperationException("Enterprise policy contains an unknown capability.");Retention?.Validate();return this;}
}
public sealed record EnterprisePolicySnapshot(Guid Id,DateTimeOffset EvaluatedAtUtc,EnterprisePolicyState State,string Fingerprint,IReadOnlyDictionary<EnterpriseCapability,EnterprisePolicyRule>Rules,EnterpriseRetentionPolicy Retention,string Explanation,string? FailureCode=null)
{
 public bool IsAllowed(EnterpriseCapability capability)=>Rules.TryGetValue(capability,out var rule)&&rule.Allowed;
 public static EnterprisePolicySnapshot SafeDefault(DateTimeOffset now)=>new(Guid.NewGuid(),now,EnterprisePolicyState.Active,"built-in",Enum.GetValues<EnterpriseCapability>().ToDictionary(x=>x,x=>new EnterprisePolicyRule(true,false,"Built-in default","Allowed unless an organization policy restricts this capability.")).ToFrozenDictionary(),EnterpriseRetentionPolicy.Default,"No organization policy restricts WAID capabilities.");
}
public sealed record EnterprisePolicyDecision(EnterpriseCapability Capability,bool Allowed,bool Locked,string Source,string Explanation,string? FailureCode=null);
public interface IEnterprisePolicyProvider{int Priority{get;}string Name{get;}Task<EnterprisePolicyDocument?>ReadAsync(CancellationToken token);}
public interface IEnterprisePolicyRepository{Task SaveAsync(EnterprisePolicySnapshot snapshot,CancellationToken token);Task<IReadOnlyList<EnterprisePolicySnapshot>>GetRecentAsync(int count,CancellationToken token);}
public interface IEnterprisePolicyService
{
 EnterprisePolicySnapshot Current{get;}
 Task<EnterprisePolicySnapshot>RefreshAsync(CancellationToken token);
 EnterprisePolicyDecision Evaluate(EnterpriseCapability capability);
 Task<EnterprisePolicySnapshot>RollbackAsync(Guid snapshotId,CancellationToken token);
}
public sealed class EnterprisePolicyException(string code,string message,string recoveryAction):InvalidOperationException(message){public string Code{get;}=code;public string RecoveryAction{get;}=recoveryAction;}