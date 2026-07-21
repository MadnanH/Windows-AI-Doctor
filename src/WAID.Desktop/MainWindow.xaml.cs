using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
using WAID.Desktop.Views;
namespace WAID.Desktop;
public sealed partial class MainWindow : Window
{
    private readonly DashboardViewModel _dashboard; private readonly SettingsViewModel _settings;
    public MainWindow(DashboardViewModel dashboard,SettingsViewModel settings) { InitializeComponent(); _dashboard=dashboard; _settings=settings; Navigation.SelectedItem=Navigation.MenuItems[0]; ShowDashboard(); }
    private void OnSelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args) { if(args.IsSettingsSelected) ContentFrame.Content=new SettingsPage(_settings); else ShowDashboard(); }
    private void ShowDashboard()=>ContentFrame.Content=new DashboardPage(_dashboard);
}
