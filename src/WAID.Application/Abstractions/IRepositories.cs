using WAID.Domain.Diagnostics;
using WAID.Domain.Settings;
using WAID.Diagnosis;
namespace WAID.Application.Abstractions;
public interface IScanRepository { Task SaveAsync(ScanSession session, CancellationToken cancellationToken); Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken cancellationToken); }
public interface ISettingsRepository { Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken); Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken); }
public interface IDiagnosisRepository { Task SaveAsync(Guid scanSessionId, AIReport report, CancellationToken cancellationToken); Task<AIReport?> GetLatestAsync(CancellationToken cancellationToken); }
public interface IDiagnosticsExportService { Task<string> ExportAsync(CancellationToken cancellationToken); }
