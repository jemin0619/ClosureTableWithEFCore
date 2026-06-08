using System.Windows.Input;

namespace Project.Presentation.Commands;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;

        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand unhandled exception: {ex}");
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CanExecuteChanged;
}

public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<T?, Task> execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;

        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            if (parameter is null)
            {
                await _execute(default);
                return;
            }

            if (parameter is T typedParameter)
            {
                await _execute(typedParameter);
                return;
            }

            throw new InvalidOperationException(
                $"Invalid command parameter type. Expected {typeof(T).Name}, actual {parameter.GetType().Name}.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand<{typeof(T).Name}> unhandled exception: {ex}");
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CanExecuteChanged;
}
