using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
using WAID.Desktop.Views;
namespace WAID.Desktop;
public sealed partial class MainWindow : Window
{
    private readonly WAID.Infrastructure.Plugins.PluginCatalog _plugins; private readonly WAID.Infrastructure.WaidModuleCatalog _modules;
    private readonly DashboardViewModel _dashboard; private readonly DiagnosisViewModel _diagnosis; private readonly SettingsViewModel _settings; private readonly HistoryViewModel _history; private readonly OperationsViewModel _operations;
    public MainWindow(DashboardViewModel dashboard,DiagnosisViewModel diagnosis,SettingsViewModel settings,HistoryViewModel history,OperationsViewModel operations,WAID.Infrastructure.Plugins.PluginCatalog plugins,WAID.Infrastructure.WaidModuleCatalog modules) { InitializeComponent(); _dashboard=dashboard; _diagnosis=diagnosis; _settings=settings; _history=history; _operations=operations;_plugins=plugins;_modules=modules; Navigation.SelectedItem=Navigation.MenuItems[0]; ShowDashboard(); }
    private void OnSelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args) { if(args.IsSettingsSelected){ContentFrame.Content=new SettingsPage(_settings);return;} var tag=(args.SelectedItem as NavigationViewItem)?.Tag as string; ContentFrame.Content=tag switch{"diagnosis"=>new DiagnosisPage(_diagnosis),"health"=>new HealthPage(_diagnosis),"operations"=>new OperationsPage(_operations),"recommended"=>new RecommendedRepairsPage(_diagnosis,_dashboard),"history"=>new HistoryPage(_history),"plugins"=>new PluginsPage(_plugins,_modules),_=>new DashboardPage(_dashboard)}; }
    private void ShowDashboard()=>ContentFrame.Content=new DashboardPage(_dashboard);
}
