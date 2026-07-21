using WAID.Application.Services;
using WAID.Domain.Diagnostics;

namespace WAID.Application.Abstractions;

public interface IScanRunRepository
{
    Task SaveAsync(ScanSession session, IReadOnlyCollection<ScannerExecutionRecord> executions, CancellationToken token);
    Task<IReadOnlyList<ScannerExecutionRecord>> GetExecutionsAsync(Guid sessionId, CancellationToken token);
}
