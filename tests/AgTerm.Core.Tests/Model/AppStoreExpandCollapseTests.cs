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
        var a = store.AddWorkspace("a", clearFocus: false);
        var b = store.AddWorkspace("b", clearFocus: false);

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
}
