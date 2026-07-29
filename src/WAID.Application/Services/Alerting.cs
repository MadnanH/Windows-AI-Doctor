using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Domain.Repairs;

namespace WAID.Application.Services;

public enum AlertCategory { Hardware, Windows, Drivers, Security, Performance, Storage, Network, Updates }
public enum AlertSeverity { Information, Advisory, Warning, Critical }
public enum AlertState { Active, Acknowledged, Snoozed, Resolved }
public enum AlertDeliveryStatus { Delivered, SuppressedCooldown, SuppressedQuietHours, SuppressedSnooze, SuppressedAcknowledged, ChannelDisabled, Failed }

public sealed record AlertEvidence(string Source, string SourceReference, DateTimeOffset CapturedAtUtc, IReadOnlyDictionary<string,string> Values);
public sealed record AlertNotification(Guid Id,string DeduplicationKey,string RuleId,string RuleVersion,AlertCategory Category,AlertSeverity Severity,string Title,string Message,string ActionLabel,string ActionTarget,DateTimeOffset CreatedAtUtc,DateTimeOffset LastObservedAtUtc,DateTimeOffset? LastDeliveredAtUtc,int Occurrences,int EscalationLevel,AlertState State,DateTimeOffset? AcknowledgedAtUtc,DateTimeOffset? SnoozedUntilUtc,IReadOnlyList<AlertEvidence> Evidence);
public sealed record AlertDelivery(Guid Id,Guid AlertId,string Channel,DateTimeOffset AttemptedAtUtc,AlertDeliveryStatus Status,string Detail);
public sealed record AlertSettings(TimeSpan Cooldown,TimeOnly QuietHoursStart,TimeOnly QuietHoursEnd,AlertSeverity MinimumSeverity,IReadOnlyCollection<string> EnabledChannels,int EscalateAfterOccurrences,string PolicySource="User")
{
    public static AlertSettings Default { get; }=new(TimeSpan.FromHours(4),new(22,0),new(7,0),AlertSeverity.Advisory,new HashSet<string>(["in-app"],StringComparer.OrdinalIgnoreCase),3);
    public AlertSettings Validate(){if(Cooldown<TimeSpan.FromMinutes(5)||Cooldown>TimeSpan.FromDays(30))throw new InvalidOperationException("Alert cooldown must be between five minutes and 30 days.");if(EscalateAfterOccurrences is<2 or>100)throw new InvalidOperationException("Escalation occurrence count must be between 2 and 100.");if(string.IsNullOrWhiteSpace(PolicySource)||PolicySource.Length>100)throw new InvalidOperationException("A valid alert policy source is required.");return this;}
}
public sealed record AlertQuery(AlertCategory? Category=null,AlertSeverity? MinimumSeverity=null,AlertState? State=null,string? Search=null,int MaximumRecords=200);
public sealed record AlertRaiseRequest(string DeduplicationKey,string RuleId,string RuleVersion,AlertCategory Category,AlertSeverity Severity,string Title,string Message,string ActionLabel,string ActionTarget,AlertEvidence Evidence);
public sealed record AlertRaiseResult(AlertNotification Alert,IReadOnlyList<AlertDelivery> Deliveries,bool Created);

public interface IAlertRepository
{
    Task<AlertNotification?> GetActiveByKeyAsync(string key,CancellationToken token);
    Task SaveAsync(AlertNotification alert,CancellationToken token);
    Task SaveDeliveryAsync(AlertDelivery delivery,CancellationToken token);
    Task<IReadOnlyList<AlertNotification>> QueryAsync(AlertQuery query,CancellationToken token);
    Task<AlertSettings> GetSettingsAsync(CancellationToken token);
    Task SaveSettingsAsync(AlertSettings settings,CancellationToken token);
}
public interface IAlertDeliveryChannel{string Id{get;}Task DeliverAsync(AlertNotification alert,CancellationToken token);}
public interface IAlertPolicy{bool IsChannelAllowed(string channelId);}
public sealed class AllowConfiguredAlertPolicy:IAlertPolicy{public bool IsChannelAllowed(string channelId)=>true;}
public sealed class InAppAlertChannel:IAlertDeliveryChannel{public string Id=>"in-app";public Task DeliverAsync(AlertNotification alert,CancellationToken token)=>Task.CompletedTask;}

