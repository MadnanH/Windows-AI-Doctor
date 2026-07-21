using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WAID.Desktop.ViewModels;
using WAID.Infrastructure;
namespace WAID.Desktop;
public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly ServiceProvider _services;
    public App() { InitializeComponent(); var services=new ServiceCollection(); var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Windows AI Doctor"); services.AddWaidInfrastructure(data).AddWaidPlugins(Path.Combine(AppContext.BaseDirectory,"Plugins"),new Version(1,0,0)); services.AddSingleton<DashboardViewModel>().AddSingleton<DiagnosisViewModel>().AddSingleton<SettingsViewModel>().AddSingleton<MainWindow>(); _services=services.BuildServiceProvider(new ServiceProviderOptions{ValidateOnBuild=true,ValidateScopes=true}); }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { _services.GetRequiredService<MainWindow>().Activate(); }
}
