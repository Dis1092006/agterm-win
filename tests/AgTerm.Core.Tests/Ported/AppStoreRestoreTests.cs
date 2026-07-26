using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>
/// Ported 1:1 from agtermCore AppStoreRestoreTests.swift (names preserved).
///
/// <c>AppStore.SetRestoreCommand</c> — the store half of the control channel's `session.restore`. The contract
/// under test: per-pane tri-state writes to the PERSISTED field only, an eager save so a hook's write
/// survives a SIGKILL, and no re-save when nothing changed. Split out of AppStoreTests to keep that file
/// within the line budget.
/// </summary>
public sealed class AppStoreRestoreTests : IDisposable
{
    private readonly string _directory;

    public AppStoreRestoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "agterm-restore-" + Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort, like Swift's try? */ }
    }

    private string FilePath => Path.Combine(_directory, "workspaces.json");

    private AppStore MakePersistedStore() =>
        new(persistence: new PersistenceStore(_directory));

    /// <summary>
    /// A store whose every save fails, the `mutationSurvivesSaveFailure` idiom: the persistence directory
    /// is under a path that can never be one, so the directory creation throws. The failure rides the
    /// existing <c>PersistenceStore(directory:)</c> injection point — no production seam exists only for
    /// this test. (Windows adaptation of Swift's `/dev/null/agterm-cannot-write`: a path under an existing
    /// plain FILE, which Directory.CreateDirectory can never turn into a directory.)
    /// </summary>
    private AppStore MakeUnwritableStore()
    {
        var blocker = Path.Combine(_directory, "unwritable-blocker");
        File.WriteAllBytes(blocker, Array.Empty<byte>());
        return new AppStore(persistence: new PersistenceStore(Path.Combine(blocker, "agterm-cannot-write")));
    }

    [Fact]
    public void SetRestoreCommandSetsEachPaneIndependently()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;

        store.SetRestoreCommand("claude --resume abc", StatusPane.Left, session.Id);
        Assert.Equal("claude --resume abc", session.RestoreCommand);
        Assert.Null(session.SplitRestoreCommand);

        store.SetRestoreCommand("tail -f /var/log/x", StatusPane.Right, session.Id);
        Assert.Equal("claude --resume abc", session.RestoreCommand);
        Assert.Equal("tail -f /var/log/x", session.SplitRestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandClearsWithNil()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.RestoreCommand = "claude --resume abc";
        session.SplitRestoreCommand = "tail -f /var/log/x";

        store.SetRestoreCommand(null, StatusPane.Left, session.Id);
        Assert.Null(session.RestoreCommand);
        Assert.Equal("tail -f /var/log/x", session.SplitRestoreCommand); // clearing one pane leaves the other

        store.SetRestoreCommand(null, StatusPane.Right, session.Id);
        Assert.Null(session.SplitRestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandStoresTheEmptyPinnedToNothingValue()
    {
        // "" is a real override (a plain shell, suppressing the capture and initialCommand) and must not
        // collapse to nil, which would mean "no override" and let the auto-capture restore instead.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;

        store.SetRestoreCommand("", StatusPane.Left, session.Id);
        store.SetRestoreCommand("", StatusPane.Right, session.Id);
        Assert.Equal("", session.RestoreCommand);
        Assert.Equal("", session.SplitRestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandNeverPopulatesThePendingSlots()
    {
        // the whole safety property: a write during this run must not execute during this run. Only an
        // app-bootstrap restore arms the pending slots the surface factories consume.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;

        store.SetRestoreCommand("claude --resume abc", StatusPane.Left, session.Id);
        store.SetRestoreCommand("tail -f /var/log/x", StatusPane.Right, session.Id);

        Assert.Null(session.PendingRestoreCommand);
        Assert.Null(session.PendingSplitRestoreCommand);
        // and the factory-facing read stays empty, so nothing can fire this launch
        Assert.Null(session.TakePendingRestoreOverride(StatusPane.Left));
        Assert.Null(session.TakePendingRestoreOverride(StatusPane.Right));
    }

    [Fact]
    public void SetRestoreCommandDoesNotDisarmAnAlreadyArmedPayload()
    {
        // a rewrite mid-launch (a hook firing on every session start) must not cancel the override the
        // bootstrap already armed, nor swap in the new value — the pending payload is frozen at bootstrap.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.RestoreCommand = "claude --resume old";
        session.PendingRestoreCommand = "claude --resume old";

        store.SetRestoreCommand("claude --resume new", StatusPane.Left, session.Id);

        Assert.Equal("claude --resume new", session.RestoreCommand);
        Assert.Equal("claude --resume old", session.PendingRestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandIgnoresAnUnknownSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;

        store.SetRestoreCommand("claude --resume abc", StatusPane.Left, Guid.NewGuid());
        Assert.Null(session.RestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandPersistsImmediately()
    {
        // the eager save is a stated requirement: a hook's write has to survive a SIGKILL, which never
        // runs the quit-time flush. The reported outcome is the write, not the memory mutation.
        var store = MakePersistedStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);

        Assert.True(store.SetRestoreCommand("claude --resume abc", StatusPane.Left, session!.Id));

        var reloaded = new PersistenceStore(_directory).Load();
        Assert.Equal("claude --resume abc",
            reloaded.Workspaces[0].Sessions.FirstOrDefault(s => s.Id == session.Id)?.RestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandRollsBackAndReportsAFailedWrite()
    {
        // clearing is the dangerous case: acking a clear whose write never landed would leave the OLD
        // shell line re-typed on every launch while the user believes it is gone. So the failure is
        // reported AND the in-memory value goes back to what is still on disk.
        var store = MakeUnwritableStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        session!.RestoreCommand = "claude --resume abc";

        Assert.False(store.SetRestoreCommand(null, StatusPane.Left, session.Id));
        Assert.Equal("claude --resume abc", session.RestoreCommand);

        // a replacement that fails leaves the old command armed too, not the half-applied new one
        Assert.False(store.SetRestoreCommand("claude --resume def", StatusPane.Left, session.Id));
        Assert.Equal("claude --resume abc", session.RestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandRetriesTheSameRequestAfterAFailedWrite()
    {
        // the rollback is what keeps the retry possible: without it memory would already hold the
        // requested value and the unchanged-value guard would swallow the repeat as a no-op success.
        // A blocking FILE rather than the /dev/null idiom above, because this case needs the disk to
        // RECOVER between the two calls — deleting the blocker makes the same path writable.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllBytes(blocker, Array.Empty<byte>());
        var stateDir = Path.Combine(blocker, "state");
        var store = new AppStore(persistence: new PersistenceStore(stateDir));
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        session!.RestoreCommand = "claude --resume abc";
        Assert.False(store.SetRestoreCommand(null, StatusPane.Left, session.Id));

        File.Delete(blocker); // the disk recovers

        Assert.True(store.SetRestoreCommand(null, StatusPane.Left, session.Id));
        Assert.Null(session.RestoreCommand);
        var reloaded = new PersistenceStore(stateDir).Load();
        var persisted = reloaded.Workspaces.FirstOrDefault()?.Sessions.FirstOrDefault(s => s.Id == session.Id);
        Assert.NotNull(persisted);
        Assert.Null(persisted!.RestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandSkipsTheSaveWhenUnchanged()
    {
        // idempotent like setFlag: re-pinning the same value writes nothing. Proven by deleting the
        // persisted file and asserting the repeat call does not recreate it.
        var store = MakePersistedStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        store.SetRestoreCommand("claude --resume abc", StatusPane.Left, session!.Id);
        Assert.True(File.Exists(FilePath));
        File.Delete(FilePath);

        store.SetRestoreCommand("claude --resume abc", StatusPane.Left, session.Id);
        Assert.False(File.Exists(FilePath)); // unchanged → no save

        store.SetRestoreCommand("claude --resume def", StatusPane.Left, session.Id);
        Assert.True(File.Exists(FilePath)); // a real change saves again
    }

    [Fact]
    public void SetRestoreCommandSkipsTheSaveWhenClearingAnAlreadyClearPane()
    {
        var store = MakePersistedStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        store.SetRestoreCommand("claude --resume abc", StatusPane.Left, session!.Id);
        File.Delete(FilePath);

        store.SetRestoreCommand(null, StatusPane.Right, session.Id); // never pinned
        Assert.False(File.Exists(FilePath));
        Assert.Null(session.SplitRestoreCommand);
    }

    [Fact]
    public void SetRestoreCommandIgnoresTheScratchPane()
    {
        // the scratch terminal is never restored; the command layer rejects it, and the store is a no-op.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;

        store.SetRestoreCommand("claude --resume abc", StatusPane.Scratch, session.Id);
        Assert.Null(session.RestoreCommand);
        Assert.Null(session.SplitRestoreCommand);
    }

    [Fact]
    public void SoftClosedSessionUndoneWithinGraceHasNothingPending()
    {
        // undo reinserts the SAME object, so a payload armed at bootstrap and never consumed would
        // otherwise survive the round trip and fire when the restored session's surface is built.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        session!.RestoreCommand = "claude --resume abc";
        session.SplitRestoreCommand = "tail -f /var/log/x";
        session.PendingRestoreCommand = "claude --resume abc";
        session.PendingSplitRestoreCommand = "tail -f /var/log/x";

        Assert.True(store.SoftCloseSession(session.Id, grace: 60));
        var closeId = store.PendingCloseSummary?.Id;
        Assert.NotNull(closeId);
        Assert.True(store.UndoPendingClose(closeId!.Value));

        var restored = store.SessionWithId(session.Id);
        Assert.NotNull(restored);
        Assert.Same(session, restored); // the same live object came back
        Assert.Null(restored!.PendingRestoreCommand);
        Assert.Null(restored.PendingSplitRestoreCommand);
        // the persisted pin is untouched, so the next launch still fires it
        Assert.Equal("claude --resume abc", restored.RestoreCommand);
        Assert.Equal("tail -f /var/log/x", restored.SplitRestoreCommand);
    }

    [Fact]
    public void SoftClosedSessionGroupUndoneWithinGraceHasNothingPending()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(ws.Id, "/b");
        Assert.NotNull(second);
        foreach (var session in new[] { first!, second! })
        {
            session.RestoreCommand = "claude --resume abc";
            session.PendingRestoreCommand = "claude --resume abc";
        }

        Assert.True(store.SoftCloseSessions(new[] { first!.Id, second!.Id }, grace: 60));
        var closeId = store.PendingCloseSummary?.Id;
        Assert.NotNull(closeId);
        Assert.True(store.UndoPendingClose(closeId!.Value));

        Assert.Null(first.PendingRestoreCommand);
        Assert.Null(second.PendingRestoreCommand);
        Assert.Equal("claude --resume abc", first.RestoreCommand);
    }

    [Fact]
    public void SoftRemovedWorkspaceUndoneWithinGraceHasNothingPending()
    {
        var store = AppStoreFixtures.MakeStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var session = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(session);
        session!.RestoreCommand = "claude --resume abc";
        session.PendingRestoreCommand = "claude --resume abc";

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        var closeId = store.PendingCloseSummary?.Id;
        Assert.NotNull(closeId);
        Assert.True(store.UndoPendingClose(closeId!.Value));

        Assert.Null(session.PendingRestoreCommand);
        Assert.Equal("claude --resume abc", session.RestoreCommand);
    }
}
