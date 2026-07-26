using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

/// <summary>
/// Ported 1:1 from agtermCore AppStoreEventTests.swift (names preserved).
///
/// Upstream drives events through WindowLibrary: the library owns the app-run ControlEventRing,
/// stamps the window id onto every AppStore draft, coalesces tree.changed per window, and reads
/// pages back via readEvents. Phase 1 (AgTerm.Core) ports the AppStore seam only, so:
/// - store-level tests capture ControlEventDraft values from the controlEventSink and treat the
///   post-anchor drafts list as the upstream readEvents(kinds:) page;
/// - ring-seam tests compose AppStore + ControlEventRing with a window-stamping sink, exactly the
///   composition the library performs;
/// - tests that exercise WindowLibrary's own lifecycle (newWindow/closeWindow/loadStore/
///   removeWindow) or its tree-event debouncers are skipped with a PORT-GAP note.
/// </summary>
public class AppStoreEventTests : IDisposable
{
    private readonly string _directory;
    private static readonly Guid Run = Guid.Parse("CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1");

    public AppStoreEventTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "agterm-events-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup, mirrors the Swift deinit's try?
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void EveryWindowStoreFeedsOneAppRunRingWithStampedIdentity()
    {
        var ring = new ControlEventRing(runId: Run, now: () => DateTimeOffset.FromUnixTimeSeconds(50));
        var (firstWindowId, firstStore) = MakeWindowStore(ring);
        var anchor = EventBatch(ring.Read(null, kinds: null, limit: 100));
        var firstWorkspace = firstStore.Workspaces.FirstOrDefault();
        Assert.NotNull(firstWorkspace);
        var firstSession = firstWorkspace!.Sessions.FirstOrDefault();
        Assert.NotNull(firstSession);

        firstStore.EmitControlEvent(ControlEventKind.Status, firstWorkspace.Id, firstSession!.Id,
            new ControlEventPayload { Name = firstSession.DisplayName, Status = "active" });

        var (secondWindowId, secondStore) = MakeWindowStore(ring);
        var secondWorkspace = secondStore.Workspaces.FirstOrDefault();
        Assert.NotNull(secondWorkspace);
        var secondSession = secondWorkspace!.Sessions.FirstOrDefault();
        Assert.NotNull(secondSession);
        secondStore.EmitControlEvent(ControlEventKind.Notify, secondWorkspace.Id, secondSession!.Id,
            new ControlEventPayload { Name = secondSession.DisplayName, Title = "done", Body = "ok" });

        var batch = EventBatch(ring.Read(
            new ControlEventCursor(anchor.Run, anchor.Next),
            new HashSet<ControlEventKind> { ControlEventKind.Status, ControlEventKind.Notify }, 100));

        Assert.Equal(Run, batch.Run);
        Assert.Equal(new[] { Uuid(firstWindowId), Uuid(secondWindowId) },
            batch.Items.Select(e => e.Window).ToList());
        Assert.Equal(new[] { Uuid(firstWorkspace.Id), Uuid(secondWorkspace.Id) },
            batch.Items.Select(e => e.Workspace).ToList());
        Assert.Equal(new[] { Uuid(firstSession.Id), Uuid(secondSession.Id) },
            batch.Items.Select(e => e.Session).ToList());
    }

    [Fact(Skip = "PORT-GAP: WindowLibrary (newWindow/closeWindow/loadStore lifecycle edges and "
                 + "per-window tree.changed coalescing) is host-level and not part of Phase 1 AgTerm.Core")]
    public void RuntimeCloseAndReopenKeepsCursorContinuity()
    {
        // PORT-GAP: upstream asserts that closing a window emits session.closed for its live
        // sessions, reopening it re-emits session.created, and the shared ring's sequence numbers
        // stay contiguous (seq == [1, 2, 3, 4, 5]) across the whole runtime close/reopen cycle.
        // All of that is WindowLibrary behavior; AgTerm.Core has no window lifecycle yet.
    }

    [Fact]
    public void CursorFailureResponseCarriesCurrentAnchor()
    {
        // WindowLibrary.readEvents wraps the ring read in a ControlResponse; Phase 1 exercises the
        // ring seam directly: a non-null Error is the response.ok == false leg, and Message() is
        // the verbatim wire error string upstream returns as the enum's rawValue.
        var ring = new ControlEventRing(runId: Run);
        var response = ring.Read(new ControlEventCursor(Guid.NewGuid(), 0), kinds: null, limit: 100);

        Assert.NotNull(response.Error);
        Assert.Equal(ControlEventReadError.RunChanged, response.Error);
        Assert.Equal("event run changed", response.Error!.Value.Message());
        var anchor = response.Batch;
        Assert.NotNull(anchor);
        Assert.Equal(Run, anchor.Run);
        Assert.Empty(anchor.Items);
    }

