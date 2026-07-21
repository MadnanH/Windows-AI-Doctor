using System.Text.Json;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;
using WAID.Infrastructure.Diagnostics;

namespace WAID.Infrastructure.Tests;

public sealed class DiagnosticsAuditTests
{
    [Fact]
    public async Task Audit_redacts_sensitive_values_and_profile_paths()
    {
        var root=CreateRoot();try{var service=new LocalAuditTrailService(root,365,TimeProvider.System);var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);await service.AppendAsync(Record($"{profile} token=top-secret"),CancellationToken.None);var item=Assert.Single(await service.SearchAsync(new(),CancellationToken.None));Assert.DoesNotContain("top-secret",item.Detail,StringComparison.Ordinal);if(!string.IsNullOrWhiteSpace(profile))Assert.DoesNotContain(profile,item.Detail,StringComparison.OrdinalIgnoreCase);}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Concurrent_audit_writes_are_complete_and_append_only()
    {
        var root=CreateRoot();try{var service=new LocalAuditTrailService(root,365,TimeProvider.System);await Task.WhenAll(Enumerable.Range(0,75).Select(index=>service.AppendAsync(Record($"event {index}"),CancellationToken.None)));var records=await service.SearchAsync(new(MaximumRecords:100),CancellationToken.None);Assert.Equal(75,records.Count);Assert.Equal(75,records.Select(item=>item.Id).Distinct().Count());Assert.Single(Directory.GetFiles(root,"audit-*.jsonl"));Assert.Equal(75,File.ReadLines(Directory.GetFiles(root,"audit-*.jsonl").Single()).Count());}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Retention_removes_only_expired_audit_files()
    {
        var root=CreateRoot();try{var service=new LocalAuditTrailService(root,30,TimeProvider.System);await service.AppendAsync(Record("current"),CancellationToken.None);var old=Path.Combine(root,"audit-20000101.jsonl");await File.WriteAllTextAsync(old,"{}\n");File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-31));await service.ApplyRetentionAsync(CancellationToken.None);Assert.False(File.Exists(old));Assert.Single(Directory.GetFiles(root,"audit-*.jsonl"));}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Audit_storage_failure_returns_typed_result_and_does_not_throw()
    {
        var root=CreateRoot();var blocked=Path.Combine(root,"blocked");await File.WriteAllTextAsync(blocked,"file");try{var result=await new LocalAuditTrailService(blocked,365,TimeProvider.System).AppendAsync(Record("failure"),CancellationToken.None);Assert.False(result.Succeeded);Assert.Equal("AUDIT_IO_FAILURE",result.FailureCode);}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Operation_context_flows_across_async_calls_and_restores_nested_scope()
    {
        var accessor=new OperationContextAccessor();using(accessor.Begin("Scan")){var outer=accessor.Current!;await Task.Yield();Assert.Equal(outer,accessor.Current);using(accessor.Begin("Scanner")){Assert.Equal(outer.CorrelationId,accessor.Current!.CorrelationId);Assert.NotEqual(outer.OperationId,accessor.Current.OperationId);}Assert.Equal(outer,accessor.Current);}Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Sanitized_support_export_combines_logs_and_audit_without_secrets()
    {
        var root=CreateRoot();var logs=Path.Combine(root,"logs");var exports=Path.Combine(root,"exports");var auditPath=Path.Combine(root,"audit");Directory.CreateDirectory(logs);
        try
        {
            var audit=new LocalAuditTrailService(auditPath,365,TimeProvider.System);await audit.AppendAsync(Record("password=hidden"),CancellationToken.None);
            var log=new{Timestamp=DateTimeOffset.UtcNow,Level="Information",RenderedMessage="token=hidden",Properties=new{SourceContext="WAID.Test",EventId=new{Id=123},CorrelationId=Guid.NewGuid(),OperationId=Guid.NewGuid()}};
            await File.WriteAllTextAsync(Path.Combine(logs,"waid-test.log"),JsonSerializer.Serialize(log)+Environment.NewLine);
            var service=new LocalDiagnosticsService(logs,exports,audit);var path=await service.ExportSanitizedAsync(new(),new(),CancellationToken.None);var content=await File.ReadAllTextAsync(path);Assert.DoesNotContain("hidden",content,StringComparison.Ordinal);Assert.Contains("WAID.Test",content,StringComparison.Ordinal);
        }
        finally{Directory.Delete(root,true);}
    }

    private static AuditRecord Record(string detail)=>new(Guid.NewGuid(),DateTimeOffset.UtcNow,AuditActor.User,"RepairExecution","waid.test",AuditResult.Succeeded,SafetyLevel.Low,true,true,Guid.NewGuid(),Guid.NewGuid(),detail);
    private static string CreateRoot(){var root=Path.Combine(Path.GetTempPath(),$"waid-audit-{Guid.NewGuid():N}");Directory.CreateDirectory(root);return root;}
}
