using WAID.Application.Abstractions;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public sealed class RepairHistory(IRepairHistoryRepository repository)
{
    public Task<IReadOnlyList<RepairHistoryEntry>> GetRecentAsync(int count, CancellationToken cancellationToken) =>
        repository.GetRecentAsync(count, cancellationToken);
}
