using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>Where a page's web view reports its load state and the page it shows.</summary>
public interface IHtmlPageStore
{
    void SetLoadState(Guid id, HtmlLoadState state, string? error);

    void SetPage(Guid id, HtmlPageInfo? info);
}

/// <summary>Writes into a store the caller already holds.</summary>
public sealed class DirectHtmlPageStore(AppStore store) : IHtmlPageStore
{
    public void SetLoadState(Guid id, HtmlLoadState state, string? error) => store.SetHtmlLoadState(id, state, error);

    public void SetPage(Guid id, HtmlPageInfo? info) => store.SetHtmlPage(id, info);
}

/// <summary>
/// Writes into the live store through its coordinator, off the UI thread. Each mutation of the
/// coordinator runs as its own task, so the writes are chained here to land in the order the page
/// made them: a page that loads and then fails must not end up loaded.
/// </summary>
public sealed class LiveHtmlPageStore(ILiveStoreCoordinator coordinator, Action<string>? log = null) : IHtmlPageStore
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Completes once every write made so far has landed or failed.</summary>
    internal Task Flushed
    {
        get
        {
            lock (_gate) return _tail;
        }
    }

    public void SetLoadState(Guid id, HtmlLoadState state, string? error) =>
        Enqueue(store => store.SetHtmlLoadState(id, state, error));

    public void SetPage(Guid id, HtmlPageInfo? info) => Enqueue(store => store.SetHtmlPage(id, info));

    private void Enqueue(Action<AppStore> mutate)
    {
        lock (_gate)
        {
            _tail = _tail.ContinueWith(_ => Write(mutate), CancellationToken.None,
                                       TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task Write(Action<AppStore> mutate)
    {
        try
        {
            await coordinator.MutateAsync(mutate).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            log?.Invoke("html page report failed: " + e.Message);
        }
    }
}
