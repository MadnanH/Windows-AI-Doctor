using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using WAID.Application.Abstractions;
using WAID.Application.Services;

namespace WAID.Infrastructure.Diagnostics;

public sealed class DiagnosticsExportService(
    string dataDirectory,
    IScanRepository scans,
    IDiagnosisRepository diagnoses,
    IRepairHistoryRepository repairs,
    TimeProvider timeProvider, IEnterprisePolicyService? enterprisePolicy = null) : IDiagnosticsExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> ExportAsync(CancellationToken token)
    {
        var decision = enterprisePolicy?.Evaluate(EnterpriseCapability.Exports);
        if (decision is { Allowed: false }) throw new EnterprisePolicyException("WAID-POLICY-EXPORT-BLOCKED", $"Diagnostic export is blocked by {decision.Source}.", "Contact the organization policy administrator.");
        var exportDirectory = Path.Combine(dataDirectory, "Exports");
        Directory.CreateDirectory(exportDirectory);
        var timestamp = timeProvider.GetUtcNow();
        var destination = Path.Combine(exportDirectory, $"WAID-Diagnostics-{timestamp:yyyyMMdd-HHmmssfff}.zip");
        var temporary = destination + ".tmp";

        try
        {
            await using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            await WriteJsonAsync(archive, "scan-data.json", await scans.GetRecentAsync(25, token).ConfigureAwait(false), token).ConfigureAwait(false);
            await WriteJsonAsync(archive, "diagnosis.json", await diagnoses.GetLatestAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
            await WriteJsonAsync(archive, "repair-history.json", await repairs.GetRecentAsync(25, token).ConfigureAwait(false), token).ConfigureAwait(false);
            await WriteJsonAsync(archive, "system-information.json", new
            {
                applicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                operatingSystem = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                generatedAtUtc = timestamp
            }, token).ConfigureAwait(false);

            var logDirectory = Path.Combine(dataDirectory, "logs");
            if (Directory.Exists(logDirectory))
            {
                foreach (var log in Directory.EnumerateFiles(logDirectory, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
                {
                    token.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry($"logs/{Path.GetFileName(log)}", CompressionLevel.Optimal);
                    await using var target = entry.Open();
                    await using var source = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
                    await source.CopyToAsync(target, token).ConfigureAwait(false);
                }
            }
            archive.Dispose();
            stream.Close();
            File.Move(temporary, destination);
            return destination;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static async Task WriteJsonAsync(ZipArchive archive, string name, object? value, CancellationToken token)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await JsonSerializer.SerializeAsync(target, value, value?.GetType() ?? typeof(object), JsonOptions, token).ConfigureAwait(false);
    }
}
