using System.Security.Cryptography;
using System.Text;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public enum RepairAuditKind { Plan, Evidence, Approval, Execution, Validation, Outcome, Rollback, Feedback }
public enum RepairOutcomeClass { Unknown, ValidatedSuccess, ValidationFailed, RolledBack, Failed, Cancelled }
public sealed record RepairAuditEntry(Guid Id,Guid OrchestrationId,Guid? TransactionId,string RepairId,RepairAuditKind Kind,DateTimeOffset OccurredAtUtc,string Actor,string Summary,IReadOnlyList<string> Evidence,RepairOutcomeClass Outcome,bool Immutable=true);
public sealed record RepairOutcomeAggregate(string RepairId,int Total,int ValidatedSuccesses,int ValidationFailures,int Rollbacks,int Failures,int Cancellations,double ValidatedSuccessRate,string AggregateVersion,DateTimeOffset RebuiltAtUtc,string Interpretation);
public sealed record RepairAuditQuery(string? RepairId=null,RepairAuditKind? Kind=null,RepairOutcomeClass? Outcome=null,DateTimeOffset? FromUtc=null,DateTimeOffset? ToUtc=null,int Limit=100);
public interface IRepairOutcomeRepository
{
 Task AppendAsync(RepairAuditEntry entry,CancellationToken token);
 Task<IReadOnlyList<RepairAuditEntry>> QueryAsync(RepairAuditQuery query,CancellationToken token);
 Task ReplaceAggregatesAsync(IReadOnlyList<RepairOutcomeAggregate> aggregates,CancellationToken token);
 Task<IReadOnlyList<RepairOutcomeAggregate>> GetAggregatesAsync(CancellationToken token);
}
public interface IRepairOutcomeExportService{Task<string> ExportAsync(RepairAuditQuery query,CancellationToken token);}
public interface IRepairOutcomeRecorder
{
 Task RecordAsync(RepairOrchestrationRecord record,CancellationToken token);
 Task RecordFeedbackAsync(Guid orchestrationId,string repairId,string actor,string feedback,CancellationToken token);
 Task<IReadOnlyList<RepairOutcomeAggregate>> RebuildAggregatesAsync(CancellationToken token);
}
public sealed class RepairOutcomeRecorder(IRepairOutcomeRepository repository,TimeProvider time):IRepairOutcomeRecorder
{
 public const string AggregateVersion="repair-outcomes-1.0";
 public async Task RecordAsync(RepairOrchestrationRecord record,CancellationToken token)
 {
  ArgumentNullException.ThrowIfNull(record);
  var kind=Kind(record.Stage);var outcome=Classify(record);
  var evidence=(record.Validation?.Evidence??[]).Concat(record.Simulation?.Effects.Select(x=>$"{x.Kind}:{x.Target}:{x.Certainty}")??[]).Select(Redact).Where(x=>x.Length>0).Distinct(StringComparer.Ordinal).Take(32).ToArray();
  var actor=Redact(record.Approval?.Actor??"WAID");
  var entry=new RepairAuditEntry(StableId(record.Id,kind,record.UpdatedAtUtc),record.Id,record.TransactionId,record.RepairId,kind,record.UpdatedAtUtc,actor,Redact(record.Outcome),evidence,outcome);
  await repository.AppendAsync(entry,token).ConfigureAwait(false);
 }
 public async Task RecordFeedbackAsync(Guid orchestrationId,string repairId,string actor,string feedback,CancellationToken token)
 {
  if(orchestrationId==Guid.Empty||string.IsNullOrWhiteSpace(repairId)||string.IsNullOrWhiteSpace(feedback))throw new ArgumentException("A repair, orchestration, and feedback are required.");
  var now=time.GetUtcNow();await repository.AppendAsync(new(StableId(orchestrationId,RepairAuditKind.Feedback,now),orchestrationId,null,repairId,RepairAuditKind.Feedback,now,Redact(actor),Redact(feedback),[],RepairOutcomeClass.Unknown),token).ConfigureAwait(false);
 }
 public async Task<IReadOnlyList<RepairOutcomeAggregate>> RebuildAggregatesAsync(CancellationToken token)
 {
  var entries=await repository.QueryAsync(new(Limit:10000),token).ConfigureAwait(false);var now=time.GetUtcNow();
  var aggregates=entries.Where(x=>x.Outcome!=RepairOutcomeClass.Unknown).GroupBy(x=>x.RepairId,StringComparer.OrdinalIgnoreCase).Select(g=>{var terminal=g.GroupBy(x=>x.OrchestrationId).Select(x=>x.OrderByDescending(e=>e.OccurredAtUtc).First()).ToArray();var success=terminal.Count(x=>x.Outcome==RepairOutcomeClass.ValidatedSuccess);var total=terminal.Length;return new RepairOutcomeAggregate(g.Key,total,success,terminal.Count(x=>x.Outcome==RepairOutcomeClass.ValidationFailed),terminal.Count(x=>x.Outcome==RepairOutcomeClass.RolledBack),terminal.Count(x=>x.Outcome==RepairOutcomeClass.Failed),terminal.Count(x=>x.Outcome==RepairOutcomeClass.Cancelled),total==0?0:Math.Round(success*100d/total,1),AggregateVersion,now,$"Observed {success} validation-backed success(es) across {total} completed workflow(s). This statistic does not authorize or create repairs.");}).OrderBy(x=>x.RepairId).ToArray();
  await repository.ReplaceAggregatesAsync(aggregates,token).ConfigureAwait(false);return aggregates;
 }
 private static RepairAuditKind Kind(RepairOrchestrationStage s)=>s switch{RepairOrchestrationStage.Requested or RepairOrchestrationStage.Assessing or RepairOrchestrationStage.Assessed=>RepairAuditKind.Plan,RepairOrchestrationStage.Simulated=>RepairAuditKind.Evidence,RepairOrchestrationStage.AwaitingApproval or RepairOrchestrationStage.Approved=>RepairAuditKind.Approval,RepairOrchestrationStage.Preparing or RepairOrchestrationStage.Executing=>RepairAuditKind.Execution,RepairOrchestrationStage.Validating=>RepairAuditKind.Validation,RepairOrchestrationStage.RollingBack or RepairOrchestrationStage.RolledBack=>RepairAuditKind.Rollback,_=>RepairAuditKind.Outcome};
 private static RepairOutcomeClass Classify(RepairOrchestrationRecord r)=>r.Stage switch{RepairOrchestrationStage.Committed when r.Validation?.Succeeded==true=>RepairOutcomeClass.ValidatedSuccess,RepairOrchestrationStage.RecoveryRequired when r.Validation?.Succeeded==false=>RepairOutcomeClass.ValidationFailed,RepairOrchestrationStage.RolledBack=>RepairOutcomeClass.RolledBack,RepairOrchestrationStage.Failed or RepairOrchestrationStage.RecoveryRequired=>RepairOutcomeClass.Failed,RepairOrchestrationStage.Cancelled=>RepairOutcomeClass.Cancelled,_=>RepairOutcomeClass.Unknown};
 private static Guid StableId(Guid orchestration,RepairAuditKind kind,DateTimeOffset time){var bytes=SHA256.HashData(Encoding.UTF8.GetBytes($"{orchestration:N}|{kind}|{time:O}"));return new Guid(bytes[..16]);}
 private static string Redact(string? value){if(string.IsNullOrWhiteSpace(value))return string.Empty;var text=value.Trim();foreach(var key in new[]{"password","token","secret","productkey","product key","api_key","api key","authorization","bearer"}){var start=text.IndexOf(key,StringComparison.OrdinalIgnoreCase);if(start>=0)text=text[..start]+key+"=[REDACTED]";}var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);if(!string.IsNullOrWhiteSpace(profile))text=text.Replace(profile,"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);return text.Length<=2048?text:text[..2048];}
}
