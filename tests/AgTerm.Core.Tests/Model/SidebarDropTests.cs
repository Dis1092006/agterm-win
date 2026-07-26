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
    public void Empty_directory_drop_prefers_focused_workspace() =>
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
}
