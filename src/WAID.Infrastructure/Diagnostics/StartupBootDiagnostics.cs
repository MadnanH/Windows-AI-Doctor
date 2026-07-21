using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WAID.Application.Abstractions;
using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Diagnostics;

public sealed class WindowsStartupInventoryProvider(IPowerShellRunner powerShell) : IStartupInventoryProvider
{
    private const string Script="""
      $items=@()
      $runPaths=@('HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run','HKCU:\Software\Microsoft\Windows\CurrentVersion\Run')
      foreach($key in $runPaths){if(Test-Path $key){$p=Get-ItemProperty $key -ErrorAction SilentlyContinue;foreach($n in $p.PSObject.Properties|Where-Object{$_.Name-notmatch'^PS'}){$items+=[pscustomobject]@{name=$n.Name;source='RunKey';publisher='Unknown';command=[string]$n.Value;enabled=$true;critical=$false;reference="$key::$($n.Name)";impact=$null}}}}
      $folders=@([Environment]::GetFolderPath('Startup'),[Environment]::GetFolderPath('CommonStartup'));foreach($folder in $folders){Get-ChildItem $folder -File -ErrorAction SilentlyContinue|ForEach-Object{$items+=[pscustomobject]@{name=$_.BaseName;source='StartupFolder';publisher='Unknown';command=$_.FullName;enabled=$true;critical=$false;reference=$_.FullName;impact=$null}}}
      $winlogon='HKLM:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon';if(Test-Path $winlogon){$wl=Get-ItemProperty $winlogon -ErrorAction SilentlyContinue;foreach($n in 'Shell','Userinit'){if($wl.$n){$items+=[pscustomobject]@{name="Windows logon $n";source='LoginTrigger';publisher='Microsoft';command=[string]$wl.$n;enabled=$true;critical=$true;reference="$winlogon::$n";impact=$null}}}}
      $serviceFailures=@(Get-WinEvent -FilterHashtable @{LogName='System';Id=7000,7001;StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue|ForEach-Object{$_.Message})
      Get-CimInstance Win32_Service -ErrorAction Stop|Where-Object{$_.StartMode-eq'Auto'}|ForEach-Object{$svc=$_;$failed=@($serviceFailures|Where-Object{$_-match[regex]::Escape($svc.Name)}).Count-gt0;$items+=[pscustomobject]@{name=$svc.DisplayName;source='Service';publisher=if($svc.PathName-match'Windows\\System32'){'Microsoft'}else{'Unknown'};command=$svc.PathName;enabled=$true;failed=$failed;critical=($svc.Name-in @('RpcSs','DcomLaunch','EventLog','WinDefend','SamSs','LSM'));reference="Service:$($svc.Name)";impact=$null}}
      Get-ScheduledTask -ErrorAction SilentlyContinue|Where-Object{$_.Triggers|Where-Object{$_.CimClass.CimClassName-match'Logon|Boot'}}|ForEach-Object{$cmd=(@($_.Actions|ForEach-Object{"$($_.Execute) $($_.Arguments)"})-join'; ');$items+=[pscustomobject]@{name=$_.TaskName;source='ScheduledTask';publisher=if($_.TaskPath-like'\Microsoft\*'){'Microsoft'}else{'Unknown'};command=$cmd;enabled=$_.State-ne'Disabled';critical=$_.TaskPath-like'\Microsoft\Windows\*';reference="Task:$($_.TaskPath)$($_.TaskName)";impact=$null}}
      Get-CimInstance Win32_StartupCommand -ErrorAction SilentlyContinue|ForEach-Object{$items+=[pscustomobject]@{name=$_.Name;source='StartupApplication';publisher='Unknown';command=$_.Command;enabled=$true;critical=$false;reference="StartupCommand:$($_.Location):$($_.Name)";impact=$null}}
      $shell='HKLM:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved';if(Test-Path $shell){(Get-ItemProperty $shell).PSObject.Properties|Where-Object{$_.Name-notmatch'^PS'}|ForEach-Object{$items+=[pscustomobject]@{name=[string]$_.Value;source='ShellExtension';publisher='Unknown';command=$_.Name;enabled=$true;critical=$false;reference="$shell::$($_.Name)";impact=$null}}}
      $boots=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational';Id=100;StartTime=(Get-Date).AddDays(-30)} -ErrorAction SilentlyContinue|Select-Object -First 20|ForEach-Object{[pscustomobject]@{time=$_.TimeCreated.ToUniversalTime().ToString('O');main=[double]$_.Properties[6].Value;post=[double]$_.Properties[7].Value;reference="EventLog:Diagnostics-Performance:100:$($_.RecordId)"}})
      [pscustomobject]@{items=$items;boots=$boots} | ConvertTo-Json -Depth 6 -Compress
      """;
    public async Task<StartupSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var result=await powerShell.RunAsync(Script,new Dictionary<string,object?>(),cancellationToken).ConfigureAwait(false);
        if(!result.Succeeded)throw new StartupDiagnosticsException("WAID-BOOT-COLLECT","Windows startup information could not be read.",string.Join("; ",result.Errors));
        var payload=JsonSerializer.Deserialize<Payload>(string.Join(Environment.NewLine,result.Output),Options)??throw new StartupDiagnosticsException("WAID-BOOT-DATA","Windows returned an empty startup inventory.","No payload was returned."); var now=DateTimeOffset.UtcNow;
        var entries=(payload.Items??[]).Where(x=>!string.IsNullOrWhiteSpace(x.Name)&&Enum.TryParse<StartupSource>(x.Source,true,out _)).Take(20000).Select(x=>Create(x)).GroupBy(x=>$"{x.Name.Trim()}|{x.Command.Trim()}",StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderBy(x=>x.Source==StartupSource.StartupApplication?1:0).First()).ToArray();
        var boots=(payload.Boots??[]).Where(x=>DateTimeOffset.TryParse(x.Time,out _)).Take(100).Select(x=>new BootMeasurement(DateTimeOffset.Parse(x.Time!).ToUniversalTime(),ValidDuration(x.Main),ValidDuration(x.Post),Clean(x.Reference))).ToArray();
        return new(now,entries,boots,boots.Length==0?["Windows boot performance events were unavailable; impact remains unknown rather than healthy."]:[]);
    }
    private static StartupEntry Create(ItemDto x){var command=Clean(x.Command,1000);var path=CommandLineParser.TryGetExecutablePath(command);var source=Enum.Parse<StartupSource>(x.Source!,true);return new(Hash($"{Clean(x.Name)}:{command}"),Clean(x.Name),source,Clean(x.Publisher),command,RedactPath(path),x.Enabled,PathExists(path),x.Failed,x.Critical||IsProtectedName(x.Name),string.Equals(x.Publisher,"Microsoft",StringComparison.OrdinalIgnoreCase),ValidDuration(x.Impact),Clean(x.Reference));}
    private static bool IsProtectedName(string? name)=>new[]{"Windows Defender","SecurityHealth","Windows Security","RPC","Event Log"}.Any(x=>(name??"").Contains(x,StringComparison.OrdinalIgnoreCase));
    private static bool PathExists(string path)=>string.IsNullOrWhiteSpace(path)||path.StartsWith("{",StringComparison.Ordinal)?true:File.Exists(Environment.ExpandEnvironmentVariables(path));
    private static double? ValidDuration(double? value)=>value is >0 and <3_600_000?value:null;
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20];
    private static string RedactPath(string value)=>Clean(value).Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);
    private static string Clean(string? value,int max=300){var clean=(value??string.Empty).Replace(Environment.UserName,"[user]",StringComparison.OrdinalIgnoreCase).Replace("token=","[redacted]=",StringComparison.OrdinalIgnoreCase).Trim();return clean.Length<=max?clean:clean[..max];}
    private static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true};
    private sealed record Payload(ItemDto[]? Items,BootDto[]? Boots); private sealed record ItemDto(string? Name,string? Source,string? Publisher,string? Command,bool Enabled,bool Failed,bool Critical,string? Reference,double? Impact); private sealed record BootDto(string? Time,double? Main,double? Post,string? Reference);
}

