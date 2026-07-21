using Microsoft.Extensions.Logging;
using WAID.Domain.Repairs;

namespace WAID.Application.Abstractions;

public enum AuditActor { User, Application, Scheduler, Plugin }
public enum AuditResult { Requested, Approved, Rejected, Succeeded, Failed, Cancelled, RolledBack }

public sealed record AuditRecord(Guid Id, DateTimeOffset TimestampUtc, AuditActor Actor, string Action, string Target,
    AuditResult Result, SafetyLevel Risk, bool ElevationRequired, bool RollbackSupported, Guid CorrelationId,
    Guid OperationId, string Detail);
public sealed record AuditQuery(string? SearchText = null, string? Action = null, DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null, int MaximumRecords = 200);
public sealed record AuditWriteResult(bool Succeeded, Guid RecordId, string? FailureCode = null);

public interface IAuditTrailService
{
    Task<AuditWriteResult> AppendAsync(AuditRecord record, CancellationToken token);
    Task<IReadOnlyList<AuditRecord>> SearchAsync(AuditQuery query, CancellationToken token);
    Task ApplyRetentionAsync(CancellationToken token);
}

public sealed record TechnicalLogEntry(DateTimeOffset TimestampUtc, string Level, string Category, int EventId,
    Guid? CorrelationId, Guid? OperationId, string Message, string TechnicalDetail);
public sealed record TechnicalLogQuery(string? SearchText = null, string? MinimumLevel = null, int MaximumRecords = 300);
public interface ILocalDiagnosticsService
{
    Task<IReadOnlyList<TechnicalLogEntry>> SearchLogsAsync(TechnicalLogQuery query, CancellationToken token);
    Task<string> ExportSanitizedAsync(AuditQuery auditQuery, TechnicalLogQuery logQuery, CancellationToken token);
}

public sealed record WaidOperationContext(Guid CorrelationId, Guid OperationId, string Category);
public interface IOperationContextAccessor
{
    WaidOperationContext? Current { get; }
    IDisposable Begin(string category, Guid? correlationId = null, Guid? operationId = null);
}

public static class WaidEventIds
{
    public static readonly EventId ScanLifecycle = new(1000, nameof(ScanLifecycle));
    public static readonly EventId ScannerCompleted = new(1100, nameof(ScannerCompleted));
    public static readonly EventId ScannerDegraded = new(1101, nameof(ScannerDegraded));
    public static readonly EventId RepairRequested = new(2000, nameof(RepairRequested));
    public static readonly EventId RepairPolicyDecision = new(2001, nameof(RepairPolicyDecision));
    public static readonly EventId RepairCompleted = new(2002, nameof(RepairCompleted));
    public static readonly EventId RepairRollback = new(2003, nameof(RepairRollback));
    public static readonly EventId MonitoringLifecycle = new(3000, nameof(MonitoringLifecycle));
    public static readonly EventId StartupFailure = new(9000, nameof(StartupFailure));
}

public static class WaidLoggingExtensions
{
    public static IDisposable BeginWaidOperation(this ILogger logger, IOperationContextAccessor accessor, string category,
        Guid? correlationId = null, Guid? operationId = null)
    {
        var operation = accessor.Begin(category, correlationId, operationId);
        var current = accessor.Current!;
        var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = current.CorrelationId,
            ["OperationId"] = current.OperationId,
            ["OperationCategory"] = current.Category
        });
        return new CombinedScope(scope, operation);
    }

    private sealed class CombinedScope(IDisposable? loggingScope, IDisposable operationScope) : IDisposable
    {
        public void Dispose() { loggingScope?.Dispose(); operationScope.Dispose(); }
    }
}
