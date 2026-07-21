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
    ILogger<RepairExecutor> logger)
{
    public async Task<RepairTransaction> ExecuteAsync(
        string repairId,
        DiagnosticFinding? finding,
        bool userConfirmed,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGet(repairId, out var module) || module is null)
            throw new KeyNotFoundException($"Repair module '{repairId}' is not registered.");

        var transaction = new RepairTransaction(Guid.NewGuid(), module.Id, timeProvider.GetUtcNow());
        logger.LogInformation("Repair {RepairId} transaction {TransactionId} requested", module.Id, transaction.Id);

        if (!userConfirmed)
            return await RejectAsync(transaction, "Repair was not confirmed by the user.", cancellationToken).ConfigureAwait(false);

        if (module.Policy.RequiresAdministrator && !administratorService.IsAdministrator())
            return await RejectAsync(transaction, "Administrator privileges are required.", cancellationToken).ConfigureAwait(false);

        BackupSnapshot? snapshot = null;
        try
        {
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
                if (snapshot.Items.Count != plan.Resources.Count)
                    return await FailPreparationAsync(
                        transaction,
                        $"Required backup was incomplete: {string.Join("; ", snapshot.Warnings)}",
                        cancellationToken).ConfigureAwait(false);
                foreach (var warning in snapshot.Warnings) transaction.AddEvent($"Backup warning: {warning}");
            }

            transaction.BeginExecution();
            logger.LogInformation("Executing repair {RepairId} transaction {TransactionId}", module.Id, transaction.Id);
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
                var rolledBackResult = result.WithRollback(rollback.Succeeded);
                transaction.MarkRolledBack(rolledBackResult, timeProvider.GetUtcNow());
                foreach (var action in rollback.Actions) transaction.AddEvent($"Rollback: {action}");
                foreach (var error in rollback.Errors) transaction.AddEvent($"Rollback error: {error}");
            }

            await historyRepository.SaveAsync(transaction, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Repair {RepairId} transaction {TransactionId} completed with status {Status}",
                module.Id, transaction.Id, transaction.Status);
            return transaction;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            transaction.Cancel(timeProvider.GetUtcNow());
            await historyRepository.SaveAsync(transaction, CancellationToken.None).ConfigureAwait(false);
            logger.LogWarning("Repair {RepairId} transaction {TransactionId} was cancelled", module.Id, transaction.Id);
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
                transaction.MarkRolledBack(transaction.Result!.WithRollback(rollback.Succeeded), timeProvider.GetUtcNow());
            }
            await historyRepository.SaveAsync(transaction, CancellationToken.None).ConfigureAwait(false);
            return transaction;
        }
    }

    private async Task<RepairTransaction> RejectAsync(RepairTransaction transaction, string message, CancellationToken token)
    {
        transaction.BeginPreparation();
        transaction.Fail(RepairResult.Failure(message), timeProvider.GetUtcNow());
        await historyRepository.SaveAsync(transaction, token).ConfigureAwait(false);
        logger.LogWarning("Repair transaction {TransactionId} rejected: {Reason}", transaction.Id, message);
        return transaction;
    }

    private async Task<RepairTransaction> FailPreparationAsync(RepairTransaction transaction, string message, CancellationToken token)
    {
        transaction.Fail(RepairResult.Failure("Repair safety preparation failed.", message), timeProvider.GetUtcNow());
        await historyRepository.SaveAsync(transaction, token).ConfigureAwait(false);
        logger.LogError("Repair transaction {TransactionId} preparation failed: {Reason}", transaction.Id, message);
        return transaction;
    }
}
