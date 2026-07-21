using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class NetworkHealthPage : Page { public NetworkHealthPage(NetworkHealthViewModel viewModel) { InitializeComponent(); DataContext = viewModel; Loaded += async (_, _) => await viewModel.InitializeAsync(); } }
