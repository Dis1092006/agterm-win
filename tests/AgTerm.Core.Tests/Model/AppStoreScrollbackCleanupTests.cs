using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// Saved pane output lives under the store's own state directory until the pane is realized again.
/// When a session is destroyed for good, its files must go too, or the state dir keeps them forever.
/// </summary>
public class AppStoreScrollbackCleanupTests : IDisposable
{
    private readonly string _stateDir =
        Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_stateDir)) Directory.Delete(_stateDir, recursive: true);
    }

    private AppStore NewStore() =>
        new(persistence: new PersistenceStore(_stateDir), stateDirectory: _stateDir);

    private string[] Seed(Guid sessionId)
    {
        ScrollbackStorage.Write(sessionId, StatusPane.Left, "left", _stateDir);
        ScrollbackStorage.Write(sessionId, StatusPane.Right, "right", _stateDir);
        return
        [
            ScrollbackStorage.FilePath(sessionId, StatusPane.Left, _stateDir),
            ScrollbackStorage.FilePath(sessionId, StatusPane.Right, _stateDir),
        ];
    }

    [Fact]
    public void Closing_a_session_removes_its_saved_output()
    {
        var store = NewStore();
        var workspace = store.AddWorkspace("w");
        var session = store.AddSession(workspace.Id, @"C:\work")!;
        var paths = Seed(session.Id);

        store.CloseSession(session.Id);

        Assert.All(paths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void Removing_a_workspace_removes_saved_output_of_its_sessions()
    {
        var store = NewStore();
        var workspace = store.AddWorkspace("w");
        var session = store.AddSession(workspace.Id, @"C:\work")!;
        store.AddWorkspace("keeper");
        var paths = Seed(session.Id);

        store.RemoveWorkspace(workspace.Id);

        Assert.All(paths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void A_pending_close_keeps_saved_output_until_it_is_finalized()
    {
        var store = NewStore();
        var workspace = store.AddWorkspace("w");
        var session = store.AddSession(workspace.Id, @"C:\work")!;
        var paths = Seed(session.Id);

        Assert.True(store.SoftCloseSession(session.Id, 60));
        Assert.All(paths, path => Assert.True(File.Exists(path)));

        store.FinalizePendingClose(store.PendingCloseSummary!.Id);
        Assert.All(paths, path => Assert.False(File.Exists(path)));
    }
}
