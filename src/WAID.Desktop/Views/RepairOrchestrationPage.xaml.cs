using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class RepairOrchestrationPage:Page{public RepairOrchestrationPage(RepairOrchestrationViewModel viewModel){InitializeComponent();DataContext=viewModel;Loaded+=async(_,_)=>await viewModel.LoadAsync();}}
