using WAID.Domain.Diagnostics;
namespace WAID.Application.Abstractions;
public interface ISystemScanner { string Id { get; } string DisplayName { get; } Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken cancellationToken); }
public sealed record ScanContext(Guid SessionId, bool IsAdministrator, DateTimeOffset StartedAtUtc);
