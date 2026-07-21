using WAID.Application.Abstractions;
namespace WAID.Application.Services;
public sealed record DestructiveVmTestRequest(bool Enabled,bool DisposableVmAcknowledged,bool SnapshotAcknowledged,bool SeparateConfirmation,string ConfirmationText);
public sealed class DestructiveVmTestGuard(IAdministratorService administrator)
{
    public async Task AuthorizeAsync(DestructiveVmTestRequest request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!request.Enabled)throw new InvalidOperationException("Destructive VM test mode is not enabled.");
        if(!request.DisposableVmAcknowledged||!request.SnapshotAcknowledged)throw new InvalidOperationException("A disposable VM and recoverable snapshot must be acknowledged.");
        if(!request.SeparateConfirmation||!string.Equals(request.ConfirmationText,"RUN DESTRUCTIVE VM TEST",StringComparison.Ordinal))throw new InvalidOperationException("The separate destructive-test confirmation phrase was not supplied.");
        if(!administrator.IsAdministrator())throw new UnauthorizedAccessException("Destructive VM tests require administrator privileges.");
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
