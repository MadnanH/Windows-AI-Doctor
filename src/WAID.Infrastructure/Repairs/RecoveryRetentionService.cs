using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Repairs;

public sealed class RecoveryRetentionService(string backupRoot, IRecoveryArtifactRepository repository, TimeProvider time, ILogger<RecoveryRetentionService> logger) : IRecoveryRetentionService
{
    public async Task<RecoveryCleanupResult> DeleteExpiredAsync(CancellationToken token)
    {
        var root = Path.GetFullPath(backupRoot); var deleted = 0; var errors = new List<string>();
        foreach (var artifact in await repository.GetExpiredAsync(time.GetUtcNow(), 100, token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            try { var path = Path.GetFullPath(artifact.Location); if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Artifact is outside the managed backup root."); if (Directory.Exists(path)) Directory.Delete(path, true); await repository.SaveAsync(artifact with { State = RecoveryArtifactState.Deleted, ValidationDetail = "Expired artifact deleted by retention policy." }, token).ConfigureAwait(false); deleted++; }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning("Recovery artifact cleanup failed with {FailureType}", exception.GetType().Name); errors.Add($"Artifact {artifact.Id:N} could not be deleted."); }
        }
        return new(deleted, errors.Count, errors);
    }
}