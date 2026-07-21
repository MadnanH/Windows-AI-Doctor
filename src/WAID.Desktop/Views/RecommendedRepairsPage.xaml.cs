using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
using WAID.Diagnosis;

namespace WAID.Desktop.Views;

public sealed partial class RecommendedRepairsPage : Page
{
    private readonly DashboardViewModel _dashboard;
    public RecommendedRepairsPage(DiagnosisViewModel diagnosis, DashboardViewModel dashboard)
    {
        InitializeComponent();
        DataContext = diagnosis;
        _dashboard = dashboard;
        Loaded += async (_, _) => await diagnosis.LoadLatestAsync();
    }

    private async void OnRepairClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RootCause cause } || string.IsNullOrWhiteSpace(cause.Recommendation.RepairId)) return;
        var dialog = new ContentDialog
        {
            Title = $"Run {cause.Recommendation.Title}?",
            Content = $"Reason: {cause.LikelyCause}\nConfidence: {cause.Confidence}%\n\nWAID will verify administrator access and required safeguards before making changes.",
            PrimaryButtonText = "Run repair",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await _dashboard.RunRepairAsync(cause.Recommendation.RepairId, true);
    }
}
