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

    [Fact]
    public void CollapseOtherWorkspacesWithoutASelectionCollapsesAll()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        var b = store.AddWorkspace("b");

        store.CollapseOtherWorkspaces();

        Assert.False(a.IsExpanded);
        Assert.False(b.IsExpanded);
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
