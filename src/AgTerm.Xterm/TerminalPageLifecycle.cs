namespace AgTerm.Xterm;

/// <summary>Bounds page readiness and replaces a failed attempt only when Reload is requested.</summary>
public sealed class TerminalPageLifecycle<T>
{
    private readonly Func<Task<T>> _boot;
    private readonly TimeSpan _readyBudget;
    private readonly Action<Exception> _failed;
    private Task<T> _current;
    private int _attempt;

    public TerminalPageLifecycle(Func<Task<T>> boot, TimeSpan readyBudget, Action<Exception> failed)
    {
        _boot = boot ?? throw new ArgumentNullException(nameof(boot));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
        if (readyBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(readyBudget));
        _readyBudget = readyBudget;
        _current = RunAsync(++_attempt);
    }

    public Task<T> Current => _current;

    public Task<T> ReloadAsync()
    {
        _current = RunAsync(++_attempt);
        return _current;
    }

    private async Task<T> RunAsync(int attempt)
    {
        try
        {
            return await _boot().WaitAsync(_readyBudget);
        }
        catch (Exception ex)
        {
            if (attempt == _attempt)
                _failed(ex);
            throw;
        }
    }
}
