using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore AppStoreHudTests.swift and the HUD cases of AppStorePaneTests.swift (upstream
// ed831ab). Upstream hands the store a measured size; the port measures in the store, so a session with
// no metrics reads the unmeasured size: width 80 or the clamped override, height 10.
public class AppStoreHudTests
{
    private static (AppStore Store, Session Session) NewSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/repo")!;
        return (store, session);
    }

    private static (AppStore Store, Session Session) SplitSession()
    {
        var (store, session) = NewSession();
        session.Surface = new SpySurface("left-token");
        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("right-token");
        return (store, session);
    }

    private static ControlSessionNode Node(AppStore store, Session session) =>
        store.ControlTree().Workspaces.SelectMany(w => w.Sessions)
            .Single(s => s.Id == session.Id.ToString("D").ToUpperInvariant());

    private static ControlHudNode Plain(string message, int sizePercent = HudLayout.MaxSizePercent) => new()
    {
        Message = message,
        Spinner = HudSpinners.NoneName,
        SizePercent = sizePercent,
        HeightPercent = 10,
        Position = "center",
        HideAfter = 0,
        Markdown = false,
        Sticky = false,
        Frame = true,
    };

    private static PendingAsk Ask() =>
        new(Guid.NewGuid().ToString("D").ToUpperInvariant(), "Continue?", null,
            new[] { new ControlAskButton { Id = "yes", Label = "Yes" } }, null, null,
            ControlAskStyle.Terminal, ControlAskAlignment.Right, null);

    [Fact]
    public void TheTreeReportsEveryFieldInUpstreamKeyOrder()
    {
        var (store, session) = NewSession();
        var spec = new HudSpec("gathering options", Detail: "scanning 400 files", Spinner: HudSpinner.Braille,
            BackgroundColor: "#2a1a3a", TextColor: "#e0e0e0", SizePercent: 35, Position: HudPosition.TopCenter);

        Assert.True(store.OpenHud(session.Id, spec, session.PaneIdentity));

        Assert.Equal(
            """{"message":"gathering options","detail":"scanning 400 files","spinner":"braille","backgroundColor":"#2a1a3a","textColor":"#e0e0e0","sizePercent":35,"heightPercent":10,"position":"top-center","pane":"left","hideAfter":0,"markdown":false,"sticky":false,"frame":true}""",
            ControlJson.Serialize(Node(store, session).Hud));
    }

    [Fact]
    public void APlainHudReadsBackItsDefaultsAndOmitsWhatTheCallerDidNotSet()
    {
        var (store, session) = NewSession();

        store.OpenHud(session.Id, new HudSpec("working"), null);

        Assert.Equal(Plain("working"), Node(store, session).Hud);
        Assert.Equal(
            """{"message":"working","spinner":"none","sizePercent":80,"heightPercent":10,"position":"center","hideAfter":0,"markdown":false,"sticky":false,"frame":true}""",
            ControlJson.Serialize(Node(store, session).Hud));
    }

    [Fact]
    public void TheReadBackReportsTheCanonicalAnchorForAnAlias()
    {
        var (store, session) = NewSession();

        store.OpenHud(session.Id, new HudSpec("working", Position: HudPositions.Parse("bottom")!.Value), null);

        Assert.Equal("bottom-center", Node(store, session).Hud!.Position);
    }

    [Fact]
    public void TheReadBackReportsMarkdownTheRequestedFontSizeStickyAndFrame()
    {
        var (store, session) = NewSession();

        store.OpenHud(session.Id, new HudSpec("**ok**", Position: HudPosition.TopCenter, Markdown: true,
            FontSize: 18, Sticky: true, Frame: false), null);

        var hud = Node(store, session).Hud!;
        Assert.True(hud.Markdown);
        Assert.Equal(18, hud.FontSize);
        Assert.True(hud.Sticky);
        Assert.False(hud.Frame);
    }

    [Fact]
    public void TheTreeOmitsHudWithoutOne()
    {
        var (store, session) = NewSession();
        Assert.Null(Node(store, session).Hud);

        store.OpenHud(session.Id, new HudSpec("working"), null);
        store.CloseHud(session.Id);

        Assert.Null(Node(store, session).Hud);
    }

    [Fact]
    public void TheTreeNeverReportsAHudAsAProgramOverlay()
    {
        var (store, session) = NewSession();
        session.Surface = new SpySurface("left-token");
        store.OpenHud(session.Id, new HudSpec("working"), null);

        var withHud = Node(store, session);
        Assert.False(session.OverlayActive);
        Assert.False(withHud.Overlay);
        Assert.Null(withHud.OverlaySizePercent);
        Assert.DoesNotContain(withHud.Surfaces!, surface => surface.Kind == "overlay");
        Assert.Same(session.Surface, session.TopmostSurface);

        Assert.True(store.OpenOverlay(session.Id, "htop", sizePercent: 70));
        var withProgram = Node(store, session);
        Assert.True(withProgram.Overlay);
        Assert.Equal(70, withProgram.OverlaySizePercent);
        Assert.Null(withProgram.Hud);
    }

    [Fact]
    public void AnUpdateHoldsTheOpensColorAndFontSizeAndReplacesEverythingElse()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("one", Detail: "d", Spinner: HudSpinner.Dot, BackgroundColor: "#2a1a3a",
            TextColor: "#e0e0e0", SizePercent: 40, Position: HudPosition.TopRight, HideAfter: 30, Markdown: true,
            FontSize: 18, Sticky: true, Frame: false), session.PaneIdentity);
        var revision = session.HudRevision;

        Assert.True(store.UpdateHud(session.Id, new HudSpec("three", BackgroundColor: "#ff0000"), null));

        Assert.Equal(new HudSpec("three", BackgroundColor: "#2a1a3a", FontSize: 18), session.Hud);
        Assert.Null(session.HudPaneIdentity);
        Assert.True(session.HudRevision > revision);
        Assert.Equal(Plain("three") with { BackgroundColor = "#2a1a3a", FontSize = 18 }, Node(store, session).Hud);
        Assert.Equal(18, session.HudFontSize);
    }

    [Fact]
    public void TheHudFontIsFixedAtOpenFromTheSpecElseTheSession()
    {
        var (store, session) = NewSession();
        session.FontSize = 13;

        store.OpenHud(session.Id, new HudSpec("a"), null);
        Assert.Equal(13, session.HudFontSize);

        session.FontSize = 24;
        store.UpdateHud(session.Id, new HudSpec("b"), null);
        Assert.Equal(13, session.HudFontSize);
        Assert.Null(Node(store, session).Hud!.FontSize);

        store.OpenHud(session.Id, new HudSpec("c", FontSize: 30), null);
        Assert.Equal(30, session.HudFontSize);
    }

    [Fact]
    public void AnOpenWithNoKnownFontLeavesTheHudFontUnset()
    {
        var (store, session) = NewSession();

        store.OpenHud(session.Id, new HudSpec("a"), null);

        Assert.Null(session.HudFontSize);
    }

    [Fact]
    public void ASecondOpenReplacesTheFirstWholeAndDropsAForcedWidth()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("a", BackgroundColor: "#101820", FontSize: 12), session.PaneIdentity);
        store.ResizeHud(session.Id, 40);
        var next = new HudSpec("b", Position: HudPosition.BottomCenter, FontSize: 48);

        Assert.True(store.OpenHud(session.Id, next, null));

        Assert.Equal(next, session.Hud);
        Assert.Equal(48, session.HudFontSize);
        Assert.Null(session.HudPaneIdentity);
        Assert.Null(session.HudForcedWidthPercent);
        Assert.Equal(HudLayout.MaxSizePercent, Node(store, session).Hud!.SizePercent);
    }

    [Fact]
    public void AProgramOverlayRefusesEveryHudCommandAndKeepsRunning()
    {
        var (store, session) = NewSession();
        Assert.True(store.OpenOverlay(session.Id, "htop", sizePercent: 60));

        Assert.False(store.OpenHud(session.Id, new HudSpec("a", FontSize: 20), null));
        Assert.False(store.UpdateHud(session.Id, new HudSpec("a"), null));
        Assert.False(store.CloseHud(session.Id));
        Assert.False(store.ResizeHud(session.Id, 40));

        Assert.Null(session.Hud);
        Assert.Null(session.HudPanelSize);
        Assert.Null(session.HudFontSize);
        Assert.True(session.OverlayActive);
        Assert.Equal("htop", session.OverlayCommand);
        Assert.Equal(60, session.OverlaySizePercent);
    }

    [Fact]
    public void OpenOverlayReplacesAHudButAProgramStillOwnsTheSlot()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("one"), null);

        Assert.True(store.OpenOverlay(session.Id, "htop"));

        Assert.False(session.HudActive);
        Assert.True(session.FullOverlayActive);
        Assert.False(store.OpenOverlay(session.Id, "other"));
        Assert.False(store.OpenHud(session.Id, new HudSpec("x"), null));
        Assert.Equal("htop", session.OverlayCommand);
    }

    [Fact]
    public void AHudDropsTheExitCodeOfTheProgramBeforeIt()
    {
        var (store, session) = NewSession();
        store.OpenOverlay(session.Id, "true");
        store.RecordOverlayExit(session.Id, 3);
        store.CloseOverlay(session.Id);
        Assert.Equal(3, session.OverlayExitCode);

        store.OpenHud(session.Id, new HudSpec("working"), null);

        Assert.Null(session.OverlayExitCode);
    }

    [Fact]
    public void TheScratchAndATerminalQuestionDoNotBlockAHud()
    {
        var (store, session) = NewSession();
        store.ToggleScratch(session.Id);
        Assert.True(store.OpenAsk(session.Id, Ask(), null, Guid.NewGuid()));

        Assert.True(store.OpenHud(session.Id, new HudSpec("working"), null));

        Assert.True(session.ScratchActive);
        Assert.NotNull(session.AskPending);
    }

    [Fact]
    public void CloseClearsEveryFieldAndASecondCloseFails()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("one", FontSize: 13), session.PaneIdentity);
        store.ResizeHud(session.Id, 40);

        Assert.True(store.CloseHud(session.Id));

        Assert.False(session.HudActive);
        Assert.Null(session.Hud);
        Assert.Null(session.HudPaneIdentity);
        Assert.Null(session.HudPanelSize);
        Assert.Null(session.HudForcedWidthPercent);
        Assert.Null(session.HudFontSize);
        Assert.False(store.CloseHud(session.Id));
    }

    [Fact]
    public void AnUnknownSessionRefusesEveryHudCall()
    {
        var store = AppStoreFixtures.MakeStore();

        Assert.False(store.OpenHud(Guid.NewGuid(), new HudSpec("x"), null));
        Assert.False(store.UpdateHud(Guid.NewGuid(), new HudSpec("x"), null));
        Assert.False(store.CloseHud(Guid.NewGuid()));
        Assert.False(store.ResizeHud(Guid.NewGuid(), 40));
    }

    [Theory]
    [InlineData(1, false, 10)]
    [InlineData(35, false, 35)]
    [InlineData(100, false, 80)]
    [InlineData(100, true, 100)]
    public void TheWidthTakesTheHudClampOnOpenAndUpdate(int requested, bool sticky, int expected)
    {
        var (store, session) = NewSession();
        var spec = new HudSpec("caption", SizePercent: requested, Position: HudPosition.TopCenter, Sticky: sticky);

        store.OpenHud(session.Id, spec, null);
        Assert.Equal(expected, Node(store, session).Hud!.SizePercent);

        store.UpdateHud(session.Id, spec, null);
        Assert.Equal(expected, Node(store, session).Hud!.SizePercent);
    }

    [Fact]
    public void AStickyFullWidthHudFallsBackToTheCapWhenAnUpdateDropsSticky()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("caption", SizePercent: 100, Position: HudPosition.TopCenter, Sticky: true), null);

        store.UpdateHud(session.Id, new HudSpec("caption", SizePercent: 100, Position: HudPosition.TopCenter), null);

        Assert.Equal(HudLayout.MaxSizePercent, Node(store, session).Hud!.SizePercent);
    }

    [Fact]
    public void AResizeForcesTheWidthUntilTheNextUpdateAndKeepsTheRevision()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("working"), null);
        var revision = session.HudRevision;

        Assert.True(store.ResizeHud(session.Id, 100));
        Assert.Equal(HudLayout.MaxSizePercent, session.HudForcedWidthPercent);
        Assert.True(store.ResizeHud(session.Id, 35));

        Assert.Equal(35, session.HudForcedWidthPercent);
        Assert.Equal(35, Node(store, session).Hud!.SizePercent);
        Assert.Equal(10, Node(store, session).Hud!.HeightPercent);
        Assert.Null(session.Hud!.SizePercent);
        Assert.Equal(revision, session.HudRevision);

        store.UpdateHud(session.Id, new HudSpec("done"), null);

        Assert.Null(session.HudForcedWidthPercent);
        Assert.Equal(HudLayout.MaxSizePercent, Node(store, session).Hud!.SizePercent);
    }

    [Fact]
    public void HudChangedFollowsEveryChangeAndNoRefusal()
    {
        var (store, session) = NewSession();
        var changes = 0;
        store.HudChanged += () => changes++;

        Assert.False(store.UpdateHud(session.Id, new HudSpec("x"), null));
        Assert.False(store.CloseHud(session.Id));
        Assert.Equal(0, changes);

        store.OpenHud(session.Id, new HudSpec("one"), null);
        var opened = session.HudRevision;
        store.UpdateHud(session.Id, new HudSpec("two"), null);
        var updated = session.HudRevision;
        store.ResizeHud(session.Id, 40);
        store.CloseHud(session.Id);

        Assert.Equal(4, changes);
        Assert.True(updated > opened);
        Assert.True(session.HudRevision > updated);
    }

    [Fact]
    public void ThePaneReadBackFollowsTheIdentityThroughPromotion()
    {
        var (store, session) = SplitSession();
        store.OpenHud(session.Id, new HudSpec("working"), session.SplitPaneIdentity);
        Assert.Equal("right", Node(store, session).Hud!.Pane);

        store.ClosePrimaryPane(session.Id);

        Assert.Equal("left", Node(store, session).Hud!.Pane);
    }

    [Fact]
    public void ClosingTheSplitClosesItsHudAndKeepsALeftOne()
    {
        var (store, session) = SplitSession();
        store.OpenHud(session.Id, new HudSpec("split"), session.SplitPaneIdentity);

        store.CloseSplit(session.Id);
        Assert.False(session.HudActive);
        Assert.Null(Node(store, session).Hud);

        store.ToggleSplit(session.Id);
        store.OpenHud(session.Id, new HudSpec("primary"), session.PaneIdentity);
        store.CloseSplit(session.Id);

        Assert.True(session.HudActive);
        Assert.Equal(OverlayPane.Left, session.HudTargetPane);
    }

    [Fact]
    public void PrimaryExitClosesItsHudAndKeepsASurvivorHud()
    {
        var (store, session) = SplitSession();
        store.OpenHud(session.Id, new HudSpec("primary"), session.PaneIdentity);
        store.ClosePrimaryPane(session.Id);
        Assert.False(session.HudActive);

        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("next-token");
        var survivor = session.SplitPaneIdentity!.Value;
        store.OpenHud(session.Id, new HudSpec("survivor"), survivor);
        store.ClosePrimaryPane(session.Id);

        Assert.True(session.HudActive);
        Assert.Equal(survivor, session.HudPaneIdentity);
        Assert.Equal(OverlayPane.Left, session.HudTargetPane);
        Assert.Equal("left", Node(store, session).Hud!.Pane);
    }

    [Theory]
    [InlineData(OverlayPane.Left)]
    [InlineData(OverlayPane.Right)]
    public void ASessionWideHudSurvivesEitherPaneClosing(OverlayPane closing)
    {
        var (store, session) = SplitSession();
        store.OpenHud(session.Id, new HudSpec("working"), null);

        if (closing == OverlayPane.Left) store.ClosePrimaryPane(session.Id);
        else store.CloseSplit(session.Id);

        Assert.True(session.HudActive);
        Assert.Null(session.HudPaneIdentity);
    }

    [Fact]
    public void ClosingAnAbsentSplitKeepsASessionWideHud()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("working"), null);

        store.CloseSplit(session.Id);

        Assert.True(session.HudActive);
    }

    [Fact]
    public void HidingTheTargetPaneKeepsTheHudForItsReturn()
    {
        var (store, session) = SplitSession();
        store.OpenHud(session.Id, new HudSpec("working"), session.SplitPaneIdentity);
        session.SplitFocused = false;

        store.ToggleSplit(session.Id);

        Assert.False(session.RendersPane(OverlayPane.Right));
        Assert.True(session.HudActive);
        Assert.Equal("right", Node(store, session).Hud!.Pane);

        store.ToggleSplit(session.Id);

        Assert.True(session.RendersPane(OverlayPane.Right));
        Assert.Equal(OverlayPane.Right, session.HudTargetPane);
    }

    // review focus 3: the host resolves `--pane right` with requireVisible false while the split is
    // hidden, so the store takes the hidden pane's identity and the HUD returns with the split
    [Fact]
    public void AnUpdateWhileTheSplitIsHiddenKeepsTheRightPaneAndReturnsWithIt()
    {
        var (store, session) = SplitSession();
        store.OpenHud(session.Id, new HudSpec("building"), session.SplitPaneIdentity);
        session.SplitFocused = false;
        store.ToggleSplit(session.Id);

        Assert.True(store.UpdateHud(session.Id, new HudSpec("still building"), session.SplitPaneIdentity));

        Assert.False(session.RendersPane(OverlayPane.Right));
        Assert.Equal("still building", session.Hud!.Message);
        Assert.Equal("right", Node(store, session).Hud!.Pane);

        store.ToggleSplit(session.Id);

        Assert.Equal(OverlayPane.Right, session.HudTargetPane);
        Assert.Equal("still building", Node(store, session).Hud!.Message);
    }

    [Fact]
    public void ClosingTheSessionClosesItsHud()
    {
        var (store, session) = NewSession();
        store.AddSession(store.Workspaces[0].Id, "/other");
        store.OpenHud(session.Id, new HudSpec("working"), null);

        store.CloseSession(session.Id);

        Assert.False(session.HudActive);
    }

    [Fact]
    public void RemovingTheWorkspaceClosesItsHuds()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddSession(store.AddWorkspace("keep").Id, "/k");
        var doomed = store.AddWorkspace("doomed");
        var first = store.AddSession(doomed.Id, "/a")!;
        var second = store.AddSession(doomed.Id, "/b")!;
        store.OpenHud(first.Id, new HudSpec("one"), null);
        store.OpenHud(second.Id, new HudSpec("two", HideAfter: 30), null);

        store.RemoveWorkspace(doomed.Id);

        Assert.False(first.HudActive);
        Assert.False(second.HudActive);
    }

    [Fact]
    public void CloseAllHudsClosesEverySessionsHud()
    {
        var (store, first) = NewSession();
        var second = store.AddSession(store.Workspaces[0].Id, "/other")!;
        store.OpenHud(first.Id, new HudSpec("one"), null);
        store.OpenHud(second.Id, new HudSpec("two"), null);

        store.CloseAllHuds();

        Assert.False(first.HudActive);
        Assert.False(second.HudActive);
    }

    [Fact]
    public void ASoftCloseTakesDownAHudThatWasCountingItselfOut()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("deploying", HideAfter: 10), null);

        Assert.True(store.SoftCloseSession(session.Id, 60));
        Assert.False(session.HudActive);

        Assert.True(store.UndoPendingClose());
        Assert.False(session.HudActive);
    }

    [Fact]
    public void ASoftCloseKeepsAnUntimedHudAndUndoBringsItBack()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("waiting"), session.PaneIdentity);

        Assert.True(store.SoftCloseSession(session.Id, 60));
        Assert.True(session.HudActive);

        Assert.True(store.UndoPendingClose());
        Assert.Same(session, store.SessionWithId(session.Id));
        Assert.Equal(Plain("waiting") with { Pane = "left" }, Node(store, session).Hud);
    }

    [Fact]
    public void FinalizingASoftCloseClosesTheUntimedHud()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("waiting"), null);
        Assert.True(store.SoftCloseSession(session.Id, 60));

        store.FinalizePendingClose(store.PendingCloseSummary!.Id);

        Assert.False(session.HudActive);
    }

    [Fact]
    public void ABatchSoftCloseClosesTimedHudsAndKeepsUntimedOnesUntilFinal()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var timed = store.AddSession(workspace.Id, "/a")!;
        var untimed = store.AddSession(workspace.Id, "/b")!;
        store.AddSession(workspace.Id, "/c");
        store.OpenHud(timed.Id, new HudSpec("deploying", HideAfter: 10), null);
        store.OpenHud(untimed.Id, new HudSpec("waiting"), null);

        Assert.True(store.SoftCloseSessions(new[] { timed.Id, untimed.Id }, 60));
        Assert.False(timed.HudActive);
        Assert.True(untimed.HudActive);

        Assert.True(store.UndoPendingClose());
        Assert.NotNull(Node(store, untimed).Hud);

        Assert.True(store.SoftCloseSessions(new[] { timed.Id, untimed.Id }, 60));
        store.FinalizeAllPendingCloses();
        Assert.False(untimed.HudActive);
    }

    [Fact]
    public void ASoftWorkspaceRemovalClosesOnlyTimedHudsUntilFinal()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddSession(store.AddWorkspace("keep").Id, "/k");
        var doomed = store.AddWorkspace("doomed");
        var timed = store.AddSession(doomed.Id, "/a")!;
        var untimed = store.AddSession(doomed.Id, "/b")!;
        store.OpenHud(timed.Id, new HudSpec("deploying", HideAfter: 10), null);
        store.OpenHud(untimed.Id, new HudSpec("waiting"), null);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.False(timed.HudActive);
        Assert.True(untimed.HudActive);

        Assert.True(store.UndoPendingClose());
        Assert.True(untimed.HudActive);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);
        Assert.False(untimed.HudActive);
    }

    [Fact]
    public void AFoldedWorkspaceCloseKeepsAnUntimedHudUntilItIsFinal()
    {
        var store = AppStoreFixtures.MakeStore();
        var doomed = store.AddWorkspace("doomed");
        store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a")!;
        var second = store.AddSession(doomed.Id, "/b")!;
        store.OpenHud(second.Id, new HudSpec("waiting"), null);

        // second rides the first workspace close; undoing first rebuilds the workspace as a shell,
        // and closing that shell folds the earlier record into the new one
        Assert.True(store.SoftCloseSession(first.Id, 60));
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.True(store.UndoPendingClose(sessionClose));
        store.OpenHud(first.Id, new HudSpec("deploying", HideAfter: 10), null);
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        var folded = store.PendingCloseSummary!.Id;

        Assert.False(first.HudActive);
        Assert.True(second.HudActive);

        store.FinalizePendingClose(folded);

        Assert.False(second.HudActive);
    }
}
