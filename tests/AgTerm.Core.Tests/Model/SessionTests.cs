using AgTerm.Core.Keymap;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

file sealed class FakeSurface : ITerminalSurface
{
    public string? PaneToken { get; init; }

    public void Teardown()
    {
    }

    public void PromoteToPrimaryPane()
    {
    }
}

public class SessionTests
{
    private static Session NewSession(string cwd = "/home/u/proj") => new(initialCwd: cwd);

    // -- displayName ------------------------------------------------------

    [Fact]
    public void DisplayNamePrefersCustomName()
    {
        var s = NewSession();
        s.CustomName = "api";
        s.OscTitle = "user@host:~";
        Assert.Equal("api", s.DisplayName);
    }

    [Fact]
    public void WhitespaceOnlyCustomNameFallsThrough()
    {
        var s = NewSession();
        s.CustomName = "   ";
        s.OscTitle = "user@host:~";
        Assert.Equal("user@host:~", s.DisplayName);
    }

    [Fact]
    public void DisplayNameUsesTrimmedOscTitleWhenNoCustomName()
    {
        var s = NewSession();
        s.OscTitle = "  build — running  ";
        Assert.Equal("build — running", s.DisplayName);
    }

    [Fact]
    public void DisplayNameFallsBackToCwdBasename()
    {
        Assert.Equal("proj", NewSession("/home/u/proj").DisplayName);
    }

    [Theory]
    [InlineData("/", "/")] // root pin
    [InlineData("/a/b/", "b")] // trailing slash ignored
    [InlineData("", "~")] // empty path → home shorthand
    [InlineData(@"C:\git\proj", "proj")] // Windows separators equally honored
    public void DisplayNameBasenamePins(string cwd, string expected)
    {
        Assert.Equal(expected, NewSession(cwd).DisplayName);
    }

    [Fact]
    public void DisplayNameTracksTheFocusedSplitPane()
    {
        var s = NewSession("/home/u/proj");
        s.SplitSurface = new FakeSurface();
        s.SplitFocused = true;
        s.SplitCwd = "/home/u/other";
        Assert.Equal("other", s.DisplayName);
    }

    // -- focusedCwd / effectiveCwd ---------------------------------------

    [Fact]
    public void FocusedCwdUsesSplitCwdOnlyWhileTheSplitSurfaceExists()
    {
        var s = NewSession("/init");
        s.CurrentCwd = "/live";
        s.SplitFocused = true;
        s.SplitCwd = "/split";

        // splitFocused alone is not enough — the split pane must exist.
        Assert.Equal("/live", s.FocusedCwd);

        s.SplitSurface = new FakeSurface();
        Assert.Equal("/split", s.FocusedCwd);
    }

    [Fact]
    public void EffectiveCwdIsAlwaysThePrimaryPanes()
    {
        var s = NewSession("/init");
        s.CurrentCwd = "/live";
        s.SplitSurface = new FakeSurface();
        s.SplitFocused = true;
        s.SplitCwd = "/split";

        Assert.Equal("/live", s.EffectiveCwd);
        s.CurrentCwd = null;
        Assert.Equal("/init", s.EffectiveCwd);
    }

    // -- searchDisplayText -------------------------------------------------

    [Theory]
    [InlineData(null, null, "")]
    [InlineData(0, null, "no matches")]
    [InlineData(5, null, "5 matches")]
    [InlineData(5, 2, "2 of 5")]
    [InlineData(2, 3, "2 of 2")] // stale selected index clamps to total
    public void SearchDisplayTextFormats(int? total, int? selected, string expected)
    {
        var s = NewSession();
        s.SearchTotal = total;
        s.SearchSelected = selected;
        Assert.Equal(expected, s.SearchDisplayText);
    }

    [Fact]
    public void ClearSearchResetsEverything()
    {
        var s = NewSession();
        s.SearchActive = true;
        s.SearchNeedle = "x";
        s.SearchTotal = 3;
        s.SearchSelected = 1;
        s.SearchSurface = new FakeSurface();

        s.ClearSearch();

        Assert.False(s.SearchActive);
        Assert.Equal("", s.SearchNeedle);
        Assert.Null(s.SearchTotal);
        Assert.Null(s.SearchSelected);
        Assert.Null(s.SearchSurface);
    }

    // -- paneRole ----------------------------------------------------------

