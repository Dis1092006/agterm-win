using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>The bulk expand/collapse that wire sidebar.expand / sidebar.collapse and the palette's
/// Expand Workspaces / Collapse Workspaces rows share (upstream AppActions expandAllWorkspaces /
/// collapseOtherWorkspaces).</summary>
public class AppStoreExpandCollapseTests
{
    [Fact]
    public void ExpandAllWorkspacesExpandsEveryWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a", collapsed: true);
        var b = store.AddWorkspace("b", collapsed: true);

        store.ExpandAllWorkspaces();

        Assert.True(a.IsExpanded);
        Assert.True(b.IsExpanded);
    }

    [Fact]
    public void CollapseOtherWorkspacesKeepsTheActiveSessionsWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        var b = store.AddWorkspace("b");
        var c = store.AddWorkspace("c");
        var session = store.AddSession(b.Id, "/b")!;
        store.SelectSession(session.Id);

        store.CollapseOtherWorkspaces();

        Assert.False(a.IsExpanded);
        Assert.True(b.IsExpanded);
        Assert.False(c.IsExpanded);
    }

    // upstream collapseOthers keeps store.currentWorkspaceID, which falls back to the last workspace.
    [Fact]
    public void CollapseOtherWorkspacesWithoutASelectionKeepsTheLastWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a", revealNewWorkspace: false);
        var b = store.AddWorkspace("b", revealNewWorkspace: false);

        store.CollapseOtherWorkspaces();

        Assert.False(a.IsExpanded);
        Assert.True(b.IsExpanded);
    }

    // upstream 0106275a: a foreground-created workspace is the current one until the selection changes.
    [Fact]
    public void CollapseOtherWorkspacesKeepsAForegroundCreatedWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var session = store.AddSession(work.Id, "/work")!;
        store.SelectSession(session.Id);
        var fresh = store.AddWorkspace("fresh");

        store.CollapseOtherWorkspaces();

        Assert.False(work.IsExpanded);
        Assert.True(fresh.IsExpanded);
    }

    [Fact]
    public void ExpandAndCollapseDoNothingInFlaggedMode()
    {
        var store = AppStoreFixtures.MakeStore();
        var expanded = store.AddWorkspace("expanded");
        var collapsed = store.AddWorkspace("collapsed", collapsed: true);
        store.SetSidebarMode(SidebarMode.Flagged);

        store.ExpandAllWorkspaces();
        Assert.False(collapsed.IsExpanded);

        store.CollapseOtherWorkspaces();
        Assert.True(expanded.IsExpanded);
    }

    // upstream 902cd853: the flagged tree layout renders workspace rows, so the folds act there
    [Fact]
    public void ExpandAndCollapseActInTheFlaggedTreeLayout()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a", collapsed: true);
        var b = store.AddWorkspace("b", collapsed: true);
        store.SetSidebarMode(SidebarMode.Flagged);
        store.FlaggedLayout = FlaggedViewLayout.Tree;

        store.ExpandAllWorkspaces();
        Assert.True(a.IsExpanded);
        Assert.True(b.IsExpanded);

        store.CollapseOtherWorkspaces();
        Assert.False(a.IsExpanded);
        Assert.True(b.IsExpanded);
    }

    [Theory]
    [InlineData(FlaggedViewLayout.Flat)]
    [InlineData(FlaggedViewLayout.Plain)]
    public void ExpandDoesNothingInTheFlatFlaggedLayouts(FlaggedViewLayout layout)
    {
        var store = AppStoreFixtures.MakeStore();
        var collapsed = store.AddWorkspace("collapsed", collapsed: true);
        store.SetSidebarMode(SidebarMode.Flagged);
        store.FlaggedLayout = layout;

        store.ExpandAllWorkspaces();

        Assert.False(collapsed.IsExpanded);
    }

    // upstream rendersWorkspaceRowsInTheOrdinaryTreeAndTheFlaggedTreeLayoutOnly
    [Fact]
    public void RendersWorkspaceRowsInTheOrdinaryTreeAndTheFlaggedTreeLayoutOnly()
    {
        var store = AppStoreFixtures.MakeStore();
        foreach (var layout in FlaggedViewLayouts.All)
        {
            store.FlaggedLayout = layout;
            store.SetSidebarMode(SidebarMode.Tree);
            Assert.True(store.RendersWorkspaceRows);
            store.SetSidebarMode(SidebarMode.Flagged);
            Assert.Equal(layout == FlaggedViewLayout.Tree, store.RendersWorkspaceRows);
        }
    }

    // the host sets the layout outside any persisted mutation, so the setter itself redraws the sidebar
    [Fact]
    public void ChangingTheFlaggedLayoutInvalidatesTheSidebarOnlyOnAChange()
    {
        var store = AppStoreFixtures.MakeStore();
        var raised = 0;
        store.SidebarChanged += () => raised++;

        store.FlaggedLayout = FlaggedViewLayout.Flat;
        Assert.Equal(0, raised);
        store.FlaggedLayout = FlaggedViewLayout.Tree;
        Assert.Equal(1, raised);
        store.FlaggedLayout = FlaggedViewLayout.Tree;
        Assert.Equal(1, raised);
    }

    // upstream canStepWorkspaces: off in flagged mode whatever its layout
    [Fact]
    public void WorkspaceStepsStayOffInTheFlaggedTreeLayout()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddWorkspace("a");
        store.AddWorkspace("b");
        store.SetSidebarMode(SidebarMode.Flagged);
        store.FlaggedLayout = FlaggedViewLayout.Tree;

        Assert.False(store.CanStepWorkspaces);
    }
}
