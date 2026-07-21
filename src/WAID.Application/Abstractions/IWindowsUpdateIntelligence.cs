namespace WAID.Application.Abstractions;
public enum UpdateAttemptState{Succeeded,Failed,Retried,Pending,Unknown} public enum UpdateCause{Network,Servicing,Policy,Storage,Reboot,Service,Unknown}
public sealed record UpdateAttempt(DateTimeOffset OccurredAtUtc,string Title,string? KnowledgeBaseId,string ErrorCode,UpdateAttemptState State,string SourceReference);
public sealed record UpdateServiceState(string Name,string State,string StartMode,string SourceReference);
public sealed record UpdatePolicyState(bool IsManaged,bool UpdatesDisabled,string Detail,string SourceReference);
public sealed record ServicingState(bool ComponentStoreRepairable,bool CorruptionDetected,string DismSummary,string CbsSummary,string SourceReference);
public sealed record UpdateEvidenceSnapshot(DateTimeOffset CollectedAtUtc,IReadOnlyList<UpdateAttempt> Attempts,IReadOnlyList<UpdateServiceState> Services,UpdatePolicyState Policy,ServicingState Servicing,bool RebootPending,long SystemDriveFreeBytes,IReadOnlyList<string> EventCodes,IReadOnlyList<string> Limitations);
public sealed record UpdateEvidence(string Signal,string Value,string SourceReference,DateTimeOffset ObservedAtUtc);
public sealed record UpdateDiagnosis(UpdateCause Cause,string Title,string Explanation,double Confidence,string Severity,IReadOnlyList<UpdateEvidence> Evidence);
public sealed record UpdateRepairStep(int Order,string Id,string Title,string Description,IReadOnlyList<string> Prerequisites,bool RequiresAdministrator,bool RequiresRestart,bool RemovesUpdates,bool RequiresApproval);
public sealed record UpdateRepairPlan(IReadOnlyList<UpdateRepairStep> Steps,string SafetyNotice);
public sealed record WindowsUpdateHealthReport(Guid Id,DateTimeOffset GeneratedAtUtc,IReadOnlyList<UpdateAttempt> Attempts,IReadOnlyList<UpdateDiagnosis> Diagnoses,UpdateRepairPlan Plan,bool RebootPending,IReadOnlyList<string> Limitations);
public sealed record UpdateRepairSimulation(bool Allowed,string Message,IReadOnlyList<string> Commands,bool RequiresAdministrator,bool RequiresRestart);
public interface IWindowsUpdateEvidenceProvider{Task<UpdateEvidenceSnapshot> CollectAsync(CancellationToken cancellationToken);} public interface IWindowsUpdateHealthRepository{Task<WindowsUpdateHealthReport?> GetLatestAsync(CancellationToken cancellationToken);Task SaveAsync(UpdateEvidenceSnapshot snapshot,WindowsUpdateHealthReport report,CancellationToken cancellationToken);} public interface IWindowsUpdateIntelligence{Task<WindowsUpdateHealthReport> AnalyzeAsync(CancellationToken cancellationToken);} public interface IUpdateRepairPlanner{UpdateRepairSimulation Simulate(UpdateRepairStep step,bool explicitlyApproved);}
