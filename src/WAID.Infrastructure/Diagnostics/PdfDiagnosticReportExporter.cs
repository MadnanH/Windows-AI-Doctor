using PdfSharp.Drawing;
using PdfSharp.Pdf;
using WAID.Application.Services;
namespace WAID.Infrastructure.Diagnostics;
public sealed class PdfDiagnosticReportExporter(string outputDirectory) : IPdfReportExporter
{
    public Task<string> ExportPdfAsync(DiagnosticReportData report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Directory.CreateDirectory(outputDirectory); var path=Path.Combine(outputDirectory,$"WAID-Report-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.pdf");
        using var document=new PdfDocument(); document.Info.Title="Windows AI Doctor diagnostic report"; document.Info.Subject=$"WAID {R(report.ApplicationVersion)}";
        var lines=BuildLines(report).SelectMany(Wrap).ToArray(); var font=new XFont("Arial",9); var heading=new XFont("Arial",15,XFontStyleEx.Bold); const double margin=42,lineHeight=15; var index=0;
        while(index<lines.Length||document.PageCount==0){token.ThrowIfCancellationRequested();var page=document.AddPage();using var graphics=XGraphics.FromPdfPage(page);var y=margin;graphics.DrawString("Windows AI Doctor diagnostic report",heading,XBrushes.Black,new XPoint(margin,y));y+=25;while(index<lines.Length&&y<page.Height.Point-45){graphics.DrawString(lines[index++],font,XBrushes.Black,new XPoint(margin,y));y+=lineHeight;}}
        for(var pageIndex=0;pageIndex<document.PageCount;pageIndex++){using var graphics=XGraphics.FromPdfPage(document.Pages[pageIndex],XGraphicsPdfPageOptions.Append);graphics.DrawString($"WAID {R(report.ApplicationVersion)} | Page {pageIndex+1} of {document.PageCount}",font,XBrushes.Gray,new XRect(margin,document.Pages[pageIndex].Height.Point-30,document.Pages[pageIndex].Width.Point-margin*2,15),XStringFormats.Center);}
        document.Save(path); return Task.FromResult(path);
    }
    private static string R(object? value)=>ReportRedactor.RedactText(value?.ToString()??"Unavailable");
    private static IEnumerable<string> BuildLines(DiagnosticReportData report)
    {
        yield return $"Version: {R(report.ApplicationVersion)}";yield return $"Generated: {report.GeneratedAtUtc:O}";yield return $"System: {R(report.SystemSummary)}";yield return $"Redaction notice: {R(report.RedactionNotice)}";yield return $"Overall health: {report.Diagnosis?.Health.Overall.ToString()??"Unavailable"}/100";
        yield return "Findings";foreach(var item in report.Diagnosis?.Findings??[])yield return $"[{item.Severity}] {R(item.Title)} - {R(item.Description)}";
        yield return "Evidence";foreach(var item in report.Evidence)yield return $"{item.CollectedAtUtc:O} {R(item.Source)} {R(item.Code)} {string.Join("; ",item.Values.Where(v=>!ReportRedactor.IsSensitiveName(v.Key)).Select(v=>$"{R(v.Key)}={R(v.Value)}"))}";
        yield return "Root causes";foreach(var item in report.Diagnosis?.RootCauses??[]){yield return $"{R(item.ExplanationDetail.ProblemStatement)} | Confidence {item.Confidence}% ({item.ExplanationDetail.Calibration.Band}) | {R(item.ExplanationDetail.Rationale)}";yield return $"Impact: {R(item.ExplanationDetail.Impact)} | Urgency: {R(item.ExplanationDetail.Urgency)}";yield return $"Next step: {R(item.ExplanationDetail.NextStep)} | Change: {R(item.ExplanationDetail.ChangeOverTime)}";}
        yield return "Recommended repair order";foreach(var item in report.RepairPlan.OrderBy(r=>r.Order))yield return $"{item.Order}. {R(item.Title)} | benefit {item.ExpectedBenefit}% | risk {item.RiskLevel} | admin {item.RequiresAdministrator} | restart {item.RestartRequired}";
        yield return "Repair history";foreach(var item in report.RepairHistory)yield return $"{item.CreatedAtUtc:O} {R(item.RepairId)} {item.Status}: {R(item.Summary)}";yield return "Known limitations";foreach(var item in report.KnownLimitations)yield return $"- {R(item)}";
    }
    private static IEnumerable<string> Wrap(string value){const int length=95;for(var index=0;index<value.Length;index+=length)yield return value.Substring(index,Math.Min(length,value.Length-index));if(value.Length==0)yield return string.Empty;}
}
