using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;

namespace WAID.Desktop.Views;

public sealed partial class CaseExchangePage : Page
{
    private readonly CaseExchangeViewModel _viewModel;
    public CaseExchangePage(CaseExchangeViewModel viewModel){InitializeComponent();_viewModel=viewModel;DataContext=viewModel;}
    private void OnPasswordChanged(object sender,Microsoft.UI.Xaml.RoutedEventArgs args){if(sender is PasswordBox password)_viewModel.Password=password.Password;}
}
