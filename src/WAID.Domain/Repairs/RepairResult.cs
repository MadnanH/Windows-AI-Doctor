namespace WAID.Domain.Repairs;

public sealed record RepairResult
{
    private RepairResult(
        bool succeeded,
        string summary,
        string? details,
        bool restartRequired,
        bool rollbackAttempted,
        bool rollbackSucceeded,
        IReadOnlyCollection<string> actions)
    {
        Succeeded = succeeded;
        Summary = Require(summary, nameof(summary));
        Details = details;
        RestartRequired = restartRequired;
        RollbackAttempted = rollbackAttempted;
        RollbackSucceeded = rollbackSucceeded;
        Actions = actions;
    }

    public bool Succeeded { get; }
    public string Summary { get; }
    public string? Details { get; }
    public bool RestartRequired { get; }
    public bool RollbackAttempted { get; }
    public bool RollbackSucceeded { get; }
    public IReadOnlyCollection<string> Actions { get; }

    public static RepairResult Success(
        string summary,
        bool restartRequired = false,
        IReadOnlyCollection<string>? actions = null) =>
        new(true, summary, null, restartRequired, false, false, actions ?? []);

    public static RepairResult Failure(
        string summary,
        string? details = null,
        bool rollbackAttempted = false,
        bool rollbackSucceeded = false,
        IReadOnlyCollection<string>? actions = null) =>
        new(false, summary, details, false, rollbackAttempted, rollbackSucceeded, actions ?? []);

    public RepairResult WithRollback(bool succeeded) =>
        new(Succeeded, Summary, Details, RestartRequired, true, succeeded, Actions);

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : value.Trim();
}
