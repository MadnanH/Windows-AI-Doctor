using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;
namespace WAID.Application.Abstractions;
public interface IRepairAction { string Id { get; } string DisplayName { get; } bool RequiresAdministrator { get; } Task<RepairResult> ExecuteAsync(DiagnosticFinding finding, CancellationToken cancellationToken); }
