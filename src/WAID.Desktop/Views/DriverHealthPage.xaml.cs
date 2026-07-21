using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class DriverHealthPage : Page
{
    public DriverHealthPage(DriverHealthViewModel viewModel){InitializeComponent();DataContext=viewModel;Loaded+=async(_,_)=>await viewModel.InitializeAsync();}
}
