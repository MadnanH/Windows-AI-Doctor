using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;

namespace WAID.Application.Services;
public enum DashboardPresentationProfile{Simple,Technician}
public enum TechnicianLayout{Compact,Detailed}
public sealed record TechnicianPreferences(DashboardPresentationProfile Profile,TechnicianLayout Layout,string SavedFilter,DateTimeOffset UpdatedAtUtc);
public sealed record TechnicianCase(Guid Id,string CaseId,string Title,string Notes,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc);
public sealed record TechnicianIncident(Guid Id,string Title,string Severity,string Source,DateTimeOffset ObservedAtUtc,string Evidence);
public sealed record TechnicianDashboardSnapshot(DateTimeOffset GeneratedAtUtc,TechnicianPreferences Preferences,IReadOnlyList<TechnicianIncident> ActiveIncidents,IReadOnlyList<RepairOrchestrationRecord> Repairs,IReadOnlyList<RepairAuditEntry> Evidence,IReadOnlyList<TechnicianCase> Cases,string SystemOverview,int ScanQueueCount);
public interface ITechnicianWorkspaceRepository{Task<TechnicianPreferences> GetPreferencesAsync(CancellationToken token);Task SavePreferencesAsync(TechnicianPreferences value,CancellationToken token);Task<IReadOnlyList<TechnicianCase>> GetCasesAsync(CancellationToken token);Task SaveCaseAsync(TechnicianCase value,CancellationToken token);}
public sealed class TechnicianDashboardService(IScanRepository scans,IRepairOrchestrationRepository repairs,IRepairOutcomeRepository outcomes,ITechnicianWorkspaceRepository workspace,TimeProvider time)
{
 public async Task<TechnicianDashboardSnapshot> LoadAsync(CancellationToken token){var preferences=await workspace.GetPreferencesAsync(token);var sessions=await scans.GetRecentAsync(10,token);var repairRows=await repairs.GetRecentAsync(100,token);var audit=await outcomes.QueryAsync(new(Limit:500),token);var cases=await workspace.GetCasesAsync(token);var incidents=sessions.SelectMany(s=>s.Findings).OrderByDescending(x=>x.Severity).Take(500).Select(x=>new TechnicianIncident(x.Id,x.Title,x.Severity.ToString(),x.ScannerId,s_time(x),x.Description)).ToArray();return new(time.GetUtcNow(),preferences,incidents,repairRows,audit,cases,$"{sessions.Count} recent scan(s); {incidents.Length} active finding(s); {repairRows.Count} repair workflow(s).",0);}
 private static DateTimeOffset s_time(DiagnosticFinding finding)=>DateTimeOffset.MinValue;
 public Task SavePreferencesAsync(TechnicianPreferences value,CancellationToken token){if(value.SavedFilter.Length>200)throw new ArgumentOutOfRangeException(nameof(value));return workspace.SavePreferencesAsync(value with{UpdatedAtUtc=time.GetUtcNow()},token);}
 public Task SaveCaseAsync(TechnicianCase value,CancellationToken token){if(string.IsNullOrWhiteSpace(value.CaseId)||value.CaseId.Length>64||value.Title.Length>200||value.Notes.Length>4000)throw new ArgumentException("Case metadata is invalid.");return workspace.SaveCaseAsync(value with{UpdatedAtUtc=time.GetUtcNow()},token);}
}
