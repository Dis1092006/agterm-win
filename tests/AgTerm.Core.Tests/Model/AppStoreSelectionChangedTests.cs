using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// SelectionChanged — the C# event replacing upstream's Observation of selectedSessionID for the
/// window chrome (which pane is visible). Fires on every show path: SelectSession (including
/// deselect-to-null) and a selected AddSession; a background add stays silent.
/// </summary>
public class AppStoreSelectionChangedTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-selchange-").FullName;
    private readonly AppStore _store;
    private int _fired;

    public AppStoreSelectionChangedTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir));
        _store.SelectionChanged += () => _fired++;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Fires_on_select_and_on_deselect()
    {
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\", select: false)!;
        _fired = 0;
        _store.SelectSession(session.Id);
        Assert.Equal(1, _fired);
        _store.SelectSession(null);
        Assert.Equal(2, _fired);
    }

    [Fact]
    public void Fires_when_a_session_is_created_selected_but_not_in_background()
    {
        var workspace = _store.AddWorkspace("w");
        _fired = 0;
        _store.AddSession(workspace.Id, "C:\\");
        Assert.Equal(1, _fired);
        _store.AddSession(workspace.Id, "C:\\", select: false);
        Assert.Equal(1, _fired);
    }

    [Fact]
    public void Ignored_unknown_id_does_not_fire()
    {
        var workspace = _store.AddWorkspace("w");
        _ = _store.AddSession(workspace.Id, "C:\\", select: false);
        _fired = 0;
        _store.SelectSession(Guid.NewGuid());
        Assert.Equal(0, _fired);
    }
}
