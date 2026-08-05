using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public sealed class RepairExecutor(
    RepairRegistry registry,
    IAdministratorService administratorService,
    IRestorePointManager restorePointManager,
    IBackupManager backupManager,
    IRollbackManager rollbackManager,
    IRepairHistoryRepository historyRepository,
    TimeProvider timeProvider,
    ILogger<RepairExecutor> logger,
    IAuditTrailService? auditTrail = null,
    IOperationContextAccessor? operationContext = null,
    DigitalTwinService? digitalTwin = null)
{
    public async Task<RepairTransaction> ExecuteAsync(
        string repairId,
        DiagnosticFinding? finding,
        bool userConfirmed,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGet(repairId, out var module) || module is null)
            throw new KeyNotFoundException($"Repair module '{repairId}' is not registered.");

        using var operation = operationContext is null ? null : logger.BeginWaidOperation(operationContext, "Repair");
        var transaction = new RepairTransaction(Guid.NewGuid(), module.Id, timeProvider.GetUtcNow());
        logger.LogInformation(WaidEventIds.RepairRequested, "Repair {RepairId} transaction {TransactionId} requested", module.Id, transaction.Id);
        await AuditAsync(module, transaction, AuditResult.Requested, "Repair execution was requested.", cancellationToken).ConfigureAwait(false);

        if (!userConfirmed)
            return await RejectAsync(transaction, "Repair was not confirmed by the user.", cancellationToken).ConfigureAwait(false);

        if (module.Policy.RequiresAdministrator && !administratorService.IsAdministrator())
            return await RejectAsync(transaction, "Administrator privileges are required.", cancellationToken).ConfigureAwait(false);

        BackupSnapshot? snapshot = null;
        DigitalTwinSnapshot? twinBefore = null;
        try
        {
            twinBefore = await TryCaptureTwinAsync(SystemSnapshotPurpose.PreRepair, transaction.Id, null, cancellationToken).ConfigureAwait(false);
            transaction.BeginPreparation();
            var plan = await module.CreatePlanAsync(finding, cancellationToken).ConfigureAwait(false);
            foreach (var resource in plan.Resources) resource.Validate();
            transaction.AddEvent($"Plan created: {plan.Description}");

            if (module.Policy.RequiresRestorePoint)
            {
                if (await restorePointManager.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    var description = $"WAID before {module.DisplayName} ({transaction.Id:N})";
                    var restorePoint = await restorePointManager.CreateAsync(description, cancellationToken).ConfigureAwait(false);
                    if (!restorePoint.Succeeded)
                        return await FailPreparationAsync(transaction, $"System Restore Point creation failed: {restorePoint.Error}", cancellationToken).ConfigureAwait(false);
                    transaction.RecordRestorePoint(restorePoint.Description);
                }
                else
                {
                    transaction.AddEvent("System Restore is unavailable; repair continued with resource backup.");
                    logger.LogWarning("System Restore is unavailable for transaction {TransactionId}", transaction.Id);
                }
            }

            if (module.Policy.RequiresBackup && plan.Resources.Count > 0)
            {
                snapshot = await backupManager.CreateAsync(transaction.Id, plan.Resources, cancellationToken).ConfigureAwait(false);
                transaction.RecordBackup(snapshot.Location);
                if (snapshot.Items.Count != plan.Resources.Count || !snapshot.IsValidated || snapshot.Capability < RecoveryCapabilityLevel.ResourceBackup)
                    return await FailPreparationAsync(
                        transaction,
                        $"Required backup was incomplete: {string.Join("; ", snapshot.Warnings)}",
                        cancellationToken).ConfigureAwait(false);
                foreach (var warning in snapshot.Warnings) transaction.AddEvent($"Backup warning: {warning}");
            }

            transaction.BeginExecution();
            logger.LogInformation(WaidEventIds.RepairRequested, "Executing repair {RepairId} transaction {TransactionId}", module.Id, transaction.Id);
            var result = await module.ExecuteAsync(
                new RepairExecutionContext(transaction.Id, finding, plan, snapshot?.Location),
                cancellationToken).ConfigureAwait(false);
            foreach (var action in result.Actions)
            {
                transaction.AddEvent($"Repair action: {action}");
                logger.LogInformation(
                    "Repair {RepairId} transaction {TransactionId}: {Action}",
                    module.Id, transaction.Id, action);
            }
            transaction.Complete(result, timeProvider.GetUtcNow());

            if (!result.Succeeded && module.Policy.SupportsRollback && snapshot is not null)
            {
                var rollback = await rollbackManager.RollbackAsync(snapshot, cancellationToken).ConfigureAwait(false);
                var rollbackVerified = rollback.Succeeded && rollback.Verified;
                var rolledBackResult = result.WithRollback(rollbackVerified);
                transaction.MarkRolledBack(rolledBackResult, timeProvider.GetUtcNow());
                foreach (var action in rollback.Actions) transaction.AddEvent($"Rollback: {action}");
                foreach (var error in rollback.Errors) transaction.AddEvent($"Rollback error: {error}");
                await AuditAsync(module, transaction, rollbackVerified ? AuditResult.RolledBack : AuditResult.Failed, "Rollback completed after an unsuccessful repair.", CancellationToken.None).ConfigureAwait(false);
            }

            await TryCaptureTwinAsync(SystemSnapshotPurpose.PostRepair, transaction.Id, twinBefore?.Id, CancellationToken.None).ConfigureAwait(false);
            await historyRepository.SaveAsync(transaction, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(WaidEventIds.RepairCompleted,
                "Repair {RepairId} transaction {TransactionId} completed with status {Status}",
                module.Id, transaction.Id, transaction.Status);
            var terminalAuditResult = transaction.Status == RepairTransactionStatus.RolledBack
                ? AuditResult.RolledBack
                : result.Succeeded ? AuditResult.Succeeded : AuditResult.Failed;
            await AuditAsync(module, transaction, terminalAuditResult, result.Summary, CancellationToken.None).ConfigureAwait(false);
            return transaction;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (transaction.Status is RepairTransactionStatus.Preparing or RepairTransactionStatus.Executing)
            {
                transaction.Fail(RepairResult.Failure("Repair was cancelled.", "Cancellation was requested during preparation or execution."), timeProvider.GetUtcNow());
                if (module.Policy.SupportsRollback && snapshot is not null)
                {
                    var rollback = await rollbackManager.RollbackAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                    transaction.MarkRolledBack(transaction.Result!.WithRollback(rollback.Succeeded && rollback.Verified), timeProvider.GetUtcNow());
                    foreach (var action in rollback.Actions) transaction.AddEvent($"Cancellation rollback: {action}");
                    foreach (var error in rollback.Errors) transaction.AddEvent($"Cancellation rollback error: {error}");
                }
            }
            else transaction.Cancel(timeProvider.GetUtcNow());
            await historyRepository.SaveAsync(transaction, CancellationToken.None).ConfigureAwait(false);
            logger.LogWarning("Repair {RepairId} transaction {TransactionId} was cancelled with status {Status}", module.Id, transaction.Id, transaction.Status);
            await AuditAsync(module, transaction, transaction.Status == RepairTransactionStatus.RolledBack ? AuditResult.RolledBack : AuditResult.Cancelled, "Repair execution was cancelled; rollback policy was applied.", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Repair {RepairId} transaction {TransactionId} failed", module.Id, transaction.Id);
            if (transaction.Status is RepairTransactionStatus.Preparing or RepairTransactionStatus.Executing)
                transaction.Fail(RepairResult.Failure("Repair failed unexpectedly.", exception.Message), timeProvider.GetUtcNow());

            if (module.Policy.SupportsRollback && snapshot is not null)
            {
                var rollback = await rollbackManager.RollbackAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                transaction.MarkRolledBack(transaction.Result!.WithRollback(rollback.Succeeded && rollback.Verified), timeProvider.GetUtcNow());
            }
            await historyRepository.SaveAsync(transaction, CancellationToken.None).ConfigureAwait(false);
            await AuditAsync(module, transaction, transaction.Status == RepairTransactionStatus.RolledBack ? AuditResult.RolledBack : AuditResult.Failed, "Repair failed unexpectedly.", CancellationToken.None).ConfigureAwait(false);
            return transaction;
        }
    }

    private async Task<DigitalTwinSnapshot?> TryCaptureTwinAsync(SystemSnapshotPurpose purpose, Guid transactionId, Guid? relatedId, CancellationToken token)
    {
        if (digitalTwin is null) return null;
        try { return await digitalTwin.CaptureAsync(purpose, true, transactionId, relatedId, token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning("System snapshot {Purpose} failed with {FailureType}; repair safety flow continues", purpose, exception.GetType().Name); return null; }
    }

    private async Task<RepairTransaction> RejectAsync(RepairTransaction transaction, string message, CancellationToken token)
    {
        transaction.BeginPreparation();
        transaction.Fail(RepairResult.Failure(message), timeProvider.GetUtcNow());
        await historyRepository.SaveAsync(transaction, token).ConfigureAwait(false);
        logger.LogWarning("Repair transaction {TransactionId} rejected: {Reason}", transaction.Id, message);
        if (registry.TryGet(transaction.RepairId, out var module) && module is not null)
            await AuditAsync(module, transaction, AuditResult.Rejected, message, CancellationToken.None).ConfigureAwait(false);
        return transaction;
    }

    private async Task<RepairTransaction> FailPreparationAsync(RepairTransaction transaction, string message, CancellationToken token)
    {
        transaction.Fail(RepairResult.Failure("Repair safety preparation failed.", message), timeProvider.GetUtcNow());
        await historyRepository.SaveAsync(transaction, token).ConfigureAwait(false);
        logger.LogError("Repair transaction {TransactionId} preparation failed: {Reason}", transaction.Id, message);
        if (registry.TryGet(transaction.RepairId, out var module) && module is not null)
            await AuditAsync(module, transaction, AuditResult.Failed, message, CancellationToken.None).ConfigureAwait(false);
        return transaction;
    }

    private async Task AuditAsync(IRepairModule module, RepairTransaction transaction, AuditResult result, string detail, CancellationToken token)
    {
        if (auditTrail is null) return;
        var context = operationContext?.Current;
        var write = await auditTrail.AppendAsync(new(Guid.NewGuid(), timeProvider.GetUtcNow(), AuditActor.User,
            result is AuditResult.Rejected ? "RepairPolicyDecision" : "RepairExecution", module.Id, result, module.Policy.SafetyLevel,
            module.Policy.RequiresAdministrator, module.Policy.SupportsRollback, context?.CorrelationId ?? transaction.Id,
            context?.OperationId ?? transaction.Id, detail), token).ConfigureAwait(false);
        if (!write.Succeeded) logger.LogWarning(WaidEventIds.RepairPolicyDecision, "Audit event {AuditRecordId} could not be stored: {FailureCode}", write.RecordId, write.FailureCode);
    }
}
