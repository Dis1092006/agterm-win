using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>
/// Ported 1:1 from agtermCore AppStoreRestoreSeedTests.swift (names preserved).
///
/// The `launchRestore` seeding gate: which rebuild paths arm a persisted `session.restore` override for
/// this launch by copying it into the transient pending slots the surface factories consume. Only an
/// app-bootstrap restore may; everything else defaults to arming nothing. Split out of AppStoreTests to
/// keep that file within the line budget.
/// </summary>
public class AppStoreRestoreSeedTests
{
    private static Snapshot MakeSnapshot(string? restore, string? splitRestore, bool isSplit)
    {
        var session = new SessionSnapshot
        {
            Id = Guid.NewGuid(), CustomName = null, Cwd = "/a", IsSplit = isSplit,
            RestoreCommand = restore, SplitRestoreCommand = splitRestore,
        };
        return new Snapshot
        {
            Workspaces = new[]
            {
                new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "work", Sessions = new[] { session } },
            },
        };
    }

    [Fact]
    public void BootstrapRestoreSeedsBothPanesForAShownSplit()
    {
        var store = AppStoreFixtures.MakeStore();
        store.Restore(MakeSnapshot(restore: "claude --resume abc", splitRestore: "tail -f /var/log/x",
                                   isSplit: true),
                      launchRestore: true);
        var session = store.Workspaces[0].Sessions[0];
        Assert.Equal("claude --resume abc", session.PendingRestoreCommand);
        Assert.Equal("tail -f /var/log/x", session.PendingSplitRestoreCommand);
        // the persisted fields are populated too, and stay populated — the override is sticky.
        Assert.Equal("claude --resume abc", session.RestoreCommand);
        Assert.Equal("tail -f /var/log/x", session.SplitRestoreCommand);
    }

    [Fact]
    public void BootstrapRestoreDropsAHiddenSplitsOverrideEntirely()
    {
        // a split hidden at quit is not rebuilt, so makeSplitSurface never runs and its pin describes a
        // pane that no longer exists. Nothing may be armed for it, and the PERSISTED pin is dropped too —
        // otherwise `tree` reports a value `session.restore --pane right` cannot clear (no split), and a
        // fresh ⌘D split shown at the next quit would inherit and run it.
        var store = AppStoreFixtures.MakeStore();
        store.Restore(MakeSnapshot(restore: "claude --resume abc", splitRestore: "tail -f /var/log/x",
                                   isSplit: false),
                      launchRestore: true);
        var session = store.Workspaces[0].Sessions[0];
        Assert.Equal("claude --resume abc", session.PendingRestoreCommand);
        Assert.Null(session.PendingSplitRestoreCommand);
        Assert.Null(session.SplitRestoreCommand);
        // the drop reaches persistence on the next save, so the orphan does not come back.
        Assert.Null(store.Snapshot().Workspaces[0].Sessions[0].SplitRestoreCommand);
    }

    [Fact]
    public void RebuildDropsAHiddenSplitsOverrideOnEveryPath()
    {
        // the drop is a property of the rebuild, not of bootstrap: Reopen Closed Item and a mid-process
        // window reload rebuild the same split-less session and must not resurrect the orphan either.
        var store = AppStoreFixtures.MakeStore();
        var snap = new SessionSnapshot
        {
            Id = Guid.NewGuid(), CustomName = null, Cwd = "/a", IsSplit = false,
            RestoreCommand = "claude --resume abc", SplitRestoreCommand = "tail -f",
        };
        Assert.Null(store.SessionFrom(snap).SplitRestoreCommand);
        Assert.Null(store.SessionFrom(snap, launchRestore: true).SplitRestoreCommand);
    }

    [Fact]
    public void BootstrapRestoreSeedsTheEmptyPinnedToNothingValue()
    {
        // "" is a real override (a plain shell, suppressing the capture and initialCommand), so it must be
        // armed like any other value rather than dropped as "no override".
        var store = AppStoreFixtures.MakeStore();
        store.Restore(MakeSnapshot(restore: "", splitRestore: "", isSplit: true), launchRestore: true);
        var session = store.Workspaces[0].Sessions[0];
        Assert.Equal("", session.PendingRestoreCommand);
        Assert.Equal("", session.PendingSplitRestoreCommand);
    }

    [Fact]
    public void NonBootstrapRestoreSeedsNeitherPane()
    {
        // reopening a closed window mid-process reloads its store through restore(from:) — arming there
        // would execute every sticky override with no app restart.
        var store = AppStoreFixtures.MakeStore();
        store.Restore(MakeSnapshot(restore: "claude --resume abc", splitRestore: "tail -f /var/log/x",
                                   isSplit: true));
        var session = store.Workspaces[0].Sessions[0];
        Assert.Null(session.PendingRestoreCommand);
        Assert.Null(session.PendingSplitRestoreCommand);
        // but the persisted values are still restored, so `tree` reads them back and the next launch fires.
        Assert.Equal("claude --resume abc", session.RestoreCommand);
        Assert.Equal("tail -f /var/log/x", session.SplitRestoreCommand);
    }

    [Fact]
    public void SessionRebuildDefaultsToSeedingNeitherPane()
    {
        // the default on session(from:) is the safe direction, so a caller added later arms nothing.
        var store = AppStoreFixtures.MakeStore();
        var snap = new SessionSnapshot
        {
            Id = Guid.NewGuid(), CustomName = null, Cwd = "/a", IsSplit = true,
            RestoreCommand = "claude --resume abc", SplitRestoreCommand = "tail -f",
        };
        var session = store.SessionFrom(snap);
        Assert.Null(session.PendingRestoreCommand);
        Assert.Null(session.PendingSplitRestoreCommand);
        Assert.Equal("claude --resume abc", session.RestoreCommand);
        Assert.Equal("tail -f", session.SplitRestoreCommand);
    }

    [Fact]
    public void BootstrapRestoreOfASessionWithoutOverridesArmsNothing()
    {
        var store = AppStoreFixtures.MakeStore();
        store.Restore(MakeSnapshot(restore: null, splitRestore: null, isSplit: true), launchRestore: true);
        var session = store.Workspaces[0].Sessions[0];
        Assert.Null(session.PendingRestoreCommand);
        Assert.Null(session.PendingSplitRestoreCommand);
        Assert.Null(session.RestoreCommand);
    }

    [Fact]
    public void FreshSessionHasNoOverrideState()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.Null(session.RestoreCommand);
        Assert.Null(session.SplitRestoreCommand);
        Assert.Null(session.PendingRestoreCommand);
        Assert.Null(session.PendingSplitRestoreCommand);
    }
}
