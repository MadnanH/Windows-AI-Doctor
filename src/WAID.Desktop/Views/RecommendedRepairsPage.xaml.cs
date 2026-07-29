using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
using WAID.Diagnosis;
using WAID.Domain.Repairs;

namespace WAID.Desktop.Views;

public sealed partial class RecommendedRepairsPage:Page
{
 private readonly DashboardViewModel _dashboard;
 public RecommendedRepairsPage(DiagnosisViewModel diagnosis,DashboardViewModel dashboard){InitializeComponent();DataContext=diagnosis;_dashboard=dashboard;Loaded+=async(_,_)=>await diagnosis.LoadLatestAsync();}
 private async void OnRepairClick(object sender,Microsoft.UI.Xaml.RoutedEventArgs args)
 {
  if(sender is not Button{DataContext:RootCause cause}||string.IsNullOrWhiteSpace(cause.Recommendation.RepairId))return;
  var option=_dashboard.AvailableRepairs.FirstOrDefault(x=>x.Id.Equals(cause.Recommendation.RepairId,StringComparison.OrdinalIgnoreCase));
  var acknowledgement=new CheckBox{Content="I reviewed the plan and accept the stated repair risk",IsChecked=option?.SafetyLevel==SafetyLevel.Low};
  var content=new StackPanel{Spacing=8};content.Children.Add(new TextBlock{Text=$"Reason: {cause.LikelyCause}\nConfidence: {cause.Confidence}%\nRisk: {option?.SafetyLevel}\n\nWAID will assess and simulate the repair, then apply centralized approval, administrator, restore-point, backup, validation, and rollback safeguards.",TextWrapping=Microsoft.UI.Xaml.TextWrapping.Wrap});content.Children.Add(acknowledgement);
  var dialog=new ContentDialog{Title=$"Assess {cause.Recommendation.Title}?",Content=content,PrimaryButtonText="Approve lifecycle",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=XamlRoot};
  if(await dialog.ShowAsync()==ContentDialogResult.Primary)await _dashboard.RunRepairAsync(cause.Recommendation.RepairId,true,acknowledgement.IsChecked==true);
 }
}
