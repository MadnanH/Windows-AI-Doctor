using System.Management.Automation;
namespace WAID.Infrastructure.PowerShell;
public interface IPowerShellRunner { Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken token); }
public sealed record PowerShellResult(IReadOnlyList<string> Output, IReadOnlyList<string> Errors) { public bool Succeeded => Errors.Count == 0; }
public sealed class PowerShellRunner : IPowerShellRunner
{
    public async Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(script)) throw new ArgumentException("Script is required.", nameof(script));
        using var shell = System.Management.Automation.PowerShell.Create(); shell.AddScript(script, useLocalScope: true);
        foreach (var item in parameters) shell.AddParameter(item.Key, item.Value);
        var output = await shell.InvokeAsync().WaitAsync(token).ConfigureAwait(false);
        return new(output.Select(x => x?.ToString() ?? string.Empty).ToArray(), shell.Streams.Error.Select(x => x.ToString()).ToArray());
    }
}
