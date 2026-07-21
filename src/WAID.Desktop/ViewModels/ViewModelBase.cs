using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
namespace WAID.Desktop.ViewModels;
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null) { if(EqualityComparer<T>.Default.Equals(field,value)) return false; field=value; PropertyChanged?.Invoke(this,new(name)); return true; }
    protected void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class AsyncCommand(Func<Task> execute,Func<bool>? canExecute=null) : ICommand
{
    private bool _running; public bool CanExecute(object? parameter)=>!_running&&(canExecute?.Invoke()??true); public event EventHandler? CanExecuteChanged;
    public async void Execute(object? parameter) { if(!CanExecute(parameter)) return; _running=true; CanExecuteChanged?.Invoke(this,EventArgs.Empty); try{await execute();}finally{_running=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);} }
    public void NotifyCanExecuteChanged()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
public sealed class RelayCommand(Action execute,Func<bool>? canExecute=null) : ICommand
{
    public bool CanExecute(object? parameter)=>canExecute?.Invoke()??true; public event EventHandler? CanExecuteChanged;
    public void Execute(object? parameter)=>execute();
    public void NotifyCanExecuteChanged()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
