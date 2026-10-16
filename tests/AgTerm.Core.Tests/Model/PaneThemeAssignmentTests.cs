using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

public class PaneThemeAssignmentTests
{
    private readonly AppStore _store = AppStoreFixtures.MakeStore();

    private static Session Realized(Session session, string token)
    {
        session.Surface = new SpySurface(token);
        return session;
    }

    [Fact]
    public void Every_pane_of_a_session_takes_its_workspace_theme()
    {
        var workspace = _store.AddWorkspace("w");
        var session = Realized(_store.AddSession(workspace.Id, "C:\\")!, "main");
        session.SplitSurface = new SpySurface("split");
        session.ScratchSurface = new SpySurface("scratch");
        session.OverlaySurface = new SpySurface("overlay");
        session.SearchSurface = session.SplitSurface;
        _store.SetWorkspaceTheme(workspace.Id, "Nord");

        var table = PaneThemeAssignment.For(_store);

        var entry = new PaneThemeEntry(workspace.Id, "Nord");
        Assert.Equal(
            new Dictionary<string, PaneThemeEntry>
            {
                ["main"] = entry, ["split"] = entry, ["scratch"] = entry, ["overlay"] = entry,
            },
            table.Panes);
    }

    [Fact]
    public void A_workspace_without_a_theme_maps_its_panes_to_the_global_theme()
    {
        var workspace = _store.AddWorkspace("w");
        Realized(_store.AddSession(workspace.Id, "C:\\")!, "main");

        Assert.Equal(new PaneThemeEntry(workspace.Id, null), PaneThemeAssignment.For(_store).Panes["main"]);
    }

    [Fact]
    public void A_session_without_surfaces_has_no_panes_and_the_quick_terminal_is_never_listed()
    {
        var workspace = _store.AddWorkspace("w");
        _ = _store.AddSession(workspace.Id, "C:\\", select: false);

        Assert.Empty(PaneThemeAssignment.For(_store).Panes);
    }

    [Fact]
    public void A_moved_session_takes_the_theme_of_its_new_workspace()
    {
        var plain = _store.AddWorkspace("plain");
        var themed = _store.AddWorkspace("themed");
        _store.SetWorkspaceTheme(themed.Id, "Nord");
        var session = Realized(_store.AddSession(plain.Id, "C:\\")!, "main");

        _store.MoveSession(session.Id, themed.Id, 0);

        Assert.Equal(new PaneThemeEntry(themed.Id, "Nord"), PaneThemeAssignment.For(_store).Panes["main"]);
    }

    [Fact]
    public void The_selected_entry_is_the_selected_session_workspace_even_before_its_pane_exists()
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceTheme(workspace.Id, "Nord");
        var session = _store.AddSession(workspace.Id, "C:\\")!;
        _store.SelectSession(session.Id);

        var table = PaneThemeAssignment.For(_store);

        Assert.Equal(new PaneThemeEntry(workspace.Id, "Nord"), table.Selected);
        Assert.Equal("Nord", PaneThemeAssignment.ForSession(_store, session.Id));
    }

    [Fact]
    public void Nothing_selected_has_no_selected_entry_and_an_unknown_session_has_no_theme()
    {
        _ = _store.AddWorkspace("w");

        Assert.Null(PaneThemeAssignment.For(_store).Selected);
        Assert.Null(PaneThemeAssignment.ForSession(_store, Guid.NewGuid()));
    }

    [Fact]
    public void Every_workspace_is_listed_also_one_without_sessions()
    {
        var first = _store.AddWorkspace("first");
        var empty = _store.AddWorkspace("empty");

        Assert.Equal(new HashSet<Guid> { first.Id, empty.Id }, PaneThemeAssignment.For(_store).WorkspaceIds);
    }

    [Fact]
    public void Two_tables_built_from_the_same_tree_are_equal()
    {
        var workspace = _store.AddWorkspace("w");
        var session = Realized(_store.AddSession(workspace.Id, "C:\\")!, "main");
        _store.SelectSession(session.Id);
        _store.SetWorkspaceTheme(workspace.Id, "Nord");

        var first = PaneThemeAssignment.For(_store);
        var second = PaneThemeAssignment.For(_store);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void A_theme_change_makes_the_tables_differ()
    {
        var workspace = _store.AddWorkspace("w");
        Realized(_store.AddSession(workspace.Id, "C:\\")!, "main");
        var before = PaneThemeAssignment.For(_store);

        _store.SetWorkspaceTheme(workspace.Id, "Nord");

        Assert.NotEqual(before, PaneThemeAssignment.For(_store));
    }

    [Fact]
    public void A_table_with_one_workspace_theme_replaced_changes_only_that_workspace()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var table = new PaneThemeTable(
            new Dictionary<string, PaneThemeEntry> { ["p1"] = new(a, "Nord"), ["p2"] = new(b, null) },
            new HashSet<Guid> { a, b },
            new PaneThemeEntry(b, null));

        var replaced = table.WithWorkspaceTheme(b, "Dracula");

        Assert.Equal(new PaneThemeEntry(a, "Nord"), replaced.Panes["p1"]);
        Assert.Equal(new PaneThemeEntry(b, "Dracula"), replaced.Panes["p2"]);
        Assert.Equal(new PaneThemeEntry(b, "Dracula"), replaced.Selected);
        Assert.Equal(table.WorkspaceIds, replaced.WorkspaceIds);
    }
}
