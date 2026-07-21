namespace WAID.Application.Abstractions;

public enum StartupSource { StartupFolder, RunKey, Service, ScheduledTask, ShellExtension, LoginTrigger, StartupApplication }
public enum StartupImpact { Unknown, Low, Medium, High }
public enum StartupConcern { None, Performance, MissingTarget, Failed, SecurityReview }

public sealed record StartupEntry(string Id,string Name,StartupSource Source,string Publisher,string Command,string ExecutablePath,
    bool Enabled,bool TargetExists,bool HasRecentFailure,bool IsCritical,bool IsMicrosoftSigned,double? MeasuredImpactMilliseconds,string SourceReference);
public sealed record BootMeasurement(DateTimeOffset BootedAtUtc,double? MainPathMilliseconds,double? PostBootMilliseconds,string SourceReference);
public sealed record StartupSnapshot(DateTimeOffset CollectedAtUtc,IReadOnlyList<StartupEntry> Entries,IReadOnlyList<BootMeasurement> BootMeasurements,IReadOnlyList<string> Limitations);
public sealed record StartupEvidence(string Signal,string Value,string SourceReference,DateTimeOffset ObservedAtUtc);
public sealed record StartupRecommendation(string EntryId,string EntryName,StartupConcern Concern,StartupImpact Impact,string Explanation,
    IReadOnlyList<StartupEvidence> Evidence,bool Reversible,bool RequiresAdministrator,string ActionPreview,string RollbackPreview,bool IsProtected);
public sealed record StartupChange(string EntryId,string Name,string ChangeType,string PreviousValue,string CurrentValue,DateTimeOffset DetectedAtUtc);
public sealed record BootHealthReport(Guid Id,DateTimeOffset GeneratedAtUtc,IReadOnlyList<StartupEntry> Entries,IReadOnlyList<BootMeasurement> BootMeasurements,
    IReadOnlyList<StartupRecommendation> Recommendations,IReadOnlyList<StartupChange> Changes,IReadOnlyList<string> Limitations);
public sealed record StartupActionSimulation(bool Allowed,bool WouldChangeState,string Message,string RollbackMetadata);

public interface IStartupInventoryProvider { Task<StartupSnapshot> CollectAsync(CancellationToken cancellationToken); }
public interface IBootHealthRepository { Task<BootHealthReport?> GetLatestAsync(CancellationToken cancellationToken); Task SaveAsync(StartupSnapshot snapshot,BootHealthReport report,CancellationToken cancellationToken); }
public interface IStartupBootAnalyzer { Task<BootHealthReport> AnalyzeAsync(CancellationToken cancellationToken); }
public interface IStartupActionPlanner { StartupActionSimulation SimulateDisable(StartupEntry entry); StartupActionSimulation SimulateRollback(StartupEntry entry,string rollbackMetadata); }
