using Microsoft.UI.Xaml.Controls;
using WAID.Desktop.ViewModels;
namespace WAID.Desktop.Views;
public sealed partial class AuditPage:Page{public AuditPage(AuditViewModel viewModel){InitializeComponent();DataContext=viewModel;}}
