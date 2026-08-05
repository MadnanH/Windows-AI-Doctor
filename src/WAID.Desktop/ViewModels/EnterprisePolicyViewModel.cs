using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Services;
namespace WAID.Desktop.ViewModels;
public sealed record EnterprisePolicyRow(string Capability,string Status,string Source,string Explanation,bool Locked);
public sealed class EnterprisePolicyViewModel:ViewModelBase
{
 readonly IEnterprisePolicyService policy;readonly IEnterprisePolicyRepository repository;string status="Loading enterprise policy…",fingerprint="",retention="";
 public EnterprisePolicyViewModel(IEnterprisePolicyService policy,IEnterprisePolicyRepository repository){this.policy=policy;this.repository=repository;RefreshCommand=new AsyncCommand(RefreshAsync);_ = RefreshAsync();}
 public ObservableCollection<EnterprisePolicyRow>Rules{get;}=[];public ObservableCollection<string>History{get;}=[];public ICommand RefreshCommand{get;}
 public string Status{get=>status;private set=>Set(ref status,value);}public string Fingerprint{get=>fingerprint;private set=>Set(ref fingerprint,value);}public string Retention{get=>retention;private set=>Set(ref retention,value);}
 async Task RefreshAsync(){var snapshot=await policy.RefreshAsync(CancellationToken.None);await Apply(snapshot);}
 async Task Apply(EnterprisePolicySnapshot snapshot){Rules.Clear();foreach(var capability in Enum.GetValues<EnterpriseCapability>()){var x=policy.Evaluate(capability);Rules.Add(new(capability.ToString(),x.Allowed?"Allowed":"Blocked",x.Source,x.Explanation,x.Locked));}Fingerprint=snapshot.Fingerprint;Retention=$"Diagnostics {snapshot.Retention.DiagnosticDays} days • Audit {snapshot.Retention.AuditDays} days • Monitoring {snapshot.Retention.MonitoringDays} days";Status=$"{snapshot.State}: {snapshot.Explanation}";History.Clear();foreach(var x in await repository.GetRecentAsync(10,CancellationToken.None))History.Add($"{x.EvaluatedAtUtc.LocalDateTime:g} | {x.State} | {x.Fingerprint[..Math.Min(12,x.Fingerprint.Length)]} | {x.Explanation}");}
}