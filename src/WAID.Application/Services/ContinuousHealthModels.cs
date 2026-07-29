using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;
using WAID.Health;

namespace WAID.Application.Services;

public enum MonitoringState { Stopped, Starting, Running, PausedBatterySaver, PausedHighLoad, Faulted }
public enum ScheduleFrequency { Daily, Weekly, Custom }

public sealed record MonitoringOptions(TimeSpan Interval, bool PauseOnBatterySaver = true, bool PauseOnHighLoad = true)
{
    public MonitoringOptions Validate() => Interval < TimeSpan.FromMinutes(1) || Interval > TimeSpan.FromDays(1)
        ? throw new InvalidOperationException("Monitoring interval must be between one minute and one day.") : this;
}

public sealed record ScanSchedule(bool Enabled, ScheduleFrequency Frequency, TimeSpan CustomInterval, DayOfWeek WeeklyDay,
    TimeOnly DailyTime, bool OnlyWhenPluggedIn, bool OnlyWhenIdle, DateTimeOffset? LastRunUtc = null)
{
    public TimeSpan Interval => Frequency switch { ScheduleFrequency.Daily => TimeSpan.FromDays(1), ScheduleFrequency.Weekly => TimeSpan.FromDays(7), _ => CustomInterval };
    public ScanSchedule Validate() => Frequency == ScheduleFrequency.Custom && (CustomInterval < TimeSpan.FromMinutes(15) || CustomInterval > TimeSpan.FromDays(30))
        ? throw new InvalidOperationException("Custom schedule interval must be between 15 minutes and 30 days.") : this;
}

public sealed record HealthSnapshot(Guid Id, DateTimeOffset CapturedAtUtc, HealthScore Health,
    IReadOnlyCollection<DiagnosticFinding> Findings, MonitoringState State);

public sealed record CollectedEvidence(string Source, string Code, DateTimeOffset CollectedAtUtc,
    IReadOnlyDictionary<string, string> Values);

public sealed record CrashRecord(string FileName, DateTimeOffset CrashTimeUtc, uint? BugCheckCode,
    string? SuspectedModule, long FileSize, string Explanation, IReadOnlyCollection<string> NextSteps);
public sealed record CrashGroup(string Key, int Count, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc,
    double CrashesPerWeek, IReadOnlyCollection<CrashRecord> Crashes);

public sealed record PrioritizedRepair(string RepairId, string Title, int Order, int ExpectedBenefit,
    SafetyLevel RiskLevel, bool RequiresAdministrator, bool RestartRequired, bool SupportsRollback,
    int Confidence, int EvidenceStrength)
{
    public RepairRankingFactors? RankingFactors { get; init; }
    public RepairCandidateStatus CandidateStatus { get; init; } = RepairCandidateStatus.Eligible;
    public string RankingExplanation { get; init; } = "Legacy ranking metadata is unavailable.";
    public IReadOnlyList<string> Prerequisites { get; init; } = [];
    public IReadOnlyList<string> Conflicts { get; init; } = [];
    public int EstimatedDowntimeMinutes { get; init; }
    public bool AutoSelectable { get; init; }
    public string RankingVersion { get; init; } = RepairPrioritizationEngine.RankingVersion;
}

public sealed record RepairApproval(Guid Id, string RepairId, DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ApprovedAtUtc, bool Approved, string EvidenceSummary, IReadOnlyCollection<string> PlannedActions);

public sealed record DiagnosticReportData(string ApplicationVersion, DateTimeOffset GeneratedAtUtc,
    string SystemSummary, AIReport? Diagnosis, IReadOnlyCollection<CollectedEvidence> Evidence,
    IReadOnlyCollection<PrioritizedRepair> RepairPlan, IReadOnlyCollection<RepairHistoryEntry> RepairHistory,
    IReadOnlyCollection<string> KnownLimitations, string RedactionNotice);

public interface IHealthSnapshotRepository { Task SaveAsync(HealthSnapshot snapshot, CancellationToken token); Task<IReadOnlyList<HealthSnapshot>> GetRecentAsync(int count, CancellationToken token); }
public interface IScanScheduleRepository { Task<ScanSchedule> GetAsync(CancellationToken token); Task SaveAsync(ScanSchedule schedule, CancellationToken token); }
public interface IRepairApprovalRepository { Task SaveAsync(RepairApproval approval, CancellationToken token); Task<IReadOnlyList<RepairApproval>> GetRecentAsync(int count, CancellationToken token); }
public interface ISystemConditionService { bool IsBatterySaverEnabled(); bool IsPluggedIn(); bool IsSystemIdle(); double GetSystemLoadPercent(); }
public interface IStartupLaunchService { bool IsEnabled(); void SetEnabled(bool enabled); }
public interface IDiagnosticReportExporter { Task<string> ExportJsonAsync(DiagnosticReportData report, CancellationToken token); Task<string> ExportHtmlAsync(DiagnosticReportData report, CancellationToken token); Task<string> ExportPackageAsync(DiagnosticReportData report, CancellationToken token); }
public interface IPdfReportExporter { Task<string> ExportPdfAsync(DiagnosticReportData report, CancellationToken token); }
