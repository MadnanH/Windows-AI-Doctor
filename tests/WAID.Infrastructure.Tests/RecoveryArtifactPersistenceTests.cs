using WAID.Application.Abstractions;
using WAID.Infrastructure.Persistence;

namespace WAID.Infrastructure.Tests;

public sealed class RecoveryArtifactPersistenceTests
{
    [Fact]
    public async Task Artifact_metadata_and_expiry_survive_repository_restart()
    {
        var path=Path.Combine(Path.GetTempPath(),$"waid-recovery-db-{Guid.NewGuid():N}.db");
        try { var database=new WaidDatabase($"Data Source={path};Pooling=False"); await database.InitializeAsync(CancellationToken.None); var repository=new SqliteRecoveryArtifactRepository(database); var now=DateTimeOffset.UtcNow; var artifact=new RecoveryArtifactRecord(Guid.NewGuid(),Guid.NewGuid(),@"%USERPROFILE%\WAID\Backups\artifact",new string('A',64),now,now.AddMinutes(-1),RecoveryArtifactProtection.LocalAccessRestricted,RecoveryCapabilityLevel.ResourceBackup,RecoveryArtifactState.Valid,now,"validated"); await repository.SaveAsync(artifact,CancellationToken.None); var restarted=new SqliteRecoveryArtifactRepository(database); Assert.Equal(artifact.Id,(await restarted.GetByTransactionAsync(artifact.TransactionId,CancellationToken.None))!.Id); Assert.Equal(artifact.Id,Assert.Single(await restarted.GetExpiredAsync(now,10,CancellationToken.None)).Id); }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
    }
}