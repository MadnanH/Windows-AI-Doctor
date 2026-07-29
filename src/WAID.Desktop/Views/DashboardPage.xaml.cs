using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;
    public DashboardPage(DashboardViewModel viewModel){InitializeComponent();_viewModel=viewModel;DataContext=viewModel;Loaded+=OnLoaded;}
    private async void OnLoaded(object sender,Microsoft.UI.Xaml.RoutedEventArgs e){Loaded-=OnLoaded;await _viewModel.LoadExplanationsAsync();}
    private async void OnRepairClick(object sender,Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if(sender is not Button { DataContext: RepairOption repair }) return;
        var dialog=new ContentDialog { Title=$"Run {repair.DisplayName}?",Content=$"Safety level: {repair.SafetyLevel}\n\n{repair.Description}\n\nWAID will verify administrator access and create available safeguards before making changes.",PrimaryButtonText="Run repair",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=XamlRoot };
        var result=await dialog.ShowAsync();
        if(result==ContentDialogResult.Primary) await _viewModel.RunRepairAsync(repair.Id,true);
    }
}
