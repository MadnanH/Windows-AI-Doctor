using System.Text;
using WAID.Application.Services;
using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.Health;
using WAID.Infrastructure.Diagnostics;
using WAID.KnowledgeBase;

namespace WAID.Infrastructure.Tests;

public sealed class ContinuousReportingTests
{
    [Fact]
    public void Minidump_parser_extracts_timestamp_and_bugcheck_from_safe_fixture()
    {
        var path=Path.Combine(Path.GetTempPath(),$"waid-safe-{Guid.NewGuid():N}.dmp");
        try
        {
            using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream)){writer.Write(0x504D444Du);writer.Write(0xA793u);writer.Write(1u);writer.Write(32u);writer.Write(0u);writer.Write(1700000000u);writer.Write(0UL);writer.Write(6u);writer.Write(168u);writer.Write(44u);writer.Write(1u);writer.Write(0u);writer.Write(0x124u);writer.Write(new byte[156]);}
            var record=new MinidumpAnalyzer(new DiagnosticKnowledgeBase()).Parse(path);
            Assert.Equal(0x124u,record.BugCheckCode);Assert.Contains("hardware error",record.Explanation,StringComparison.OrdinalIgnoreCase);Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000),record.CrashTimeUtc);
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    [Fact]
    public async Task Report_exports_html_json_and_zip_without_sensitive_fields()
    {
        var root=Path.Combine(Path.GetTempPath(),$"waid-reports-{Guid.NewGuid():N}");
        try
        {
            var finding=new DiagnosticFinding("scanner","CODE","Finding","Details token=description-secret",DiagnosticSeverity.Warning,evidence:new Dictionary<string,string>{{"password","do-not-export"},{"serialNumber","SERIAL-123"},{"eventId","41"}});
            var diagnosis=new AIReport(DateTimeOffset.UtcNow,"Summary",new HealthScore(100,100,100,100,100,100,100,100),[],[],[finding]);
            var report=new DiagnosticReportData("0.6.0",DateTimeOffset.UtcNow,"Windows",diagnosis,[new("scanner","CODE",DateTimeOffset.UtcNow,new Dictionary<string,string>{{"eventId","41"}})],[],[],["Limitation"],"Sensitive data is excluded.");
            var exporter=new DiagnosticReportExporter(root);
            var json=await exporter.ExportJsonAsync(report,CancellationToken.None);var html=await exporter.ExportHtmlAsync(report,CancellationToken.None);var zip=await exporter.ExportPackageAsync(report,CancellationToken.None);
            var content=await File.ReadAllTextAsync(json);Assert.DoesNotContain("do-not-export",content,StringComparison.Ordinal);Assert.DoesNotContain("SERIAL-123",content,StringComparison.Ordinal);Assert.DoesNotContain("description-secret",content,StringComparison.Ordinal);Assert.Contains("eventId",content,StringComparison.Ordinal);var htmlContent=await File.ReadAllTextAsync(html);Assert.DoesNotContain("description-secret",htmlContent,StringComparison.Ordinal);Assert.Contains("Redaction",htmlContent,StringComparison.OrdinalIgnoreCase);Assert.True(File.Exists(zip));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
