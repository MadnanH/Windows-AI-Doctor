using System.Text.Json;
using System.Text.Json.Serialization;
namespace WAID.Application.Services;
public enum CliOutputFormat{Human,Json}
public enum CliExitCode{Success=0,Usage=2,NotFound=3,PolicyDenied=4,PermissionDenied=5,Cancelled=6,Conflict=7,OperationFailed=8,InternalError=10}
public sealed record CliRequest(string Command,string? Subcommand,IReadOnlyDictionary<string,string>Options,CliOutputFormat OutputFormat,bool HelpRequested)
{
 public string? Option(string name)=>Options.TryGetValue(name,out var value)?value:null;public bool Flag(string name)=>Options.TryGetValue(name,out var value)&&value.Equals("true",StringComparison.OrdinalIgnoreCase);
}
public sealed record CliError(string Code,string Message,string RecoveryAction);
public sealed record CliResult(CliExitCode ExitCode,object? Data=null,CliError? Error=null){public bool Succeeded=>ExitCode==CliExitCode.Success;public static CliResult Success(object?data)=>new(CliExitCode.Success,data);public static CliResult Failure(CliExitCode exit,string code,string message,string recovery)=>new(exit,null,new(code,message,recovery));}
public sealed record CliEnvelope(string SchemaVersion,string Command,bool Succeeded,int ExitCode,DateTimeOffset CompletedAtUtc,object?Data,CliError?Error);
public sealed class CliParseException(string message):InvalidOperationException(message);
public static class WaidCliParser
{
 static readonly HashSet<string>Commands=new(["version","status","scan","findings","report","timeline","policy","plugins","repair-plan"],StringComparer.OrdinalIgnoreCase);
 static readonly HashSet<string>Flags=new(["json","human","help","approve","acknowledge-risk","refresh"],StringComparer.OrdinalIgnoreCase);
 public static CliRequest Parse(IReadOnlyList<string>args){if(args.Count>64)throw new CliParseException("Too many command-line arguments.");if(args.Count==0)return new("help",null,new Dictionary<string,string>(),CliOutputFormat.Human,true);var index=0;var command=args[index++].Trim().ToLowerInvariant();if(command is "help" or "--help" or "-h")return new("help",null,new Dictionary<string,string>(),CliOutputFormat.Human,true);if(!Commands.Contains(command))throw new CliParseException($"Unknown command '{Clean(command)}'.");string?sub=null;if(command=="repair-plan"&&index<args.Count&&!args[index].StartsWith('-'))sub=args[index++].Trim().ToLowerInvariant();var options=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);while(index<args.Count){var token=args[index++];if(!token.StartsWith("--",StringComparison.Ordinal)||token.Length<3)throw new CliParseException($"Unexpected argument '{Clean(token)}'.");var key=token[2..];if(options.ContainsKey(key))throw new CliParseException($"Option --{Clean(key)} was specified more than once.");if(Flags.Contains(key)){options[key]="true";continue;}if(index>=args.Count||args[index].StartsWith("--",StringComparison.Ordinal))throw new CliParseException($"Option --{Clean(key)} requires a value.");var value=args[index++];if(value.Length>1000)throw new CliParseException($"Option --{Clean(key)} exceeds 1,000 characters.");options[key]=value;}if(options.ContainsKey("json")&&options.ContainsKey("human"))throw new CliParseException("Choose either --json or --human, not both.");if(command=="repair-plan"&&sub is not("list" or "simulate" or "execute"))throw new CliParseException("repair-plan requires list, simulate, or execute.");return new(command,sub,options,options.ContainsKey("json")?CliOutputFormat.Json:CliOutputFormat.Human,options.ContainsKey("help"));}
 static string Clean(string value)=>new(value.Where(x=>!char.IsControl(x)).Take(100).ToArray());
}
public static class RepairExecutionConfirmation
{
 public static bool IsValid(CliRequest request,Guid planId,out string expected)
 {
  expected=$"EXECUTE WAID REPAIR {planId:D}";
  return Guid.TryParse(request.Option("id"),out var requestedPlanId)
   && requestedPlanId==planId
   && request.Flag("approve")
   && request.Flag("acknowledge-risk")
   && string.Equals(request.Option("confirmation"),expected,StringComparison.Ordinal);
 }
}
public interface IWaidCliRuntime{Task<CliResult>ExecuteAsync(CliRequest request,IProgress<string>?progress,CancellationToken token);}
public sealed class WaidCliApplication(IWaidCliRuntime runtime,TimeProvider time)
{
 public const string SchemaVersion="waid-cli-1.0";
 static readonly JsonSerializerOptions JsonOptions=CreateOptions();
 public async Task<int>RunAsync(IReadOnlyList<string>args,TextWriter output,TextWriter error,CancellationToken token){CliRequest request;try{request=WaidCliParser.Parse(args);}catch(CliParseException e){await error.WriteLineAsync(e.Message);await error.WriteLineAsync("Run 'waid help' for usage.");return(int)CliExitCode.Usage;}if(request.HelpRequested||request.Command=="help"){await output.WriteAsync(Help);return 0;}var progress=new InlineProgress(message=>error.WriteLine(message));CliResult result;try{result=await runtime.ExecuteAsync(request,progress,token);}catch(CliParseException e){result=CliResult.Failure(CliExitCode.Usage,"WAID-CLI-OPTION",e.Message,"Run 'waid help' and correct the option.");}catch(OperationCanceledException){result=CliResult.Failure(CliExitCode.Cancelled,"WAID-CLI-CANCELLED","The command was cancelled.","Run the command again when ready.");}catch(EnterprisePolicyException e){result=CliResult.Failure(CliExitCode.PolicyDenied,e.Code,e.Message,e.RecoveryAction);}catch(UnauthorizedAccessException){result=CliResult.Failure(CliExitCode.PermissionDenied,"WAID-CLI-PERMISSION","Windows denied permission.","Use an authorized account; elevate only when executing an approved repair.");}catch(Exception){result=CliResult.Failure(CliExitCode.InternalError,"WAID-CLI-UNEXPECTED","The command could not complete.","Review Logs & Audit and retry the non-destructive command.");}var envelope=new CliEnvelope(SchemaVersion,$"{request.Command}{(request.Subcommand is null?"":" "+request.Subcommand)}",result.Succeeded,(int)result.ExitCode,time.GetUtcNow(),result.Data,result.Error);if(request.OutputFormat==CliOutputFormat.Json)await output.WriteLineAsync(JsonSerializer.Serialize(envelope,JsonOptions));else await output.WriteAsync(RenderHuman(envelope));return(int)result.ExitCode;}
 static string RenderHuman(CliEnvelope x){if(!x.Succeeded)return$"Failed [{x.Error!.Code}] {x.Error.Message}{Environment.NewLine}Recovery: {x.Error.RecoveryAction}{Environment.NewLine}";var data=x.Data is null?"No data.":JsonSerializer.Serialize(x.Data,JsonOptions);return$"WAID {x.Command} - succeeded{Environment.NewLine}{data}{Environment.NewLine}";}
 static JsonSerializerOptions CreateOptions(){var o=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};o.Converters.Add(new JsonStringEnumConverter());return o;}
 public const string Help="""
Windows AI Doctor CLI

Usage: waid <command> [options] [--json|--human]

Commands:
  version                         Show CLI and application contract versions.
  status                          Show database, modules, and effective policy status.
  scan                            Run the same read-only scanners as the desktop app.
  findings [--limit N]            Show persisted findings from recent scans.
  report [--export package]       Show the latest diagnosis or export a redacted ZIP package.
  timeline [--limit N]            Show persisted reliability timeline events.
  policy [--refresh]              Show effective enterprise policy and locks.
  plugins                         Show certified plugin inventory and state.
  repair-plan list                Show recent repair plans.
  repair-plan simulate --repair ID
                                  Create a read-only repair simulation; no command executes.
  repair-plan execute --id GUID --approve --acknowledge-risk --confirmation "EXECUTE WAID REPAIR GUID"
                                  Execute only the exact current plan through all WAID safety gates.

JSON output uses schema waid-cli-1.0. Progress is written to stderr so stdout remains machine-readable.
Exit codes: 0 success, 2 usage, 3 not found, 4 policy denied, 5 permission, 6 cancelled, 7 conflict, 8 operation failure, 10 internal error.
""";
 sealed class InlineProgress(Action<string> report):IProgress<string>{public void Report(string value)=>report(value);}
}