    [Fact]
    public void PaneRoleResolvesTokensByLiveSlotOccupancy()
    {
        var s = NewSession();
        s.Surface = new FakeSurface { PaneToken = "main-token" };
        s.SplitSurface = new FakeSurface { PaneToken = "split-token" };
        s.ScratchSurface = new FakeSurface { PaneToken = "scratch-token" };

        Assert.Equal(StatusPane.Left, s.PaneRole("main-token"));
        Assert.Equal(StatusPane.Right, s.PaneRole("split-token"));
        Assert.Equal(StatusPane.Scratch, s.PaneRole("scratch-token"));
        Assert.Null(s.PaneRole(""));
        Assert.Null(s.PaneRole("unknown"));
    }

    // -- pending restore overrides ------------------------------------------

    [Fact]
    public void TakePendingRestoreOverrideConsumesThePayload()
    {
        var s = NewSession();
        s.PendingRestoreCommand = "make watch";
        s.PendingSplitRestoreCommand = "top";

        Assert.Equal("make watch", s.TakePendingRestoreOverride(StatusPane.Left));
        Assert.Null(s.TakePendingRestoreOverride(StatusPane.Left)); // consumed
        Assert.Equal("top", s.TakePendingRestoreOverride(StatusPane.Right));
        Assert.Null(s.TakePendingRestoreOverride(StatusPane.Scratch)); // scratch is never restored
    }

    [Fact]
    public void ClearPendingRestoreOverridesLeavesPersistedFieldsAlone()
    {
        var s = NewSession();
        s.RestoreCommand = "persisted";
        s.PendingRestoreCommand = "pending";
        s.PendingSplitRestoreCommand = "pending-split";

        s.ClearPendingRestoreOverrides();

        Assert.Null(s.PendingRestoreCommand);
        Assert.Null(s.PendingSplitRestoreCommand);
        Assert.Equal("persisted", s.RestoreCommand);
    }

    // -- overlay flags -------------------------------------------------------

    [Fact]
    public void FullVersusFloatingOverlayIsDecidedBySizePercent()
    {
        var s = NewSession();
        s.OverlayActive = true;
        Assert.True(s.FullOverlayActive);
        Assert.False(s.FloatingOverlayActive);

        s.OverlaySizePercent = 60;
        Assert.False(s.FullOverlayActive);
        Assert.True(s.FloatingOverlayActive);
    }

    // -- snapshot capture ------------------------------------------------------

    [Fact]
    public void ToSnapshotCapturesLiveCwdAndSeeds()
    {
        var s = NewSession("/init");
        s.CurrentCwd = "/live";
        s.InitialSplitCwd = "/split-seed";
        s.CommandWait = false;

        var snap = s.ToSnapshot();

        Assert.Equal(s.Id, snap.Id);
        Assert.Equal("/live", snap.Cwd);
        Assert.Equal("/split-seed", snap.SplitCwd); // live splitCwd wins when present, seed otherwise
        Assert.Null(snap.CommandWait); // false is omitted, matching upstream's `? true : nil`

        s.SplitCwd = "/split-live";
        s.CommandWait = true;
        var snap2 = s.ToSnapshot();
        Assert.Equal("/split-live", snap2.SplitCwd);
        Assert.True(snap2.CommandWait);
    }

    [Fact]
    public void WorkspaceToSnapshotWritesCollapsedOnlyWhenCollapsed()
    {
        var workspace = new Workspace(Guid.NewGuid(), "work");
        Assert.Null(workspace.ToSnapshot().Collapsed);

        workspace.IsExpanded = false;
        Assert.True(workspace.ToSnapshot().Collapsed);
    }

    [Fact]
    public void WorkspaceUnseenCountSumsItsSessions()
    {
        var workspace = new Workspace(Guid.NewGuid(), "work");
        var a = NewSession();
        var b = NewSession();
        a.UnseenCount = 2;
        b.UnseenCount = 3;
        workspace.Sessions.Add(a);
        workspace.Sessions.Add(b);

        Assert.Equal(5, workspace.UnseenCount);
    }

    [Fact]
    public void CwdForPaneResolvesPaneSpecificDirectory()
    {
        var s = NewSession("/repo");
        s.CurrentCwd = "/repo/primary";
        s.SplitCwd = "/var/log";
        Assert.Equal("/repo/primary", s.CwdFor(CommandContext.Pane.Left));
        Assert.Equal("/repo/primary", s.CwdFor(CommandContext.Pane.Scratch));
        Assert.Equal("/var/log", s.CwdFor(CommandContext.Pane.Right));
        s.SplitCwd = null;
        s.InitialSplitCwd = "/var/restored";
        Assert.Equal("/var/restored", s.CwdFor(CommandContext.Pane.Right));
        s.InitialSplitCwd = null;
        Assert.Equal("/repo/primary", s.CwdFor(CommandContext.Pane.Right));
    }
}
