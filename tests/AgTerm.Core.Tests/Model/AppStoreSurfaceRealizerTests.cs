using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// The SurfaceRealizer seam — the port's stand-in for "SwiftUI shows the selected pane's view and
/// the surface realizes": showing a session (selecting it, or creating it selected) realizes its
/// main-pane surface through the host-injected callback. Background creations stay unrealized
/// (upstream parity: session.new --no-select errors with "session not realized; use select").
/// </summary>
public class AppStoreSurfaceRealizerTests : IDisposable
{
    private sealed class StubSurface : ITerminalSurface
    {
        public string? PaneToken => null;
        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-realizer-").FullName;
    private readonly AppStore _store;
    private readonly List<Guid> _realized = new();

    public AppStoreSurfaceRealizerTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir));
        _store.SurfaceRealizer = session =>
        {
            _realized.Add(session.Id);
            return new StubSurface();
        };
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Selecting_an_unrealized_session_realizes_its_main_pane()
    {
        _store.SurfaceRealizer = null;
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\", select: false)!;
        Assert.Null(session.Surface);

        _store.SurfaceRealizer = s => { _realized.Add(s.Id); return new StubSurface(); };
        _store.SelectSession(session.Id);
        Assert.Equal(new[] { session.Id }, _realized);
        Assert.NotNull(session.Surface);
    }

    [Fact]
    public void Selecting_an_already_realized_session_does_not_rerealize()
    {
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\")!;
        _realized.Clear();
        _store.SelectSession(session.Id);
        Assert.Empty(_realized);
    }

    [Fact]
    public void Creating_a_selected_session_realizes_it_immediately()
    {
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\")!;
        Assert.Contains(session.Id, _realized);
        Assert.NotNull(session.Surface);
    }

    [Fact]
    public void A_background_add_stays_unrealized()
    {
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\", select: false)!;
        Assert.DoesNotContain(session.Id, _realized);
        Assert.Null(session.Surface);
    }

    [Fact]
    public void A_realizer_returning_null_leaves_the_session_unrealized()
    {
        _store.SurfaceRealizer = _ => null;
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\")!;
        Assert.Null(session.Surface);
        _store.SelectSession(session.Id); // and selecting it again still must not throw
        Assert.Null(session.Surface);
    }

    [Fact]
    public void Selecting_null_or_unknown_does_not_touch_the_realizer()
    {
        var workspace = _store.AddWorkspace("w");
        _ = _store.AddSession(workspace.Id, "C:\\", select: false);
        _realized.Clear();
        _store.SelectSession(null);
        _store.SelectSession(Guid.NewGuid());
        Assert.Empty(_realized);
    }
}
