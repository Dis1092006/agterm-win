using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// A store persists into the directory it was given, so it must remove a session's rendered .text
/// PNG from that same state directory. Resolving the watermark path from the process-global default
/// instead deletes from whatever dir the default points at — the real user state dir under a test,
/// and another window's root in the multi-window app.
/// </summary>
public class AppStoreWatermarkStateDirTests : IDisposable
{
    private readonly string _stateDir =
        Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_stateDir)) Directory.Delete(_stateDir, recursive: true);
    }

    private AppStore NewStore() =>
        new(persistence: new PersistenceStore(_stateDir), stateDirectory: _stateDir);

    private string SeedWatermark(Guid sessionId)
    {
        var path = WatermarkStorage.RenderedTextPath(sessionId, _stateDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "png");
        return path;
    }

    [Fact]
    public void ClosingASessionRemovesItsWatermarkFromTheStoresOwnStateDirectory()
    {
        var store = NewStore();
        var workspace = store.AddWorkspace("w");
        var session = Assert.IsType<Session>(store.AddSession(workspace.Id, @"C:\work"));
        var path = SeedWatermark(session.Id);

        store.CloseSession(session.Id);

        Assert.False(File.Exists(path), "the watermark under the store's state dir should be removed");
    }

    [Fact]
    public void RemovingAWorkspaceRemovesSessionWatermarksFromTheStoresOwnStateDirectory()
    {
        var store = NewStore();
        var workspace = store.AddWorkspace("w");
        var session = Assert.IsType<Session>(store.AddSession(workspace.Id, @"C:\work"));
        store.AddWorkspace("keeper"); // RemoveWorkspace no-ops while only one workspace exists
        var path = SeedWatermark(session.Id);

        store.RemoveWorkspace(workspace.Id);

        Assert.False(File.Exists(path), "the watermark under the store's state dir should be removed");
    }
}
