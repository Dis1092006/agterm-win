using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// The visibility rules of upstream SessionAskInput.visible (upstream ed831ab, AskDialogView.swift).
public class TerminalAskVisibilityTests
{
    private static readonly Guid WindowId = Guid.NewGuid();

    private static PendingAsk Ask() =>
        new("ASK", "Continue?", null, new[] { new ControlAskButton { Id = "yes", Label = "Yes" } },
            null, null, ControlAskStyle.Terminal, ControlAskAlignment.Right, null);

    private static (AppStore Store, Session Session) Selected()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("right-token");
        store.SelectSession(session.Id);
        return (store, session);
    }

    [Fact]
    public void ASessionWideQuestionOfTheSelectedSessionIsShown()
    {
        var (store, session) = Selected();
        var ask = Ask();
        store.OpenAsk(session.Id, ask, null, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal(session.Id, placement.SessionId);
        Assert.Same(ask, placement.Ask);
        Assert.Null(placement.Pane);
        Assert.Null(placement.PaneId);
        Assert.Equal(new[] { "left-token", "right-token" }, placement.CoveredPaneIds);
        Assert.Equal(placement, TerminalAskVisibility.For(store, false, false, false));
    }

    [Fact]
    public void APaneQuestionCarriesThePagePaneId()
    {
        var (store, session) = Selected();
        var ask = Ask();
        store.OpenAsk(session.Id, ask, session.SplitPaneIdentity, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal(OverlayPane.Right, placement.Pane);
        Assert.Equal("right-token", placement.PaneId);
        Assert.Equal(new[] { "right-token" }, placement.CoveredPaneIds);
    }

    // a dialog shown again, after a session switch, starts from what the user typed or checked before
    [Fact]
    public void APlacementCarriesACopyOfTheSessionDraftWithoutComparingIt()
    {
        var (store, session) = Selected();
        var ask = Ask() with { Form = new AskForm.Input(new AskInputForm(ControlAskInputMode.Line, "prefill")) };
        store.OpenAsk(session.Id, ask, null, WindowId);
        var before = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal("prefill", before.Draft!.Text);

        store.UpdateAskDraft(session.Id, "ASK", draft => draft.Text = "typed");

        var after = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal("typed", after.Draft!.Text);
        Assert.Equal("prefill", before.Draft.Text);
        Assert.NotSame(session.AskDraft, after.Draft);
        Assert.Equal(before, after);
    }

    [Fact]
    public void APlacementCarriesTheSessionSelection()
    {
        var (store, session) = Selected();
        var items = new[] { new ControlAskChoice { Id = "a", Label = "A" }, new ControlAskChoice { Id = "b", Label = "B" } };
        store.OpenAsk(session.Id, Ask() with { Form = new AskForm.Choices(new AskChoiceForm(items, Multiple: true)) }, null, WindowId);

        store.UpdateAskDraft(session.Id, "ASK", draft => draft.Toggle(1));

        var draft = TerminalAskVisibility.For(store, false, false, false)!.Draft!;
        Assert.Equal(["b"], draft.Selected);
        Assert.Equal(1, draft.Cursor);
    }

    private static void OpenOverlay(Session session)
    {
        session.OverlaySurface = new SpySurface("overlay-token");
        session.OverlayActive = true;
    }

    // upstream wantsFocus: a pane question on the session's focused side takes the overlay's focus
    [Fact]
    public void APaneQuestionOnTheFocusedSideAlsoCoversTheOverlay()
    {
        var (store, session) = Selected();
        OpenOverlay(session);
        session.SplitFocused = false;
        store.OpenAsk(session.Id, Ask(), session.PaneIdentity, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal("left-token", placement.PaneId);
        Assert.Equal(new[] { "left-token", "overlay-token" }, placement.CoveredPaneIds);
    }

    [Fact]
    public void APaneQuestionOnTheUnfocusedSideLeavesTheOverlayUncovered()
    {
        var (store, session) = Selected();
        OpenOverlay(session);
        session.SplitFocused = true;
        store.OpenAsk(session.Id, Ask(), session.PaneIdentity, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal(new[] { "left-token" }, placement.CoveredPaneIds);
    }

    [Theory]
    [InlineData(true, new[] { "right-token", "overlay-token" })]
    [InlineData(false, new[] { "right-token" })]
    public void ARightPaneQuestionCoversTheOverlayOnlyWhileTheRightSideIsFocused(bool splitFocused, string[] covered)
    {
        var (store, session) = Selected();
        OpenOverlay(session);
        session.SplitFocused = splitFocused;
        store.OpenAsk(session.Id, Ask(), session.SplitPaneIdentity, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal(covered, placement.CoveredPaneIds);
    }

    [Fact]
    public void APaneQuestionOnTheOnlyShownPaneCoversTheOverlay()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.SelectSession(session.Id);
        OpenOverlay(session);
        store.OpenAsk(session.Id, Ask(), session.PaneIdentity, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal(new[] { "left-token", "overlay-token" }, placement.CoveredPaneIds);
    }

    [Fact]
    public void APaneQuestionWithoutAShownOverlayCoversOnlyItsPane()
    {
        var (store, session) = Selected();
        OpenOverlay(session);
        session.OverlayActive = false;
        session.SplitFocused = false;
        store.OpenAsk(session.Id, Ask(), session.PaneIdentity, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal(new[] { "left-token" }, placement.CoveredPaneIds);
    }

    // upstream 7f450d17: focus asks the visible predicates, and a hidden overlay holds no focus to defer
    [Fact]
    public void APaneQuestionLeavesAHiddenOverlayUncovered()
    {
        var (store, session) = Selected();
        OpenOverlay(session);
        session.OverlayHidden = true;
        session.SplitFocused = false;
        store.OpenAsk(session.Id, Ask(), session.PaneIdentity, WindowId);

        var placement = TerminalAskVisibility.For(store, false, false, false)!;
        Assert.Equal(new[] { "left-token" }, placement.CoveredPaneIds);
    }

    [Fact]
    public void ZoomAndTheDashboardHideEveryQuestion()
    {
        var (store, session) = Selected();
        store.OpenAsk(session.Id, Ask(), null, WindowId);

        Assert.Null(TerminalAskVisibility.For(store, zoomActive: true, dashboardOpen: false, quickTerminalVisible: false));
        Assert.Null(TerminalAskVisibility.For(store, zoomActive: false, dashboardOpen: true, quickTerminalVisible: false));
    }

    [Fact]
    public void TheDetachedQuickTerminalLeavesQuestionsRendered()
    {
        var (store, session) = Selected();
        store.OpenAsk(session.Id, Ask(), null, WindowId);
        Assert.NotNull(TerminalAskVisibility.For(store, false, false, quickTerminalVisible: true));

        store.ResolveAsk(session.Id, "ASK", ControlAskResult.Escaped());
        Assert.True(store.OpenAsk(session.Id, Ask() with { Id = "NEXT" }, session.SplitPaneIdentity, WindowId));
        Assert.NotNull(TerminalAskVisibility.For(store, false, false, quickTerminalVisible: true));
        Assert.NotNull(TerminalAskVisibility.For(store, false, false, quickTerminalVisible: false));
    }

    [Fact]
    public void AnUnselectedSessionsQuestionIsHidden()
    {
        var (store, session) = Selected();
        var other = store.AddSession(store.Workspaces[0].Id, "/tmp")!;
        store.OpenAsk(session.Id, Ask(), null, WindowId);

        store.SelectSession(other.Id);

        Assert.Null(TerminalAskVisibility.For(store, false, false, false));
    }

    [Fact]
    public void APaneQuestionHidesWhenItsPaneIsNotLaidOutOrTheScratchCoversIt()
    {
        var (store, session) = Selected();
        store.OpenAsk(session.Id, Ask(), session.SplitPaneIdentity, WindowId);
        session.SplitFocused = false;
        store.ToggleSplit(session.Id);
        Assert.Null(TerminalAskVisibility.For(store, false, false, false));

        store.ToggleSplit(session.Id);
        session.ScratchActive = true;
        Assert.Null(TerminalAskVisibility.For(store, false, false, false));
    }

    [Fact]
    public void ASessionWideQuestionDrawsAboveTheScratch()
    {
        var (store, session) = Selected();
        store.OpenAsk(session.Id, Ask(), null, WindowId);
        session.ScratchActive = true;

        Assert.NotNull(TerminalAskVisibility.For(store, false, false, false));
    }

    [Fact]
    public void APaneWithoutASurfaceShowsNothing()
    {
        var (store, session) = Selected();
        store.OpenAsk(session.Id, Ask(), session.PaneIdentity, WindowId);
        session.Surface = null;

        Assert.Null(TerminalAskVisibility.For(store, false, false, false));
    }

    [Fact]
    public void OpeningAndResolvingRaiseAsksChanged()
    {
        var (store, session) = Selected();
        var raised = 0;
        store.AsksChanged += () => raised++;

        store.OpenAsk(session.Id, Ask(), null, WindowId);
        store.ResolveAsk(session.Id, "ASK", ControlAskResult.Escaped());
        store.OpenAsk(session.Id, Ask() with { Id = "NEXT" }, null, WindowId);
        store.CloseSession(session.Id);

        Assert.Equal(4, raised);
    }
}
