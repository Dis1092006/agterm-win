using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// session.selected, pane.split, pane.scratch and status.previous (upstream a4944436 and #694)
public class AppStoreHookEventsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-hook-events-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly AppStore _store;
    private readonly Workspace _workspace;
    private readonly Session _first;

    public AppStoreHookEventsTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
        _workspace = _store.AddWorkspace("work");
        _first = _store.AddSession(_workspace.Id, "/repo")!;
        _store.PaneFocusDebouncer.Flush();
        _events.Clear();
    }

    public void Dispose()
    {
        _store.PaneFocusDebouncer.Cancel();
        Directory.Delete(_dir, recursive: true);
    }

    private ControlEventDraft[] Of(params ControlEventKind[] kinds) =>
        _events.Where(e => kinds.Contains(e.Kind)).ToArray();

    private static string Id(Guid id) => id.ToString("D").ToUpperInvariant();

    [Fact]
    public void SelectingAnotherSessionEmitsItWithTheOneThatLostTheSelection()
    {
        var second = _store.AddSession(_workspace.Id, "/tmp", name: "api", select: false)!;
        _events.Clear();

        _store.SelectSession(second.Id);
        _store.SelectSession(second.Id);
        _store.SelectSession(null);

        var selected = Of(ControlEventKind.SessionSelected);
        Assert.Equal(2, selected.Length);
        Assert.Equal(Id(second.Id), selected[0].Session);
        Assert.Equal(Id(_workspace.Id), selected[0].Workspace);
        Assert.Equal("api", selected[0].Payload.Name);
        Assert.Equal(Id(_first.Id), selected[0].Payload.Previous);
        Assert.Null(selected[1].Session);
        Assert.Null(selected[1].Workspace);
        Assert.Null(selected[1].Payload.Name);
        Assert.Equal(Id(second.Id), selected[1].Payload.Previous);
    }

    [Fact]
    public void ASelectedAddIsCreatedBeforeItIsSelectedAndABackgroundAddSelectsNothing()
    {
        var added = _store.AddSession(_workspace.Id, "/tmp")!;
        _store.AddSession(_workspace.Id, "/tmp", select: false);

        var events = Of(ControlEventKind.SessionCreated, ControlEventKind.SessionSelected);
        Assert.Equal(
            new[] { ControlEventKind.SessionCreated, ControlEventKind.SessionSelected, ControlEventKind.SessionCreated },
            events.Select(e => e.Kind));
        Assert.Equal(Id(added.Id), events[1].Session);
        Assert.Equal(Id(_first.Id), events[1].Payload.Previous);
    }

    [Fact]
    public void ClosingTheSelectedSessionEmitsTheReselection()
    {
        var added = _store.AddSession(_workspace.Id, "/tmp")!;
        _events.Clear();

        _store.CloseSession(added.Id);

        var events = Of(ControlEventKind.SessionClosed, ControlEventKind.SessionSelected);
        Assert.Equal(new[] { ControlEventKind.SessionClosed, ControlEventKind.SessionSelected },
            events.Select(e => e.Kind));
        Assert.Equal(Id(_first.Id), events[1].Session);
        Assert.Equal(Id(added.Id), events[1].Payload.Previous);
    }

    [Fact]
    public void ReloadingAStoreFromItsSnapshotSelectsNothing()
    {
        _store.AddSession(_workspace.Id, "/tmp");
        var snapshot = _store.Snapshot();
        _store.SelectSession(_first.Id);
        _events.Clear();

        _store.Restore(snapshot);

        Assert.Empty(Of(ControlEventKind.SessionSelected));
        Assert.Equal(snapshot.SelectedSessionId, _store.SelectedSessionId);
    }

    [Fact]
    public void StatusEventsCarryThePreviousStatus()
    {
        _store.SetAgentIndicator(new AgentIndicator { Status = AgentStatus.Active }, _first.Id);
        _store.SetAgentIndicator(new AgentIndicator { Status = AgentStatus.Blocked }, _first.Id);
        _store.SetAgentIndicator(new AgentIndicator { Status = AgentStatus.Blocked, Shape = StatusShape.Star }, _first.Id);
        _store.SetAgentIndicator(new AgentIndicator { Status = AgentStatus.Blocked, Shape = StatusShape.Star }, _first.Id);
        _store.SetAgentIndicator(new AgentIndicator(), _first.Id);

        var status = Of(ControlEventKind.Status);
        Assert.Equal(new[] { "active", "blocked", "blocked", "idle" }, status.Select(e => e.Payload.Status));
        Assert.Equal(new[] { "idle", "active", "blocked", "blocked" }, status.Select(e => e.Payload.Previous));
    }

    [Fact]
    public void SplitEventsFollowRealVisibilityTransitionsOnly()
    {
        _first.Surface = new SpySurface();
        _events.Clear();

        _store.SetSplitVisibility(_first.Id, shown: true);
        _first.SplitSurface = new SpySurface();
        _store.SetSplitVisibility(_first.Id, shown: true);
        _store.ToggleSplit(_first.Id, SplitAxis.TopBottom);
        _store.SetSplitVisibility(_first.Id, shown: false);
        _store.CloseSplit(_first.Id);
        _store.ToggleSplit(_first.Id);
        _first.SplitSurface = new SpySurface();
        _store.CloseSplitPane(_first.Id);
        _store.ToggleSplit(_first.Id);
        _first.SplitSurface = new SpySurface();
        _store.ClosePrimaryPane(_first.Id);

        var split = Of(ControlEventKind.PaneSplit);
        Assert.Equal(new[] { "shown", "hidden", "shown", "hidden", "shown", "hidden" },
            split.Select(e => e.Payload.Status));
        Assert.All(split, e => Assert.Equal(Id(_first.Id), e.Session));
        Assert.All(split, e => Assert.Equal(Id(_workspace.Id), e.Workspace));
        Assert.All(split, e => Assert.Equal(_first.DisplayName, e.Payload.Name));
    }

    [Fact]
    public void PromotingTheSurvivorOfAHiddenSplitEmitsNoSplitEvent()
    {
        _first.Surface = new SpySurface();
        _store.SetSplitVisibility(_first.Id, shown: true);
        var survivor = new SpySurface();
        _first.SplitSurface = survivor;
        _store.SetSplitVisibility(_first.Id, shown: false);
        _events.Clear();

        _store.ClosePrimaryPane(_first.Id);

        Assert.Same(survivor, _first.Surface);
        Assert.Empty(Of(ControlEventKind.PaneSplit));
    }

    [Fact]
    public void ScratchEventsFollowRealVisibilityTransitionsOnly()
    {
        Assert.False(_store.CloseScratch(_first.Id));
        _store.ToggleScratch(_first.Id);
        _first.ScratchSurface = new SpySurface();
        _store.ToggleScratch(_first.Id);
        Assert.True(_store.CloseScratch(_first.Id));
        _store.ToggleScratch(_first.Id);
        _first.ScratchSurface = new SpySurface();
        Assert.True(_store.CloseScratch(_first.Id));

        var scratch = Of(ControlEventKind.PaneScratch);
        Assert.Equal(new[] { "shown", "hidden", "shown", "hidden" }, scratch.Select(e => e.Payload.Status));
        Assert.All(scratch, e => Assert.Equal(Id(_first.Id), e.Session));
    }

    [Fact]
    public void ScratchTeardownAndReshowCanSuppressTheirVisibilityEvents()
    {
        _store.ToggleScratch(_first.Id);
        _first.ScratchSurface = new SpySurface();
        _events.Clear();

        Assert.True(_store.CloseScratch(_first.Id, emitVisibility: false));
        _store.ToggleScratch(_first.Id, emitVisibility: false);

        Assert.True(_first.ScratchActive);
        Assert.Empty(Of(ControlEventKind.PaneScratch));
    }
}
