using System.Threading.Channels;
using WAID.Domain.Diagnostics;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public sealed class RepairQueue : IAsyncDisposable
{
    private readonly Channel<QueuedRepair> _channel = Channel.CreateUnbounded<QueuedRepair>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly RepairExecutor _executor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _processor;

    public RepairQueue(RepairExecutor executor)
    {
        _executor = executor;
        _processor = ProcessAsync(_shutdown.Token);
    }

    public async Task<RepairTransaction> EnqueueAsync(
        string repairId,
        DiagnosticFinding? finding,
        bool userConfirmed,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<RepairTransaction>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new QueuedRepair(repairId, finding, userConfirmed, cancellationToken, completion);
        await _channel.Writer.WriteAsync(queued, cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try { await _processor.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }

    private async Task ProcessAsync(CancellationToken shutdownToken)
    {
        await foreach (var queued in _channel.Reader.ReadAllAsync(shutdownToken).ConfigureAwait(false))
        {
            try
            {
                var result = await _executor.ExecuteAsync(
                    queued.RepairId, queued.Finding, queued.UserConfirmed, queued.CancellationToken).ConfigureAwait(false);
                queued.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (queued.CancellationToken.IsCancellationRequested)
            {
                queued.Completion.TrySetCanceled(queued.CancellationToken);
            }
            catch (Exception exception)
            {
                queued.Completion.TrySetException(exception);
            }
        }
    }

    private sealed record QueuedRepair(
        string RepairId,
        DiagnosticFinding? Finding,
        bool UserConfirmed,
        CancellationToken CancellationToken,
        TaskCompletionSource<RepairTransaction> Completion);
}
