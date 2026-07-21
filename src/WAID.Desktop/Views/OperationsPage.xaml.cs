using Microsoft.UI.Xaml.Controls;
using WAID.Application.Services;
using WAID.Desktop.ViewModels;
using WAID.Domain.Repairs;

namespace WAID.Desktop.Views;

public sealed partial class OperationsPage : Page
{
    private readonly OperationsViewModel _viewModel;
    public OperationsPage(OperationsViewModel viewModel){InitializeComponent();_viewModel=viewModel;DataContext=viewModel;Loaded+=async(_,_)=>await viewModel.LoadAsync();}
    private async void OnRepairClick(object sender,Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if(sender is not Button{DataContext:PrioritizedRepair repair})return;
        var details=await _viewModel.GetRepairDetailsAsync(repair);
        var acknowledgement=new CheckBox{Content="I understand the stated risk and approve this repair",IsChecked=repair.RiskLevel==SafetyLevel.Low};
        var dialog=new ContentDialog{Title=repair.Title,Content=new StackPanel{Spacing=8,Children={new TextBlock{Text=$"{details}\n\nExpected benefit: {repair.ExpectedBenefit}%\nAdministrator: {repair.RequiresAdministrator}\nConfidence: {repair.Confidence}%\nEvidence strength: {repair.EvidenceStrength}%",TextWrapping=Microsoft.UI.Xaml.TextWrapping.Wrap},acknowledgement}},PrimaryButtonText="Approve and run",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=XamlRoot};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)await _viewModel.ApproveAndRunAsync(repair,acknowledgement.IsChecked==true);
    }
    private async void OnApproveSafeClick(object sender,Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        var dialog=new ContentDialog{Title="Approve all low-risk repairs?",Content="Only repairs classified Low risk will be processed. Each repair receives a separate approval audit record and all normal safety gates remain active.",PrimaryButtonText="Approve low-risk repairs",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=XamlRoot};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)await _viewModel.ApproveAllSafeAsync();
    }
}
