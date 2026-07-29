using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WAID.Application.Services;
using WAID.Desktop.ViewModels;
using WAID.Infrastructure;
using WAID.Infrastructure.Diagnostics;
using WAID.Infrastructure.Plugins;

namespace WAID.Desktop;

public partial class App : Microsoft.UI.Xaml.Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private WaidStartupException? _startupFailure;
    private Window? _recoveryWindow;
    private readonly string _dataDirectory;

    public App()
    {
        _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Windows AI Doctor");
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        InitializeComponent();
        try
        {
            var options = WaidHostOptions.CreateDesktopDefaults(_dataDirectory);
            var services = new ServiceCollection();
            services.AddWaidInfrastructure(options)
                .AddWaidPlugins(new PluginSecurityPolicy(options.AllowedPluginPublishers, options.RequireSignedPlugins), options.PluginDirectory, options.HostVersion);
            services.AddSingleton<DashboardViewModel>().AddSingleton<DiagnosisViewModel>().AddSingleton<SettingsViewModel>()
                .AddSingleton<HistoryViewModel>().AddSingleton<DigitalTwinViewModel>().AddSingleton<PerformanceHistoryViewModel>().AddSingleton<ReliabilityTimelineViewModel>().AddSingleton<LiveMonitoringViewModel>().AddSingleton<PredictiveHealthViewModel>().AddSingleton<EvidenceExplorerViewModel>().AddSingleton<KnowledgeViewModel>().AddSingleton<OperationsViewModel>().AddSingleton<AuditViewModel>().AddSingleton<DriverHealthViewModel>().AddSingleton<BootHealthViewModel>().AddSingleton<UpdateHealthViewModel>().AddSingleton<StorageCenterViewModel>().AddSingleton<SecurityCenterViewModel>().AddSingleton<NetworkHealthViewModel>().AddSingleton<ChatViewModel>().AddSingleton<MainWindow>();
            _services = services.BuildValidatedWaidServiceProvider();
            _logger = _services.GetRequiredService<ILogger<App>>();
        }
        catch (WaidStartupException exception) { _startupFailure = exception; RecordFatal("Application configuration or service validation failed", exception); }
        catch (Exception exception)
        {
            _startupFailure = new WaidStartupException("WAID-STARTUP-UNEXPECTED", "Windows AI Doctor could not initialize safely.", "Restart the application. If the problem continues, repair the installation.", exception);
            RecordFatal("Application initialization failed", exception);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_startupFailure is not null || _services is null) { ShowRecovery(_startupFailure ?? new("WAID-STARTUP-MISSING", "Application services are unavailable.", "Repair the application installation.")); return; }
        try { _services.GetRequiredService<ScheduledScanLoopService>().Start(); _services.GetRequiredService<MainWindow>().Activate(); }
        catch (Exception exception)
        {
            RecordFatal("Application launch failed", exception);
            ShowRecovery(new WaidStartupException("WAID-LAUNCH-FAILED", "Windows AI Doctor could not open its main workspace.", "Close the application, disable recently installed plugins, and try again.", exception));
        }
    }

    private void ShowRecovery(WaidStartupException failure)
    {
        var panel = new StackPanel { Padding = new Thickness(32), Spacing = 14, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = "Windows AI Doctor needs attention", FontSize = 28, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(title, "StartupRecoveryTitle");
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = failure.UserMessage, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Reference: {failure.Code}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = failure.RecoveryAction, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "No scanner or repair was started. Configuration and service validation stopped startup safely.", TextWrapping = TextWrapping.Wrap });
        _recoveryWindow = new Window { Title = "Windows AI Doctor - Recovery", Content = panel };
        _recoveryWindow.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args) { RecordFatal("Unhandled UI exception", args.Exception); args.Handled = true; }
    private void OnDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs args) => RecordFatal("Unhandled application-domain exception", args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject?.ToString()));
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args) { RecordFatal("Unobserved task exception", args.Exception); args.SetObserved(); }

    private void RecordFatal(string message, Exception exception)
    {
        var safeDetail = ReportRedactor.RedactText($"{exception.GetType().Name}: {exception.Message}");
        _logger?.LogCritical("{FailureMessage}. {FailureDetail}", message, safeDetail);
        try { Directory.CreateDirectory(Path.Combine(_dataDirectory, "logs")); File.AppendAllText(Path.Combine(_dataDirectory, "logs", "crash.log"), $"{DateTimeOffset.UtcNow:O} {message}. {safeDetail}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
