using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore AppStorePaneTests.swift ask cases and SessionTests.swift ask cases (upstream ed831ab);
// the close paths are the port's, since upstream cancels session asks from its app layer.
public class SessionAskTests
{
    private static readonly Guid WindowId = Guid.NewGuid();

    private static PendingAsk Ask(string? id = null) =>
        new(id ?? Guid.NewGuid().ToString("D").ToUpperInvariant(), "Continue?", null,
            new[] { new ControlAskButton { Id = "yes", Label = "Yes" } }, null, null,
            ControlAskStyle.Terminal, ControlAskAlignment.Right, null);

    private static (AppStore Store, Session Session) SplitSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace.Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("right-token");
        return (store, session);
    }

    [Fact]
    public void SplitIdentityExistsWhileTheSessionHasASplitAndSurvivesHiding()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/a")!;
        Assert.Null(session.SplitPaneIdentity);

        store.ToggleSplit(session.Id);
        var identity = session.SplitPaneIdentity;
        Assert.NotNull(identity);
        Assert.NotEqual(session.PaneIdentity, identity);

        store.ToggleSplit(session.Id);
        Assert.Equal(identity, session.SplitPaneIdentity);
        Assert.Equal(OverlayPane.Right, session.PaneRole(identity!.Value));
        Assert.Equal(OverlayPane.Left, session.PaneRole(session.PaneIdentity));
        Assert.Null(session.PaneRole(Guid.NewGuid()));
    }

    [Fact]
    public void ClosingTheSplitForgetsItsIdentity()
    {
        var (store, session) = SplitSession();
        var first = session.SplitPaneIdentity;

        store.CloseSplit(session.Id);
        Assert.Null(session.SplitPaneIdentity);

        store.ToggleSplit(session.Id);
        Assert.NotEqual(first, session.SplitPaneIdentity);
    }

    [Fact]
    public void PromotionMovesTheSplitIdentityToTheLeftSlot()
    {
        var (store, session) = SplitSession();
        var split = session.SplitPaneIdentity!.Value;

        store.ClosePrimaryPane(session.Id);

        Assert.Equal(split, session.PaneIdentity);
        Assert.Null(session.SplitPaneIdentity);
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, true)]
    public void RendersPaneFollowsTheShownSplitOrTheFocusedPane(bool isSplit, bool splitFocused, bool left, bool right)
    {
        var session = new Session("/a") { IsSplit = isSplit, SplitFocused = splitFocused };
        Assert.Equal(left, session.RendersPane(OverlayPane.Left));
        Assert.Equal(right, session.RendersPane(OverlayPane.Right));
    }

    [Fact]
    public void OpenRegistersTheSessionAsOwnerAndRefusesASecondQuestion()
    {
        var (store, session) = SplitSession();
        var ask = Ask();

        Assert.True(store.OpenAsk(session.Id, ask, session.SplitPaneIdentity, WindowId));

        Assert.Same(ask, session.AskPending);
        Assert.Equal(OverlayPane.Right, session.AskTargetPane);
        Assert.Equal(new AskOwner.Session(session.Id, WindowId), store.Asks.Owner(ask.Id));
        Assert.False(store.OpenAsk(session.Id, Ask(), null, WindowId));
        Assert.False(store.OpenAsk(Guid.NewGuid(), Ask(), null, WindowId));
    }

    [Fact]
    public void ResolveRetainsTheOutcomeAndIgnoresStaleIdsAndPendingOutcomes()
    {
        var (store, session) = SplitSession();
        var ask = Ask();
        store.OpenAsk(session.Id, ask, null, WindowId);

        Assert.False(store.ResolveAsk(session.Id, "stale", ControlAskResult.Escaped()));
        Assert.False(store.ResolveAsk(session.Id, ask.Id, ControlAskResult.Pending()));
        Assert.True(store.ResolveAsk(session.Id, ask.Id, ControlAskResult.Answered("yes", "Yes", 0)));

        Assert.Null(session.AskPending);
        Assert.Null(session.AskPaneIdentity);
        Assert.Equal(new AskLookup(ControlAskResult.Answered("yes", "Yes", 0), WindowId, null), store.Asks.Find(ask.Id));
        Assert.False(store.CancelAsk(session.Id, ask.Id));
    }

    [Theory]
    [InlineData(OverlayPane.Left, "session")]
    [InlineData(OverlayPane.Left, "left")]
    [InlineData(OverlayPane.Left, "right")]
    [InlineData(OverlayPane.Right, "session")]
    [InlineData(OverlayPane.Right, "left")]
    [InlineData(OverlayPane.Right, "right")]
    public void APaneCloseCancelsOnlyItsOwnAnchoredQuestion(OverlayPane closing, string scope)
    {
        var (store, session) = SplitSession();
        Guid? identity = scope switch
        {
            "left" => session.PaneIdentity,
            "right" => session.SplitPaneIdentity,
            _ => null,
        };
        var ask = Ask();
        store.OpenAsk(session.Id, ask, identity, WindowId);

        if (closing == OverlayPane.Left) store.ClosePrimaryPane(session.Id);
        else store.CloseSplit(session.Id);

        Assert.Same(session, store.SessionWithId(session.Id));
        if (scope == OverlayPanes.Wire(closing))
        {
            Assert.Null(session.AskPending);
            Assert.Equal(new AskLookup(ControlAskResult.Cancelled(), WindowId, null), store.Asks.Find(ask.Id));
        }
        else
        {
            Assert.Same(ask, session.AskPending);
            Assert.Equal(identity, session.AskPaneIdentity);
            Assert.Equal(scope == "session" ? null : OverlayPane.Left, session.AskTargetPane);
        }
    }

    [Fact]
    public void TheLastPrimaryPaneClosingCancelsTheSessionQuestion()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        var ask = Ask();
        store.OpenAsk(session.Id, ask, null, WindowId);

        store.ClosePrimaryPane(session.Id);

        Assert.Null(store.SessionWithId(session.Id));
        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(ask.Id)!.Result);
    }

    [Theory]
    [InlineData(OverlayPane.Left)]
    [InlineData(OverlayPane.Right)]
    public void CollapsingTheSplitKeepsTheQuestionWhetherOrNotItsPaneStaysVisible(OverlayPane focused)
    {
        var (store, session) = SplitSession();
        var identity = session.SplitPaneIdentity!.Value;
        var ask = Ask();
        store.OpenAsk(session.Id, ask, identity, WindowId);
        session.SplitFocused = focused == OverlayPane.Right;

        store.ToggleSplit(session.Id);

        Assert.Equal(focused == OverlayPane.Right, session.RendersPane(OverlayPane.Right));
        Assert.Same(ask, session.AskPending);
        store.ToggleSplit(session.Id);
        Assert.True(session.RendersPane(OverlayPane.Right));
        Assert.Equal(OverlayPane.Right, session.AskTargetPane);
    }

    [Fact]
    public void ClosingTheSessionCancelsItsQuestion()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace.Id, "/tmp")!;
        store.AddSession(workspace.Id, "/tmp");
        var ask = Ask();
        store.OpenAsk(session.Id, ask, null, WindowId);

        store.CloseSession(session.Id);

        Assert.Null(session.AskPending);
        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(ask.Id)!.Result);
    }

    [Fact]
    public void RemovingTheWorkspaceCancelsItsQuestions()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddSession(store.AddWorkspace("keep").Id, "/tmp");
        var doomed = store.AddWorkspace("doomed");
        var session = store.AddSession(doomed.Id, "/tmp")!;
        var ask = Ask();
        store.OpenAsk(session.Id, ask, null, WindowId);

        store.RemoveWorkspace(doomed.Id);

        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(ask.Id)!.Result);
    }

    private static AppStore StoreSeeing(Action<ControlEventDraft> sink)
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("D"));
        return new AppStore(persistence: new PersistenceStore(dir), controlEventSink: sink, stateDirectory: dir);
    }

    [Fact]
    public void ASoftCloseCancelsTheQuestionBeforeAnnouncingTheClose()
    {
        Session? session = null;
        var pendingWhenAnnounced = new List<PendingAsk?>();
        var store = StoreSeeing(draft =>
        {
            if (draft.Kind == ControlEventKind.SessionClosed) pendingWhenAnnounced.Add(session!.AskPending);
        });
        var workspace = store.AddWorkspace("work");
        session = store.AddSession(workspace.Id, "/tmp")!;
        store.AddSession(workspace.Id, "/tmp");
        store.OpenAsk(session.Id, Ask(), null, WindowId);

        Assert.True(store.SoftCloseSession(session.Id, 60));

        Assert.Equal(new PendingAsk?[] { null }, pendingWhenAnnounced);
    }

    [Fact]
    public void ASoftWorkspaceRemovalCancelsTheQuestionBeforeAnnouncingTheClose()
    {
        Session? session = null;
        var pendingWhenAnnounced = new List<PendingAsk?>();
        var store = StoreSeeing(draft =>
        {
            if (draft.Kind == ControlEventKind.SessionClosed) pendingWhenAnnounced.Add(session!.AskPending);
        });
        store.AddSession(store.AddWorkspace("keep").Id, "/tmp");
        var doomed = store.AddWorkspace("doomed");
        session = store.AddSession(doomed.Id, "/tmp")!;
        store.OpenAsk(session.Id, Ask(), null, WindowId);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));

        Assert.Equal(new PendingAsk?[] { null }, pendingWhenAnnounced);
    }

    [Fact]
    public void ASoftCloseCancelsTheQuestionAndUndoRestoresTheSessionWithoutIt()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace.Id, "/tmp")!;
        store.AddSession(workspace.Id, "/tmp");
        var ask = Ask();
        store.OpenAsk(session.Id, ask, null, WindowId);

        Assert.True(store.SoftCloseSession(session.Id, 60));
        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(ask.Id)!.Result);

        Assert.True(store.UndoPendingClose());
        Assert.Same(session, store.SessionWithId(session.Id));
        Assert.Null(session.AskPending);
    }

    [Fact]
    public void ABatchSoftCloseCancelsEveryQuestion()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var first = store.AddSession(workspace.Id, "/tmp")!;
        var second = store.AddSession(workspace.Id, "/tmp")!;
        store.AddSession(workspace.Id, "/tmp");
        var a = Ask();
        var b = Ask();
        store.OpenAsk(first.Id, a, null, WindowId);
        store.OpenAsk(second.Id, b, null, WindowId);

        Assert.True(store.SoftCloseSessions(new[] { first.Id, second.Id }, 60));

        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(a.Id)!.Result);
        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(b.Id)!.Result);
    }

    [Fact]
    public void ASoftWorkspaceRemovalCancelsItsQuestions()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddSession(store.AddWorkspace("keep").Id, "/tmp");
        var doomed = store.AddWorkspace("doomed");
        var session = store.AddSession(doomed.Id, "/tmp")!;
        var ask = Ask();
        store.OpenAsk(session.Id, ask, null, WindowId);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));

        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(ask.Id)!.Result);
    }

    [Fact]
    public void CancelAllAsksCancelsEverySessionQuestion()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("work");
        var first = store.AddSession(workspace.Id, "/tmp")!;
        var second = store.AddSession(workspace.Id, "/tmp")!;
        var a = Ask();
        var b = Ask();
        store.OpenAsk(first.Id, a, null, WindowId);
        store.OpenAsk(second.Id, b, null, WindowId);

        store.CancelAllAsks();

        Assert.Null(first.AskPending);
        Assert.Null(second.AskPending);
        Assert.Equal(ControlAskResult.Cancelled(), store.Asks.Find(b.Id)!.Result);
    }

    [Fact]
    public void TheTreeProjectsTheSessionQuestionWithItsCurrentRole()
    {
        var (store, session) = SplitSession();
        var ask = Ask("ASK");
        store.OpenAsk(session.Id, ask, session.SplitPaneIdentity, WindowId);

        var node = store.ControlTree(askPending: () => "GUI").Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == session.Id.ToString("D").ToUpperInvariant());
        Assert.Equal(new ControlSessionAsk { Id = "ASK", Pane = "right" }, node.Ask);
        Assert.Equal("GUI", store.ControlTree(askPending: () => "GUI").AskPending);

        store.ClosePrimaryPane(session.Id);
        node = store.ControlTree().Workspaces.SelectMany(w => w.Sessions).Single();
        Assert.Equal(new ControlSessionAsk { Id = "ASK", Pane = "left" }, node.Ask);
    }

    [Fact]
    public void ASessionWideQuestionHasNoPaneInTheTree()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        store.OpenAsk(session.Id, Ask("ASK"), null, WindowId);

        Assert.Equal(new ControlSessionAsk { Id = "ASK" }, store.ControlTree().Workspaces.Single().Sessions.Single().Ask);
        Assert.Null(store.ControlTree().AskPending);
    }
}