public static class CommandLineParser
{
    public static string TryGetExecutablePath(string? command)
    {
        if(string.IsNullOrWhiteSpace(command))return string.Empty;var value=Environment.ExpandEnvironmentVariables(command.Trim());
        if(value[0]=='"'){var end=value.IndexOf('"',1);return end>1?value[1..end]:string.Empty;}
        var exe=value.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);return exe>=0?value[..(exe+4)].Trim():value.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??string.Empty;
    }
}

public sealed class StartupBootAnalyzer(IStartupInventoryProvider provider,IBootHealthRepository repository,ILogger<StartupBootAnalyzer> logger):IStartupBootAnalyzer
{
    public async Task<BootHealthReport> AnalyzeAsync(CancellationToken cancellationToken){var snapshot=await provider.CollectAsync(cancellationToken).ConfigureAwait(false);var previous=await repository.GetLatestAsync(cancellationToken).ConfigureAwait(false);var changes=Changes(previous,snapshot);var recommendations=Evaluate(snapshot);var report=new BootHealthReport(Guid.NewGuid(),snapshot.CollectedAtUtc,snapshot.Entries,snapshot.BootMeasurements,recommendations,changes,snapshot.Limitations);await repository.SaveAsync(snapshot,report,cancellationToken).ConfigureAwait(false);logger.LogInformation("Boot analysis {ReportId} stored {EntryCount} entries, {BootCount} boot measurements and {RecommendationCount} recommendations",report.Id,report.Entries.Count,report.BootMeasurements.Count,report.Recommendations.Count);return report;}
    public static IReadOnlyList<StartupRecommendation> Evaluate(StartupSnapshot snapshot)
    {
        var list=new List<StartupRecommendation>();var bootMedian=Median(snapshot.BootMeasurements.Select(x=>(x.MainPathMilliseconds??0)+(x.PostBootMilliseconds??0)).Where(x=>x>0));
        foreach(var e in snapshot.Entries){var evidence=new List<StartupEvidence>{new("source",e.Source.ToString(),e.SourceReference,snapshot.CollectedAtUtc)};var impact=Impact(e.MeasuredImpactMilliseconds,bootMedian,snapshot.Entries.Count);if(e.MeasuredImpactMilliseconds is not null)evidence.Add(new("measuredImpactMs",e.MeasuredImpactMilliseconds.Value.ToString("F0"),e.SourceReference,snapshot.CollectedAtUtc));
            if(e.HasRecentFailure){evidence.Add(new("recentFailure","true",e.SourceReference,snapshot.CollectedAtUtc));list.Add(Recommendation(e,StartupConcern.Failed,StartupImpact.Unknown,"Windows recorded a recent startup failure linked to this entry.",evidence));}
            if(!e.TargetExists&&!string.IsNullOrWhiteSpace(e.ExecutablePath))list.Add(Recommendation(e,StartupConcern.MissingTarget,StartupImpact.Low,"The startup command points to a target that was not found. Removable drives and packaged apps can make this temporary.",evidence));
            else if(e.Enabled&&impact is StartupImpact.High or StartupImpact.Medium&&!e.IsCritical)list.Add(Recommendation(e,StartupConcern.Performance,impact,e.MeasuredImpactMilliseconds is null?"This non-critical entry contributes to a large startup set; impact is estimated, not measured.":"Windows evidence reports measurable startup impact for this non-critical entry.",evidence));
            if(!e.IsMicrosoftSigned&&!e.IsCritical&&e.Publisher.Equals("Unknown",StringComparison.OrdinalIgnoreCase)&&e.Enabled)list.Add(Recommendation(e,StartupConcern.SecurityReview,StartupImpact.Unknown,"Publisher identity is unavailable. This is a review signal, not a malware verdict.",evidence));}
        return list.GroupBy(x=>new{x.EntryId,x.Concern}).Select(x=>x.First()).ToArray();
    }
    private static StartupRecommendation Recommendation(StartupEntry e,StartupConcern concern,StartupImpact impact,string explanation,IReadOnlyList<StartupEvidence> evidence)=>new(e.Id,e.Name,concern,impact,explanation,evidence,!e.IsCritical,e.Source is StartupSource.Service or StartupSource.ScheduledTask,e.IsCritical?"Protected Windows entry: inspection only; WAID will not disable it.":$"After explicit approval, disable '{e.Name}' at {e.SourceReference}; no file is deleted.",e.IsCritical?"No state change is offered for protected entries.":$"Restore the captured enabled state and source metadata for '{e.Name}'.",e.IsCritical);
    private static StartupImpact Impact(double? measured,double median,int count)=>measured switch{>=5000=>StartupImpact.High,>=1500=>StartupImpact.Medium,>0=>StartupImpact.Low,_ when median>=60_000&&count>=25=>StartupImpact.Medium,_=>StartupImpact.Unknown};
    private static double Median(IEnumerable<double> values){var a=values.Order().ToArray();return a.Length==0?0:a.Length%2==1?a[a.Length/2]:(a[a.Length/2-1]+a[a.Length/2])/2;}
    private static IReadOnlyList<StartupChange> Changes(BootHealthReport? old,StartupSnapshot current){if(old is null)return[];var map=old.Entries.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);return current.Entries.Where(x=>map.TryGetValue(x.Id,out var p)&&p.Enabled!=x.Enabled).Select(x=>new StartupChange(x.Id,x.Name,"EnabledStateChanged",map[x.Id].Enabled.ToString(),x.Enabled.ToString(),current.CollectedAtUtc)).ToArray();}
}

