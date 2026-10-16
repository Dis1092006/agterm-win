using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class PaneThemeAssignerTests
{
    private static readonly Guid A = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000002");

    private static readonly ThemeCatalog Catalog = ThemeCatalog.FromSources(
    [
        ("Nord", ThemeTexts.Valid("#2e3440")),
        ("Dracula", ThemeTexts.Valid("#282a36")),
    ]);

    private readonly PaneThemeAssigner _assigner = new(Catalog.Resolve);

    private static PaneThemeTable Table(PaneThemeEntry? selected, params (string Pane, PaneThemeEntry Entry)[] panes) =>
        new(panes.ToDictionary(p => p.Pane, p => p.Entry, StringComparer.Ordinal),
            new HashSet<Guid> { A, B },
            selected);

    [Fact]
    public void Names_become_catalog_themes_and_panes_on_the_global_theme_are_left_alone()
    {
        var changes = _assigner.Update(
            Table(null, ("p1", new(A, "Nord")), ("p2", new(B, null))),
            globalBackground: true);

        var only = Assert.Single(changes.Panes);
        Assert.Equal("p1", only.Key);
        Assert.Same(Catalog.Resolve("Nord"), only.Value);
    }

    [Fact]
    public void The_same_table_again_sends_nothing()
    {
        var table = Table(new(A, "Nord"), ("p1", new(A, "Nord")));
        _assigner.Update(table, globalBackground: false);

        Assert.True(_assigner.Update(table, globalBackground: false).IsEmpty);
    }

    [Fact]
    public void Clearing_a_theme_sends_a_null_for_the_global_theme()
    {
        _assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true);

        var changes = _assigner.Update(Table(null, ("p1", new(A, null))), globalBackground: true);

        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p1"] = null }, changes.Panes);
    }

    [Fact]
    public void A_name_the_catalog_lacks_shows_the_global_theme()
    {
        _assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true);

        var changes = _assigner.Update(Table(new(A, "Gone"), ("p1", new(A, "Gone"))), globalBackground: false);

        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p1"] = null }, changes.Panes);
        Assert.Null(changes.Background);
        Assert.Null(_assigner.Resolve("Gone"));
    }

    [Fact]
    public void A_pane_that_opened_in_its_theme_is_not_sent_again()
    {
        Assert.True(_assigner.Opened("p1", "Nord").IsEmpty);

        Assert.True(_assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true).IsEmpty);
    }

    [Fact]
    public void A_pane_that_opened_with_an_older_theme_gets_the_current_one()
    {
        _assigner.Update(Table(null, ("p1", new(A, "Dracula"))), globalBackground: true);

        var changes = _assigner.Opened("p1", "Nord");

        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p1"] = Catalog.Resolve("Dracula") }, changes.Panes);
    }

    [Fact]
    public void A_pane_missing_from_the_table_keeps_what_it_has()
    {
        _assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true);

        Assert.True(_assigner.Update(Table(null), globalBackground: true).IsEmpty);
        Assert.True(_assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true).IsEmpty);
    }

    [Fact]
    public void A_closed_pane_is_forgotten()
    {
        _assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true);

        _assigner.Closed("p1");

        Assert.Single(_assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true).Panes);
    }

    [Fact]
    public void The_background_follows_the_selected_workspace_unless_the_layout_needs_the_global_one()
    {
        var table = Table(new(A, "Nord"), ("p1", new(A, "Nord")));

        var global = _assigner.Update(table, globalBackground: true);
        Assert.False(global.BackgroundChanged);

        var themed = _assigner.Update(table, globalBackground: false);
        Assert.True(themed.BackgroundChanged);
        Assert.Equal("#2e3440", themed.Background);

        var plain = _assigner.Update(Table(new(B, null), ("p1", new(A, "Nord"))), globalBackground: false);
        Assert.True(plain.BackgroundChanged);
        Assert.Null(plain.Background);
    }

    [Fact]
    public void The_dashboard_an_empty_layout_and_a_zoomed_quick_terminal_use_the_global_background()
    {
        var session = Guid.NewGuid();

        Assert.True(PaneThemeAssigner.UsesGlobalBackground(new LiveSelectionSnapshot(null, null)));
        Assert.True(PaneThemeAssigner.UsesGlobalBackground(
            new LiveSelectionSnapshot(session, "s", "w", MainPaneId: "p1", DashboardOpen: true)));
        Assert.True(PaneThemeAssigner.UsesGlobalBackground(
            new LiveSelectionSnapshot(session, "s", "w", MainPaneId: "p1", QuickPaneId: "q", ZoomPaneId: "q")));
        Assert.False(PaneThemeAssigner.UsesGlobalBackground(
            new LiveSelectionSnapshot(session, "s", "w", MainPaneId: "p1")));
        Assert.False(PaneThemeAssigner.UsesGlobalBackground(
            new LiveSelectionSnapshot(session, "s", "w", MainPaneId: "p1", QuickPaneId: "q", QuickVisible: true)));
    }

    [Fact]
    public void A_preview_changes_only_the_previewed_workspace_and_its_end_restores_it()
    {
        var table = Table(null, ("p1", new(A, "Nord")), ("p2", new(B, null)));
        _assigner.Update(table, globalBackground: true);

        var preview = _assigner.Preview(B, "Dracula");
        var ended = _assigner.EndPreview();

        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p2"] = Catalog.Resolve("Dracula") }, preview.Panes);
        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p2"] = null }, ended.Panes);
    }

    [Fact]
    public void Previewing_the_global_row_shows_the_global_theme_on_a_themed_workspace()
    {
        _assigner.Update(Table(null, ("p1", new(A, "Nord"))), globalBackground: true);

        var preview = _assigner.Preview(A, null);

        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p1"] = null }, preview.Panes);
    }

    [Fact]
    public void A_session_moved_into_the_previewed_workspace_shows_the_preview()
    {
        _assigner.Update(Table(null, ("p1", new(A, "Nord")), ("p2", new(B, null))), globalBackground: true);
        _assigner.Preview(B, "Dracula");

        var moved = _assigner.Update(Table(null, ("p1", new(B, null)), ("p2", new(B, null))), globalBackground: true);

        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p1"] = Catalog.Resolve("Dracula") }, moved.Panes);
    }

    [Fact]
    public void The_background_shows_the_preview_when_the_selected_session_is_in_that_workspace()
    {
        _assigner.Update(Table(new(B, null), ("p2", new(B, null))), globalBackground: false);

        var preview = _assigner.Preview(B, "Nord");

        Assert.True(preview.BackgroundChanged);
        Assert.Equal("#2e3440", preview.Background);
    }

    [Fact]
    public void A_saved_pick_leaves_no_preview_behind()
    {
        _assigner.Update(Table(null, ("p2", new(B, null))), globalBackground: true);
        _assigner.Preview(B, "Dracula");
        var version = _assigner.TableVersion;

        Assert.True(_assigner.Saved(B, "Dracula", version).IsEmpty);
        Assert.True(_assigner.Update(Table(null, ("p2", new(B, "Dracula"))), globalBackground: true).IsEmpty);

        // a later agtermctl workspace theme must show; a preview left behind would hide it
        var later = _assigner.Update(Table(null, ("p2", new(B, "Nord"))), globalBackground: true);
        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p2"] = Catalog.Resolve("Nord") }, later.Panes);
    }

    [Fact]
    public void A_table_that_arrived_during_the_save_wins_over_the_saved_pick()
    {
        _assigner.Update(Table(null, ("p2", new(B, null))), globalBackground: true);
        _assigner.Preview(B, "Dracula");
        var version = _assigner.TableVersion;

        // another command set the workspace theme before the save response came back
        _assigner.Update(Table(null, ("p2", new(B, "Nord"))), globalBackground: true);
        var saved = _assigner.Saved(B, "Dracula", version);

        Assert.Equal(new Dictionary<string, TerminalTheme?> { ["p2"] = Catalog.Resolve("Nord") }, saved.Panes);
        Assert.True(_assigner.Update(Table(null, ("p2", new(B, "Nord"))), globalBackground: true).IsEmpty);
    }

    [Fact]
    public void A_workspace_is_known_until_a_table_drops_it()
    {
        _assigner.Update(Table(null), globalBackground: true);
        Assert.True(_assigner.HasWorkspace(B));

        _assigner.Update(
            new PaneThemeTable(new Dictionary<string, PaneThemeEntry>(), new HashSet<Guid> { A }, null),
            globalBackground: true);

        Assert.False(_assigner.HasWorkspace(B));
    }
}
