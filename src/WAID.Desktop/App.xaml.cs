using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WAID.Desktop.ViewModels;
using WAID.Infrastructure;
using Serilog;
namespace WAID.Desktop;
public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly ServiceProvider _services;
    private readonly string _dataDirectory;
    public App()
    {
        _dataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Windows AI Doctor");
        UnhandledException+=OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException+=OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException+=OnUnobservedTaskException;
        try
        {
            InitializeComponent();
            var services=new ServiceCollection();
            services.AddWaidInfrastructure(_dataDirectory).AddWaidPlugins(Path.Combine(AppContext.BaseDirectory,"Plugins"),new Version(1,0,0));
            services.AddSingleton<DashboardViewModel>().AddSingleton<DiagnosisViewModel>().AddSingleton<SettingsViewModel>().AddSingleton<HistoryViewModel>().AddSingleton<MainWindow>();
            _services=services.BuildServiceProvider(new ServiceProviderOptions{ValidateOnBuild=true,ValidateScopes=true});
        }
        catch(Exception exception) { RecordFatal("Application initialization failed",exception); throw; }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { _services.GetRequiredService<MainWindow>().Activate(); }
        catch(Exception exception) { RecordFatal("Application launch failed",exception); throw; }
    }
    private void OnUnhandledException(object sender,Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        RecordFatal("Unhandled UI exception",args.Exception);
        args.Handled=true;
    }
    private void OnDomainUnhandledException(object? sender,System.UnhandledExceptionEventArgs args) => RecordFatal("Unhandled application-domain exception",args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject?.ToString()));
    private void OnUnobservedTaskException(object? sender,UnobservedTaskExceptionEventArgs args) { RecordFatal("Unobserved task exception",args.Exception); args.SetObserved(); }
    private void RecordFatal(string message,Exception exception)
    {
        Log.Fatal(exception,message);
        try { Directory.CreateDirectory(Path.Combine(_dataDirectory,"logs")); File.AppendAllText(Path.Combine(_dataDirectory,"logs","crash.log"),$"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}"); } catch { }
    }
}
