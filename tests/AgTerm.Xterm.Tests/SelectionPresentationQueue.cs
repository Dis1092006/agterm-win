namespace AgTerm.Xterm.Tests;

internal sealed class SelectionPresentationQueue(
    Func<LiveSelectionSnapshot, LiveSelectionSnapshot, long, Task> present)
{
    private readonly object _gate = new();
    private Task _presentation = Task.CompletedTask;
    private LiveSelectionSnapshot _previous = new(null, null);
    private long _generation;
    private Func<LiveSelectionSnapshot, bool>? _expected;
    private TaskCompletionSource<Task>? _observed;

    public void Record(LiveSelectionSnapshot current)
    {
        lock (_gate)
        {
            _presentation = PresentAfterAsync(_presentation, _previous, current, ++_generation);
            _previous = current;
            if (_expected is { } expected && _observed is { } observed && expected(current))
            {
                _expected = null;
                _observed = null;
                observed.SetResult(_presentation);
            }
        }
    }

    public Task WaitForAsync(Func<LiveSelectionSnapshot, bool> expected)
    {
        lock (_gate)
        {
            if (_observed is not null)
                throw new InvalidOperationException("A presentation wait is already pending.");
            _expected = expected;
            _observed = new TaskCompletionSource<Task>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _observed.Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    public Task FlushAsync()
    {
        lock (_gate) return _presentation.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private async Task PresentAfterAsync(
        Task pending,
        LiveSelectionSnapshot previous,
        LiveSelectionSnapshot current,
        long generation)
    {
        await pending;
        await present(previous, current, generation);
    }
}
