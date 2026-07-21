using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class DashboardPage : Page { public DashboardPage(DashboardViewModel viewModel){InitializeComponent();DataContext=viewModel;} }
