using System.Management.Automation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
namespace WAID.Infrastructure.PowerShell;
public interface IPowerShellRunner { Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken token); }
public sealed record PowerShellResult(IReadOnlyList<string> Output, IReadOnlyList<string> Errors) { public bool Succeeded => Errors.Count == 0; }
public sealed class PowerShellRunner(ILogger<PowerShellRunner> logger) : IPowerShellRunner
{
    public async Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(script)) throw new ArgumentException("Script is required.", nameof(script));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)))[..12];
        logger.LogInformation(
            "Executing PowerShell action {ScriptFingerprint} with parameters {ParameterNames}",
            fingerprint, parameters.Keys);
        using var shell = System.Management.Automation.PowerShell.Create(); shell.AddScript(script, useLocalScope: true);
        foreach (var item in parameters) shell.AddParameter(item.Key, item.Value);
        var output = await shell.InvokeAsync().WaitAsync(token).ConfigureAwait(false);
        var result = new PowerShellResult(
            output.Select(x => x?.ToString() ?? string.Empty).ToArray(),
            shell.Streams.Error.Select(x => x.ToString()).ToArray());
        if (result.Succeeded) logger.LogInformation("PowerShell action {ScriptFingerprint} completed", fingerprint);
        else logger.LogError("PowerShell action {ScriptFingerprint} failed: {Errors}", fingerprint, result.Errors);
        return result;
    }
}
