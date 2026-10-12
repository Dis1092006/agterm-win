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

    [Fact]
    public void ZoomAndTheDashboardHideEveryQuestion()
    {
        var (store, session) = Selected();
        store.OpenAsk(session.Id, Ask(), null, WindowId);

        Assert.Null(TerminalAskVisibility.For(store, zoomActive: true, dashboardOpen: false, quickTerminalVisible: false));
        Assert.Null(TerminalAskVisibility.For(store, zoomActive: false, dashboardOpen: true, quickTerminalVisible: false));
    }

    // spec 5: the quick terminal takes input before a terminal question, so the question waits
    [Fact]
    public void TheQuickTerminalHidesEveryQuestion()
    {
        var (store, session) = Selected();
        store.OpenAsk(session.Id, Ask(), null, WindowId);
        Assert.Null(TerminalAskVisibility.For(store, false, false, quickTerminalVisible: true));

        store.ResolveAsk(session.Id, "ASK", ControlAskResult.Escaped());
        Assert.True(store.OpenAsk(session.Id, Ask() with { Id = "NEXT" }, session.SplitPaneIdentity, WindowId));
        Assert.Null(TerminalAskVisibility.For(store, false, false, quickTerminalVisible: true));
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
