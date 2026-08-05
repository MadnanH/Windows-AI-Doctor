using Microsoft.Extensions.DependencyInjection;
using WAID.Application.Services;
using WAID.Infrastructure;
using WAID.Infrastructure.Configuration;
using WAID.Infrastructure.Plugins;
var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(sender,eventArgs)=>{eventArgs.Cancel=true;cancellation.Cancel();};try
{
 var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Windows AI Doctor");var options=WaidHostOptions.CreateDesktopDefaults(root);var retention=EnterprisePolicyBootstrap.ReadRetention(options.EnterprisePolicyPath);if(retention is not null)options=options with{TechnicalLogRetentionDays=Math.Min(options.TechnicalLogRetentionDays,retention.DiagnosticDays),AuditRetentionDays=Math.Min(options.AuditRetentionDays,retention.AuditDays)};var services=new ServiceCollection();services.AddWaidInfrastructure(options);var pluginsAllowed=EnterprisePolicyBootstrap.IsAllowed(options.EnterprisePolicyPath,EnterpriseCapability.Plugins);services.AddWaidPlugins(new PluginSecurityPolicy(pluginsAllowed?options.AllowedPluginPublishers:[],options.RequireSignedPlugins),options.PluginDirectory,options.HostVersion);await using var provider=services.BuildValidatedWaidServiceProvider();await provider.GetRequiredService<IEnterprisePolicyService>().RefreshAsync(cancellation.Token);var app=new WaidCliApplication(provider.GetRequiredService<IWaidCliRuntime>(),provider.GetRequiredService<TimeProvider>());return await app.RunAsync(args,Console.Out,Console.Error,cancellation.Token);
}
catch(OperationCanceledException){await Console.Error.WriteLineAsync("WAID CLI cancelled.");return(int)CliExitCode.Cancelled;}
catch(WaidStartupException e){await Console.Error.WriteLineAsync($"Startup failed [{e.Code}] {e.UserMessage} Recovery: {e.RecoveryAction}");return(int)CliExitCode.OperationFailed;}
catch(Exception){await Console.Error.WriteLineAsync("WAID CLI could not start. Review the local WAID logs.");return(int)CliExitCode.InternalError;}