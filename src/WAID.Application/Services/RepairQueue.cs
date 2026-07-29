using System.Threading.Channels;
using WAID.Domain.Diagnostics;

namespace WAID.Application.Services;

public sealed class RepairQueue:IAsyncDisposable
{
 private readonly Channel<QueuedRepair> _channel=Channel.CreateBounded<QueuedRepair>(new BoundedChannelOptions(20){SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait});private readonly RepairOrchestrator _orchestrator;private readonly CancellationTokenSource _shutdown=new();private readonly Task _processor;
 public RepairQueue(RepairOrchestrator orchestrator){_orchestrator=orchestrator;_processor=ProcessAsync(_shutdown.Token);}
 public async Task<RepairOrchestrationRecord>EnqueueAsync(string repairId,DiagnosticFinding? finding,bool userConfirmed,bool riskAcknowledged,CancellationToken cancellationToken){var completion=new TaskCompletionSource<RepairOrchestrationRecord>(TaskCreationOptions.RunContinuationsAsynchronously);var queued=new QueuedRepair(repairId,finding,userConfirmed,riskAcknowledged,cancellationToken,completion);await _channel.Writer.WriteAsync(queued,cancellationToken).ConfigureAwait(false);return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);}
 public async ValueTask DisposeAsync(){_channel.Writer.TryComplete();await _shutdown.CancelAsync().ConfigureAwait(false);try{await _processor.ConfigureAwait(false);}catch(OperationCanceledException){}while(_channel.Reader.TryRead(out var pending))pending.Completion.TrySetCanceled();_shutdown.Dispose();}
 private async Task ProcessAsync(CancellationToken shutdownToken){await foreach(var queued in _channel.Reader.ReadAllAsync(shutdownToken).ConfigureAwait(false)){try{var request=new RepairOrchestrationRequest(queued.RepairId,queued.Finding,new HashSet<string>(StringComparer.OrdinalIgnoreCase),new HashSet<string>(StringComparer.OrdinalIgnoreCase));var plan=await _orchestrator.AssessAndSimulateAsync(request,null,queued.CancellationToken).ConfigureAwait(false);if(plan.Stage!=RepairOrchestrationStage.AwaitingApproval){queued.Completion.TrySetResult(plan);continue;}var result=await _orchestrator.ExecuteAsync(plan.Id,queued.UserConfirmed,queued.RiskAcknowledged,null,queued.CancellationToken).ConfigureAwait(false);queued.Completion.TrySetResult(result);}catch(OperationCanceledException)when(queued.CancellationToken.IsCancellationRequested){queued.Completion.TrySetCanceled(queued.CancellationToken);}catch(Exception exception){queued.Completion.TrySetException(exception);}}}
 private sealed record QueuedRepair(string RepairId,DiagnosticFinding? Finding,bool UserConfirmed,bool RiskAcknowledged,CancellationToken CancellationToken,TaskCompletionSource<RepairOrchestrationRecord> Completion);
}
