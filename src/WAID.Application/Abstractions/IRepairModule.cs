using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;

namespace WAID.Application.Abstractions;

public interface IRepairModule
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    RepairPolicy Policy { get; }
    Task<RepairPlan> CreatePlanAsync(DiagnosticFinding? finding, CancellationToken cancellationToken);
    Task<RepairResult> ExecuteAsync(RepairExecutionContext context, CancellationToken cancellationToken);
}

public sealed record RepairExecutionContext(
    Guid TransactionId,
    DiagnosticFinding? Finding,
    RepairPlan Plan,
    string? BackupLocation);
