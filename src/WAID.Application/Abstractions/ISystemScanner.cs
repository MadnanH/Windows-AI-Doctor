using System.Collections.Frozen;
using WAID.Domain.Diagnostics;

namespace WAID.Application.Abstractions;

public static class ScannerPrerequisites
{
    public const string Windows = "windows";
    public const string Administrator = "administrator";
    public const string PowerShell = "powershell";
    public static readonly IReadOnlySet<string> Known = new[] { Windows, Administrator, PowerShell }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}

public sealed record ScannerMetadata(string Id, string DisplayName, string Description, string Category, Version Version,
    IReadOnlyList<string> Prerequisites, IReadOnlyList<string> Dependencies, TimeSpan? RecommendedTimeout = null)
{
    public ScannerMetadata Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 || Id.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-'))) throw new InvalidOperationException("Scanner ID is invalid.");
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 120 || string.IsNullOrWhiteSpace(Description) || Description.Length > 500) throw new InvalidOperationException($"Scanner {Id} metadata is incomplete.");
        if (string.IsNullOrWhiteSpace(Category) || Version.Major < 1) throw new InvalidOperationException($"Scanner {Id} category or version is invalid.");
        if (Dependencies.Any(item => string.Equals(item, Id, StringComparison.OrdinalIgnoreCase)) || Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Dependencies.Count) throw new InvalidOperationException($"Scanner {Id} dependencies are invalid.");
        if (Prerequisites.Any(item => !ScannerPrerequisites.Known.Contains(item))) throw new InvalidOperationException($"Scanner {Id} has an unknown prerequisite.");
        if (RecommendedTimeout is { } timeout && (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10))) throw new InvalidOperationException($"Scanner {Id} timeout is invalid.");
        return this;
    }

    public static ScannerMetadata Legacy(string id, string name) => new(id, name, $"Runs the {name} diagnostic check.", "General", new Version(1, 0, 0), [], []);
}

public sealed record ScannerObservation(string Key, string Value, DateTimeOffset ObservedAtUtc, string SourceReference,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record ScannerOutput(IReadOnlyCollection<ScannerObservation> Observations, IReadOnlyCollection<DiagnosticFinding> Findings)
{
    public static ScannerOutput FromLegacy(IReadOnlyCollection<DiagnosticFinding> findings, DateTimeOffset observedAtUtc) => new(
        findings.SelectMany(finding => finding.Evidence.Select(item => new ScannerObservation(item.Key, item.Value, observedAtUtc,
            $"{finding.ScannerId}:{finding.Code}", new Dictionary<string, string> { ["findingId"] = finding.Id.ToString() }))).ToArray(), findings);
}

public sealed record ScannerStepProgress(string Stage, double Percentage, string Detail);

public interface ISystemScanner
{
    string Id { get; }
    string DisplayName { get; }
    ScannerMetadata Metadata => ScannerMetadata.Legacy(Id, DisplayName);
    Task<IReadOnlyCollection<DiagnosticFinding>> ScanAsync(ScanContext context, CancellationToken cancellationToken);
    async Task<ScannerOutput> ScanDetailedAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = await ScanAsync(context, cancellationToken).ConfigureAwait(false);
        return ScannerOutput.FromLegacy(findings, context.StartedAtUtc);
    }
}

public sealed record ScanContext(Guid SessionId, bool IsAdministrator, DateTimeOffset StartedAtUtc, IProgress<ScannerStepProgress>? Progress = null);

public interface IScanDataSanitizer
{
    ScannerOutput Sanitize(ScannerMetadata metadata, ScannerOutput output);
}
