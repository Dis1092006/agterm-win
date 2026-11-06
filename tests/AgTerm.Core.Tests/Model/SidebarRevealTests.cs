using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// The view-only reveal (upstream WorkspaceSidebar.syncSelection, 902cd853): the sidebar opens the
/// selected session's collapsed workspace on screen without saving Workspace.IsExpanded, and only
/// when the selection changes or the sidebar switches views. SyncSidebarReveal is the render-time
/// step the sidebar runs before it projects the rows.
/// </summary>
public class SidebarRevealTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-reveal-").FullName;
    private readonly AppStore _store;

    public SidebarRevealTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (Workspace W1, Workspace W2, Session A, Session B, Session C) Seed()
    {
        var w1 = _store.AddWorkspace("work");
        var a = _store.AddSession(w1.Id, "C:\\proj\\api")!;
        var b = _store.AddSession(w1.Id, "C:\\proj\\web", select: false)!;
        var w2 = _store.AddWorkspace("personal", revealNewWorkspace: false);
        var c = _store.AddSession(w2.Id, "C:\\home", select: false)!;
        _store.SelectSession(a.Id);
        Rows();
        return (w1, w2, a, b, c);
    }

    private IReadOnlyList<SidebarRow> Rows()
    {
        _store.SyncSidebarReveal();
        return SidebarProjection.Project(_store);
    }

    [Fact]
    public void Selecting_a_session_in_a_collapsed_workspace_opens_it_on_screen_without_saving()
    {
        var (_, w2, _, _, c) = Seed();
        _store.SetWorkspaceExpanded(w2.Id, false);
        Assert.DoesNotContain(Rows(), r => r.Id == c.Id);

        _store.SelectSession(c.Id);
        var rows = Rows();

        Assert.Contains(rows, r => r.Id == c.Id);
        Assert.False(rows.Single(r => r.Id == w2.Id).Collapsed);
        Assert.True(_store.IsWorkspaceOpenInSidebar(w2));
        Assert.False(w2.IsExpanded);
        Assert.True(_store.Snapshot().Workspaces.Single(w => w.Id == w2.Id).Collapsed);
    }

    [Fact]
    public void A_revealed_workspace_stays_open_after_the_selection_moves_on()
    {
        var (_, w2, a, _, c) = Seed();
        _store.SetWorkspaceExpanded(w2.Id, false);
        _store.SelectSession(c.Id);
        Rows();

        _store.SelectSession(a.Id);

        Assert.Contains(Rows(), r => r.Id == c.Id);
    }

    [Fact]
    public void A_collapse_after_the_reveal_holds_until_the_selection_changes()
    {
        var (_, w2, _, _, c) = Seed();
        _store.SetWorkspaceExpanded(w2.Id, false);
        _store.SelectSession(c.Id);
        Rows();

        _store.SetWorkspaceExpanded(w2.Id, false);
        Assert.DoesNotContain(Rows(), r => r.Id == c.Id);

        // an unrelated update to the selected session, or reselecting it, is not a selection change
        _store.RenameSession(c.Id, "renamed");
        _store.SelectSession(c.Id);
        Assert.DoesNotContain(Rows(), r => r.Id == c.Id);
        Assert.False(_store.IsWorkspaceOpenInSidebar(w2));
    }

    [Fact]
    public void A_sidebar_mode_switch_reveals_the_selection_again()
    {
        var (_, w2, _, _, c) = Seed();
        _store.SetFlag(true, c.Id);
        _store.SelectSession(c.Id);
        Rows();
        _store.SetWorkspaceExpanded(w2.Id, false);
        Assert.DoesNotContain(Rows(), r => r.Id == c.Id);

        _store.FlaggedLayout = FlaggedViewLayout.Tree;
        _store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Contains(Rows(), r => r.Id == c.Id);

        _store.SetWorkspaceExpanded(w2.Id, false);
        _store.SetSidebarMode(SidebarMode.Tree);
        Assert.Contains(Rows(), r => r.Id == c.Id);
        Assert.False(w2.IsExpanded);
    }

    [Fact]
    public void A_flagged_layout_switch_reveals_the_selection_again()
    {
        var (_, w2, _, _, c) = Seed();
        _store.SetFlag(true, c.Id);
        _store.SetSidebarMode(SidebarMode.Flagged);
        _store.SelectSession(c.Id);
        Rows();
        _store.FlaggedLayout = FlaggedViewLayout.Tree;
        Rows();
        _store.SetWorkspaceExpanded(w2.Id, false);
        Assert.DoesNotContain(Rows(), r => r.Id == c.Id);

        _store.FlaggedLayout = FlaggedViewLayout.Flat;
        Rows();
        _store.FlaggedLayout = FlaggedViewLayout.Tree;

        Assert.Contains(Rows(), r => r.Id == c.Id);
    }

    [Fact]
    public void The_flagged_tree_does_not_open_a_workspace_for_an_unflagged_selection()
    {
        var (_, w2, _, _, c) = Seed();
        _store.SetFlag(true, c.Id);
        _store.FlaggedLayout = FlaggedViewLayout.Tree;
        _store.SetSidebarMode(SidebarMode.Flagged); // reselects c, the only flagged session
        Rows();
        _store.SetWorkspaceExpanded(w2.Id, false);
        Assert.False(_store.IsWorkspaceOpenInSidebar(w2));
        var d = _store.AddSession(w2.Id, "C:\\tmp", select: false)!;

        _store.SelectSession(d.Id);
        Rows();

        Assert.False(_store.IsWorkspaceOpenInSidebar(w2));
    }

    [Fact]
    public void A_hidden_sidebar_reveals_nothing_until_it_is_shown_and_forgets_its_reveals()
    {
        var (w1, w2, a, _, c) = Seed();
        _store.SetWorkspaceExpanded(w1.Id, false);
        _store.SetWorkspaceExpanded(w2.Id, false);
        _store.SelectSession(c.Id);
        Rows();
        Assert.True(_store.IsWorkspaceOpenInSidebar(w2));

        _store.SetSidebarVisible(false);
        _store.SelectSession(a.Id);
        Rows();
        Assert.False(_store.IsWorkspaceOpenInSidebar(w2));
        Assert.False(_store.IsWorkspaceOpenInSidebar(w1));

        _store.SetSidebarVisible(true);
        Rows();
        Assert.True(_store.IsWorkspaceOpenInSidebar(w1));
        Assert.False(_store.IsWorkspaceOpenInSidebar(w2));
    }

    [Fact]
    public void Collapse_workspaces_folds_revealed_rows_except_the_current_one()
    {
        var (w1, w2, a, _, c) = Seed();
        _store.SetWorkspaceExpanded(w2.Id, false);
        _store.SelectSession(c.Id);
        Rows();
        _store.SelectSession(a.Id);
        Rows();
        Assert.True(_store.IsWorkspaceOpenInSidebar(w2));

        _store.CollapseOtherWorkspaces();

        Assert.False(_store.IsWorkspaceOpenInSidebar(w2));
        Assert.True(_store.IsWorkspaceOpenInSidebar(w1));
    }

    [Fact]
    public void Collapsing_a_revealed_workspace_notifies_the_sidebar_though_nothing_is_saved()
    {
        var (_, w2, _, _, c) = Seed();
        _store.SetWorkspaceExpanded(w2.Id, false);
        _store.SelectSession(c.Id);
        Rows();
        var notified = 0;
        _store.SidebarChanged += () => notified++;

        _store.SetWorkspaceExpanded(w2.Id, false);

        Assert.Equal(1, notified);
    }

    [Fact]
    public void The_reveal_generation_moves_on_a_selection_change_or_a_view_switch_only()
    {
        var (_, _, a, b, _) = Seed();
        var start = _store.SidebarRevealGeneration;

        Rows();
        _store.RenameSession(a.Id, "renamed");
        Rows();
        Assert.Equal(start, _store.SidebarRevealGeneration);

        _store.SelectSession(b.Id);
        Rows();
        Assert.Equal(start + 1, _store.SidebarRevealGeneration);

        _store.SetFlag(true, b.Id);
        _store.SetSidebarMode(SidebarMode.Flagged);
        Rows();
        Assert.Equal(start + 2, _store.SidebarRevealGeneration);
    }

    [Fact]
    public void A_restore_reveals_the_restored_selection()
    {
        var (_, w2, _, _, c) = Seed();
        _store.SetWorkspaceExpanded(w2.Id, false);
        _store.SelectSession(c.Id);
        Rows();
        _store.SetWorkspaceExpanded(w2.Id, false);
        var saved = _store.Snapshot();

        _store.Restore(saved);
        var restored = _store.Workspaces.Single(w => w.Id == w2.Id);

        Assert.Contains(Rows(), r => r.Id == c.Id);
        Assert.False(restored.IsExpanded);
    }
}
