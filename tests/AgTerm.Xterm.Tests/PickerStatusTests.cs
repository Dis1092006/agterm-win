using System.Collections.ObjectModel;
using AgTerm.App;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

public sealed class PickerStatusTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Palette_rows_keep_reference_identity_when_status_or_subscribers_change(bool subscribe)
    {
        var first = new PaletteItemViewModel("same", "Same", status: AgentStatus.Idle);
        var clicked = new PaletteItemViewModel("same", "Same", status: AgentStatus.Idle);
        var rows = new ObservableCollection<PaletteItemViewModel> { first, clicked };
        var identities = new HashSet<PaletteItemViewModel> { clicked };
        var originalHash = clicked.GetHashCode();

        if (subscribe)
            clicked.PropertyChanged += (_, _) => { };
        else
            clicked.RefreshIndicator(new AgentIndicator(AgentStatus.Blocked), new AppSettings());

        Assert.NotEqual(first, clicked);
        Assert.Equal(1, rows.IndexOf(clicked));
        Assert.Equal(originalHash, clicked.GetHashCode());
        Assert.Contains(clicked, identities);
        Assert.DoesNotContain(first, identities);
    }

    [Fact]
    public void Native_menu_and_overlay_rows_draw_trailing_shared_glyphs()
    {
        var root = FindRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "MainWindow.xaml"));
        Assert.Contains("x:Key=\"RecentSessionItemStyle\"", xaml);
        Assert.Contains("TargetType=\"MenuFlyoutItem\"", xaml);
        Assert.Contains("BasedOn=\"{StaticResource DefaultMenuFlyoutItemStyle}\"", xaml);
        Assert.Contains("local:SessionStatusGlyph", xaml);
        Assert.Contains("Grid.Column=\"1\"", xaml);
        var source = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "MainWindow.xaml.cs"));
        Assert.Contains("_switcher.RefreshIndicators(state.SessionIndicators)", source);
        Assert.Contains("PaletteOverlay.RefreshIndicators(", source);
        Assert.Contains("Resources[\"RecentSessionItemStyle\"]", source);
        var switcher = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "SessionSwitcherView.xaml.cs"));
        Assert.Contains("SessionStatusGlyph", switcher);
        Assert.Contains("Grid.SetColumn(glyph, 1)", switcher);
        var palette = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "PaletteView.xaml"));
        Assert.Contains("Grid.Column=\"3\"", palette);
        Assert.Contains("local:SessionStatusGlyph", palette);
        var sidebar = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "SidebarRowViewModel.cs"));
        Assert.Contains("StatusGlyphPresentation.Data", sidebar);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void Open_palette_updates_existing_rows_without_reranking_or_changing_selection()
    {
        var id = Guid.NewGuid();
        var row = new PaletteItemViewModel(id.ToString("D"), "Agent", status: AgentStatus.Idle);
        var controller = new PaletteController();
        controller.Open([row, new PaletteItemViewModel("other", "Other")]);
        controller.SetQuery("agent");
        var rows = controller.FilteredItems;
        var notifications = new List<string?>();
        row.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var settings = new AppSettings { ActiveStatusColorHex = "#123456", ActiveStatusShape = "diamond" };
        var indicators = new Dictionary<Guid, AgentIndicator> { [id] = new(AgentStatus.Active) };
        controller.RefreshIndicators(indicators, settings, liveSessions: true);
        Assert.Same(rows, controller.FilteredItems);
        Assert.Same(row, controller.SelectedItem);
        Assert.Equal("agent", controller.Query);
        Assert.Equal(AgentStatus.Active, row.Status);
        Assert.Equal("#123456", row.StatusColor);
        Assert.Equal(StatusShape.Diamond, row.StatusShape);
        Assert.Contains("active status", row.AutomationName);
        Assert.Contains(nameof(row.GlyphData), notifications);
        Assert.Contains(nameof(row.AutomationName), notifications);
        indicators[id] = new(AgentStatus.Completed, Color: "#abcdef", Shape: StatusShape.Star);
        controller.RefreshIndicators(indicators, settings, liveSessions: true);
        Assert.Equal("#abcdef", row.StatusColor);
        Assert.Equal(StatusShape.Star, row.StatusShape);
        indicators.Clear();
        controller.RefreshIndicators(indicators, settings, liveSessions: true);
        Assert.False(row.HasStatusGlyph);
        Assert.DoesNotContain("status", row.AutomationName);
        Assert.Same(rows, controller.FilteredItems);
    }

    [Fact]
    public void Caller_pick_ids_are_not_treated_as_live_session_ids()
    {
        var id = Guid.NewGuid();
        var row = new PaletteItemViewModel(id.ToString("D"), "Pick", status: AgentStatus.Blocked);
        var controller = new PaletteController();
        controller.Open([row]);
        controller.RefreshIndicators(new Dictionary<Guid, AgentIndicator> { [id] = new(AgentStatus.Completed) },
            new AppSettings { BlockedStatusShape = "star" }, liveSessions: false);
        Assert.Equal(AgentStatus.Blocked, row.Status);
        Assert.Equal(StatusShape.Star, row.StatusShape);
    }

    [Fact]
    public void Live_indicator_snapshot_includes_sessions_outside_the_visible_MRU_and_is_immutable()
    {
        var store = TestStores.Scratch();
        var visible = store.AddWorkspace("Visible");
        var hidden = store.AddWorkspace("Hidden");
        var hiddenSession = store.AddSession(hidden.Id, @"C:\hidden")!;
        for (var i = 0; i < TitleBarState.MaxRecentSessions + 2; i++)
        {
            var session = store.AddSession(visible.Id, @"C:\visible")!;
            store.SessionRecency.Push(session.Id);
        }
        store.FocusedWorkspaceId = visible.Id;
        var before = TitleBarState.Project(store);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed), hiddenSession.Id);
        var after = TitleBarState.Project(store);
        Assert.Equal(TitleBarState.MaxRecentSessions, after.SwitcherSessions.Count);
        Assert.Equal(TitleBarState.MaxRecentSessions + 3, after.SessionIndicators.Count);
        Assert.DoesNotContain(after.SwitcherSessions, row => row.Id == hiddenSession.Id);
        Assert.Equal(AgentStatus.Completed, after.SessionIndicators[hiddenSession.Id].Status);
        Assert.Equal(AgentStatus.Idle, before.SessionIndicators[hiddenSession.Id].Status);
    }

    [Fact]
    public void Titlebar_rows_carry_status_but_switcher_keeps_its_original_order()
    {
        var store = TestStores.Scratch();
        var workspace = store.AddWorkspace("Work");
        var first = store.AddSession(workspace.Id, @"C:\first")!;
        var second = store.AddSession(workspace.Id, @"C:\second")!;
        store.SessionRecency.Push(first.Id);
        store.SessionRecency.Push(second.Id);
        var switcher = new SessionSwitcher();
        switcher.Tab(false, true, () => TitleBarState.Project(store).SwitcherSessions);
        var ids = switcher.Candidates.Select(row => row.Id).ToArray();
        var indicator = new AgentIndicator(AgentStatus.Blocked, Color: "#ff8800", Shape: StatusShape.Star);
        store.SetAgentIndicator(indicator, first.Id);
        var fresh = TitleBarState.Project(store);
        Assert.Equal(indicator, fresh.RecentSessions.Single(row => row.Id == first.Id).Indicator);
        switcher.RefreshIndicators(fresh.SessionIndicators);
        Assert.Equal(ids, switcher.Candidates.Select(row => row.Id));
        Assert.Equal(1, switcher.Index);
        Assert.Equal(indicator, switcher.Candidates.Single(row => row.Id == first.Id).Indicator);
    }

    [Theory]
    [InlineData("blocked", "#ff8800", "star", AgentStatus.Blocked, StatusShape.Star)]
    [InlineData("active", null, "hexagon", AgentStatus.Active, StatusShape.Circle)]
    [InlineData("idle", "#123456", "star", AgentStatus.Idle, StatusShape.Circle)]
    [InlineData("busy", "#123456", "star", AgentStatus.Idle, StatusShape.Circle)]
    [InlineData(null, null, null, AgentStatus.Idle, StatusShape.Circle)]
    public void Pick_rows_render_the_normalized_indicator_without_changing_the_answer(
        string? status, string? color, string? shape, AgentStatus expected, StatusShape expectedShape)
    {
        var item = new ControlPickItem { Id = "a", Label = "A", Status = status, StatusColor = color, StatusShape = shape };
        var row = Assert.Single(PickPalette.Items(new PendingPick("P", [item], null, null, false, null)));
        Assert.Equal(expected, row.Status);
        Assert.Equal(expectedShape, row.StatusShape);
        Assert.Equal(expected != AgentStatus.Idle, row.HasStatusGlyph);
        Assert.Equal(ControlPickResult.Picked("a", "A", 0), PickPalette.Outcome(row));
    }

    [Theory]
    [InlineData(AgentStatus.Active, AppSettings.DefaultActiveStatusColorHex)]
    [InlineData(AgentStatus.Blocked, AppSettings.DefaultBlockedStatusColorHex)]
    [InlineData(AgentStatus.Completed, AppSettings.DefaultCompletedStatusColorHex)]
    public void Palette_defaults_use_the_sidebar_status_colors(AgentStatus status, string color)
    {
        Assert.Equal(color, new PaletteItemViewModel("a", "A", status: status).StatusColor);
    }
}
