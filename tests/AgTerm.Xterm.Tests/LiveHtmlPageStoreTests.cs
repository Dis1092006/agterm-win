using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// The live app's page reports go to the store through the store coordinator, which runs each
// mutation on its own thread-pool task; the page store keeps them in the order the page made them.
public sealed class LiveHtmlPageStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-html-pages-").FullName;
    private readonly AppStore _store;
    private readonly HtmlOverlay _page = new(new HtmlSource.File(@"C:\work\r.html", null));

    public LiveHtmlPageStoreTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir), stateDirectory: _dir);
        var session = _store.AddSession(_store.AddWorkspace("work").Id, _dir)!;
        Assert.Null(_store.OpenHtmlOverlay(session.Id, _page, sizePercent: null));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ReportsReachTheStoreInTheOrderThePageMadeThem()
    {
        var coordinator = new FakeCoordinator(_store) { Delay = call => call == 0 ? 200 : 0 };
        var pages = new LiveHtmlPageStore(coordinator);
        var info = new HtmlPageInfo(@"C:\work\r.html", "R", false, false);

        pages.SetLoadState(_page.Id, HtmlLoadState.Loading, null);
        pages.SetLoadState(_page.Id, HtmlLoadState.Loaded, null);
        pages.SetPage(_page.Id, info);
        await pages.Flushed;

        Assert.Equal(HtmlLoadState.Loaded, _page.LoadState);
        Assert.Equal(info, _page.Current);
        Assert.Equal(3, coordinator.Calls);
    }

    [Fact]
    public async Task AFailedReportDoesNotStopTheNext()
    {
        var coordinator = new FakeCoordinator(_store) { Throw = call => call == 0 };
        var pages = new LiveHtmlPageStore(coordinator);

        pages.SetLoadState(_page.Id, HtmlLoadState.Loaded, null);
        pages.SetLoadState(_page.Id, HtmlLoadState.Failed, "boom");
        await pages.Flushed;

        Assert.Equal(HtmlLoadState.Failed, _page.LoadState);
        Assert.Equal("boom", _page.LoadError);
    }

    private sealed class FakeCoordinator(AppStore store) : ILiveStoreCoordinator
    {
        private int _calls;

        public Func<int, int> Delay { get; init; } = _ => 0;

        public Func<int, bool> Throw { get; init; } = _ => false;

        public int Calls => Volatile.Read(ref _calls);

        public Task MutateAsync(Action<AppStore> mutate, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls) - 1;
            if (Throw(call)) throw new ObjectDisposedException(nameof(LiveStoreCoordinator));
            var delay = Delay(call);
            return Task.Run(async () =>
            {
                await Task.Delay(delay, cancellationToken);
                lock (store) mutate(store);
            }, cancellationToken);
        }

        public Task<T> ReadAsync<T>(Func<AppStore, T> read, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ReadAsync<TSnapshot, TResult>(Func<TSnapshot> captureOutsideGate,
                                                           Func<AppStore, TSnapshot, TResult> readUnderGate,
                                                           CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public event Action? SidebarInvalidated { add { } remove { } }

        public event Action<Guid?>? SelectionChanged { add { } remove { } }

        public event Action<LiveSelectionSnapshot>? SelectionSnapshotChanged { add { } remove { } }

        public void Dispose() { }
    }
}
