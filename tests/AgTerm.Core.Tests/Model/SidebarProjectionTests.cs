using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// SidebarProjection — the host-free row model the WinUI sidebar renders. Tree mode walks
/// VisibleWorkspaces (the focus filter) and lists sessions only under expanded workspaces;
/// flagged mode is the flat "name : workspace" list. Rows are records, so the view reconciles by
/// per-row equality; ShapeKey changes force a full rebuild (row identity/order changed).
/// </summary>
public class SidebarProjectionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-proj-").FullName;
    private readonly AppStore _store;

    public SidebarProjectionTests()
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
        return (w1, w2, a, b, c);
    }

    [Fact]
    public void Tree_mode_lists_workspaces_with_their_sessions_in_order()
    {
        var (w1, w2, a, b, c) = Seed();
        var rows = SidebarProjection.Project(_store);
        Assert.Equal(new[] { w1.Id, a.Id, b.Id, w2.Id, c.Id }, rows.Select(r => r.Id));
        Assert.Equal(
            new[] { SidebarRowKind.Workspace, SidebarRowKind.Session, SidebarRowKind.Session,
                    SidebarRowKind.Workspace, SidebarRowKind.Session },
            rows.Select(r => r.Kind));
        Assert.Equal("work", rows[0].Label);
        Assert.Equal("api", rows[1].Label);       // displayName = cwd basename
        Assert.True(rows[1].Selected);
        Assert.False(rows[2].Selected);
    }

    [Fact]
    public void A_collapsed_workspace_hides_its_sessions_but_keeps_the_unseen_rollup()
    {
        var (w1, _, a, b, _) = Seed();
        b.UnseenCount = 2;
        a.UnseenCount = 1;
        _store.SetWorkspaceExpanded(w1.Id, false);
        var rows = SidebarProjection.Project(_store);
        Assert.DoesNotContain(rows, r => r.Id == a.Id || r.Id == b.Id);
        var w1Row = rows.Single(r => r.Id == w1.Id);
        Assert.True(w1Row.Collapsed);
        Assert.Equal(3, w1Row.Unseen);
    }

    [Fact]
    public void Focus_filters_to_one_workspace_and_marks_it_focused()
    {
        var (w1, w2, _, _, c) = Seed();
        _store.SetFocusedWorkspace(w2.Id);
        var rows = SidebarProjection.Project(_store);
        Assert.Equal(new[] { w2.Id, c.Id }, rows.Select(r => r.Id));
        Assert.True(rows[0].Focused);
        _ = w1;
    }

    [Fact]
    public void Flagged_mode_is_the_flat_decorated_list_without_workspace_rows()
    {
        var (_, w2, a, _, c) = Seed();
        _store.SetFlag(true, a.Id);
        _store.SetFlag(true, c.Id);
        _store.SetFocusedWorkspace(w2.Id); // flagged view ignores focus
        _store.SetSidebarMode(SidebarMode.Flagged);

        var rows = SidebarProjection.Project(_store);
        Assert.Equal(new[] { a.Id, c.Id }, rows.Select(r => r.Id));
        Assert.All(rows, r => Assert.Equal(SidebarRowKind.Session, r.Kind));
        Assert.Equal("api : work", rows[0].Label);
        Assert.Equal("home : personal", rows[1].Label);
        Assert.All(rows, r => Assert.True(r.Flagged));
    }

    // upstream f8c3eba3: plain is the flat list labelled by the session name alone
    [Fact]
    public void The_plain_flagged_layout_is_the_flat_list_without_workspace_names()
    {
        var (_, _, a, _, c) = Seed();
        _store.SetFlag(true, a.Id);
        _store.SetFlag(true, c.Id);
        _store.SetSidebarMode(SidebarMode.Flagged);
        _store.FlaggedLayout = FlaggedViewLayout.Plain;

        var rows = SidebarProjection.Project(_store);
        Assert.Equal(new[] { a.Id, c.Id }, rows.Select(r => r.Id));
        Assert.All(rows, r => Assert.Equal(SidebarRowKind.Session, r.Kind));
        Assert.Equal(new[] { "api", "home" }, rows.Select(r => r.Label));
    }

    // upstream 902cd853 workspaceProjection: every workspace with a flagged session, focus ignored,
    // its flagged sessions only, labelled by name alone
    [Fact]
    public void The_tree_flagged_layout_nests_flagged_sessions_under_their_workspace_rows()
    {
        var (w1, w2, a, _, c) = Seed();
        var w3 = _store.AddWorkspace("idle", revealNewWorkspace: false);
        _store.AddSession(w3.Id, "C:\\idle", select: false);
        _store.SetFlag(true, a.Id);
        _store.SetFlag(true, c.Id);
        _store.SetFocusedWorkspace(w2.Id);
        _store.SetSidebarMode(SidebarMode.Flagged);
        _store.FlaggedLayout = FlaggedViewLayout.Tree;

        var rows = SidebarProjection.Project(_store);
        Assert.Equal(new[] { w1.Id, a.Id, w2.Id, c.Id }, rows.Select(r => r.Id));
        Assert.Equal(
            new[] { SidebarRowKind.Workspace, SidebarRowKind.Session, SidebarRowKind.Workspace, SidebarRowKind.Session },
            rows.Select(r => r.Kind));
        Assert.Equal(new[] { "work", "api", "personal", "home" }, rows.Select(r => r.Label));
        Assert.Equal(w1.Id, rows[1].WorkspaceId);
    }

    [Fact]
    public void A_collapsed_workspace_in_the_flagged_tree_hides_its_flagged_sessions()
    {
        var (w1, _, a, _, c) = Seed();
        _store.SetFlag(true, a.Id);
        _store.SetFlag(true, c.Id);
        _store.SetWorkspaceExpanded(w1.Id, false);
        _store.SetSidebarMode(SidebarMode.Flagged);
        _store.FlaggedLayout = FlaggedViewLayout.Tree;

        var rows = SidebarProjection.Project(_store);
        Assert.DoesNotContain(rows, r => r.Id == a.Id);
        Assert.True(rows.Single(r => r.Id == w1.Id).Collapsed);
        Assert.Contains(rows, r => r.Id == c.Id);
    }

    // upstream displayedUnseen: a flagged-tree workspace row counts only the children it renders
    [Fact]
    public void A_flagged_tree_workspace_row_counts_only_its_flagged_sessions_unseen()
    {
        var (w1, _, a, b, _) = Seed();
        a.UnseenCount = 2;
        b.UnseenCount = 5;
        _store.SetFlag(true, a.Id);
        _store.SetSidebarMode(SidebarMode.Flagged);
        _store.FlaggedLayout = FlaggedViewLayout.Tree;
        Assert.Equal(2, SidebarProjection.Project(_store).Single(r => r.Id == w1.Id).Unseen);

        _store.SetWorkspaceExpanded(w1.Id, false);
        Assert.Equal(2, SidebarProjection.Project(_store).Single(r => r.Id == w1.Id).Unseen);

        _store.SetSidebarMode(SidebarMode.Tree);
        Assert.Equal(7, SidebarProjection.Project(_store).Single(r => r.Id == w1.Id).Unseen);
    }

    // the layout is dormant under the ordinary tree
    [Fact]
    public void The_flagged_layout_does_not_change_the_ordinary_tree()
    {
        var (_, _, a, _, _) = Seed();
        _store.SetFlag(true, a.Id);
        var rows = SidebarProjection.Project(_store);
        var key = SidebarProjection.ShapeKey(_store);

        _store.FlaggedLayout = FlaggedViewLayout.Tree;

        Assert.Equal(rows, SidebarProjection.Project(_store));
        Assert.Equal(key, SidebarProjection.ShapeKey(_store));
    }

    [Fact]
    public void The_shape_key_changes_with_the_flagged_layout_in_flagged_mode()
    {
        var (_, _, a, _, c) = Seed();
        _store.SetFlag(true, a.Id);
        _store.SetFlag(true, c.Id);
        _store.SetSidebarMode(SidebarMode.Flagged);
        var flat = SidebarProjection.ShapeKey(_store);

        _store.FlaggedLayout = FlaggedViewLayout.Plain;
        var plain = SidebarProjection.ShapeKey(_store);
        _store.FlaggedLayout = FlaggedViewLayout.Tree;
        var tree = SidebarProjection.ShapeKey(_store);

        Assert.NotEqual(flat, plain);
        Assert.NotEqual(flat, tree);
        Assert.NotEqual(plain, tree);
    }

    [Fact]
    public void Session_rows_carry_split_flag_indicator_and_unseen()
    {
        var (_, _, a, _, _) = Seed();
        a.HasSplit = true;
        a.UnseenCount = 7;
        _store.SetFlag(true, a.Id);
        _store.SetAgentIndicator(
            new AgentIndicator(AgentStatus.Blocked, Blink: true, Color: "#123456"), a.Id);

        var row = SidebarProjection.Project(_store).Single(r => r.Id == a.Id);
        Assert.True(row.HasSplit);
        Assert.True(row.Flagged);
        Assert.Equal(7, row.Unseen);
        Assert.Equal(AgentStatus.Blocked, row.Indicator.Status);
        Assert.True(row.Indicator.Blink);
        Assert.Equal("#123456", row.Indicator.Color);
    }

    [Fact]
    public void Shape_key_tracks_identity_order_mode_and_collapse_but_not_labels()
    {
        var (w1, _, a, _, _) = Seed();
        var initial = SidebarProjection.ShapeKey(_store);

        _store.RenameSession(a.Id, "renamed");
        Assert.Equal(initial, SidebarProjection.ShapeKey(_store)); // content diff, not a rebuild

        _store.ReorderSession(a.Id, ReorderDirection.Bottom);
        var reordered = SidebarProjection.ShapeKey(_store);
        Assert.NotEqual(initial, reordered);

        _store.SetWorkspaceExpanded(w1.Id, false);
        var collapsed = SidebarProjection.ShapeKey(_store);
        Assert.NotEqual(reordered, collapsed);

        _store.SetSidebarMode(SidebarMode.Flagged);
        Assert.NotEqual(collapsed, SidebarProjection.ShapeKey(_store));
    }

    [Fact]
    public void Workspace_and_session_rows_carry_the_workspace_and_its_theme()
    {
        var (w1, w2, a, _, c) = Seed();
        _store.SetWorkspaceTheme(w1.Id, "Ubuntu");

        var rows = SidebarProjection.Project(_store);

        var w1Row = rows.Single(r => r.Id == w1.Id);
        Assert.Equal((w1.Id, "Ubuntu"), (w1Row.WorkspaceId, w1Row.ThemeName));
        var aRow = rows.Single(r => r.Id == a.Id);
        Assert.Equal((w1.Id, "Ubuntu"), (aRow.WorkspaceId, aRow.ThemeName));
        var cRow = rows.Single(r => r.Id == c.Id);
        Assert.Equal((w2.Id, (string?)null), (cRow.WorkspaceId, cRow.ThemeName));
    }

    [Fact]
    public void A_workspace_row_carries_the_icon_name_and_session_rows_do_not()
    {
        var (w1, _, a, _, _) = Seed();
        _store.SetWorkspaceIcon(w1.Id, "MapPin");

        var rows = SidebarProjection.Project(_store);

        Assert.Equal("MapPin", rows.Single(r => r.Id == w1.Id).IconName);
        Assert.Null(rows.Single(r => r.Id == a.Id).IconName);
    }

    [Fact]
    public void Flagged_rows_carry_the_theme_of_their_own_workspace()
    {
        var (w1, w2, a, _, c) = Seed();
        _store.SetFlag(true, a.Id);
        _store.SetFlag(true, c.Id);
        _store.SetWorkspaceTheme(w2.Id, "Nord");
        _store.SetSidebarMode(SidebarMode.Flagged);

        var rows = SidebarProjection.Project(_store);

        var aRow = rows.Single(r => r.Id == a.Id);
        Assert.Equal((w1.Id, (string?)null), (aRow.WorkspaceId, aRow.ThemeName));
        var cRow = rows.Single(r => r.Id == c.Id);
        Assert.Equal((w2.Id, "Nord"), (cRow.WorkspaceId, cRow.ThemeName));
    }

    [Fact]
    public void A_moved_session_takes_the_theme_of_its_new_workspace()
    {
        var (_, w2, a, _, _) = Seed();
        _store.SetWorkspaceTheme(w2.Id, "Nord");

        _store.MoveSession(a.Id, w2.Id);

        var aRow = SidebarProjection.Project(_store).Single(r => r.Id == a.Id);
        Assert.Equal((w2.Id, "Nord"), (aRow.WorkspaceId, aRow.ThemeName));
    }

    [Fact]
    public void A_theme_change_changes_the_rows_but_not_the_shape_key()
    {
        var (w1, _, _, _, _) = Seed();
        var rowsBefore = SidebarProjection.Project(_store);
        var keyBefore = SidebarProjection.ShapeKey(_store);

        _store.SetWorkspaceTheme(w1.Id, "Ubuntu");

        Assert.False(rowsBefore.SequenceEqual(SidebarProjection.Project(_store)));
        Assert.Equal(keyBefore, SidebarProjection.ShapeKey(_store));
    }

    [Fact]
    public void Each_row_carries_its_own_tint_and_a_workspace_tint_never_reaches_its_sessions()
    {
        var (w1, _, a, b, _) = Seed();
        _store.SetWorkspaceTint(w1.Id, new SidebarTint(TintColor.Blue, TintParts.Name));
        _store.SetSessionTint(a.Id, new SidebarTint(TintColor.Red));

        var rows = SidebarProjection.Project(_store);

        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Name), rows.Single(r => r.Id == w1.Id).Tint);
        Assert.Equal(new SidebarTint(TintColor.Red), rows.Single(r => r.Id == a.Id).Tint);
        Assert.Null(rows.Single(r => r.Id == b.Id).Tint);
    }

    [Fact]
    public void A_flagged_row_carries_its_session_tint()
    {
        var (_, _, a, _, _) = Seed();
        _store.SetSessionTint(a.Id, new SidebarTint(TintColor.Green, TintParts.Icon | TintParts.Name));
        _store.SetFlag(true, a.Id);
        _store.SetSidebarMode(SidebarMode.Flagged);

        Assert.Equal(new SidebarTint(TintColor.Green, TintParts.Icon | TintParts.Name),
            SidebarProjection.Project(_store).Single().Tint);
    }

    [Fact]
    public void A_tint_change_changes_the_rows_but_not_the_shape_key()
    {
        var (_, _, a, _, _) = Seed();
        var rowsBefore = SidebarProjection.Project(_store);
        var keyBefore = SidebarProjection.ShapeKey(_store);

        _store.SetSessionTint(a.Id, new SidebarTint(TintColor.Gray));

        Assert.False(rowsBefore.SequenceEqual(SidebarProjection.Project(_store)));
        Assert.Equal(keyBefore, SidebarProjection.ShapeKey(_store));
    }

    [Fact]
    public void A_session_row_carries_its_info_line_and_a_workspace_row_carries_none()
    {
        var (w1, _, a, b, _) = Seed();
        var info = SessionInfo.Make("**3** agents", true, 0, out _)!;
        _store.SetInfo(info, a.Id);

        var rows = SidebarProjection.Project(_store);

        Assert.Equal(info, rows.Single(r => r.Id == a.Id).Info);
        Assert.Null(rows.Single(r => r.Id == b.Id).Info);
        Assert.Null(rows.Single(r => r.Id == w1.Id).Info);
    }

    [Fact]
    public void An_info_change_changes_the_rows_but_not_the_shape_key()
    {
        var (_, _, a, _, _) = Seed();
        _store.SetInfo(SessionInfo.Make("idle", false, 0, out _)!, a.Id);
        var rowsBefore = SidebarProjection.Project(_store);
        var keyBefore = SidebarProjection.ShapeKey(_store);

        _store.SetInfo(SessionInfo.Make("busy", false, 0, out _)!, a.Id);

        Assert.False(rowsBefore.SequenceEqual(SidebarProjection.Project(_store)));
        Assert.Equal(keyBefore, SidebarProjection.ShapeKey(_store));
        _store.SetInfo(SessionInfo.Make("busy", false, 0, out _)!, a.Id);
        Assert.True(SidebarProjection.Project(_store).SequenceEqual(SidebarProjection.Project(_store)));
    }

    [Theory]
    [InlineData(SessionInfoDisplay.Always, SidebarMode.Tree, true)]
    [InlineData(SessionInfoDisplay.Always, SidebarMode.Flagged, true)]
    [InlineData(SessionInfoDisplay.Flagged, SidebarMode.Tree, false)]
    [InlineData(SessionInfoDisplay.Flagged, SidebarMode.Flagged, true)]
    [InlineData(SessionInfoDisplay.Off, SidebarMode.Tree, false)]
    [InlineData(SessionInfoDisplay.Off, SidebarMode.Flagged, false)]
    public void The_display_setting_gates_the_line_by_sidebar_mode(SessionInfoDisplay display, SidebarMode mode, bool shows)
    {
        Assert.Equal(shows, SessionInfoDisplays.Shows(display, mode));
    }
}
