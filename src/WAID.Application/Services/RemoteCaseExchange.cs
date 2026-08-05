using System.Text.Json;

namespace WAID.Application.Services;

[Flags]
public enum CasePackageContent
{
    None = 0,
    Scans = 1,
    Diagnosis = 2,
    Timeline = 4,
    RepairHistory = 8,
    SanitizedLogs = 16,
    SystemSummary = 32,
    Notes = 64,
    All = Scans | Diagnosis | Timeline | RepairHistory | SanitizedLogs | SystemSummary | Notes
}

public enum CaseRedactionProfile { Standard, Maximum }

public sealed record CaseExportRequest(CasePackageContent Content, CaseRedactionProfile RedactionProfile,
    string Password, string? Notes)
{
    public CaseExportRequest Validate()
    {
        if (Content == CasePackageContent.None || (Content & ~CasePackageContent.All) != 0)
            throw new CaseExchangeException("WAID-CASE-CONTENT", "Select at least one supported content category.", "Review the package content selection.");
        if (Password.Length is < 12 or > 256)
            throw new CaseExchangeException("WAID-CASE-PASSWORD", "The package password must contain 12 to 256 characters.", "Use a strong password and share it separately from the package.");
        if (Notes?.Length > 4000)
            throw new CaseExchangeException("WAID-CASE-NOTES", "Case notes exceed 4,000 characters.", "Shorten the notes and retry.");
        return this;
    }
}

public sealed record CaseManifestEntry(string Path, long Length, string Sha256);
public sealed record CasePackageManifest(string Format, int SchemaVersion, string ApplicationVersion,
    DateTimeOffset CreatedAtUtc, CaseRedactionProfile RedactionProfile, CasePackageContent IncludedContent,
    IReadOnlyList<CaseManifestEntry> Entries, string PrivacyNotice);
public sealed record CaseExportPreview(IReadOnlyList<string> Included, IReadOnlyList<string> Excluded,
    string RedactionSummary, bool Encrypted, bool ReviewOnlyImport);
public sealed record ImportedCaseReview(CasePackageManifest Manifest,
    IReadOnlyDictionary<string, JsonElement> Documents, string ReviewBanner, bool CanMutateHost);

public sealed class CaseExchangeException(string code, string message, string recoveryAction, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public string Code { get; } = code;
    public string RecoveryAction { get; } = recoveryAction;
}

public interface IRemoteCaseExchangeService
{
    CaseExportPreview Preview(CaseExportRequest request);
    Task<string> ExportAsync(CaseExportRequest request, CancellationToken token);
    Task<ImportedCaseReview> ImportForReviewAsync(string packagePath, string password, CancellationToken token);
}
