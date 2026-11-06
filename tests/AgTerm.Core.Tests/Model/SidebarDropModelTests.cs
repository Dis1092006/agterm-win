using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>SidebarDropModel.Resolve — a sidebar session drop turned into the store move it performs, in
/// each layout (upstream 8088e146 WorkspaceSidebar+DragDrop validate/accept).</summary>
public sealed class SidebarDropModelTests
{
    private static readonly Guid WsA = Guid.NewGuid(), WsB = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid(), H = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid(), K = Guid.NewGuid();

    // wsA: a, h, b; wsB: c, k. Flagged: a, b, c, in the order c, a, b.
    private static SidebarDropModel Model(SidebarMode mode, FlaggedViewLayout layout = FlaggedViewLayout.Flat) => new(
        mode, layout, [(WsA, [A, H, B]), (WsB, [C, K])], new HashSet<Guid> { A, B, C }, [C, A, B]);

    private static readonly SidebarDropModel Tree = Model(SidebarMode.Tree);
    private static readonly SidebarDropModel FlatList = Model(SidebarMode.Flagged);
    private static readonly SidebarDropModel FlaggedTree = Model(SidebarMode.Flagged, FlaggedViewLayout.Tree);

    [Fact]
    public void A_list_gap_reorders_the_flagged_order()
    {
        Assert.Equal(new SidebarSessionMove.Reorder([B], FlaggedOrderPlacement.Before, C),
                     FlatList.Resolve([B], new SidebarSessionDrop.ListGap(0)));
    }

    [Fact]
    public void A_drop_on_a_list_row_lands_after_it()
    {
        Assert.Equal(new SidebarSessionMove.Reorder([C], FlaggedOrderPlacement.After, A),
                     FlatList.Resolve([C], new SidebarSessionDrop.OnSession(A)));
    }

    [Fact]
    public void A_list_drop_that_keeps_the_order_is_a_no_op()
    {
        Assert.Null(FlatList.Resolve([A], new SidebarSessionDrop.ListGap(2)));
        Assert.Null(FlatList.Resolve([A], new SidebarSessionDrop.OnSession(A)));
    }

    [Fact]
    public void The_plain_layout_reorders_like_the_flat_one() =>
        Assert.Equal(new SidebarSessionMove.Reorder([B], FlaggedOrderPlacement.Before, C),
                     Model(SidebarMode.Flagged, FlaggedViewLayout.Plain).Resolve([B], new SidebarSessionDrop.ListGap(0)));

    [Fact]
    public void A_tree_gap_moves_the_sessions_to_its_slot() =>
        Assert.Equal(new SidebarSessionMove.Sessions(WsA, 0), Tree.Resolve([B], new SidebarSessionDrop.Gap(WsA, 0)));

    [Fact]
    public void A_tree_drop_on_a_workspace_row_appends() =>
        Assert.Equal(new SidebarSessionMove.Sessions(WsB, 2), Tree.Resolve([A], new SidebarSessionDrop.OnWorkspace(WsB)));

    [Fact]
    public void A_tree_drop_on_a_session_row_lands_after_it() =>
        Assert.Equal(new SidebarSessionMove.Sessions(WsB, 1), Tree.Resolve([A], new SidebarSessionDrop.OnSession(C)));

    [Fact]
    public void A_tree_drop_back_into_place_is_a_no_op()
    {
        Assert.Null(Tree.Resolve([H], new SidebarSessionDrop.Gap(WsA, 1)));
        Assert.Null(Tree.Resolve([H], new SidebarSessionDrop.OnSession(A)));
    }

    [Fact]
    public void A_flagged_tree_gap_counts_only_the_visible_rows() =>
        Assert.Equal(new SidebarSessionMove.Sessions(WsA, 0), FlaggedTree.Resolve([B], new SidebarSessionDrop.Gap(WsA, 0)));

    [Fact]
    public void A_flagged_tree_move_across_a_hidden_sibling_only_is_a_no_op()
    {
        Assert.Null(FlaggedTree.Resolve([A], new SidebarSessionDrop.Gap(WsA, 1)));
        // the plain tree sees the same move as a real one: a lands after h
        Assert.Equal(new SidebarSessionMove.Sessions(WsA, 1), Tree.Resolve([A], new SidebarSessionDrop.Gap(WsA, 2)));
    }

    [Fact]
    public void A_flagged_tree_drop_on_a_workspace_row_appends_after_its_last_session() =>
        Assert.Equal(new SidebarSessionMove.Sessions(WsB, 2), FlaggedTree.Resolve([A], new SidebarSessionDrop.OnWorkspace(WsB)));

    [Fact]
    public void A_drop_that_does_not_fit_the_layout_is_a_no_op()
    {
        Assert.Null(Tree.Resolve([A], new SidebarSessionDrop.ListGap(0)));
        Assert.Null(FlatList.Resolve([A], new SidebarSessionDrop.Gap(WsB, 0)));
        Assert.Null(FlatList.Resolve([A], new SidebarSessionDrop.OnWorkspace(WsB)));
    }

    [Fact]
    public void A_drag_of_a_session_outside_the_tree_is_a_no_op()
    {
        Assert.Null(Tree.Resolve([Guid.NewGuid()], new SidebarSessionDrop.Gap(WsA, 0)));
        Assert.Null(Tree.Resolve([A], new SidebarSessionDrop.OnSession(Guid.NewGuid())));
        Assert.Null(Tree.Resolve([A], new SidebarSessionDrop.OnWorkspace(Guid.NewGuid())));
    }

    [Fact]
    public void The_model_reads_the_store()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SetFlag(true, b.Id);
        store.SetFlag(true, a.Id);
        store.SidebarMode = SidebarMode.Flagged;
        store.FlaggedLayout = FlaggedViewLayout.Plain;

        var model = SidebarDropModel.From(store);

        Assert.Equal(SidebarMode.Flagged, model.Mode);
        Assert.Equal(FlaggedViewLayout.Plain, model.Layout);
        Assert.Equal(store.Workspaces.Select(w => w.Id), model.Workspaces.Select(w => w.Id));
        Assert.Equal([a.Id, b.Id], model.Workspaces.Single(w => w.Id == ws.Id).Sessions);
        Assert.Equal(new HashSet<Guid> { a.Id, b.Id }, model.Flagged);
        Assert.Equal([b.Id, a.Id], model.FlaggedOrder);
    }
}
