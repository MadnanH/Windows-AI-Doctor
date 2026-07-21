using WAID.Domain.Diagnostics;
using WAID.Domain.Settings;
namespace WAID.Application.Abstractions;
public interface IScanRepository { Task SaveAsync(ScanSession session, CancellationToken cancellationToken); Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken cancellationToken); }
public interface ISettingsRepository { Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken); Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken); }
