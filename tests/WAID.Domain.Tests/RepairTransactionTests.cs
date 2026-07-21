using WAID.Domain.Repairs;

namespace WAID.Domain.Tests;

public sealed class RepairTransactionTests
{
    [Fact]
    public void Successful_transaction_follows_required_lifecycle()
    {
        var created = DateTimeOffset.UtcNow;
        var transaction = new RepairTransaction(Guid.NewGuid(), "waid.test", created);

        transaction.BeginPreparation();
        transaction.RecordBackup("C:\\backup");
        transaction.BeginExecution();
        transaction.Complete(RepairResult.Success("Completed"), created.AddMinutes(1));

        Assert.Equal(RepairTransactionStatus.Succeeded, transaction.Status);
        Assert.True(transaction.Result!.Succeeded);
        Assert.NotNull(transaction.CompletedAtUtc);
    }

    [Fact]
    public void Rollback_policy_requires_backup() =>
        Assert.Throws<InvalidOperationException>(() =>
            new RepairPolicy(SafetyLevel.High, RequiresBackup: false, SupportsRollback: true).Validate());

    [Fact]
    public void Failed_transaction_can_record_successful_rollback()
    {
        var transaction = new RepairTransaction(Guid.NewGuid(), "waid.test", DateTimeOffset.UtcNow);
        transaction.BeginPreparation();
        transaction.BeginExecution();
        transaction.Complete(RepairResult.Failure("Failed"), DateTimeOffset.UtcNow);

        transaction.MarkRolledBack(transaction.Result!.WithRollback(true), DateTimeOffset.UtcNow);

        Assert.Equal(RepairTransactionStatus.RolledBack, transaction.Status);
        Assert.True(transaction.Result!.RollbackSucceeded);
    }

    [Fact]
    public void Failed_rollback_does_not_claim_transaction_was_rolled_back()
    {
        var transaction = new RepairTransaction(Guid.NewGuid(), "waid.test", DateTimeOffset.UtcNow);
        transaction.BeginPreparation();
        transaction.BeginExecution();
        transaction.Complete(RepairResult.Failure("Failed"), DateTimeOffset.UtcNow);

        transaction.MarkRolledBack(transaction.Result!.WithRollback(false), DateTimeOffset.UtcNow);

        Assert.Equal(RepairTransactionStatus.Failed, transaction.Status);
        Assert.True(transaction.Result!.RollbackAttempted);
        Assert.False(transaction.Result.RollbackSucceeded);
    }
}
