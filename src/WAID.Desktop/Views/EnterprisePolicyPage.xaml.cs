using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class EnterprisePolicyPage:Page{public EnterprisePolicyPage(EnterprisePolicyViewModel viewModel){InitializeComponent();DataContext=viewModel;}}