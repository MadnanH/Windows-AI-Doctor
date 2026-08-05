using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WAID.Application.Abstractions;
using WAID.Application.Services;
using WAID.Domain.Repairs;

namespace WAID.Infrastructure.Diagnostics;

public sealed class RemoteCaseExchangeService(
    string dataDirectory,
    IScanRepository scans,
    IDiagnosisRepository diagnoses,
    IRepairHistoryRepository repairs,
    IReliabilityTimelineRepository timeline,
    ILocalDiagnosticsService diagnostics,
    IAuditTrailService audit,
    IEnterprisePolicyService policy,
    TimeProvider time) : IRemoteCaseExchangeService
{
    public const int CurrentSchemaVersion = 1;
    private const int Iterations = 210_000;
    private const int MaximumPackageBytes = 64 * 1024 * 1024;
    private const long MaximumExpandedBytes = 50L * 1024 * 1024;
    private const long MaximumEntryBytes = 10L * 1024 * 1024;
    private const int MaximumEntries = 32;
    private const double MaximumCompressionRatio = 100;
    private static readonly byte[] Magic = "WAIDCASE1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 32
    };
    private static readonly Regex MaximumRedactionName = new(
        "(?i)(path|command|account|user.?name|computer.?name|machine.?name|host.?name|ip.?address|mac.?address)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> AllowedDocumentNames = new([
        "scans.json", "findings.json", "crash-metadata.json", "diagnosis.json", "timeline.json",
        "repair-history.json", "sanitized-logs.json", "system-summary.json", "notes.json"
    ], StringComparer.Ordinal);

    public CaseExportPreview Preview(CaseExportRequest request)
    {
        request.Validate();
        var included = Names(request.Content).ToArray();
        var excluded = Names(CasePackageContent.All & ~request.Content).Concat([
            "Full crash dumps and minidump contents", "Passwords, tokens, product keys, browser data, and personal files",
            "Raw audit records and unrestricted logs", "Unnecessary hardware serial numbers"
        ]).ToArray();
        var redaction = request.RedactionProfile == CaseRedactionProfile.Maximum
            ? "Maximum: standard secret redaction plus user, host, address, command, and path fields removed."
            : "Standard: secrets and identifiers removed; profile paths are replaced with %USERPROFILE%.";
        return new(included, excluded, redaction, true, true);
    }

    public async Task<string> ExportAsync(CaseExportRequest request, CancellationToken token)
    {
        request.Validate();
        EnsureExportsAllowed();
        token.ThrowIfCancellationRequested();
        var now = time.GetUtcNow();
        var files = await CollectAsync(request, token).ConfigureAwait(false);
        var manifest = new CasePackageManifest("WAID encrypted diagnostic case", CurrentSchemaVersion,
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown", now,
            request.RedactionProfile, request.Content,
            files.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new CaseManifestEntry(x.Key, x.Value.Length,
                Convert.ToHexString(SHA256.HashData(x.Value)))).ToArray(),
            "Content is redacted and encrypted. Imported content is untrusted and review-only; it cannot execute repairs or mutate the host.");
        var archiveBytes = BuildArchive(files, manifest, token);
        if (archiveBytes.Length > MaximumPackageBytes)
            throw new CaseExchangeException("WAID-CASE-SIZE", "The selected diagnostic package is too large.", "Select fewer records or content categories.");

        var directory = Path.Combine(dataDirectory, "CaseExchange");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"WAID-Case-{now:yyyyMMdd-HHmmssfff}.waidcase");
        var temporary = destination + ".tmp";
        try
        {
            var encrypted = Encrypt(archiveBytes, request.Password);
            await File.WriteAllBytesAsync(temporary, encrypted, token).ConfigureAwait(false);
            File.Move(temporary, destination);
            await AuditAsync("CasePackageExport", Path.GetFileName(destination), AuditResult.Succeeded,
                $"Encrypted redacted case exported with {manifest.Entries.Count} entries; password and notes were not audited.", token).ConfigureAwait(false);
            return destination;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(archiveBytes);
        }
    }

    public async Task<ImportedCaseReview> ImportForReviewAsync(string packagePath, string password, CancellationToken token)
    {
        EnsureExportsAllowed();
        if (string.IsNullOrWhiteSpace(packagePath) || !Path.IsPathFullyQualified(packagePath))
            throw new CaseExchangeException("WAID-CASE-PATH", "Select a fully qualified package path.", "Choose a local .waidcase file and retry.");
        if (password.Length is < 12 or > 256)
            throw new CaseExchangeException("WAID-CASE-PASSWORD", "The package password must contain 12 to 256 characters.", "Enter the password supplied separately by the package creator.");
        byte[] encrypted;
        try
        {
            var info = new FileInfo(packagePath);
            if (!info.Exists) throw new CaseExchangeException("WAID-CASE-NOT-FOUND", "The selected package does not exist.", "Check the path and retry.");
            if (info.Length is <= 0 or > MaximumPackageBytes)
                throw new CaseExchangeException("WAID-CASE-SIZE", "The package exceeds the safe encrypted size limit.", "Request a smaller WAID package.");
            encrypted = await File.ReadAllBytesAsync(packagePath, token).ConfigureAwait(false);
        }
        catch (CaseExchangeException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CaseExchangeException("WAID-CASE-READ", "The package could not be read safely.", "Check file access and retry without elevation.", ex);
        }

        byte[] archiveBytes;
        try { archiveBytes = Decrypt(encrypted, password); }
        catch (AuthenticationTagMismatchException ex)
        {
            await AuditAsync("CasePackageImport", Path.GetFileName(packagePath), AuditResult.Rejected,
                "Package authentication failed; no content was imported.", CancellationToken.None).ConfigureAwait(false);
            throw new CaseExchangeException("WAID-CASE-TAMPERED", "The password is incorrect or the package was modified.", "Verify the password and obtain a new package if integrity is uncertain.", ex);
        }
        catch (CaseExchangeException) { throw; }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or JsonException)
        {
            throw new CaseExchangeException("WAID-CASE-CORRUPT", "The package envelope is invalid or corrupted.", "Obtain a new package from the source.", ex);
        }

        try
        {
            var review = ReadArchive(archiveBytes, token);
            await AuditAsync("CasePackageImport", Path.GetFileName(packagePath), AuditResult.Succeeded,
                $"Validated {review.Manifest.Entries.Count} review-only entries; no imported data was persisted or executed.", token).ConfigureAwait(false);
            return review;
        }
        catch (CaseExchangeException)
        {
            await AuditAsync("CasePackageImport", Path.GetFileName(packagePath), AuditResult.Rejected,
                "Package archive validation failed; no content was imported.", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(archiveBytes); }
    }

    private async Task<Dictionary<string, byte[]>> CollectAsync(CaseExportRequest request, CancellationToken token)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sessions = request.Content.HasFlag(CasePackageContent.Scans)
            ? await scans.GetRecentAsync(25, token).ConfigureAwait(false) : [];
        if (request.Content.HasFlag(CasePackageContent.Scans))
        {
            Add(result, "scans.json", sessions, request.RedactionProfile);
            Add(result, "findings.json", sessions.SelectMany(x => x.Findings).ToArray(), request.RedactionProfile);
            Add(result, "crash-metadata.json", sessions.SelectMany(x => x.Findings)
                .Where(x => x.ScannerId.Contains("bsod", StringComparison.OrdinalIgnoreCase) || x.Code.Contains("BUGCHECK", StringComparison.OrdinalIgnoreCase))
                .Select(x => new { x.Code, x.Title, x.Severity, x.Evidence }).ToArray(), request.RedactionProfile);
        }
        if (request.Content.HasFlag(CasePackageContent.Diagnosis)) Add(result, "diagnosis.json", await diagnoses.GetLatestAsync(token).ConfigureAwait(false), request.RedactionProfile);
        if (request.Content.HasFlag(CasePackageContent.Timeline)) Add(result, "timeline.json", await timeline.QueryAsync(new(0, 200), token).ConfigureAwait(false), request.RedactionProfile);
        if (request.Content.HasFlag(CasePackageContent.RepairHistory)) Add(result, "repair-history.json", await repairs.GetRecentAsync(100, token).ConfigureAwait(false), request.RedactionProfile);
        if (request.Content.HasFlag(CasePackageContent.SanitizedLogs)) Add(result, "sanitized-logs.json", await diagnostics.SearchLogsAsync(new(MaximumRecords: 300), token).ConfigureAwait(false), request.RedactionProfile);
        if (request.Content.HasFlag(CasePackageContent.SystemSummary)) Add(result, "system-summary.json", new
        {
            operatingSystem = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            applicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            generatedAtUtc = time.GetUtcNow()
        }, request.RedactionProfile);
        if (request.Content.HasFlag(CasePackageContent.Notes)) Add(result, "notes.json", new { notes = request.Notes ?? string.Empty }, request.RedactionProfile);
        return result;
    }

    private static void Add(Dictionary<string, byte[]> files, string path, object? value, CaseRedactionProfile profile)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions) ?? new JsonObject();
        ReportRedactor.Redact(node);
        if (profile == CaseRedactionProfile.Maximum) ApplyMaximumRedaction(node);
        files.Add(path, Encoding.UTF8.GetBytes(node.ToJsonString(JsonOptions)));
    }

    private static void ApplyMaximumRedaction(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
                if (MaximumRedactionName.IsMatch(property.Key)) obj.Remove(property.Key);
                else if (property.Value is not null) ApplyMaximumRedaction(property.Value);
        else if (node is JsonArray array)
            foreach (var item in array) if (item is not null) ApplyMaximumRedaction(item);
    }

    private static byte[] BuildArchive(IReadOnlyDictionary<string, byte[]> files, CasePackageManifest manifest, CancellationToken token)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                using var target = entry.Open();
                target.Write(file.Value);
            }
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var manifestTarget = manifestEntry.Open();
            JsonSerializer.Serialize(manifestTarget, manifest, JsonOptions);
        }
        return output.ToArray();
    }

    private static ImportedCaseReview ReadArchive(byte[] bytes, CancellationToken token)
    {
        using var input = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, false);
        if (archive.Entries.Count is < 2 or > MaximumEntries)
            throw InvalidArchive("The archive contains an unsafe number of entries.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!IsSafeEntryName(entry.FullName) || !names.Add(entry.FullName)) throw InvalidArchive("The archive contains an unsafe or duplicate path.");
            if (entry.Length < 0 || entry.Length > MaximumEntryBytes) throw InvalidArchive("An archive entry exceeds the safe size limit.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes) throw InvalidArchive("The archive exceeds the safe expanded size limit.");
            if ((entry.Length > 1024 * 1024 && entry.CompressedLength == 0) ||
                (entry.CompressedLength > 0 && (double)entry.Length / entry.CompressedLength > MaximumCompressionRatio))
                throw InvalidArchive("The archive has an unsafe compression ratio.");
        }
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw InvalidArchive("The package manifest is missing.");
        CasePackageManifest manifest;
        using (var stream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<CasePackageManifest>(stream, JsonOptions) ?? throw InvalidArchive("The package manifest is invalid.");
        if (manifest.Format != "WAID encrypted diagnostic case" || manifest.SchemaVersion != CurrentSchemaVersion)
            throw new CaseExchangeException("WAID-CASE-INCOMPATIBLE", "The package format or schema version is unsupported.", "Use a compatible WAID version or ask the source to export again.");
        if (manifest.Entries is null || manifest.CreatedAtUtc == default || !Enum.IsDefined(manifest.RedactionProfile) ||
            manifest.IncludedContent == CasePackageContent.None || (manifest.IncludedContent & ~CasePackageContent.All) != 0 ||
            manifest.Entries.Count != archive.Entries.Count - 1 || manifest.Entries.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Entries.Count)
            throw InvalidArchive("The manifest does not match the archive entry set.");
        var documents = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var expected in manifest.Entries)
        {
            if (!AllowedDocumentNames.Contains(expected.Path) || expected.Length < 0 || expected.Length > MaximumEntryBytes || string.IsNullOrWhiteSpace(expected.Sha256)) throw InvalidArchive("The manifest contains an unsupported document declaration.");
            var entry = archive.GetEntry(expected.Path) ?? throw InvalidArchive("A manifest entry is missing from the archive.");
            if (entry.Length != expected.Length) throw IntegrityFailure();
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var content = memory.ToArray();
            byte[] expectedHash;
            try { expectedHash = expected.Sha256.Length == 64 ? Convert.FromHexString(expected.Sha256) : []; }
            catch (FormatException) { throw IntegrityFailure(); }
            if (expectedHash.Length != 32 || !CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(content))) throw IntegrityFailure();
            try
            {
                using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow });
                documents.Add(expected.Path, document.RootElement.Clone());
            }
            catch (JsonException ex) { throw new CaseExchangeException("WAID-CASE-JSON", "A package document is invalid.", "Obtain a new package from the source.", ex); }
        }
        return new(manifest, documents, "UNTRUSTED REVIEW-ONLY CASE - content cannot run commands, install plugins, approve repairs, or modify local WAID data.", false);
    }

    private static byte[] Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[plaintext.Length]; var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plaintext, ciphertext, tag, Magic);
        CryptographicOperations.ZeroMemory(key);
        var header = JsonSerializer.SerializeToUtf8Bytes(new EnvelopeHeader(CurrentSchemaVersion, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce), ciphertext.Length), JsonOptions);
        var output = new byte[Magic.Length + 4 + header.Length + ciphertext.Length + tag.Length];
        Magic.CopyTo(output, 0); BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(Magic.Length, 4), header.Length);
        header.CopyTo(output, Magic.Length + 4); ciphertext.CopyTo(output, Magic.Length + 4 + header.Length); tag.CopyTo(output, output.Length - tag.Length);
        return output;
    }

    private static byte[] Decrypt(byte[] envelope, string password)
    {
        if (envelope.Length < Magic.Length + 4 + 16 || !envelope.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw InvalidEnvelope();
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(envelope.AsSpan(Magic.Length, 4));
        if (headerLength is < 2 or > 4096 || envelope.Length < Magic.Length + 4 + headerLength + 16) throw InvalidEnvelope();
        var header = JsonSerializer.Deserialize<EnvelopeHeader>(envelope.AsSpan(Magic.Length + 4, headerLength), JsonOptions) ?? throw InvalidEnvelope();
        if (header.SchemaVersion != CurrentSchemaVersion || header.Iterations != Iterations || header.CiphertextLength < 1 || header.CiphertextLength > MaximumPackageBytes) throw InvalidEnvelope();
        if (Magic.Length + 4 + headerLength + header.CiphertextLength + 16 != envelope.Length) throw InvalidEnvelope();
        byte[] salt, nonce;
        try
        {
            if (string.IsNullOrWhiteSpace(header.Salt) || string.IsNullOrWhiteSpace(header.Nonce)) throw InvalidEnvelope();
            salt = Convert.FromBase64String(header.Salt); nonce = Convert.FromBase64String(header.Nonce);
        }
        catch (FormatException) { throw InvalidEnvelope(); }
        if (salt.Length != 16 || nonce.Length != 12) throw InvalidEnvelope();
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var plaintext = new byte[header.CiphertextLength];
        var ciphertext = envelope.AsSpan(Magic.Length + 4 + headerLength, header.CiphertextLength);
        var tag = envelope.AsSpan(envelope.Length - 16, 16);
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, ciphertext, tag, plaintext, Magic); return plaintext; }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private void EnsureExportsAllowed()
    {
        var decision = policy.Evaluate(EnterpriseCapability.Exports);
        if (!decision.Allowed) throw new EnterprisePolicyException("WAID-POLICY-CASE-EXCHANGE-BLOCKED", $"Case exchange is blocked by {decision.Source}.", "Contact the organization policy administrator.");
    }

    private async Task AuditAsync(string action, string target, AuditResult result, string detail, CancellationToken token) =>
        await audit.AppendAsync(new(Guid.NewGuid(), time.GetUtcNow(), AuditActor.User, action, target, result,
            SafetyLevel.Low, false, false, Guid.NewGuid(), Guid.NewGuid(), detail), token).ConfigureAwait(false);
    private static bool IsSafeEntryName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 100 && name == Path.GetFileName(name) && !Path.IsPathFullyQualified(name) && !name.Contains("..", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal);
    private static IEnumerable<string> Names(CasePackageContent content) => Enum.GetValues<CasePackageContent>().Where(x => x != CasePackageContent.None && x != CasePackageContent.All && content.HasFlag(x)).Select(x => x.ToString());
    private static CaseExchangeException InvalidEnvelope() => new("WAID-CASE-ENVELOPE", "The encrypted package envelope is invalid.", "Obtain a new package from the source.");
    private static CaseExchangeException InvalidArchive(string message) => new("WAID-CASE-ARCHIVE", message, "Do not trust this package; obtain a new export from the source.");
    private static CaseExchangeException IntegrityFailure() => new("WAID-CASE-INTEGRITY", "Package integrity verification failed.", "Do not review the content; obtain a new package from the source.");
    private sealed record EnvelopeHeader(int SchemaVersion, int Iterations, string Salt, string Nonce, int CiphertextLength);
}
