using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class SettingsPage : Page { public SettingsPage(SettingsViewModel viewModel){InitializeComponent();DataContext=viewModel;} }
