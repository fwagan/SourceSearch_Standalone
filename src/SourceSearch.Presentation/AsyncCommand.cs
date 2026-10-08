using System.Windows.Input;
namespace SourceSearch.Presentation;
public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public Task ExecuteAsync() => CanExecute(null) ? execute() : Task.CompletedTask;
    public async void Execute(object? parameter)
    {
        try { await ExecuteAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { onError(error); }
    }
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
