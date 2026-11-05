using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported from agtermCore AppStoreMessagesTests.swift (upstream b0839692, #847). A mailbox is keyed by
/// the surface's pane token (AGTERM_PANE_ID), which the port keeps apart from the session's pane
/// identity, so the panes here are the tokens of the surfaces the fixture mounts.</summary>
public class AppStoreMessagesTests
{
    private sealed record Fixture(AppStore Store, Workspace Work, Session Session, MessageBus Bus);

    private sealed class LazySurface(string token) : ITerminalSurface, IRealizableSurface
    {
        public string? PaneToken => token;
        public bool IsRealized { get; set; } = true;
        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
    }

    private static string Token() => Guid.NewGuid().ToString("N");

    private static Fixture MakeFixture(bool split = false, bool scratch = false, List<ControlEventDraft>? drafts = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("D"));
        var store = new AppStore(persistence: new PersistenceStore(dir), stateDirectory: dir,
            controlEventSink: drafts is null ? null : drafts.Add);
        var bus = new MessageBus();
        store.MessageBus = bus;
        var work = store.AddWorkspace("work");
        store.AddWorkspace("other");
        var session = store.AddSession(work.Id, "/w")!;
        session.Surface = new SpySurface(Token());
        if (split)
        {
            store.SetSplitVisibility(session.Id, shown: true);
            session.SplitSurface = new SpySurface(Token());
        }
        if (scratch) session.ScratchSurface = new SpySurface(Token());
        return new Fixture(store, work, session, bus);
    }

    private static Guid Pane(ITerminalSurface? surface) => Guid.Parse(surface!.PaneToken!);

    private static Guid[] Panes(Session session) =>
        new[] { session.Surface, session.SplitSurface, session.ScratchSurface }.Where(s => s is not null).Select(Pane).ToArray();

    private static void Deliver(MessageBus bus, params Guid[] panes) =>
        bus.Send("hi", null, panes.Select(p => new MessageRecipient(p, $"S:{p:N}")).ToArray(), now: 1);

    private static int[] Pending(MessageBus bus, params Guid[] panes) => panes.Select(bus.Pending).ToArray();

    [Fact]
    public void HardCloseDisposesEveryPaneOfTheSession()
    {
        var f = MakeFixture(split: true, scratch: true);
        var panes = Panes(f.Session);
        Deliver(f.Bus, panes);

        f.Store.CloseSession(f.Session.Id);

        Assert.Equal(3, panes.Length);
        Assert.Equal([0, 0, 0], Pending(f.Bus, panes));
    }

    // mail sent before a soft close survived an undo with no bus operation between them
    [Fact]
    public void SoftCloseDisposesAndUndoRestoresAnEmptyMailbox()
    {
        var f = MakeFixture(split: true);
        var panes = Panes(f.Session);
        Deliver(f.Bus, panes);

        Assert.True(f.Store.SoftCloseSession(f.Session.Id));
        Assert.True(f.Store.UndoPendingClose());

        Assert.Equal([0, 0], Pending(f.Bus, panes));
    }

    [Fact]
    public void ABatchSoftCloseDisposesEverySession()
    {
        var f = MakeFixture(split: true);
        var other = f.Store.AddSession(f.Work.Id, "/o")!;
        other.Surface = new SpySurface(Token());
        var panes = Panes(f.Session).Concat(Panes(other)).ToArray();
        Deliver(f.Bus, panes);

        Assert.True(f.Store.SoftCloseSessions([f.Session.Id, other.Id]));

        Assert.Equal([0, 0, 0], Pending(f.Bus, panes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkspaceDeleteDisposesItsSessions(bool soft)
    {
        var f = MakeFixture(split: true);
        var panes = Panes(f.Session);
        Deliver(f.Bus, panes);

        if (soft) Assert.True(f.Store.SoftRemoveWorkspace(f.Work.Id));
        else f.Store.RemoveWorkspace(f.Work.Id);

        Assert.Equal([0, 0], Pending(f.Bus, panes));
    }

    [Fact]
    public void SplitCloseDisposesOnlyTheSplit()
    {
        var f = MakeFixture(split: true);
        var (main, split) = (Pane(f.Session.Surface), Pane(f.Session.SplitSurface));
        Deliver(f.Bus, main, split);

        f.Store.CloseSplit(f.Session.Id);

        Assert.Equal([1, 0], Pending(f.Bus, main, split));
    }

    [Fact]
    public void ClosingThePrimaryKeepsThePromotedSurvivorsMail()
    {
        var f = MakeFixture(split: true);
        var (main, split) = (Pane(f.Session.Surface), Pane(f.Session.SplitSurface));
        Deliver(f.Bus, main, split);

        f.Store.ClosePrimaryPane(f.Session.Id);

        Assert.Equal(split, Pane(f.Session.Surface));
        Assert.Equal([0, 1], Pending(f.Bus, main, split));
    }

    [Fact]
    public void HidingASplitKeepsItsMailbox()
    {
        var f = MakeFixture(split: true);
        var split = Pane(f.Session.SplitSurface);
        Deliver(f.Bus, split);

        f.Store.SetSplitVisibility(f.Session.Id, shown: false);

        Assert.Equal(1, f.Bus.Pending(split));
    }

    [Fact]
    public void ScratchCloseDisposesTheScratchOnly()
    {
        var f = MakeFixture(scratch: true);
        var (main, scratch) = (Pane(f.Session.Surface), Pane(f.Session.ScratchSurface));
        Deliver(f.Bus, main, scratch);

        Assert.True(f.Store.CloseScratch(f.Session.Id));

        Assert.Equal([1, 0], Pending(f.Bus, main, scratch));
    }

    [Fact]
    public void DisposingEveryMailboxEmptiesEachLocalPane()
    {
        var f = MakeFixture(split: true, scratch: true);
        var panes = Panes(f.Session);
        Deliver(f.Bus, panes);

        f.Store.DisposeAllMailboxes();

        Assert.Equal([0, 0, 0], Pending(f.Bus, panes));
    }

    [Fact]
    public void ARoleOrAPaneIdResolvesToTheLivePane()
    {
        var f = MakeFixture(split: true, scratch: true);
        var (main, split, scratch) = (Pane(f.Session.Surface), Pane(f.Session.SplitSurface), Pane(f.Session.ScratchSurface));
        string Canonical(Guid pane) => MessageAddress.Canonical(f.Session.Id, pane);

        Assert.Equal(new MessageRecipient(main, Canonical(main)),
            f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Left)));
        Assert.Equal(split, f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Right))?.Pane);
        Assert.Equal(scratch, f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Scratch))?.Pane);
        Assert.Equal(new MessageRecipient(split, Canonical(split)),
            f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Id(split)));
    }

    [Fact]
    public void APaneThatIsNotLiveInThatSessionDoesNotResolve()
    {
        var f = MakeFixture();
        var other = f.Store.AddSession(f.Work.Id, "/o")!;
        other.Surface = new SpySurface(Token());

        f.Store.SetSplitVisibility(f.Session.Id, shown: true);
        Assert.Null(f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Right)));
        Assert.Null(f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Id(Pane(other.Surface))));
        Assert.Null(f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Scratch)));
        Assert.Null(f.Store.MessageRecipient(Guid.NewGuid(), MessageAddressPane.Role(StatusPane.Left)));

        var lazy = new LazySurface(Token()) { IsRealized = false };
        f.Session.Surface = lazy;
        Assert.Null(f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Left)));
        lazy.IsRealized = true;
        Assert.NotNull(f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Left)));
    }

    [Fact]
    public void ASurfaceWithoutAPaneTokenHasNoMailbox()
    {
        var f = MakeFixture();
        f.Session.Surface = new SpySurface("");

        Assert.Null(f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Left)));
        Assert.Empty(f.Store.MessageRecipients());
    }

    [Fact]
    public void BroadcastListsEveryLivePaneOrOneWorkspacesPanes()
    {
        var f = MakeFixture(split: true, scratch: true);
        var elsewhere = f.Store.AddSession(f.Store.Workspaces[1].Id, "/e")!;
        elsewhere.Surface = new SpySurface(Token());

        Assert.Equal(Panes(f.Session).Append(Pane(elsewhere.Surface)).ToHashSet(),
            f.Store.MessageRecipients().Select(r => r.Pane).ToHashSet());
        Assert.Equal([Pane(elsewhere.Surface)], f.Store.MessageRecipients(f.Store.Workspaces[1].Id).Select(r => r.Pane));
    }

    [Fact]
    public void AQueuedMessageEmitsItsEventFromTheStoreHoldingThePane()
    {
        var drafts = new List<ControlEventDraft>();
        var f = MakeFixture(drafts: drafts);
        var recipient = f.Store.MessageRecipient(f.Session.Id, MessageAddressPane.Role(StatusPane.Left))!.Value;
        var id = Guid.NewGuid();
        drafts.Clear();

        Assert.True(f.Store.EmitMessageQueued(id, "S:Q", recipient));
        Assert.False(f.Store.EmitMessageQueued(id, null, new MessageRecipient(Guid.NewGuid(), "x")));

        var draft = Assert.Single(drafts);
        Assert.Equal(ControlEventKind.Message, draft.Kind);
        Assert.Equal(f.Work.Id.ToString("D").ToUpperInvariant(), draft.Workspace);
        Assert.Equal(f.Session.Id.ToString("D").ToUpperInvariant(), draft.Session);
        Assert.Equal(
            ControlJson.Serialize(new ControlEventPayload
            {
                Name = f.Session.DisplayName, PaneId = f.Session.Surface!.PaneToken, From = "S:Q",
                MessageId = id.ToString("D").ToUpperInvariant(),
            }),
            ControlJson.Serialize(draft.Payload));
    }

    [Fact]
    public void TheTreeReportsAPanesPendingCountAndOmitsZero()
    {
        var f = MakeFixture(split: true);
        Deliver(f.Bus, Pane(f.Session.Surface));
        Deliver(f.Bus, Pane(f.Session.Surface));

        var surfaces = f.Store.ControlTree().Workspaces.SelectMany(w => w.Sessions)
            .First(s => s.Id == f.Session.Id.ToString("D").ToUpperInvariant()).Surfaces!;

        Assert.Equal(2, surfaces.First(s => s.Kind == "left").PendingMessages);
        Assert.Null(surfaces.First(s => s.Kind == "right").PendingMessages);
    }
}
