using Microsoft.UI.Xaml.Controls;using WAID.Desktop.ViewModels;namespace WAID.Desktop.Views;
public sealed partial class BootHealthPage:Page{public BootHealthPage(BootHealthViewModel vm){InitializeComponent();DataContext=vm;Loaded+=async(_,_)=>await vm.InitializeAsync();}}
