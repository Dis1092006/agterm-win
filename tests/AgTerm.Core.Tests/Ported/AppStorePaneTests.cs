using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore/Tests/agtermCoreTests/AppStorePaneTests.swift.</summary>
public class AppStorePaneTests
{
    // MARK: - split panes

    [Fact]
    public void ToggleSplitFlipsFlag()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.False(session.IsSplit);
        Assert.False(session.HasSplit);
        store.ToggleSplit(session.Id);
        Assert.True(session.IsSplit);
        Assert.True(session.HasSplit);
        Assert.True(session.SplitFocused);  // opening focuses the new (right) pane
        store.ToggleSplit(session.Id);
        Assert.False(session.IsSplit);
        // hiding the split keeps hasSplit so the sidebar/title split indicators persist, and keeps
        // splitFocused so the focused pane is the one shown maximized.
        Assert.True(session.HasSplit);
        Assert.True(session.SplitFocused);
    }

    [Fact]
    public void ToggleSplitReshowPreservesFocusedPane()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.ToggleSplit(session.Id);          // open a NEW split -> focuses the new (right) pane
        Assert.True(session.SplitFocused);
        session.SplitFocused = false;           // focus the left pane
        store.ToggleSplit(session.Id);          // hide (zoom): left pane stays the maximized one
        Assert.False(session.IsSplit);
        Assert.False(session.SplitFocused);
        store.ToggleSplit(session.Id);          // re-show (un-zoom): must keep the left pane focused
        Assert.True(session.IsSplit);
        Assert.False(session.SplitFocused);     // regression guard: no jerk back to the right pane
    }

    [Fact]
    public void CloseSplitHidesAndTearsDownSurface()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SplitFocused = true;
        var split = new SpySurface();
        session.SplitSurface = split;
        session.SplitCwd = "/var/log";
        session.SplitRatio = 0.7;
        store.CloseSplit(session.Id);
        Assert.False(session.IsSplit);
        Assert.False(session.HasSplit);
        Assert.False(session.SplitFocused);
        Assert.Null(session.SplitSurface);
        Assert.Null(session.SplitCwd);
        Assert.Null(session.InitialSplitCwd);
        Assert.Null(session.SplitRatio); // teardown clears geometry too, so a fresh re-split opens even
        Assert.Equal(1, split.TeardownCount);
    }

    [Fact]
    public void ClampSplitRatioBoundsValue()
    {
        Assert.Equal(0.7, AppStore.ClampSplitRatio(0.7));
        Assert.Equal(AppStore.SplitRatioMax, AppStore.ClampSplitRatio(2.0));   // above the cap
        Assert.Equal(AppStore.SplitRatioMin, AppStore.ClampSplitRatio(-1.0));  // below the floor
        Assert.Equal(AppStore.SplitRatioMin, AppStore.ClampSplitRatio(AppStore.SplitRatioMin));
        Assert.Equal(AppStore.SplitRatioMax, AppStore.ClampSplitRatio(AppStore.SplitRatioMax));
    }

    [Fact]
    public void ApplySplitRatioClampsSetsAndReturns()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.Equal(0.7, store.ApplySplitRatio(0.7, session.Id));
        Assert.Equal(0.7, session.SplitRatio);
        // out-of-range clamps to the cap, on both the return and the stored value.
        Assert.Equal(AppStore.SplitRatioMax, store.ApplySplitRatio(2.0, session.Id));
        Assert.Equal(AppStore.SplitRatioMax, session.SplitRatio);
    }

    [Fact]
    public void ApplySplitRatioUnknownSessionReturnsNil()
    {
        var store = AppStoreFixtures.MakeStore();
        Assert.Null(store.ApplySplitRatio(0.5, Guid.NewGuid()));
    }

    [Fact]
    public void ClosePrimaryPaneWithSplitKeepsSessionAndPromotesSurvivor()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SplitFocused = true;
        session.SplitCwd = "/var/log";
        session.SplitTitle = "remote-host";
        session.SplitForegroundCommand = new[] { "ssh", "host" };
        session.SplitRatio = 0.3;
        session.InitialCommand = "ssh host"; // a --command primary whose command has now exited
        store.ClosePrimaryPane(session.Id);
        Assert.NotNull(store.SessionWithId(session.Id)); // session survives
        Assert.Equal(1, primary.TeardownCount);          // the dead primary is torn down
        Assert.Equal(0, split.TeardownCount);            // the survivor is kept
        Assert.Equal(1, split.PromotedCount);            // the survivor is promoted to the primary role
        // the survivor MOVES into the primary slot — the session is now a plain single pane
        Assert.Same(split, session.Surface);
        Assert.Null(session.SplitSurface);
        Assert.False(session.IsSplit);
        Assert.False(session.HasSplit);
        Assert.False(session.SplitFocused);              // no split anymore; the survivor is the main pane
        Assert.Null(session.SplitRatio);                 // promoted to single, so a later split opens even
        Assert.Null(session.InitialCommand);             // the command pane is gone; a restart must NOT resurrect it
        // the survivor's metadata migrates up to the session (main) fields, and the split fields clear
        Assert.Equal("/var/log", session.CurrentCwd);
        Assert.Equal("remote-host", session.OscTitle);
        Assert.Equal(new[] { "ssh", "host" }, session.ForegroundCommand);
        Assert.Null(session.SplitCwd);
        Assert.Null(session.SplitTitle);
        Assert.Null(session.SplitForegroundCommand);
        // the session-scoped control arms (session.copy/paste/selectall, font.*) must still reach the live
        // shell: the survivor now sits IN `Surface`, so `AddressableSurface` resolves it through the primary
        // slot (the `?? SplitSurface` fallback is for a shown split pre-collapse, not for promotion).
        Assert.Same(split, session.AddressableSurface);
    }

    [Fact]
    public void AddressableSurfaceIsTheMainPaneUntilThePrimaryExits()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        Assert.Same(primary, session.AddressableSurface); // plain session

        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        Assert.Same(primary, session.AddressableSurface); // split shown, main pane still addressed

        session.SplitFocused = true;
        Assert.Same(primary, session.AddressableSurface); // NOT focus-aware: selectall + copy stay paired
    }

    [Fact]
    public void AddressableSurfaceIsNilWhenNoPaneIsRealized()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.Null(session.AddressableSurface);          // never-shown session still errors "session not realized"
    }

    [Fact]
    public void ClosePrimaryPaneUsesRestoredSplitCwdAndClearsTitleWhenSplitHasNoOSCYet()
    {
        // a RESTORED split whose shell hasn't emitted OSC yet: `InitialSplitCwd` is seeded but the live
        // `SplitCwd`/`SplitTitle` are still nil. Exiting the primary must promote the survivor showing ITS
        // restore-seed cwd and NO title — not the exited primary's cwd/title lingering until the survivor's
        // next report.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface();
        session.SplitSurface = new SpySurface();
        session.IsSplit = true;
        session.HasSplit = true;
        session.CurrentCwd = "/primary";             // the exited primary's live cwd on the main field
        session.OscTitle = "primary-title";          // the exited primary's title
        session.InitialSplitCwd = "/restored-split"; // the survivor's restore-seed; no live splitCwd/splitTitle yet
        store.ClosePrimaryPane(session.Id);
        Assert.Equal("/restored-split", session.CurrentCwd); // the survivor's restore-seed cwd, not the primary's
        Assert.Null(session.OscTitle);                       // the primary's title must NOT linger on the survivor
        Assert.Null(session.InitialSplitCwd);                // split fields cleared after promotion
    }

    [Fact]
    public void ClosePrimaryPaneKeepsSearchOwnedBySurvivor()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SearchActive = true;      // the SURVIVING (split) pane owns an open search bar
        session.SearchSurface = split;
        store.ClosePrimaryPane(session.Id);
        Assert.True(session.SearchActive);         // the survivor's search stays valid across promotion
        Assert.Same(split, session.SearchSurface);
    }

    [Fact]
    public void ClosePrimaryPaneClearsSearchOwnedByExitingPrimary()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SearchActive = true;      // the EXITING primary owns the bar → reset it (no stuck bar)
        session.SearchSurface = primary;
        store.ClosePrimaryPane(session.Id);
        Assert.False(session.SearchActive);
        Assert.Null(session.SearchSurface);
    }

    // Regression: after the primary exits and the split is promoted into the main slot, the promoted
    // surface still carries the split pane's `onExit` (→ `CloseSplitPane`). Since it is now the session's
    // sole pane, that exit must CLOSE the session — not collapse a split that no longer exists, which left
    // a zombie session with a torn-down surface before `CloseSplitPane`'s guard was tightened.
    [Fact]
    public void CloseSplitPaneAfterPromotionClosesSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SplitFocused = true;
        store.ClosePrimaryPane(session.Id);                // primary exits → split promoted into the main slot
        Assert.Same(split, session.Surface);               // precondition: the survivor is now the sole pane
        Assert.Null(session.SplitSurface);
        store.CloseSplitPane(session.Id);                  // the survivor's stale split `onExit` fires
        Assert.Null(store.SessionWithId(session.Id));      // last pane → session closed, no zombie
        Assert.Equal(1, split.TeardownCount);              // the promoted surface is torn down exactly once
    }

    // Regression (the fix `handlePaneExit` routes to): promote a survivor into the main slot, split AGAIN,
    // then exit the (promoted) MAIN pane. Because the survivor's role is now primary, its exit runs
    // `ClosePrimaryPane` — which must collapse onto the FRESH right pane (promote it, tear down the exited
    // main), not the stale `CloseSplitPane` that would tear down the new split and strand the dead main.
    [Fact]
    public void ClosePrimaryPaneAfterPromotionAndResplitCollapsesToNewSplit()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var firstSplit = new SpySurface(); session.SplitSurface = firstSplit;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SplitFocused = true;
        store.ClosePrimaryPane(session.Id);                // primary exits → firstSplit promoted into main
        Assert.Same(firstSplit, session.Surface);          // precondition: firstSplit is now the sole pane
        Assert.Null(session.SplitSurface);
        // re-split the promoted single pane: a fresh right pane mounts beside the survivor.
        var secondSplit = new SpySurface(); session.SplitSurface = secondSplit;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SplitFocused = true;
        store.ClosePrimaryPane(session.Id);                // the promoted MAIN pane's own exit routes here
        Assert.NotNull(store.SessionWithId(session.Id));   // session survives — the split is promoted, not lost
        Assert.Same(secondSplit, session.Surface);         // the FRESH right pane took over the main slot
        Assert.Null(session.SplitSurface);
        Assert.Equal(1, secondSplit.PromotedCount);        // it was promoted, not torn down
        Assert.Equal(0, secondSplit.TeardownCount);
        Assert.Equal(1, firstSplit.TeardownCount);         // the exited (promoted) main pane is torn down
    }

    [Fact]
    public void ClosePrimaryPaneWithoutSplitClosesSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        store.ClosePrimaryPane(session.Id);
        Assert.Null(store.SessionWithId(session.Id)); // single session → closed
        Assert.Equal(1, primary.TeardownCount);
    }

    [Fact]
    public void CloseSplitPaneWithPrimaryCollapsesToPrimary()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SplitRatio = 0.4;
        store.CloseSplitPane(session.Id);
        Assert.NotNull(store.SessionWithId(session.Id)); // session survives
        Assert.Equal(1, split.TeardownCount);            // the split is torn down
        Assert.Equal(0, primary.TeardownCount);          // the primary is kept
        Assert.Null(session.SplitSurface);
        Assert.False(session.IsSplit);
        Assert.Null(session.SplitRatio);                 // delegates to CloseSplit, which clears the ratio
    }

    [Fact]
    public void CloseSplitPaneWithoutPrimaryClosesSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        // the primary already exited (surface nil); only the split survives, so this is the last pane.
        var split = new SpySurface(); session.SplitSurface = split;
        store.CloseSplitPane(session.Id);
        Assert.Null(store.SessionWithId(session.Id)); // last pane → closed
        Assert.Equal(1, split.TeardownCount);
    }

    [Fact]
    public void ClosePrimaryPaneMigratesBothRestoreOverrideHalvesUp()
    {
        // the survivor's restore-command override follows it into the main slot: the persisted pin (so the
        // next launch restores the promoted pane's command, not the dead primary's) AND any payload still
        // armed for this launch (so a surface built after promotion still runs it).
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface();
        session.SplitSurface = new SpySurface();
        session.IsSplit = true;
        session.HasSplit = true;
        session.RestoreCommand = "claude --resume primary";
        session.PendingRestoreCommand = "claude --resume primary";
        session.SplitRestoreCommand = "tail -f /var/log/x";
        session.PendingSplitRestoreCommand = "tail -f /var/log/x";

        store.ClosePrimaryPane(session.Id);

        Assert.Equal("tail -f /var/log/x", session.RestoreCommand);        // the survivor's pin replaces the dead primary's
        Assert.Equal("tail -f /var/log/x", session.PendingRestoreCommand);
        Assert.Null(session.SplitRestoreCommand);                          // nothing still describes the gone pane
        Assert.Null(session.PendingSplitRestoreCommand);
    }

    [Fact]
    public void ClosePrimaryPaneMigratesAnAbsentSplitOverrideAsAClear()
    {
        // migration replaces OUTRIGHT (like foregroundCommand): a survivor with no override must not
        // inherit the exited primary's pin, or the next launch would run a command in the wrong pane's shell.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface();
        session.SplitSurface = new SpySurface();
        session.IsSplit = true;
        session.HasSplit = true;
        session.RestoreCommand = "claude --resume primary";
        session.PendingRestoreCommand = "claude --resume primary";

        store.ClosePrimaryPane(session.Id);

        Assert.Null(session.RestoreCommand);
        Assert.Null(session.PendingRestoreCommand);
    }

    [Fact]
    public void CloseSplitClearsBothRestoreOverrideHalves()
    {
        // the right pane is gone, so its override describes nothing — and a payload left armed would fire
        // on the next manual ⌘D instead of the launch it was seeded for.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface();
        session.SplitSurface = new SpySurface();
        session.IsSplit = true;
        session.HasSplit = true;
        session.RestoreCommand = "claude --resume main";
        session.PendingRestoreCommand = "claude --resume main";
        session.SplitRestoreCommand = "tail -f /var/log/x";
        session.PendingSplitRestoreCommand = "tail -f /var/log/x";

        store.CloseSplit(session.Id);

        Assert.Null(session.SplitRestoreCommand);
        Assert.Null(session.PendingSplitRestoreCommand);
        // the surviving main pane keeps both of its halves
        Assert.Equal("claude --resume main", session.RestoreCommand);
        Assert.Equal("claude --resume main", session.PendingRestoreCommand);
    }

    [Fact]
    public void CloseSplitClearsStuckSearchOnSurvivingSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var split = new SpySurface();
        session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        // search opened on the split pane, pinned as the owner
        session.SearchActive = true;
        session.SearchNeedle = "needle";
        session.SearchTotal = 3;
        session.SearchSelected = 1;
        session.SearchSurface = split;
        store.CloseSplit(session.Id);
        Assert.False(session.SearchActive);
        Assert.Equal("", session.SearchNeedle);
        Assert.Null(session.SearchTotal);
        Assert.Null(session.SearchSelected);
        Assert.Null(session.SearchSurface);
    }

    [Fact]
    public void ClosePrimaryPaneClearsStuckSearchOnPromotedSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        // search opened on the primary, which is torn down + promoted while the session survives
        session.SearchActive = true;
        session.SearchNeedle = "needle";
        session.SearchTotal = 2;
        session.SearchSelected = 1;
        session.SearchSurface = primary;
        store.ClosePrimaryPane(session.Id);
        Assert.NotNull(store.SessionWithId(session.Id)); // session survives
        Assert.False(session.SearchActive);
        Assert.Equal("", session.SearchNeedle);
        Assert.Null(session.SearchTotal);
        Assert.Null(session.SearchSelected);
        Assert.Null(session.SearchSurface);
    }

    [Fact]
    public void CloseSplitPaneClearsStuckSearchWhenCollapsingToPrimary()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        var split = new SpySurface(); session.SplitSurface = split;
        session.IsSplit = true;
        session.HasSplit = true;
        session.SearchActive = true;
        session.SearchTotal = 5;
        session.SearchSurface = split;
        store.CloseSplitPane(session.Id); // primary alive → collapses via CloseSplit, which clears search
        Assert.NotNull(store.SessionWithId(session.Id));
        Assert.False(session.SearchActive);
        Assert.Null(session.SearchTotal);
        Assert.Null(session.SearchSurface);
    }

    // MARK: - overlay

    [Fact]
    public void OpenOverlaySetsCommandAndFlag()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.True(store.OpenOverlay(session.Id, "revdiff", cwd: "/b"));
        Assert.True(session.OverlayActive);
        Assert.Equal("revdiff", session.OverlayCommand);
        Assert.Equal("/b", session.OverlayCwd);
        // no size given → the default full-pane overlay, not a floating one.
        Assert.Null(session.OverlaySizePercent);
        // a second open while one is active is a no-op.
        Assert.False(store.OpenOverlay(session.Id, "other"));
        Assert.Equal("revdiff", session.OverlayCommand);
    }

    [Fact]
    public void OpenOverlayCarriesBackgroundColorAndCloseClears()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.True(store.OpenOverlay(session.Id, "revdiff", backgroundColor: "#2a1a3a"));
        Assert.Equal("#2a1a3a", session.OverlayBackgroundColor);
        // close clears the overlay's color back to nil, like the other ephemeral overlay fields.
        store.CloseOverlay(session.Id);
        Assert.Null(session.OverlayBackgroundColor);
        // omitting the color leaves it nil (default theme background, unchanged behavior).
        Assert.True(store.OpenOverlay(session.Id, "revdiff"));
        Assert.Null(session.OverlayBackgroundColor);
    }

    [Fact]
    public void OverlayExitCodeRecordedAndSurvivesClose()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.OpenOverlay(session.Id, "revdiff");
        Assert.Null(session.OverlayExitCode);
        store.RecordOverlayExit(session.Id, 10);
        Assert.True(store.CloseOverlay(session.Id));
        // the exit code survives close (read by session.overlay.result after the overlay vanishes)...
        Assert.Equal(10, session.OverlayExitCode);
        // ...and is reset when a new overlay opens.
        Assert.True(store.OpenOverlay(session.Id, "revdiff"));
        Assert.Null(session.OverlayExitCode);
    }

    [Fact]
    public void RecordOverlayExitUnknownSessionIsNoop()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        // a bogus id must be a no-op, not a crash, and must not touch any existing session.
        store.RecordOverlayExit(Guid.NewGuid(), 5);
        Assert.Null(session.OverlayExitCode);
    }

    [Fact]
    public void OpenOverlayFloatingClampsSizePercent()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.True(store.OpenOverlay(session.Id, "htop", sizePercent: 70));
        Assert.Equal(70, session.OverlaySizePercent);
        // close clears the floating size back to nil.
        store.CloseOverlay(session.Id);
        Assert.Null(session.OverlaySizePercent);
        // out-of-range values clamp to 1...100, including negatives; the exact bounds pass through.
        store.OpenOverlay(session.Id, "htop", sizePercent: 250);
        Assert.Equal(100, session.OverlaySizePercent);
        store.CloseOverlay(session.Id);
        store.OpenOverlay(session.Id, "htop", sizePercent: 0);
        Assert.Equal(1, session.OverlaySizePercent);
        store.CloseOverlay(session.Id);
        store.OpenOverlay(session.Id, "htop", sizePercent: -5);
        Assert.Equal(1, session.OverlaySizePercent);
        store.CloseOverlay(session.Id);
        store.OpenOverlay(session.Id, "htop", sizePercent: 100);
        Assert.Equal(100, session.OverlaySizePercent);
        store.CloseOverlay(session.Id);
        store.OpenOverlay(session.Id, "htop", sizePercent: 1);
        Assert.Equal(1, session.OverlaySizePercent);
    }

    [Fact]
    public void ResizeOverlaySwitchesFullAndFloatingAndClamps()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        // no overlay open → no-op, leaves size untouched.
        Assert.False(store.ResizeOverlay(session.Id, 50));
        Assert.Null(session.OverlaySizePercent);
        // open full, then resize it to a floating percent (nil → 60).
        store.OpenOverlay(session.Id, "htop");
        Assert.Null(session.OverlaySizePercent);
        Assert.True(store.ResizeOverlay(session.Id, 60));
        Assert.Equal(60, session.OverlaySizePercent);
        Assert.True(session.FloatingOverlayActive);
        // resize back to full (nil).
        Assert.True(store.ResizeOverlay(session.Id, null));
        Assert.Null(session.OverlaySizePercent);
        Assert.True(session.FullOverlayActive);
        // out-of-range percents clamp to 1...100.
        store.ResizeOverlay(session.Id, 250);
        Assert.Equal(100, session.OverlaySizePercent);
        store.ResizeOverlay(session.Id, 0);
        Assert.Equal(1, session.OverlaySizePercent);
        // the overlay program keeps running across every resize (no re-spawn).
        Assert.True(session.OverlayActive);
    }

    [Fact]
    public void OverlayPresentationMutationsNotifySidebar()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace.Id, "/a")!;
        var notifications = 0;
        store.SidebarChanged += () => notifications++;

        Assert.True(store.OpenOverlay(session.Id, "pause"));
        Assert.Equal(1, notifications);
        Assert.False(store.OpenOverlay(session.Id, "other"));
        Assert.Equal(1, notifications);

        Assert.True(store.ResizeOverlay(session.Id, 60));
        Assert.Equal(2, notifications);
        Assert.True(store.CloseOverlay(session.Id));
        Assert.Equal(3, notifications);
        Assert.False(store.CloseOverlay(session.Id));
        Assert.Equal(3, notifications);
    }

    [Fact]
    public void CloseOverlayCapturesExitCodeAlreadyCompletedOnSurface()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace.Id, "/a")!;
        Assert.True(store.OpenOverlay(session.Id, "exit 64", wait: true));
        session.OverlaySurface = new SpySurface { CompletedExitCode = 64 };

        Assert.True(store.CloseOverlay(session.Id));

        Assert.Equal(64, session.OverlayExitCode);
    }

    [Fact]
    public void SearchMutationsKeepExactOwnerAndRejectStalePaneToken()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace.Id, "/a")!;
        var owner = new SpySurface("owner-pane");
        session.Surface = owner;

        Assert.True(store.OpenSearch(session.Id));
        Assert.Same(owner, session.SearchSurface);
        Assert.False(store.UpdateSearch(session.Id, "stale-pane", "needle", 3, 1));
        Assert.Equal("", session.SearchNeedle);
        Assert.False(store.CloseSearch(session.Id, "stale-pane"));
        Assert.True(session.SearchActive);

        Assert.True(store.UpdateSearch(session.Id, "owner-pane", "needle", 3, 1));
        Assert.Equal("needle", session.SearchNeedle);
        Assert.Equal("1 of 3", session.SearchDisplayText);
        Assert.True(store.CloseSearch(session.Id, "owner-pane"));
        Assert.False(session.SearchActive);
    }

    [Fact]
    public void SearchTotalFollowsOwnerResultsWithoutReplacingSelection()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace.Id, "/a")!;
        session.Surface = new SpySurface("owner-pane");

        Assert.True(store.OpenSearch(session.Id));
        Assert.True(store.UpdateSearch(session.Id, "owner-pane", "needle", 3, 3));
        Assert.False(store.UpdateSearchTotal(session.Id, "other-pane", "needle", 2));
        Assert.False(store.UpdateSearchTotal(session.Id, "owner-pane", "old-needle", 2));
        Assert.Equal(3, session.SearchTotal);

        Assert.True(store.UpdateSearchTotal(session.Id, "owner-pane", "needle", 2));
        Assert.Equal(2, session.SearchTotal);
        Assert.Equal(3, session.SearchSelected);
        Assert.Equal("2 of 2", session.SearchDisplayText);

        Assert.True(store.UpdateSearchTotal(session.Id, "owner-pane", "needle", 4));
        Assert.Equal("3 of 4", session.SearchDisplayText);
        Assert.True(store.CloseSearch(session.Id, "owner-pane"));
        Assert.False(store.UpdateSearchTotal(session.Id, "owner-pane", "needle", 5));
        Assert.Null(session.SearchTotal);
    }

    [Fact]
    public void ControlTreeReportsCommandWait()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        // a held --command session reports the flag so a script can record and restore it.
        store.AddSession(ws.Id, "/a", command: "make test", wait: true);
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal(true, node!.CommandWait);
        // a non-holding command session omits it (nil).
        var plain = store.AddSession(ws.Id, "/b", command: "make test")!;
        node = store.ControlTree().Workspaces[0].Sessions
            .FirstOrDefault(n => n.Id == plain.Id.ToString("D").ToUpperInvariant());
        Assert.NotNull(node);
        Assert.Null(node!.CommandWait);
        // a plain session (no command) omits it even when the flag IS set — gated on initialCommand, so a
        // mutant that drops the `InitialCommand != null` term would report true here and fail.
        var shell = store.AddSession(ws.Id, "/c")!;
        shell.CommandWait = true;
        node = store.ControlTree().Workspaces[0].Sessions
            .FirstOrDefault(n => n.Id == shell.Id.ToString("D").ToUpperInvariant());
        Assert.NotNull(node);
        Assert.Null(node!.CommandWait);
    }

    [Fact]
    public void ControlTreeReportsOverlaySizePercent()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        // no overlay: the field is omitted (nil).
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.False(node!.Overlay);
        Assert.Null(node.OverlaySizePercent);
        // floating overlay: the percent rides the node so a script can record it before zooming.
        store.OpenOverlay(session.Id, "htop", sizePercent: 95);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.True(node!.Overlay);
        Assert.Equal(95, node.OverlaySizePercent);
        // full-pane overlay: open but no size (nil = full).
        store.ResizeOverlay(session.Id, null);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.True(node!.Overlay);
        Assert.Null(node.OverlaySizePercent);
    }

    [Fact]
    public void ControlTreeReportsSplitRatio()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        // no split: the field is omitted (nil).
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.False(node!.Split);
        Assert.Null(node.SplitRatio);
        // a split with the divider still at the default (never moved): still nil.
        store.ToggleSplit(session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.True(node!.Split);
        Assert.Null(node.SplitRatio);
        // moving the divider surfaces the ratio so a script can record and restore it.
        _ = store.ApplySplitRatio(0.3, session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal(0.3, node!.SplitRatio);
        // a hidden split keeps its ratio readable (gated on hasSplit, not isSplit).
        store.ToggleSplit(session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.False(node!.Split);
        Assert.Equal(0.3, node.SplitRatio);
    }

    [Fact]
    public void ControlTreeReportsRestoreCommand()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.ToggleSplit(session.Id);
        // no override: both fields are omitted (nil).
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Null(node!.RestoreCommand);
        Assert.Null(node.SplitRestoreCommand);
        // both panes pinned, the split one to nothing — "" must read back as an empty string, not as nil.
        store.SetRestoreCommand("claude --resume abc", StatusPane.Left, session.Id);
        store.SetRestoreCommand("", StatusPane.Right, session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal("claude --resume abc", node!.RestoreCommand);
        Assert.Equal("", node.SplitRestoreCommand);
        // the override is STICKY: consuming this launch's pending payloads (what the surface factories do
        // at bootstrap) must not change the read-back — a builder wired to the pending slots reports nil here.
        session.PendingRestoreCommand = session.RestoreCommand;
        session.PendingSplitRestoreCommand = session.SplitRestoreCommand;
        Assert.Equal("claude --resume abc", session.TakePendingRestoreOverride(StatusPane.Left));
        Assert.Equal("", session.TakePendingRestoreOverride(StatusPane.Right));
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal("claude --resume abc", node!.RestoreCommand);
        Assert.Equal("", node.SplitRestoreCommand);
        // unpinning drops the fields back to omitted.
        store.SetRestoreCommand(null, StatusPane.Left, session.Id);
        store.SetRestoreCommand(null, StatusPane.Right, session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Null(node!.RestoreCommand);
        Assert.Null(node.SplitRestoreCommand);
    }

    [Fact]
    public void ControlTreeThreadsFontSizesFromClosures()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        _ = store.AddSession(ws.Id, "/a")!;
        // default closures: the font-size fields are omitted (nil), like foreground.
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Null(node!.FontSize);
        Assert.Null(node.SplitFontSize);
        Assert.Null(node.ScratchFontSize);
        // the host supplies live per-pane sizes via closures (the app reads them off the surfaces).
        node = store.ControlTree(fontSize: _ => 13, splitFontSize: _ => 9.5,
                                 scratchFontSize: _ => 11).Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal(13, node!.FontSize);
        Assert.Equal(9.5, node.SplitFontSize);
        Assert.Equal(11, node.ScratchFontSize);
    }

    [Fact]
    public void ControlTreeFontSizeReadsPromotedSurvivorViaAddressableSurface()
    {
        // regression: after the primary pane exits, the fontSize read-back must resolve through
        // AddressableSurface — the same surface the font default/left WRITE path targets. With true
        // promotion the survivor MOVES into `Surface`, so AddressableSurface == Surface and the
        // read-back keeps reporting the live shell across the collapse.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface();
        var survivor = new SpySurface();
        session.SplitSurface = survivor;
        session.IsSplit = true;
        session.HasSplit = true;
        store.ClosePrimaryPane(session.Id);                // primary exits -> survivor promoted into `Surface`
        Assert.Same(survivor, session.Surface);
        Assert.Same(survivor, session.AddressableSurface);
        var promoted = store.ControlTree(fontSize: s => s.AddressableSurface is not null ? 13 : (double?)null);
        Assert.Equal(13, promoted.Workspaces[0].Sessions.FirstOrDefault()?.FontSize);
        // the `?? SplitSurface` term is a defensive fallback now — hand-build the surface-less state it
        // covers and keep the addressable-vs-bare-`Surface` distinction guarded: a closure over bare
        // `Surface` reports nothing there, the addressable one still finds the live split shell.
        var fallback = store.AddSession(ws.Id, "/b")!;
        fallback.HasSplit = true;
        fallback.SplitSurface = new SpySurface();
        var viaAddressable = store.ControlTree(fontSize: s => s.AddressableSurface is not null ? 13 : (double?)null);
        Assert.Equal(13, viaAddressable.Workspaces[0].Sessions.LastOrDefault()?.FontSize);
        var viaSurface = store.ControlTree(fontSize: s => s.Surface is not null ? 13 : (double?)null);
        Assert.Null(viaSurface.Workspaces[0].Sessions.LastOrDefault()?.FontSize);
    }

    [Fact]
    public void ControlTreeReportsSplitFocused()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        // no split: the field is omitted (nil).
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Null(node!.SplitFocused);
        // opening a split focuses the new (right) pane.
        store.ToggleSplit(session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal(true, node!.SplitFocused);
        // focusing the main (left) pane surfaces false — distinct from nil (= no split).
        session.SplitFocused = false;
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal(false, node!.SplitFocused);
        // a hidden split keeps the focus readable (gated on hasSplit, not isSplit).
        store.ToggleSplit(session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.False(node!.Split);
        Assert.Equal(false, node.SplitFocused);
    }

    [Fact]
    public void ControlTreeReportsStatusModifiers()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        // idle: no status, so blink/color are omitted.
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Null(node!.StatusBlink);
        Assert.Null(node.StatusColor);
        // a blocked status with blink + a color override surfaces both modifiers.
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, Blink: true, Color: "#ff8800"), session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal("blocked", node!.Status);
        Assert.Equal(true, node.StatusBlink);
        Assert.Equal("#ff8800", node.StatusColor);
        // a status without blink omits statusBlink (false -> nil); without a color override omits statusColor.
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), session.Id);
        node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);
        Assert.Equal("active", node!.Status);
        Assert.Null(node.StatusBlink);
        Assert.Null(node.StatusColor);
    }

    [Fact]
    public void CloseOverlayTearsDownAndClears()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.OpenOverlay(session.Id, "revdiff");
        var overlay = new SpySurface();
        session.OverlaySurface = overlay;
        Assert.True(store.CloseOverlay(session.Id));
        Assert.False(session.OverlayActive);
        Assert.Null(session.OverlaySurface);
        Assert.Null(session.OverlayCommand);
        Assert.Equal(1, overlay.TeardownCount);
        // closing again is a no-op.
        Assert.False(store.CloseOverlay(session.Id));
    }

    [Fact]
    public void CloseSessionTearsDownOverlaySurface()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.OpenOverlay(session.Id, "revdiff");
        var overlay = new SpySurface();
        session.OverlaySurface = overlay;
        store.CloseSession(session.Id);
        Assert.Equal(1, overlay.TeardownCount);
    }

    // MARK: - scratch

    [Fact]
    public void ToggleScratchFlipsFlagAndKeepsSurfaceAlive()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.False(session.ScratchActive);
        store.ToggleScratch(session.Id);
        Assert.True(session.ScratchActive);
        // the detail pane lazily creates the surface on show; simulate that.
        var scratch = new SpySurface();
        session.ScratchSurface = scratch;
        // hiding keeps the shell alive (slot retained), so a re-show reuses it.
        store.ToggleScratch(session.Id);
        Assert.False(session.ScratchActive);
        Assert.Same(scratch, session.ScratchSurface);
        Assert.Equal(0, scratch.TeardownCount);
        store.ToggleScratch(session.Id);
        Assert.True(session.ScratchActive);
        Assert.Same(scratch, session.ScratchSurface);
    }

    [Fact]
    public void CloseScratchTearsDownAndClears()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.ScratchActive = true;
        var scratch = new SpySurface();
        session.ScratchSurface = scratch;
        Assert.True(store.CloseScratch(session.Id));
        Assert.False(session.ScratchActive);
        Assert.Null(session.ScratchSurface);
        Assert.Equal(1, scratch.TeardownCount);
        // closing again (no surface) is a no-op.
        Assert.False(store.CloseScratch(session.Id));
    }

    [Fact]
    public void CloseScratchClearsStuckSearchWhenScratchOwnsIt()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.ScratchActive = true;
        var scratch = new SpySurface();
        session.ScratchSurface = scratch;
        // search opened on the scratch, pinned as the owner — its teardown must reset search.
        session.SearchActive = true;
        session.SearchNeedle = "needle";
        session.SearchTotal = 4;
        session.SearchSelected = 2;
        session.SearchSurface = scratch;
        Assert.True(store.CloseScratch(session.Id));
        Assert.False(session.SearchActive);
        Assert.Equal("", session.SearchNeedle);
        Assert.Null(session.SearchTotal);
        Assert.Null(session.SearchSelected);
        Assert.Null(session.SearchSurface);
    }

    [Fact]
    public void CloseScratchLeavesSearchOwnedByMainPane()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var primary = new SpySurface(); session.Surface = primary;
        session.ScratchActive = true;
        var scratch = new SpySurface();
        session.ScratchSurface = scratch;
        // search is owned by the MAIN pane, not the scratch covering the session — tearing the scratch
        // down must not nuke a valid main-pane search.
        session.SearchActive = true;
        session.SearchNeedle = "needle";
        session.SearchSurface = primary;
        Assert.True(store.CloseScratch(session.Id));
        Assert.True(session.SearchActive);
        Assert.Equal("needle", session.SearchNeedle);
        Assert.Same(primary, session.SearchSurface);
    }

    [Fact]
    public void ToggleScratchUnknownSessionIsNoop()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.ToggleScratch(Guid.NewGuid()); // unknown id
        Assert.False(session.ScratchActive); // existing session untouched
    }

    [Fact]
    public void CloseScratchUnknownSessionReturnsFalse()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        _ = store.AddSession(ws.Id, "/a");
        Assert.False(store.CloseScratch(Guid.NewGuid())); // unknown id, no surface
    }

    [Fact]
    public void CloseSessionTearsDownScratchSurface()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var scratch = new SpySurface();
        session.ScratchSurface = scratch;
        store.CloseSession(session.Id);
        Assert.Equal(1, scratch.TeardownCount);
    }

    [Fact]
    public void RemoveWorkspaceTearsDownScratchSurface()
    {
        var store = AppStoreFixtures.MakeStore();
        var keep = store.AddWorkspace("keep");
        _ = store.AddSession(keep.Id, "/k");
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var scratch = new SpySurface();
        session.ScratchSurface = scratch;
        store.RemoveWorkspace(ws.Id);
        Assert.Equal(1, scratch.TeardownCount);
    }

    // MARK: - status-pane reconcile on pane teardown

    [Fact]
    public void CloseSplitClearsRightTaggedStatus()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.SplitSurface = new SpySurface(); session.IsSplit = true; session.HasSplit = true;
        // a block set by the split (`--pane right`); once the split shell exits, the surviving main pane can
        // never keystroke-clear a `.right` tag, so teardown must clear it.
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right), session.Id);
        store.CloseSplit(session.Id);
        Assert.Equal(AgentStatus.Idle, session.AgentIndicator.Status);
        Assert.Null(session.AgentIndicator.StatusPane);
    }

    [Fact]
    public void CloseSplitLeavesMainTaggedStatus()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.SplitSurface = new SpySurface(); session.IsSplit = true; session.HasSplit = true;
        // a `.left`/main block is owned by the surviving main pane — tearing the split down must NOT clear it.
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Left), session.Id);
        store.CloseSplit(session.Id);
        Assert.Equal(AgentStatus.Blocked, session.AgentIndicator.Status);
        Assert.Equal(StatusPane.Left, session.AgentIndicator.StatusPane);
    }

    [Fact]
    public void ClosePrimaryPaneClearsLeftAndNilTaggedStatus()
    {
        foreach (var pane in new StatusPane?[] { StatusPane.Left, null })
        {
            var store = AppStoreFixtures.MakeStore();
            var ws = store.AddWorkspace("work");
            var session = store.AddSession(ws.Id, "/a")!;
            session.Surface = new SpySurface(); session.SplitSurface = new SpySurface();
            session.IsSplit = true; session.HasSplit = true;
            // the primary owned a `.left`/nil-tagged block and is promoted away, so the promoted (right-wired)
            // survivor could never keystroke-clear it — teardown must.
            store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: pane), session.Id);
            store.ClosePrimaryPane(session.Id);
            Assert.Equal(AgentStatus.Idle, session.AgentIndicator.Status);
        }
    }

    [Fact]
    public void ClosePrimaryPaneMigratesRightTaggedStatusToLeft()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface(); session.SplitSurface = new SpySurface();
        session.IsSplit = true; session.HasSplit = true;
        // the `.right` block is owned by the split, which is PROMOTED into the main slot — its status follows,
        // re-tagged to `.left` (like the cwd/title migration) so `tree` (now split:false) and the survivor's
        // left-role-aware keystroke-clear agree, instead of a self-contradictory split:false + statusPane:right.
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right), session.Id);
        store.ClosePrimaryPane(session.Id);
        Assert.Equal(AgentStatus.Blocked, session.AgentIndicator.Status); // the survivor's block persists across promotion
        Assert.Equal(StatusPane.Left, session.AgentIndicator.StatusPane); // re-tagged to the (now sole) main pane
    }

    [Fact]
    public void SetAgentIndicatorCoercesRightToLeftWithoutLiveSplit()
    {
        // a promoted survivor's shell keeps its baked `AGTERM_PANE=right`, so the agent-status hook re-emits
        // `--pane right` on every status AFTER promotion — but there is no right pane. `SetAgentIndicator`
        // coerces `.right` to `.left` when the session has no split (`HasSplit` false), so a post-promotion
        // status can't re-create the `split:false` + `statusPane:"right"` contradiction and the sole
        // `.left`-role-aware pane can still keystroke-clear it. (Drives what the agent-status wrapper does;
        // host-free, CI-covered.)
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        // an actual PROMOTED SURVIVOR: split, exit the primary (survivor promotes), then the still-.right-baked
        // hook fires the next status — the reviewer's exact scenario.
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface(); session.SplitSurface = new SpySurface();
        session.IsSplit = true; session.HasSplit = true;
        store.ClosePrimaryPane(session.Id); // primary exits → survivor promoted, HasSplit/SplitSurface cleared
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right), session.Id);
        Assert.Equal(StatusPane.Left, session.AgentIndicator.StatusPane);              // coerced — no live split
        Assert.True(session.AgentIndicator.ClearedBy(StatusPane.Left, isInterrupt: false)); // the sole (left) pane clears it
        // and the tree agrees: split:false with statusPane "left", never the contradictory "right".
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.False(node?.Split);
        Assert.Equal("left", node?.StatusPane);
        // a session with a LIVE split keeps `.right` — the right pane really exists (incl. a hidden-but-live split).
        var split = store.AddSession(ws.Id, "/b")!;
        split.HasSplit = true; split.SplitSurface = new SpySurface();
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right), split.Id);
        Assert.Equal(StatusPane.Right, split.AgentIndicator.StatusPane);            // kept — a live split owns it
    }

    [Fact]
    public void SetAgentIndicatorKeepsRightDuringSplitRealization()
    {
        // the realization window: `ToggleSplit` sets `HasSplit` synchronously while the deck creates
        // `SplitSurface` a render pass later — so a scripted `session.split` + immediate
        // `session.status --pane right` arrives with `HasSplit == true` but `SplitSurface == null`.
        // `.right` is the correct forward tag there and must NOT be coerced to `.left`, or the realized
        // split reports `split:true` + `statusPane:"left"` and only the LEFT pane could clear a block the
        // RIGHT pane owns — the mirror of the promoted-survivor bug. Guards the `!HasSplit` predicate:
        // the old `SplitSurface == null` gate rewrites this tag and fails the test.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface();
        store.ToggleSplit(session.Id); // HasSplit set synchronously; SplitSurface not yet realized
        Assert.True(session.HasSplit);
        Assert.Null(session.SplitSurface);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right), session.Id);
        Assert.Equal(StatusPane.Right, session.AgentIndicator.StatusPane);          // kept — the split is coming up
        // once the deck realizes the surface, the block is exactly where the right pane can clear it.
        session.SplitSurface = new SpySurface();
        Assert.True(session.AgentIndicator.ClearedBy(StatusPane.Right, isInterrupt: false));
        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.True(node?.Split);
        Assert.Equal("right", node?.StatusPane);
    }

    [Fact]
    public void CloseScratchClearsScratchTaggedStatus()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.ScratchActive = true; session.ScratchSurface = new SpySurface();
        // a scratch-tagged block loses its owning surface on the scratch shell's exit.
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Scratch), session.Id);
        Assert.True(store.CloseScratch(session.Id));
        Assert.Equal(AgentStatus.Idle, session.AgentIndicator.Status);
        Assert.Null(session.AgentIndicator.StatusPane);
    }

    [Fact]
    public void CloseScratchLeavesMainTaggedStatus()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        session.Surface = new SpySurface();
        session.ScratchActive = true; session.ScratchSurface = new SpySurface();
        // a main-pane block survives the scratch teardown (the main pane is still there to clear it).
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Left), session.Id);
        Assert.True(store.CloseScratch(session.Id));
        Assert.Equal(AgentStatus.Blocked, session.AgentIndicator.Status);
    }
}
