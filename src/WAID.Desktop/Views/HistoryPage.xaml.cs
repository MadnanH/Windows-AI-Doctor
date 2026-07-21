using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;

namespace WAID.Desktop.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
