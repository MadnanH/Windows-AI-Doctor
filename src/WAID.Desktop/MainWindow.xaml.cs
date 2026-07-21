using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
using WAID.Desktop.Views;
namespace WAID.Desktop;
public sealed partial class MainWindow : Window
{
    private readonly DashboardViewModel _dashboard; private readonly DiagnosisViewModel _diagnosis; private readonly SettingsViewModel _settings;
    public MainWindow(DashboardViewModel dashboard,DiagnosisViewModel diagnosis,SettingsViewModel settings) { InitializeComponent(); _dashboard=dashboard; _diagnosis=diagnosis; _settings=settings; Navigation.SelectedItem=Navigation.MenuItems[0]; ShowDashboard(); }
    private void OnSelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args) { if(args.IsSettingsSelected){ContentFrame.Content=new SettingsPage(_settings);return;} var tag=(args.SelectedItem as NavigationViewItem)?.Tag as string; ContentFrame.Content=tag switch{"diagnosis"=>new DiagnosisPage(_diagnosis),"health"=>new HealthPage(_diagnosis),_=>new DashboardPage(_dashboard)}; }
    private void ShowDashboard()=>ContentFrame.Content=new DashboardPage(_dashboard);
}