public sealed class StartupActionPlanner:IStartupActionPlanner
{
    public StartupActionSimulation SimulateDisable(StartupEntry entry)=>entry.IsCritical?new(false,false,"This Windows-critical startup entry is protected and cannot be disabled by WAID.",string.Empty):new(true,entry.Enabled,"Simulation only: explicit approval and a captured rollback record are required before execution.",JsonSerializer.Serialize(new{entry.Id,entry.Source,entry.SourceReference,entry.Enabled}));
    public StartupActionSimulation SimulateRollback(StartupEntry entry,string rollbackMetadata){if(string.IsNullOrWhiteSpace(rollbackMetadata))return new(false,false,"Rollback metadata is required.",string.Empty);try{using var doc=JsonDocument.Parse(rollbackMetadata);var id=doc.RootElement.GetProperty("Id").GetString();return string.Equals(id,entry.Id,StringComparison.Ordinal)?new(true,true,"Simulation confirms the captured enabled state can be restored.",rollbackMetadata):new(false,false,"Rollback metadata belongs to a different entry.",string.Empty);}catch(JsonException){return new(false,false,"Rollback metadata is invalid.",string.Empty);}}
}
public sealed class StartupDiagnosticsException(string code,string message,string detail):InvalidOperationException(message){public string Code{get;}=code;public string Detail{get;}=detail;}
