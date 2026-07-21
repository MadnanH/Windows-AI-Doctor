namespace WAID.Domain.Repairs;

public sealed record RepairResult(bool Succeeded, string Summary, string? Details = null, bool RestartRequired = false)
{
    public static RepairResult Success(string summary, bool restartRequired = false) =>
        new(true, summary, RestartRequired: restartRequired);

    public static RepairResult Failure(string summary, string? details = null) =>
        new(false, summary, details);
}