public sealed class AlertManager(IAlertRepository repository,IEnumerable<IAlertDeliveryChannel> channels,IAlertPolicy policy,TimeProvider time,ILogger<AlertManager> logger,IAuditTrailService? audit=null)
{
    private static readonly Regex SafeId=new("^[a-zA-Z0-9._:-]{1,160}$",RegexOptions.Compiled,TimeSpan.FromMilliseconds(100));
    private static readonly Regex Secret=new("(?i)(password|token|secret|product.?key|authorization|cookie|bearer)",RegexOptions.Compiled,TimeSpan.FromMilliseconds(100));
    private readonly IReadOnlyDictionary<string,IAlertDeliveryChannel> _channels=channels.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);

    public async Task<AlertRaiseResult> RaiseAsync(AlertRaiseRequest request,CancellationToken token)
    {
        Validate(request);var now=time.GetUtcNow();var settings=(await repository.GetSettingsAsync(token).ConfigureAwait(false)).Validate();var existing=await repository.GetActiveByKeyAsync(request.DeduplicationKey,token).ConfigureAwait(false);var created=existing is null;
        var evidence=Sanitize(request.Evidence);var occurrence=(existing?.Occurrences??0)+1;var escalation=Math.Min(3,occurrence/settings.EscalateAfterOccurrences);var alert=existing is null
            ?new(Guid.NewGuid(),request.DeduplicationKey,request.RuleId,request.RuleVersion,request.Category,request.Severity,Safe(request.Title),Safe(request.Message),Safe(request.ActionLabel),request.ActionTarget,now,now,null,1,escalation,AlertState.Active,null,null,[evidence])
            :existing with{RuleVersion=request.RuleVersion,Severity=(AlertSeverity)Math.Max((int)existing.Severity,(int)request.Severity),LastObservedAtUtc=now,Occurrences=occurrence,EscalationLevel=escalation,Evidence=existing.Evidence.Append(evidence).OrderByDescending(x=>x.CapturedAtUtc).Take(20).ToArray()};
        if(alert.State==AlertState.Snoozed&&alert.SnoozedUntilUtc<=now)alert=alert with{State=AlertState.Active,SnoozedUntilUtc=null};
        await repository.SaveAsync(alert,token).ConfigureAwait(false);
        var suppression=Suppression(alert,settings,now);var deliveries=new List<AlertDelivery>();
        if(suppression is not null)deliveries.Add(await RecordDelivery(alert,"policy",suppression.Value,Reason(suppression.Value),token).ConfigureAwait(false));
        else foreach(var channelId in settings.EnabledChannels.Order(StringComparer.OrdinalIgnoreCase))
        {
            if(!_channels.TryGetValue(channelId,out var channel)||!policy.IsChannelAllowed(channelId)){deliveries.Add(await RecordDelivery(alert,channelId,AlertDeliveryStatus.ChannelDisabled,"The channel is unavailable or disabled by policy.",token).ConfigureAwait(false));continue;}
            try{await channel.DeliverAsync(alert,token).ConfigureAwait(false);deliveries.Add(await RecordDelivery(alert,channelId,AlertDeliveryStatus.Delivered,"Delivered.",token).ConfigureAwait(false));alert=alert with{LastDeliveredAtUtc=now};}
            catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
            catch(Exception exception){logger.LogWarning("Alert channel {ChannelId} failed with {FailureType}",channelId,exception.GetType().Name);deliveries.Add(await RecordDelivery(alert,channelId,AlertDeliveryStatus.Failed,$"Delivery failed ({exception.GetType().Name}).",token).ConfigureAwait(false));}
        }
        await repository.SaveAsync(alert,token).ConfigureAwait(false);return new(alert,deliveries,created);
    }

    public async Task<AlertNotification> AcknowledgeAsync(Guid id,CancellationToken token){var alert=await Find(id,token).ConfigureAwait(false);var now=time.GetUtcNow();alert=alert with{State=AlertState.Acknowledged,AcknowledgedAtUtc=now,SnoozedUntilUtc=null};await repository.SaveAsync(alert,token).ConfigureAwait(false);await Audit(alert,"AlertAcknowledged",token).ConfigureAwait(false);return alert;}
    public async Task<AlertNotification> SnoozeAsync(Guid id,TimeSpan duration,CancellationToken token){if(duration<TimeSpan.FromMinutes(5)||duration>TimeSpan.FromDays(30))throw new ArgumentOutOfRangeException(nameof(duration));var alert=await Find(id,token).ConfigureAwait(false);alert=alert with{State=AlertState.Snoozed,SnoozedUntilUtc=time.GetUtcNow()+duration};await repository.SaveAsync(alert,token).ConfigureAwait(false);await Audit(alert,"AlertSnoozed",token).ConfigureAwait(false);return alert;}
    public async Task<AlertNotification> ResolveAsync(Guid id,CancellationToken token){var alert=await Find(id,token).ConfigureAwait(false);alert=alert with{State=AlertState.Resolved,SnoozedUntilUtc=null};await repository.SaveAsync(alert,token).ConfigureAwait(false);return alert;}
    private async Task<AlertNotification> Find(Guid id,CancellationToken token)=> (await repository.QueryAsync(new(MaximumRecords:500),token).ConfigureAwait(false)).FirstOrDefault(x=>x.Id==id)??throw new KeyNotFoundException("The alert no longer exists.");
    private static AlertDeliveryStatus? Suppression(AlertNotification alert,AlertSettings settings,DateTimeOffset now){if(alert.State==AlertState.Acknowledged)return AlertDeliveryStatus.SuppressedAcknowledged;if(alert.State==AlertState.Snoozed&&alert.SnoozedUntilUtc>now)return AlertDeliveryStatus.SuppressedSnooze;if(alert.Severity<settings.MinimumSeverity)return AlertDeliveryStatus.ChannelDisabled;if(alert.LastDeliveredAtUtc is{} delivered&&now-delivered<settings.Cooldown)return AlertDeliveryStatus.SuppressedCooldown;if(alert.Severity<AlertSeverity.Critical&&InsideQuietHours(TimeOnly.FromDateTime(now.LocalDateTime),settings.QuietHoursStart,settings.QuietHoursEnd))return AlertDeliveryStatus.SuppressedQuietHours;return null;}
    public static bool InsideQuietHours(TimeOnly value,TimeOnly start,TimeOnly end)=>start==end||start<end?value>=start&&value<end:value>=start||value<end;
    private async Task<AlertDelivery> RecordDelivery(AlertNotification alert,string channel,AlertDeliveryStatus status,string detail,CancellationToken token){var item=new AlertDelivery(Guid.NewGuid(),alert.Id,channel,time.GetUtcNow(),status,detail);await repository.SaveDeliveryAsync(item,token).ConfigureAwait(false);return item;}
    private async Task Audit(AlertNotification alert,string action,CancellationToken token){if(audit is null)return;try{await audit.AppendAsync(new(Guid.NewGuid(),time.GetUtcNow(),AuditActor.User,action,alert.Id.ToString(),AuditResult.Succeeded,SafetyLevel.Low,false,false,alert.Id,alert.Id,$"{action}; category {alert.Category}; severity {alert.Severity}."),token).ConfigureAwait(false);}catch(Exception exception)when(exception is not OperationCanceledException){logger.LogWarning("Alert audit failed with {FailureType}",exception.GetType().Name);}}
    private static void Validate(AlertRaiseRequest request){if(!SafeId.IsMatch(request.DeduplicationKey)||!SafeId.IsMatch(request.RuleId)||string.IsNullOrWhiteSpace(request.RuleVersion)||request.RuleVersion.Length>40)throw new InvalidDataException("Alert identifiers or rule version are invalid.");if(string.IsNullOrWhiteSpace(request.Title)||request.Title.Length>200||string.IsNullOrWhiteSpace(request.Message)||request.Message.Length>2000)throw new InvalidDataException("Alert text is invalid.");if(!request.ActionTarget.StartsWith("waid://",StringComparison.OrdinalIgnoreCase)||request.ActionTarget.Length>200)throw new InvalidDataException("Alert action target must be an internal WAID link.");}
    private static AlertEvidence Sanitize(AlertEvidence e){if(string.IsNullOrWhiteSpace(e.Source)||string.IsNullOrWhiteSpace(e.SourceReference))throw new InvalidDataException("Alert evidence provenance is required.");return e with{Values=e.Values.Where(x=>!Secret.IsMatch(x.Key)&&!Secret.IsMatch(x.Value)).Take(50).ToDictionary(x=>Safe(x.Key),x=>Safe(x.Value),StringComparer.OrdinalIgnoreCase)};}
    private static string Safe(string value){var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);var safe=string.IsNullOrWhiteSpace(profile)?value:value.Replace(profile,"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);return Secret.Replace(safe,"[redacted]");}
    private static string Reason(AlertDeliveryStatus s)=>s switch{AlertDeliveryStatus.SuppressedCooldown=>"Suppressed during cooldown.",AlertDeliveryStatus.SuppressedQuietHours=>"Suppressed during quiet hours.",AlertDeliveryStatus.SuppressedSnooze=>"Suppressed while snoozed.",AlertDeliveryStatus.SuppressedAcknowledged=>"Suppressed after acknowledgement.",_=>"Suppressed by severity or channel policy."};
}
