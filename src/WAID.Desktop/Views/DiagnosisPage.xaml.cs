using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class DiagnosisPage : Page { public DiagnosisPage(DiagnosisViewModel viewModel){InitializeComponent();DataContext=viewModel;Loaded+=async (_,_)=>await viewModel.LoadLatestAsync();} }
