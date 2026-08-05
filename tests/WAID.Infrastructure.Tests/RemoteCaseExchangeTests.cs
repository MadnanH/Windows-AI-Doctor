using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;
using WAID.Infrastructure.Diagnostics;

namespace WAID.Infrastructure.Tests;

public sealed class RemoteCaseExchangeTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public void Preview_requires_content_and_strong_password_and_lists_permanent_exclusions()
    {
        using var fixture = new Fixture();
        Assert.Equal("WAID-CASE-CONTENT", Assert.Throws<CaseExchangeException>(() => fixture.Service.Preview(new(CasePackageContent.None,CaseRedactionProfile.Standard,Password,null))).Code);
        Assert.Equal("WAID-CASE-PASSWORD", Assert.Throws<CaseExchangeException>(() => fixture.Service.Preview(new(CasePackageContent.Notes,CaseRedactionProfile.Standard,"short",null))).Code);
        var preview=fixture.Service.Preview(new(CasePackageContent.Notes,CaseRedactionProfile.Maximum,Password,"safe"));
        Assert.True(preview.Encrypted);Assert.True(preview.ReviewOnlyImport);Assert.Contains(preview.Excluded,x=>x.Contains("dump",StringComparison.OrdinalIgnoreCase));Assert.Contains("Maximum",preview.RedactionSummary);
    }

    [Fact]
    public async Task Round_trip_is_encrypted_redacted_integrity_checked_and_review_only()
    {
        using var fixture=new Fixture();
        var path=await fixture.Service.ExportAsync(new(CasePackageContent.Notes|CasePackageContent.SanitizedLogs,CaseRedactionProfile.Standard,Password,$"token=note-secret user {Environment.UserName}"),CancellationToken.None);
        var raw=await File.ReadAllBytesAsync(path);var rawText=Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("note-secret",rawText,StringComparison.Ordinal);Assert.DoesNotContain("log-secret",rawText,StringComparison.Ordinal);
        var review=await fixture.Service.ImportForReviewAsync(path,Password,CancellationToken.None);
        Assert.False(review.CanMutateHost);Assert.Contains("REVIEW-ONLY",review.ReviewBanner,StringComparison.Ordinal);Assert.Contains("notes.json",review.Documents.Keys);Assert.Contains("[REDACTED]",review.Documents["notes.json"].GetRawText(),StringComparison.Ordinal);Assert.DoesNotContain(Environment.UserName,review.Documents["notes.json"].GetRawText(),StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fixture.Audit.Records,x=>x.Action=="CasePackageExport"&&x.Result==AuditResult.Succeeded);Assert.Contains(fixture.Audit.Records,x=>x.Action=="CasePackageImport"&&x.Result==AuditResult.Succeeded);
    }

    [Fact]
    public async Task Wrong_password_and_ciphertext_tampering_are_indistinguishable_and_blocked()
    {
        using var fixture=new Fixture();var path=await fixture.Service.ExportAsync(new(CasePackageContent.Notes,CaseRedactionProfile.Standard,Password,"safe"),CancellationToken.None);
        var wrong=await Assert.ThrowsAsync<CaseExchangeException>(()=>fixture.Service.ImportForReviewAsync(path,"wrong password value",CancellationToken.None));Assert.Equal("WAID-CASE-TAMPERED",wrong.Code);
        var bytes=await File.ReadAllBytesAsync(path);bytes[^20]^=0x40;var tampered=Path.Combine(fixture.Root,"tampered.waidcase");await File.WriteAllBytesAsync(tampered,bytes);
        var changed=await Assert.ThrowsAsync<CaseExchangeException>(()=>fixture.Service.ImportForReviewAsync(tampered,Password,CancellationToken.None));Assert.Equal("WAID-CASE-TAMPERED",changed.Code);
    }

    [Fact]
    public async Task Corrupt_envelope_is_rejected_before_archive_processing()
    {
        using var fixture=new Fixture();var path=Path.Combine(fixture.Root,"corrupt.waidcase");await File.WriteAllBytesAsync(path,"not a case"u8.ToArray());
        var error=await Assert.ThrowsAsync<CaseExchangeException>(()=>fixture.Service.ImportForReviewAsync(path,Password,CancellationToken.None));Assert.Equal("WAID-CASE-ENVELOPE",error.Code);
    }

    [Fact]
    public async Task Traversal_entry_is_rejected_without_extracting_any_file()
    {
        using var fixture=new Fixture();var path=Path.Combine(fixture.Root,"traversal.waidcase");await File.WriteAllBytesAsync(path,BuildPackage(new Dictionary<string,byte[]>{{"../escape.json","{}"u8.ToArray()}},1));
        var error=await Assert.ThrowsAsync<CaseExchangeException>(()=>fixture.Service.ImportForReviewAsync(path,Password,CancellationToken.None));Assert.Equal("WAID-CASE-ARCHIVE",error.Code);Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.Root)!,"escape.json")));
    }

    [Fact]
    public async Task Excessive_compression_ratio_is_rejected_as_zip_bomb()
    {
        using var fixture=new Fixture();var path=Path.Combine(fixture.Root,"bomb.waidcase");var content=new byte[2*1024*1024];content[0]=(byte)'[';content[^1]=(byte)']';await File.WriteAllBytesAsync(path,BuildPackage(new Dictionary<string,byte[]>{{"notes.json",content}},1));
        var error=await Assert.ThrowsAsync<CaseExchangeException>(()=>fixture.Service.ImportForReviewAsync(path,Password,CancellationToken.None));Assert.Equal("WAID-CASE-ARCHIVE",error.Code);
    }

    [Fact]
    public async Task Manifest_hash_mismatch_blocks_import()
    {
        using var fixture=new Fixture();var path=Path.Combine(fixture.Root,"hash.waidcase");await File.WriteAllBytesAsync(path,BuildPackage(new Dictionary<string,byte[]>{{"notes.json","{}"u8.ToArray()}},1,true));
        var error=await Assert.ThrowsAsync<CaseExchangeException>(()=>fixture.Service.ImportForReviewAsync(path,Password,CancellationToken.None));Assert.Equal("WAID-CASE-INTEGRITY",error.Code);
    }

    [Fact]
    public async Task Unsupported_manifest_schema_is_rejected()
    {
        using var fixture=new Fixture();var path=Path.Combine(fixture.Root,"future.waidcase");await File.WriteAllBytesAsync(path,BuildPackage(new Dictionary<string,byte[]>{{"notes.json","{}"u8.ToArray()}},99));
        var error=await Assert.ThrowsAsync<CaseExchangeException>(()=>fixture.Service.ImportForReviewAsync(path,Password,CancellationToken.None));Assert.Equal("WAID-CASE-INCOMPATIBLE",error.Code);
    }

    [Fact]
    public async Task Enterprise_export_policy_blocks_export_and_import()
    {
        using var fixture=new Fixture(false);
        await Assert.ThrowsAsync<EnterprisePolicyException>(()=>fixture.Service.ExportAsync(new(CasePackageContent.Notes,CaseRedactionProfile.Standard,Password,"safe"),CancellationToken.None));
        await Assert.ThrowsAsync<EnterprisePolicyException>(()=>fixture.Service.ImportForReviewAsync(Path.Combine(fixture.Root,"missing.waidcase"),Password,CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_is_observed_before_collection_or_file_creation()
    {
        using var fixture=new Fixture();using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>fixture.Service.ExportAsync(new(CasePackageContent.Notes,CaseRedactionProfile.Standard,Password,"safe"),cancellation.Token));
        Assert.Empty(Directory.Exists(Path.Combine(fixture.Root,"CaseExchange"))?Directory.GetFiles(Path.Combine(fixture.Root,"CaseExchange")):[]);
    }

    private static byte[] BuildPackage(IReadOnlyDictionary<string,byte[]> files,int manifestSchema,bool badHash=false)
    {
        var entries=files.Select(x=>new CaseManifestEntry(x.Key,x.Value.Length,badHash?new string('0',64):Convert.ToHexString(SHA256.HashData(x.Value)))).ToArray();
        var manifest=new CasePackageManifest("WAID encrypted diagnostic case",manifestSchema,"test",DateTimeOffset.UtcNow,CaseRedactionProfile.Standard,CasePackageContent.Notes,entries,"review only");
        byte[] zip;using(var memory=new MemoryStream()){using(var archive=new ZipArchive(memory,ZipArchiveMode.Create,true)){foreach(var file in files){var entry=archive.CreateEntry(file.Key,CompressionLevel.Optimal);using var target=entry.Open();target.Write(file.Value);}var manifestEntry=archive.CreateEntry("manifest.json",CompressionLevel.Optimal);using var targetManifest=manifestEntry.Open();JsonSerializer.Serialize(targetManifest,manifest,new JsonSerializerOptions(JsonSerializerDefaults.Web));}zip=memory.ToArray();}
        return Encrypt(zip);
    }

    private static byte[] Encrypt(byte[] plaintext)
    {
        var magic="WAIDCASE1"u8.ToArray();var salt=RandomNumberGenerator.GetBytes(16);var nonce=RandomNumberGenerator.GetBytes(12);var key=Rfc2898DeriveBytes.Pbkdf2(Password,salt,210_000,HashAlgorithmName.SHA256,32);var cipher=new byte[plaintext.Length];var tag=new byte[16];using(var aes=new AesGcm(key,16))aes.Encrypt(nonce,plaintext,cipher,tag,magic);var header=JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,iterations=210_000,salt=Convert.ToBase64String(salt),nonce=Convert.ToBase64String(nonce),ciphertextLength=cipher.Length},new JsonSerializerOptions(JsonSerializerDefaults.Web));var result=new byte[magic.Length+4+header.Length+cipher.Length+tag.Length];magic.CopyTo(result,0);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(magic.Length,4),header.Length);header.CopyTo(result,magic.Length+4);cipher.CopyTo(result,magic.Length+4+header.Length);tag.CopyTo(result,result.Length-tag.Length);CryptographicOperations.ZeroMemory(key);return result;
    }

    private sealed class Fixture:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),$"waid-case-{Guid.NewGuid():N}");public Audit Audit{get;}=new();public RemoteCaseExchangeService Service{get;}
        public Fixture(bool exportsAllowed=true){Directory.CreateDirectory(Root);Service=new(Root,new ScanRepo(),new DiagnosisRepo(),new RepairRepo(),new TimelineRepo(),new Diagnostics(),Audit,new Policy(exportsAllowed),TimeProvider.System);}
        public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
    private sealed class ScanRepo:IScanRepository{public Task SaveAsync(ScanSession session,CancellationToken token)=>Task.CompletedTask;public Task<IReadOnlyList<ScanSession>>GetRecentAsync(int count,CancellationToken token)=>Task.FromResult<IReadOnlyList<ScanSession>>([]);}
    private sealed class DiagnosisRepo:IDiagnosisRepository{public Task SaveAsync(Guid id,AIReport report,CancellationToken token)=>Task.CompletedTask;public Task<AIReport?>GetLatestAsync(CancellationToken token)=>Task.FromResult<AIReport?>(null);}
    private sealed class RepairRepo:IRepairHistoryRepository{public Task SaveAsync(RepairTransaction transaction,CancellationToken token)=>Task.CompletedTask;public Task<IReadOnlyList<RepairHistoryEntry>>GetRecentAsync(int count,CancellationToken token)=>Task.FromResult<IReadOnlyList<RepairHistoryEntry>>([]);}
    private sealed class TimelineRepo:IReliabilityTimelineRepository{public Task ReplaceAsync(ReliabilityTimelineProjection projection,CancellationToken token)=>Task.CompletedTask;public Task<TimelinePage>QueryAsync(TimelineQuery query,CancellationToken token)=>Task.FromResult(new TimelinePage([],0,query.PageSize,0));public Task<ReliabilityIncident?>GetIncidentAsync(string id,CancellationToken token)=>Task.FromResult<ReliabilityIncident?>(null);public Task<IReadOnlyList<ReliabilityIncident>>GetRecentIncidentsAsync(int count,CancellationToken token)=>Task.FromResult<IReadOnlyList<ReliabilityIncident>>([]);}
    private sealed class Diagnostics:ILocalDiagnosticsService{public Task<IReadOnlyList<TechnicalLogEntry>>SearchLogsAsync(TechnicalLogQuery query,CancellationToken token)=>Task.FromResult<IReadOnlyList<TechnicalLogEntry>>([new(DateTimeOffset.UtcNow,"Error","Test",1,null,null,"token=log-secret",$"user {Environment.UserName}")]);public Task<string>ExportSanitizedAsync(AuditQuery auditQuery,TechnicalLogQuery logQuery,CancellationToken token)=>throw new NotSupportedException();}
    private sealed class Audit:IAuditTrailService{public List<AuditRecord>Records{get;}=[];public Task<AuditWriteResult>AppendAsync(AuditRecord record,CancellationToken token){Records.Add(record);return Task.FromResult(new AuditWriteResult(true,record.Id));}public Task<IReadOnlyList<AuditRecord>>SearchAsync(AuditQuery query,CancellationToken token)=>Task.FromResult<IReadOnlyList<AuditRecord>>(Records);public Task ApplyRetentionAsync(CancellationToken token)=>Task.CompletedTask;}
    private sealed class Policy(bool allowed):IEnterprisePolicyService{public EnterprisePolicySnapshot Current=>EnterprisePolicySnapshot.SafeDefault(DateTimeOffset.UtcNow);public EnterprisePolicyDecision Evaluate(EnterpriseCapability capability)=>new(capability,allowed,true,"Test",allowed?"Allowed":"Blocked");public Task<EnterprisePolicySnapshot>RefreshAsync(CancellationToken token)=>Task.FromResult(Current);public Task<EnterprisePolicySnapshot>RollbackAsync(Guid id,CancellationToken token)=>Task.FromResult(Current);}
}
