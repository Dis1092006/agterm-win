using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>Port of agtermCore SidebarDropTests.swift — the pure drop index math (post-removal
/// off-by-one, drop-on-row redirect, cross-workspace index spaces, no-op detection).</summary>
public class SidebarDropTests
{
    private static readonly Guid WsA = Guid.NewGuid();
    private static readonly Guid WsB = Guid.NewGuid();
    private const int OnItem = SidebarDrop.OnItemIndex;

    private static SidebarDrop.SessionDropTarget WorkspaceRow(Guid id, int count) =>
        new SidebarDrop.SessionDropTarget.WorkspaceRow(id, count);

    private static SidebarDrop.SessionDropTarget SessionRow(Guid ws, int index, int count) =>
        new SidebarDrop.SessionDropTarget.SessionRow(ws, index, count);

    // MARK: - Finder directory import

    [Fact]
    public void Directory_drop_targets_explicit_row_workspace() =>
        Assert.Equal(WsB, SidebarDrop.ResolveDirectoryWorkspace(SidebarMode.Tree, WsB, WsA, WsA));

    [Fact]
    public void Empty_directory_drop_prefers_the_fallback_workspace() =>
        Assert.Equal(WsB, SidebarDrop.ResolveDirectoryWorkspace(SidebarMode.Tree, null, WsB, WsA));

    [Fact]
    public void Empty_directory_drop_falls_back_to_current_workspace() =>
        Assert.Equal(WsA, SidebarDrop.ResolveDirectoryWorkspace(SidebarMode.Tree, null, null, WsA));

    [Fact]
    public void Directory_drop_is_rejected_in_flagged_mode() =>
        Assert.Null(SidebarDrop.ResolveDirectoryWorkspace(SidebarMode.Flagged, WsB, null, WsA));

    // MARK: - Session, same workspace

    [Fact]
    public void Session_same_workspace_up_drop_on_row()
    {
        var move = SidebarDrop.ResolveSession(WsA, 2, SessionRow(WsA, 0, 3), OnItem);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 1, 1), move);
    }

    [Fact]
    public void Session_same_workspace_down_past_middle_row_applies_minus_one()
    {
        var move = SidebarDrop.ResolveSession(WsA, 0, SessionRow(WsA, 2, 4), OnItem);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 3, 2), move);
    }

    [Fact]
    public void Session_same_workspace_down_between_rows_applies_minus_one()
    {
        var move = SidebarDrop.ResolveSession(WsA, 0, WorkspaceRow(WsA, 4), 3);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 3, 2), move);
    }

    [Fact]
    public void Session_same_workspace_up_between_rows_no_adjustment()
    {
        var move = SidebarDrop.ResolveSession(WsA, 2, WorkspaceRow(WsA, 3), 1);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 1, 1), move);
    }

    [Fact]
    public void Session_same_workspace_no_op_when_dropped_into_own_slot() =>
        Assert.Null(SidebarDrop.ResolveSession(WsA, 1, WorkspaceRow(WsA, 3), 2));

    [Fact]
    public void Session_same_workspace_append_already_last_is_no_op() =>
        Assert.Null(SidebarDrop.ResolveSession(WsA, 2, WorkspaceRow(WsA, 3), OnItem));

    [Fact]
    public void Session_same_workspace_append_from_middle_moves_to_end()
    {
        var move = SidebarDrop.ResolveSession(WsA, 0, WorkspaceRow(WsA, 3), OnItem);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, OnItem, 3), move);
    }

    // MARK: - Session, cross workspace (no -1, no same-slot no-op)

    [Fact]
    public void Session_cross_workspace_insert_at_middle_slot()
    {
        var move = SidebarDrop.ResolveSession(WsA, 0, WorkspaceRow(WsB, 3), 2);
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 2, 2), move);
    }

    [Fact]
    public void Session_cross_workspace_drop_on_row_inserts_after()
    {
        var move = SidebarDrop.ResolveSession(WsA, 0, SessionRow(WsB, 1, 3), OnItem);
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 2, 2), move);
    }

    [Fact]
    public void Session_cross_workspace_append_onto_header()
    {
        var move = SidebarDrop.ResolveSession(WsA, 0, WorkspaceRow(WsB, 2), OnItem);
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, OnItem, 2), move);
    }

    // MARK: - Batch session drag

    [Fact]
    public void Batch_session_cross_workspace_insert_at_middle_slot()
    {
        var move = SidebarDrop.ResolveSessions(
            new[] { new SidebarDrop.SessionSource(WsA, 0), new SidebarDrop.SessionSource(WsA, 1) },
            WorkspaceRow(WsB, 3), 2);
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 2, 2), move);
    }

    [Fact]
    public void Batch_session_same_workspace_down_adjusts_for_every_removed_source_before_drop()
    {
        var move = SidebarDrop.ResolveSessions(
            new[] { new SidebarDrop.SessionSource(WsA, 0), new SidebarDrop.SessionSource(WsA, 1) },
            WorkspaceRow(WsA, 5), 4);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 4, 2), move);
    }

    [Fact]
    public void Batch_session_same_workspace_contiguous_drop_inside_selection_is_no_op() =>
        Assert.Null(SidebarDrop.ResolveSessions(
            new[] { new SidebarDrop.SessionSource(WsA, 1), new SidebarDrop.SessionSource(WsA, 2) },
            WorkspaceRow(WsA, 4), 2));

    [Fact]
    public void Batch_session_same_workspace_non_contiguous_drop_at_first_source_can_still_move()
    {
        var move = SidebarDrop.ResolveSessions(
            new[] { new SidebarDrop.SessionSource(WsA, 1), new SidebarDrop.SessionSource(WsA, 3) },
            WorkspaceRow(WsA, 5), 1);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 1, 1), move);
    }

    [Fact]
    public void Batch_session_mixed_target_adjusts_for_moved_rows_already_in_target()
    {
        var move = SidebarDrop.ResolveSessions(
            new[] { new SidebarDrop.SessionSource(WsA, 0), new SidebarDrop.SessionSource(WsB, 0) },
            WorkspaceRow(WsB, 3), 2);
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 2, 1), move);
    }

    // MARK: - Anchor-relative placement (control --after/--before)

    [Fact]
    public void Relative_same_workspace_after_anchor()
    {
        var move = SidebarDrop.ResolveRelative((WsA, 2), (WsA, 0, 3), placeAfter: true);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 1, 1), move);
    }

    [Fact]
    public void Relative_same_workspace_after_anchor_downward_applies_minus_one()
    {
        var move = SidebarDrop.ResolveRelative((WsA, 0), (WsA, 2, 4), placeAfter: true);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 3, 2), move);
    }

    [Fact]
    public void Relative_same_workspace_before_anchor()
    {
        var move = SidebarDrop.ResolveRelative((WsA, 2), (WsA, 1, 3), placeAfter: false);
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 1, 1), move);
    }

    [Fact]
    public void Relative_cross_workspace_after_anchor_no_adjustment()
    {
        var move = SidebarDrop.ResolveRelative((WsA, 0), (WsB, 1, 3), placeAfter: true);
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 2, 2), move);
    }

    [Fact]
    public void Relative_cross_workspace_before_anchor_no_adjustment()
    {
        var move = SidebarDrop.ResolveRelative((WsA, 0), (WsB, 1, 3), placeAfter: false);
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 1, 1), move);
    }

    [Fact]
    public void Relative_anchor_is_source_before_is_no_op() =>
        Assert.Null(SidebarDrop.ResolveRelative((WsA, 1), (WsA, 1, 3), placeAfter: false));

    [Fact]
    public void Relative_anchor_is_source_after_is_no_op() =>
        Assert.Null(SidebarDrop.ResolveRelative((WsA, 1), (WsA, 1, 3), placeAfter: true));

    // MARK: - Workspace reorder

    [Fact]
    public void Workspace_move_up()
    {
        var move = SidebarDrop.ResolveWorkspace(sourceIndex: 2, count: 3, childIndex: 1);
        Assert.Equal(new SidebarDrop.WorkspaceResolution(1, 1), move);
    }

    [Fact]
    public void Workspace_move_down_applies_minus_one()
    {
        var move = SidebarDrop.ResolveWorkspace(sourceIndex: 0, count: 3, childIndex: 2);
        Assert.Equal(new SidebarDrop.WorkspaceResolution(2, 1), move);
    }

    [Fact]
    public void Workspace_move_to_bottom_append()
    {
        var move = SidebarDrop.ResolveWorkspace(sourceIndex: 0, count: 3, childIndex: OnItem);
        Assert.Equal(new SidebarDrop.WorkspaceResolution(3, 2), move);
    }

    [Fact]
    public void Workspace_move_no_op_into_own_slot() =>
        Assert.Null(SidebarDrop.ResolveWorkspace(sourceIndex: 1, count: 3, childIndex: 2));

    [Fact]
    public void Workspace_move_no_op_append_already_last() =>
        Assert.Null(SidebarDrop.ResolveWorkspace(sourceIndex: 2, count: 3, childIndex: OnItem));

    // MARK: - Workspace slot under the focus filter

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Insert_index_is_the_slot_when_every_workspace_is_rendered(int slot) =>
        Assert.Equal(slot, SidebarDrop.WorkspaceInsertIndex([0, 1, 2], slot));

    [Fact]
    public void Insert_index_above_the_first_rendered_row_lands_just_before_it() =>
        Assert.Equal(1, SidebarDrop.WorkspaceInsertIndex([1, 3], 0));

    [Fact]
    public void Insert_index_between_rendered_rows_lands_just_after_the_row_above_the_cursor() =>
        Assert.Equal(2, SidebarDrop.WorkspaceInsertIndex([1, 3], 1));

    [Fact]
    public void Insert_index_below_the_last_rendered_row_lands_just_after_it() =>
        Assert.Equal(4, SidebarDrop.WorkspaceInsertIndex([1, 3], 2));

    [Fact]
    public void Insert_index_clamps_a_slot_past_the_rendered_rows() =>
        Assert.Equal(4, SidebarDrop.WorkspaceInsertIndex([1, 3], 9));

    [Fact]
    public void Insert_index_with_nothing_rendered_is_zero() =>
        Assert.Equal(0, SidebarDrop.WorkspaceInsertIndex([], 2));

    // MARK: - flagged view (upstream 8088e146)

    private static readonly Guid[] Ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
    private static readonly Guid[] Flat = Ids[..4];

    private static Guid[] FlatIds(int[] indices) => indices.Select(i => Flat[i]).ToArray();

    [Theory]
    [InlineData(new[] { 3 }, null, 0, new[] { 3 }, FlaggedOrderPlacement.Before, 0)]
    [InlineData(new[] { 0 }, null, 4, new[] { 0 }, FlaggedOrderPlacement.After, 3)]
    [InlineData(new[] { 0 }, null, 2, new[] { 0 }, FlaggedOrderPlacement.Before, 2)]
    [InlineData(new[] { 1 }, 3, -1, new[] { 1 }, FlaggedOrderPlacement.After, 3)]
    [InlineData(new[] { 2, 0 }, null, 2, new[] { 0, 2 }, FlaggedOrderPlacement.Before, 3)]
    [InlineData(new[] { 3, 2 }, null, 0, new[] { 2, 3 }, FlaggedOrderPlacement.Before, 0)]
    public void Flat_drop_resolves_to_a_move_of_the_block_in_list_order(
        int[] dragged, int? onRow, int slot, int[] wantIds, FlaggedOrderPlacement wantPlacement, int wantAnchor)
    {
        var move = SidebarDrop.ResolveFlaggedReorder(Flat, FlatIds(dragged),
                                                     onRow is { } row ? Flat[row] : null, slot);
        Assert.NotNull(move);
        Assert.Equal(FlatIds(wantIds), move.Value.Ids);
        Assert.Equal(wantPlacement, move.Value.Placement);
        Assert.Equal(Flat[wantAnchor], move.Value.Anchor);
    }

    [Theory]
    [InlineData(new[] { 1 }, null, 1)]
    [InlineData(new[] { 1 }, null, 2)]
    [InlineData(new[] { 1 }, 1, -1)]
    [InlineData(new[] { 1 }, 0, -1)]
    [InlineData(new[] { 3 }, null, 4)]
    [InlineData(new[] { 0, 1 }, null, 0)]
    [InlineData(new[] { 0, 1, 2, 3 }, null, 2)]
    public void Flat_drop_that_keeps_the_order_is_a_no_op(int[] dragged, int? onRow, int slot) =>
        Assert.Null(SidebarDrop.ResolveFlaggedReorder(Flat, FlatIds(dragged),
                                                      onRow is { } row ? Flat[row] : null, slot));

    [Theory]
    [InlineData(new[] { 3, 1 }, FlaggedOrderPlacement.Before, 0, new[] { 1, 3, 0, 2 })]
    [InlineData(new[] { 0 }, FlaggedOrderPlacement.After, 3, new[] { 1, 2, 3, 0 })]
    [InlineData(new[] { 1 }, FlaggedOrderPlacement.Before, 2, null)]
    [InlineData(new[] { 1 }, FlaggedOrderPlacement.After, 0, null)]
    [InlineData(new[] { 1 }, FlaggedOrderPlacement.Before, 1, null)]
    public void Reordered_flagged_moves_the_block_in_list_order(int[] moving, FlaggedOrderPlacement placement,
                                                                int anchor, int[]? want)
    {
        var reordered = SidebarDrop.ReorderedFlagged(Flat, FlatIds(moving).ToHashSet(), placement, Flat[anchor]);
        if (want is null) Assert.Null(reordered);
        else Assert.Equal(FlatIds(want), reordered);
    }

    [Fact]
    public void Reordered_flagged_with_an_anchor_outside_the_list_is_null() =>
        Assert.Null(SidebarDrop.ReorderedFlagged(Flat, new HashSet<Guid> { Flat[0] },
                                                 FlaggedOrderPlacement.Before, Guid.NewGuid()));

    [Fact]
    public void Flat_drop_of_rows_outside_the_list_is_a_no_op()
    {
        Assert.Null(SidebarDrop.ResolveFlaggedReorder(Flat, [Guid.NewGuid()], null, 0));
        Assert.Null(SidebarDrop.ResolveFlaggedReorder([], [Flat[0]], null, 0));
    }

    [Theory]
    [InlineData(new[] { 1, 3 }, 0, 1)]
    [InlineData(new[] { 1, 3 }, 1, 3)]
    [InlineData(new[] { 1, 3 }, 2, 4)]
    [InlineData(new[] { 0, 1 }, 2, 2)]
    [InlineData(new[] { 1, 3 }, OnItem, OnItem)]
    [InlineData(new int[0], 0, 0)]
    public void Flagged_tree_gap_maps_next_to_the_visible_row_it_was_aimed_at(int[] visible, int slot, int want) =>
        Assert.Equal(want, SidebarDrop.FlaggedTreeChildIndex(visible, slot));

    private static readonly Guid TA = Ids[0], TH = Ids[1], TB = Ids[2], TC = Ids[3], TK = Ids[4];

    // wsA: a, h (hidden, unflagged), b; wsB: c, k (hidden, unflagged)
    private static readonly SidebarDrop.FlaggedTree Tree = new(
        [(WsA, [TA, TH, TB]), (WsB, [TC, TK])], new HashSet<Guid> { TA, TB, TC });

    private abstract record TreeTarget
    {
        public sealed record Gap(Guid Workspace, int[] Visible, int Count, int Slot) : TreeTarget;

        public sealed record OnWorkspace(Guid Workspace, int Count) : TreeTarget;

        public sealed record OnSession(Guid Workspace, int Index, int Count) : TreeTarget;
    }

    private static SidebarDrop.SessionResolution? TreeDrop(Guid dragged, (Guid Workspace, int Index) source,
                                                           TreeTarget target)
    {
        SidebarDrop.SessionSource[] sources = [new(source.Workspace, source.Index)];
        var resolution = target switch
        {
            TreeTarget.Gap(var workspace, var visible, var count, var slot) => SidebarDrop.ResolveSessions(
                sources, new SidebarDrop.SessionDropTarget.WorkspaceRow(workspace, count),
                SidebarDrop.FlaggedTreeChildIndex(visible, slot)),
            TreeTarget.OnWorkspace(var workspace, var count) => SidebarDrop.ResolveSessions(
                sources, new SidebarDrop.SessionDropTarget.WorkspaceRow(workspace, count),
                SidebarDrop.FlaggedTreeChildIndex([], OnItem)),
            TreeTarget.OnSession(var workspace, var index, var count) => SidebarDrop.ResolveSessions(
                sources, new SidebarDrop.SessionDropTarget.SessionRow(workspace, index, count), OnItem),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        return resolution is { } move && Tree.ChangesView([dragged], move) ? move : null;
    }

    [Fact]
    public void Flagged_tree_drop_back_above_the_next_flagged_row_across_a_hidden_sibling_is_a_no_op() =>
        Assert.Null(TreeDrop(TA, (WsA, 0), new TreeTarget.Gap(WsA, [0, 2], 3, 1)));

    [Fact]
    public void Flagged_tree_reorder_inside_a_workspace_keeps_the_hidden_sibling_in_place()
    {
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 0, 0),
                     TreeDrop(TB, (WsA, 2), new TreeTarget.Gap(WsA, [0, 2], 3, 0)));
        Assert.Equal(new SidebarDrop.SessionResolution(WsA, 3, 2),
                     TreeDrop(TA, (WsA, 0), new TreeTarget.Gap(WsA, [0, 2], 3, 2)));
    }

    [Fact]
    public void Flagged_tree_move_to_another_workspace_counts_even_when_the_flat_order_stays_the_same() =>
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 0, 0),
                     TreeDrop(TB, (WsA, 2), new TreeTarget.Gap(WsB, [0], 2, 0)));

    [Fact]
    public void Flagged_tree_end_of_group_gap_lands_right_after_the_last_flagged_row() =>
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 1, 1),
                     TreeDrop(TA, (WsA, 0), new TreeTarget.Gap(WsB, [0], 2, 1)));

    [Fact]
    public void Flagged_tree_drop_on_a_workspace_row_appends_after_its_last_session() =>
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, OnItem, 2),
                     TreeDrop(TA, (WsA, 0), new TreeTarget.OnWorkspace(WsB, 2)));

    [Fact]
    public void Flagged_tree_drop_on_a_session_row_lands_right_after_it() =>
        Assert.Equal(new SidebarDrop.SessionResolution(WsB, 1, 1),
                     TreeDrop(TA, (WsA, 0), new TreeTarget.OnSession(WsB, 0, 2)));

    [Fact]
    public void Flagged_tree_drop_on_its_own_row_is_a_no_op() =>
        Assert.Null(TreeDrop(TB, (WsA, 2), new TreeTarget.OnSession(WsA, 2, 3)));
}
