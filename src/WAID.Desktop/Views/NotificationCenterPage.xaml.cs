using Microsoft.UI.Xaml.Controls;
using WAID.Application.Services;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class NotificationCenterPage:Page
{
 private readonly NotificationCenterViewModel _viewModel;public NotificationCenterPage(NotificationCenterViewModel viewModel){InitializeComponent();_viewModel=viewModel;DataContext=viewModel;Loaded+=async(_,_)=>await viewModel.LoadAsync();}
 private async void OnAcknowledge(object sender,Microsoft.UI.Xaml.RoutedEventArgs e){if(sender is Button{DataContext:AlertNotification alert})await _viewModel.AcknowledgeAsync(alert);}
 private async void OnSnooze(object sender,Microsoft.UI.Xaml.RoutedEventArgs e){if(sender is Button{DataContext:AlertNotification alert})await _viewModel.SnoozeAsync(alert);}
 private void OnOpenAction(object sender,Microsoft.UI.Xaml.RoutedEventArgs e){if(sender is Button{DataContext:AlertNotification alert})_viewModel.OpenAction(alert);}
}
