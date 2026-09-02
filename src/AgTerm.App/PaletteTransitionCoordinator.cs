namespace AgTerm.App;

public sealed class PaletteTransitionCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(Func<Task> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        await _gate.WaitAsync();
        try
        {
            await transition();
        }
        finally
        {
            _gate.Release();
        }
    }
}