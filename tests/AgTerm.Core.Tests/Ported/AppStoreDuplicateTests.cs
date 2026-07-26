using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore AppStoreDuplicateTests.swift.
/// AppStore.DuplicateSession — the store half of the sidebar's "Duplicate Session" row action and the
/// session.duplicate control command. The contract under test: same workspace, inserted directly after
/// the source, seeded with the source's LIVE cwd, and carrying over NOTHING else.</summary>
public sealed class AppStoreDuplicateTests
{
    [Fact]
    public void DuplicateSessionInsertsAfterSourceInSameWorkspaceAndSelects()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a");
        Assert.NotNull(first);
        var last = store.AddSession(ws.Id, "/c");
        Assert.NotNull(last);

        var dupe = store.DuplicateSession(first!.Id);
        Assert.NotNull(dupe);

        // lands directly AFTER its source, not appended at the end.
        Assert.Equal(new[] { first.Id, dupe!.Id, last!.Id },
            store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal("/a", dupe.InitialCwd);
        Assert.Equal(dupe.Id, store.SelectedSessionId);
    }

    /// <summary>The seed is the FOCUSED pane's cwd, not the primary's: a split focused on its non-primary pane
    /// duplicates into the split pane's directory (FocusedCwd), not the primary's (EffectiveCwd). Pins
    /// the FocusedCwd-over-EffectiveCwd choice — swapping the seed to EffectiveCwd fails only here.</summary>
    [Fact]
    public void DuplicateSessionSeedsFromFocusedSplitPane()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var source = store.AddSession(ws.Id, "/primary");
        Assert.NotNull(source);
        source!.CurrentCwd = "/primary";
        source.IsSplit = true;
        source.HasSplit = true;
        source.SplitSurface = new SpySurface(); // a focused split always has a live split surface
        source.SplitCwd = "/split-pane";
        source.SplitFocused = true;

        var dupe = store.DuplicateSession(source.Id);
        Assert.NotNull(dupe);

        // the duplicate opens where FOCUS is (the split pane), not the primary pane's cwd.
        Assert.Equal("/split-pane", dupe!.InitialCwd);
    }

    [Fact]
    public void DuplicateSessionTracksLiveCwd()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var source = store.AddSession(ws.Id, "/start");
        Assert.NotNull(source);
        source!.CurrentCwd = "/moved"; // an OSC 7 report after a `cd`

        var dupe = store.DuplicateSession(source.Id);
        Assert.NotNull(dupe);

        // the duplicate opens where the shell IS, not where it started.
        Assert.Equal("/moved", dupe!.InitialCwd);
    }

    /// <summary>The directory-only contract: a duplicate is a plain new session, NOT a clone of the source's state.</summary>
    [Fact]
    public void DuplicateSessionCopiesOnlyTheDirectory()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var source = store.AddSession(ws.Id, "/a", command: "ssh host", name: "prod");
        Assert.NotNull(source);
        source!.Flagged = true;
        source.IsSplit = true;
        source.HasSplit = true;
        source.FontSize = 18;

        var dupe = store.DuplicateSession(source.Id);
        Assert.NotNull(dupe);

        Assert.Equal("/a", dupe!.InitialCwd);
        Assert.Null(dupe.CustomName); // auto basename, NOT "prod"
        Assert.Null(dupe.InitialCommand);
        Assert.False(dupe.Flagged);
        Assert.False(dupe.IsSplit);
        Assert.False(dupe.HasSplit);
        Assert.Null(dupe.FontSize);
        Assert.NotEqual(source.Id, dupe.Id);
    }

    /// <summary>A duplicate is a fresh Session from AddSession, so it inherits no restore-command override —
    /// neither the persisted pin (which names the SOURCE's command) nor an armed payload.</summary>
    [Fact]
    public void DuplicateSessionCopiesNoRestoreOverride()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var source = store.AddSession(ws.Id, "/a");
        Assert.NotNull(source);
        source!.RestoreCommand = "claude --resume abc";
        source.SplitRestoreCommand = "tail -f /var/log/x";
        source.PendingRestoreCommand = "claude --resume abc";
        source.PendingSplitRestoreCommand = "tail -f /var/log/x";

        var dupe = store.DuplicateSession(source.Id);
        Assert.NotNull(dupe);

        Assert.Null(dupe!.RestoreCommand);
        Assert.Null(dupe.SplitRestoreCommand);
        Assert.Null(dupe.PendingRestoreCommand);
        Assert.Null(dupe.PendingSplitRestoreCommand);
        Assert.Equal("claude --resume abc", source.RestoreCommand); // the source keeps its own
    }

    [Fact]
    public void DuplicateSessionOfUnknownSessionReturnsNil()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        store.AddSession(ws.Id, "/a");
        Assert.Null(store.DuplicateSession(Guid.NewGuid()));
        Assert.Single(store.Workspaces[0].Sessions);
    }
}
