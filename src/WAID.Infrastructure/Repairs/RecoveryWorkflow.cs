using System.Text.Json;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Repairs;

namespace WAID.Infrastructure.Repairs;

public sealed class RecoveryWorkflow(IRecoveryArtifactRepository repository, IRollbackManager rollback, IAuditTrailService audit, TimeProvider time, ILogger<RecoveryWorkflow> logger) : IRecoveryWorkflow
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<RecoveryRollbackResult> RollbackAsync(Guid transactionId, bool explicitlyConfirmed, CancellationToken token)
    {
        if (!explicitlyConfirmed) return new(false, false, "Explicit rollback confirmation is required.");
        var artifact = await repository.GetByTransactionAsync(transactionId, token).ConfigureAwait(false);
        if (artifact is null || artifact.State != RecoveryArtifactState.Valid || artifact.ExpiresAtUtc <= time.GetUtcNow()) return new(false, false, "A current validated recovery artifact is not available.");
        var metadata = Path.Combine(artifact.Location, "snapshot.json");
        if (!File.Exists(metadata)) return new(false, false, "Recovery metadata is unavailable.");
        BackupSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize<BackupSnapshot>(await File.ReadAllTextAsync(metadata, token).ConfigureAwait(false), Json); }
        catch (JsonException) { return new(false, false, "Recovery metadata failed validation."); }
        if (snapshot is null || !string.Equals(Path.GetFullPath(snapshot.Location), Path.GetFullPath(artifact.Location), StringComparison.OrdinalIgnoreCase) || !string.Equals(snapshot.ManifestSha256, artifact.ManifestSha256, StringComparison.OrdinalIgnoreCase)) return new(false, false, "Recovery metadata failed validation.");
        var result = await rollback.RollbackAsync(snapshot, token).ConfigureAwait(false);
        await audit.AppendAsync(new(Guid.NewGuid(), time.GetUtcNow(), AuditActor.User, "RecoveryRollback", transactionId.ToString("N"), result.Verified ? AuditResult.RolledBack : AuditResult.Failed, SafetyLevel.High, true, true, transactionId, transactionId, result.VerificationDetail), CancellationToken.None).ConfigureAwait(false);
        logger.LogWarning("Explicit recovery rollback for transaction {TransactionId} completed with verified={Verified}", transactionId, result.Verified);
        return new(result.Succeeded, result.Verified, result.VerificationDetail);
    }
}