    [Fact]
    public void NormalizedStatusChangesEmitCompletePayloadsAndIdleEdge()
    {
        var (store, drafts, _, session) = MakeActiveStore();
        drafts.Clear(); // anchor

        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active, StatusPane: StatusPane.Right), session.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, Blink: true, Color: "#aabbcc",
            Shape: StatusShape.Triangle, StatusPane: StatusPane.Scratch), session.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed), session.Id);
        store.SetAgentIndicator(new AgentIndicator(), session.Id);

        var items = Events(drafts, ControlEventKind.Status);
        Assert.Equal(new[] { "active", "blocked", "completed", "idle" },
            items.Select(d => d.Payload.Status).ToList());
        Assert.Equal("left", items[0].Payload.Pane);
        Assert.Null(items[0].Payload.Shape);
        Assert.Equal("scratch", items[1].Payload.Pane);
        Assert.True(items[1].Payload.Blink == true);
        Assert.Equal("#aabbcc", items[1].Payload.Color);
        Assert.Equal("triangle", items[1].Payload.Shape);
        Assert.All(items, d => Assert.Equal(session.DisplayName, d.Payload.Name));
    }

    [Fact]
    public void ShapeOnlyStatusChangeEmitsAnEventCarryingTheShape()
    {
        var (store, drafts, _, session) = MakeActiveStore();
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), session.Id);
        drafts.Clear(); // anchor

        // only the shape differs, so the `previous != indicator` guard passes and an event fires — a
        // consumer must be able to explain it, which is what the payload field is for
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, Shape: StatusShape.Star), session.Id);
        // the negative leg of the same guard: re-asserting the IDENTICAL shaped indicator is not a change,
        // so a repeated hook call must stay silent — this is what breaks if `shape` ever stops
        // participating in `AgentIndicator` equality
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, Shape: StatusShape.Star), session.Id);

        var items = Events(drafts, ControlEventKind.Status);
        Assert.Equal(1, items.Count);
        Assert.Equal("blocked", items.FirstOrDefault()?.Payload.Status);
        Assert.Equal("star", items.FirstOrDefault()?.Payload.Shape);
    }

    [Fact]
    public void SameNormalizedStatusAndUnknownSessionDoNotEmit()
    {
        var (store, drafts, _, session) = MakeActiveStore();
        drafts.Clear(); // anchor
        var indicator = new AgentIndicator(AgentStatus.Blocked, Blink: true, StatusPane: StatusPane.Right);

        store.SetAgentIndicator(indicator, session.Id);
        store.SetAgentIndicator(indicator, session.Id);
        store.SetAgentIndicator(indicator, Guid.NewGuid());

        var items = Events(drafts, ControlEventKind.Status);
        Assert.Equal(1, items.Count);
        Assert.True(session.StatusChangedAt != null);
    }

    [Fact]
    public void AutoResetVisitRoutesThroughStatusSetterAndEmitsIdle()
    {
        var (store, drafts, _, session) = MakeActiveStore();
        drafts.Clear(); // anchor

        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed, AutoReset: true), session.Id);
        store.SelectSession(session.Id);

        var items = Events(drafts, ControlEventKind.Status);
        Assert.Equal(new[] { "completed", "idle" }, items.Select(d => d.Payload.Status).ToList());
    }

    [Fact]
    public void NotificationRecordingRequiresSessionResolutionAndUsesEffectiveTitle()
    {
        var (store, drafts, _, session) = MakeActiveStore();
        drafts.Clear(); // anchor

        Assert.Null(store.RecordNotificationEvent(Guid.NewGuid(), "missing", "no"));
        var effectiveTitle = store.RecordNotificationEvent(session.Id, "", "tests passed");

        Assert.Equal(session.DisplayName, effectiveTitle);
        var items = Events(drafts, ControlEventKind.Notify);
        Assert.Equal(1, items.Count);
        Assert.Equal(session.DisplayName, items[0].Payload.Name);
        Assert.Equal(session.DisplayName, items[0].Payload.Title);
        Assert.Equal("tests passed", items[0].Payload.Body);
    }

    [Fact]
    public void AddSoftCloseUndoAndGraceFinalizationEmitVisibleMembershipEdgesOnly()
    {
        var (store, drafts, workspace, _) = MakeActiveStore();
        drafts.Clear(); // anchor
        var session = store.AddSession(workspace.Id, "/tmp", name: "api");
        Assert.NotNull(session);

        Assert.True(store.SoftCloseSession(session!.Id, grace: 60));
        Assert.True(store.UndoPendingClose());
        Assert.True(store.SoftCloseSession(session.Id, grace: 60));
        store.FinalizeAllPendingCloses();

        var items = Events(drafts, ControlEventKind.SessionCreated, ControlEventKind.SessionClosed);
        Assert.Equal(new[]
        {
            ControlEventKind.SessionCreated, ControlEventKind.SessionClosed,
            ControlEventKind.SessionCreated, ControlEventKind.SessionClosed,
        }, items.Select(d => d.Kind).ToList());
        Assert.All(items, d =>
        {
            Assert.Equal(Uuid(session.Id), d.Session);
            Assert.Equal("api", d.Payload.Name);
        });
    }

    [Fact]
    public void DuplicateSessionEmitsOneCreatedEdge()
    {
        var (store, drafts, _, source) = MakeActiveStore();
        var workspace = store.WorkspaceForSession(source.Id);
        Assert.NotNull(workspace);
        drafts.Clear(); // anchor

        var duplicate = store.DuplicateSession(source.Id);
        Assert.NotNull(duplicate);

        var items = Events(drafts, ControlEventKind.SessionCreated);
        Assert.Equal(new[] { Uuid(duplicate!.Id) }, items.Select(d => d.Session).ToList());
        Assert.Equal(new[] { Uuid(workspace!.Id) }, items.Select(d => d.Workspace).ToList());
    }

    [Fact]
    public void FoldingPendingWorkspaceCloseDoesNotRepeatAlreadyHiddenSessionEdges()
    {
        var (store, drafts, _, _) = MakeActiveStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/one", name: "one");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/two", name: "two");
        Assert.NotNull(second);
        drafts.Clear(); // anchor

        Assert.True(store.SoftCloseSession(first!.Id, grace: 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        Assert.True(store.UndoPendingClose(sessionClose));
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));

        var items = Events(drafts, ControlEventKind.SessionCreated, ControlEventKind.SessionClosed);
        Assert.Equal(new[]
        {
            ControlEventKind.SessionClosed, ControlEventKind.SessionClosed,
            ControlEventKind.SessionCreated, ControlEventKind.SessionClosed,
        }, items.Select(d => d.Kind).ToList());
        Assert.Equal(new[]
        {
            Uuid(first.Id), Uuid(second!.Id), Uuid(first.Id), Uuid(first.Id),
        }, items.Select(d => d.Session).ToList());
    }

    [Fact]
    public void WorkspaceRemovalEmitsClosedSessionsInTreeOrder()
    {
        var (store, drafts, _, _) = MakeActiveStore();
        var workspace = store.AddWorkspace("batch");
        var first = store.AddSession(workspace.Id, "/one", name: "one");
        Assert.NotNull(first);
        var second = store.AddSession(workspace.Id, "/two", name: "two");
        Assert.NotNull(second);
        drafts.Clear(); // anchor

        store.RemoveWorkspace(workspace.Id);

        var items = Events(drafts, ControlEventKind.SessionClosed);
        Assert.Equal(new[] { Uuid(first!.Id), Uuid(second!.Id) },
            items.Select(d => d.Session).ToList());
        Assert.Equal(new[] { "one", "two" }, items.Select(d => d.Payload.Name).ToList());
    }

    [Fact(Skip = "PORT-GAP: WindowLibrary.closeWindow/loadStore (runtime window close/reopen edges) "
                 + "is host-level and not part of Phase 1 AgTerm.Core")]
    public void RuntimeWindowCloseAndReopenEmitBalancedSessionEdges()
    {
        // PORT-GAP: upstream asserts closeWindow emits session.closed for the window's session and
        // loadStore re-emits session.created for the same id — the balanced pair is produced by
        // WindowLibrary, which AgTerm.Core does not port in Phase 1.
    }

    [Fact(Skip = "PORT-GAP: WindowLibrary.removeWindow/flushTreeEvents (window deletion + per-window "
                 + "tree-event coalescing) is host-level and not part of Phase 1 AgTerm.Core")]
    public void DeletingClosedWindowStillEmitsStructuralInvalidation()
    {
        // PORT-GAP: upstream asserts that deleting an already-closed window still emits one
        // window-stamped tree.changed. Window deletion and the tree-event debouncers live in
        // WindowLibrary.
    }

    [Fact(Skip = "PORT-GAP: WindowLibrary.removeWindow/flushTreeEvents (window deletion + per-window "
                 + "tree-event coalescing) is host-level and not part of Phase 1 AgTerm.Core")]
    public void DeletingOpenWindowEmitsClosedEdgeAndStructuralInvalidation()
    {
        // PORT-GAP: upstream asserts that deleting an OPEN window emits session.closed for its live
        // session followed by one tree.changed, all stamped with the deleted window's id. Both the
        // deletion path and the coalesced flush live in WindowLibrary.
    }

    [Fact(Skip = "PORT-GAP: WindowLibrary.flushTreeEvents (per-window tree.changed debounce/coalesce) "
                 + "is host-level and not part of Phase 1 AgTerm.Core; AppStore emits every "
                 + "tree.changed draft eagerly through the sink")]
    public void TreeChangesCoalescePerWindowAndStayIndependent()
    {
        // PORT-GAP: upstream asserts scheduled tree changes are invisible before flushTreeEvents and
        // coalesce to exactly one tree.changed per window afterwards. The debouncers are owned by
        // WindowLibrary; the AppStore seam ported here forwards each ScheduleTreeChanged draft
        // immediately, so the coalescing contract cannot be exercised in Phase 1.
    }

    [Fact]
    public void StatusAndSelectionDoNotScheduleStructuralInvalidation()
    {
        var (store, drafts, _, session) = MakeActiveStore();
        // upstream flushes the library's tree-event debouncers around the anchor; the Phase 1 sink
        // sees every scheduled tree.changed draft eagerly, so no flush is needed on either side.
        drafts.Clear(); // anchor

        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), session.Id);
        store.SelectSession(session.Id);

        var items = Events(drafts, ControlEventKind.TreeChanged);
        Assert.Empty(items);
    }

    [Fact]
    public void OpenRecentRecreationEmitsCreatedAfterHardClose()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid());
        var recentClosed = new RecentClosedStore(dir);
        var drafts = new List<ControlEventDraft>();
        var store = new AppStore(persistence: new PersistenceStore(dir),
            recentClosedStore: recentClosed, controlEventSink: d => drafts.Add(d));
        var workspace = store.AddWorkspace("workspace 1");
        _ = store.AddSession(workspace.Id, "/work")!; // the library-bootstrap default session
        var session = store.AddSession(workspace.Id, "/tmp", name: "recent");
        Assert.NotNull(session);
        drafts.Clear(); // anchor

        store.CloseSession(session!.Id);
        var recent = recentClosed.Load().FirstOrDefault(item => item.Session?.Snapshot.Id == session.Id);
        Assert.NotNull(recent);
        // upstream's library.reopenRecentClosed(recent.id, into: store) resolves the item by id and
        // delegates to the store; Phase 1 calls the store seam directly.
        Assert.True(store.RestoreRecentClosed(recent!));

        var items = Events(drafts, ControlEventKind.SessionCreated, ControlEventKind.SessionClosed);
        Assert.Equal(new[] { ControlEventKind.SessionClosed, ControlEventKind.SessionCreated },
            items.Select(d => d.Kind).ToList());
    }

    /// <summary>Adapted eventBatch(_:): unwraps a ring read, asserting it succeeded (upstream
    /// asserts response.ok and unwraps result.events).</summary>
    private static ControlEventBatch EventBatch(ControlEventReadResult response)
    {
        Assert.Null(response.Error);
        return response.Batch;
    }

    /// <summary>The drafts of the given kinds, in emission order — the stand-in for upstream's
    /// readEvents(after: anchor.next, kinds:) page over the shared ring.</summary>
    private static List<ControlEventDraft> Events(IEnumerable<ControlEventDraft> drafts,
        params ControlEventKind[] kinds) =>
        drafts.Where(d => kinds.Contains(d.Kind)).ToList();

    private static string Uuid(Guid id) => id.ToString("D").ToUpperInvariant();

    /// <summary>The WindowLibrary bootstrap analogue for the store-level tests: a store whose sink
    /// records drafts, seeded like library.activeStore with one default workspace holding one
    /// selected session. Callers Clear() the drafts at the test's anchor point.</summary>
    private static (AppStore Store, List<ControlEventDraft> Drafts, Workspace Workspace, Session Session)
        MakeActiveStore()
    {
        var drafts = new List<ControlEventDraft>();
        var store = new AppStore(
            persistence: new PersistenceStore(Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid())),
            controlEventSink: d => drafts.Add(d));
        var workspace = store.AddWorkspace("workspace 1");
        var session = store.AddSession(workspace.Id, "/work")!;
        return (store, drafts, workspace, session);
    }

    /// <summary>The WindowLibrary composition for one window: the library-owned sink stamps the
    /// window id onto every AppStore draft and appends it to the shared app-run ring; a new window
    /// bootstraps one default workspace holding one session.</summary>
    private (Guid WindowId, AppStore Store) MakeWindowStore(ControlEventRing ring)
    {
        var windowId = Guid.NewGuid();
        var store = new AppStore(
            persistence: new PersistenceStore(Path.Combine(_directory, windowId.ToString("D"))),
            controlEventSink: d => ring.Append(d with { Window = Uuid(windowId) }));
        var workspace = store.AddWorkspace("workspace 1");
        store.AddSession(workspace.Id, "/work");
        return (windowId, store);
    }
}
