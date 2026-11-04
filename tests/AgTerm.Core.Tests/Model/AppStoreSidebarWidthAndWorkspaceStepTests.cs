using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>Upstream 7977f202 setSidebarWidth and 3624dd05 navigateWorkspace / canStepWorkspaces,
/// the store side of wire sidebar.width and workspace.go.</summary>
public class AppStoreSidebarWidthAndWorkspaceStepTests
{
    private static (AppStore Store, Func<int> Writes) Counted()
    {
        var store = AppStoreFixtures.MakeStore();
        var writes = 0;
        store.SidebarChanged += () => writes++;
        return (store, () => writes);
    }

    [Theory]
    [InlineData(300.0, 300.0)]
    [InlineData(10.0, AppStore.SidebarWidthMin)]
    [InlineData(5000.0, AppStore.SidebarWidthMax)]
    [InlineData(AppStore.SidebarWidthMin, AppStore.SidebarWidthMin)]
    [InlineData(AppStore.SidebarWidthMax, AppStore.SidebarWidthMax)]
    public void SetSidebarWidthClampsToTheDragBounds(double requested, double stored)
    {
        var store = AppStoreFixtures.MakeStore();

        store.SetSidebarWidth(requested);

        Assert.Equal(stored, store.SidebarWidth);
    }

    [Fact]
    public void SetSidebarWidthPersistsTheClampedWidth()
    {
        var (store, writes) = Counted();

        store.SetSidebarWidth(300);

        Assert.Equal(1, writes());
    }

    [Fact]
    public void SetSidebarWidthIsACleanNoOpWhenTheClampedWidthIsCurrent()
    {
        var (store, writes) = Counted();
        store.SetSidebarWidth(AppStore.SidebarWidthMax);
        var before = writes();

        store.SetSidebarWidth(AppStore.SidebarWidthMax);
        store.SetSidebarWidth(9999);

        Assert.Equal(before, writes());
    }

    [Theory]
    [InlineData("next", WorkspaceNavigation.Next)]
    [InlineData("prev", WorkspaceNavigation.Previous)]
    [InlineData("previous", WorkspaceNavigation.Previous)]
    public void WorkspaceNavigationParsesBothSpellingsOfPrevious(string wire, WorkspaceNavigation expected) =>
        Assert.Equal(expected, WorkspaceNavigations.FromWire(wire));

    [Theory]
    [InlineData("first")]
    [InlineData("last")]
    [InlineData("")]
    [InlineData("NEXT")]
    public void WorkspaceNavigationRejectsEverythingElse(string wire) =>
        Assert.Null(WorkspaceNavigations.FromWire(wire));

    private static (AppStore Store, Workspace A, Workspace B, Workspace C) ThreeWorkspacesSelectingTheMiddle()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        var b = store.AddWorkspace("b");
        var c = store.AddWorkspace("c");
        store.AddSession(a.Id, "/a");
        var inB = store.AddSession(b.Id, "/b")!;
        store.AddSession(c.Id, "/c");
        store.SelectSession(inB.Id);
        return (store, a, b, c);
    }

    [Fact]
    public void NavigateWorkspaceStepsForwardAndBackWithWrap()
    {
        var (store, a, b, c) = ThreeWorkspacesSelectingTheMiddle();

        Assert.Equal(c.Id, store.NavigateWorkspace(WorkspaceNavigation.Next));
        Assert.Equal(c.Id, store.CurrentWorkspaceId);
        Assert.Equal(a.Id, store.NavigateWorkspace(WorkspaceNavigation.Next));
        Assert.Equal(c.Id, store.NavigateWorkspace(WorkspaceNavigation.Previous));
        Assert.Equal(b.Id, store.NavigateWorkspace(WorkspaceNavigation.Previous));
        Assert.Equal(a.Id, store.NavigateWorkspace(WorkspaceNavigation.Previous));
        Assert.Equal(a.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void NavigateWorkspaceIgnoresCollapseState()
    {
        var (store, _, b, c) = ThreeWorkspacesSelectingTheMiddle();
        store.SetWorkspaceExpanded(c.Id, false);

        Assert.Equal(c.Id, store.NavigateWorkspace(WorkspaceNavigation.Next));
        Assert.Equal(b.Id, store.NavigateWorkspace(WorkspaceNavigation.Previous));
    }

    [Fact]
    public void NavigateWorkspaceLandsOnAnEmptyWorkspaceAndMakesItCurrent()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        store.AddSession(a.Id, "/a");
        var empty = store.AddWorkspace("empty", revealNewWorkspace: false);
        store.SelectSession(a.Sessions[0].Id);

        Assert.Equal(empty.Id, store.NavigateWorkspace(WorkspaceNavigation.Next));
        Assert.Equal(empty.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void NavigateWorkspaceIsConfinedByTheFocusFilter()
    {
        var (store, a, b, _) = ThreeWorkspacesSelectingTheMiddle();
        // a focus on a lone workspace leaves nowhere to step; stepping must not leave the filter
        store.SetFocusedWorkspace(b.Id);

        Assert.False(store.CanStepWorkspaces);
        Assert.Null(store.NavigateWorkspace(WorkspaceNavigation.Next));
        Assert.Equal(b.Id, store.SoleFocusedWorkspaceId);
        Assert.NotEqual(a.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void NavigateWorkspaceRefusesInFlaggedMode()
    {
        var (store, _, b, _) = ThreeWorkspacesSelectingTheMiddle();
        store.SetSidebarMode(SidebarMode.Flagged);

        Assert.False(store.CanStepWorkspaces);
        Assert.Null(store.NavigateWorkspace(WorkspaceNavigation.Next));
        Assert.Equal(b.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void NavigateWorkspaceRefusesALoneWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var only = store.AddWorkspace("only");
        store.AddSession(only.Id, "/only");

        Assert.False(store.CanStepWorkspaces);
        Assert.Null(store.NavigateWorkspace(WorkspaceNavigation.Next));
    }

    [Fact]
    public void NavigateWorkspaceRefusesWithoutWorkspaces()
    {
        var store = AppStoreFixtures.MakeStore();

        Assert.False(store.CanStepWorkspaces);
        Assert.Null(store.NavigateWorkspace(WorkspaceNavigation.Previous));
    }

    // a current workspace outside the visible set: the step enters it from the end it comes from
    [Fact]
    public void NavigateWorkspaceFromOutsideTheVisibleSetLandsOnTheEndTheStepComesFrom()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        store.AddWorkspace("b");
        store.SetFocusedWorkspace(a.Id);

        Assert.True(store.CanStepWorkspaces);
        Assert.NotEqual(a.Id, store.CurrentWorkspaceId);
        Assert.Equal(a.Id, store.NavigateWorkspace(WorkspaceNavigation.Next));
    }

    [Fact]
    public void NavigateWorkspacePreviousFromOutsideTheVisibleSetLandsOnTheLast()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        store.AddWorkspace("b");
        store.SetFocusedWorkspace(a.Id);

        Assert.Equal(a.Id, store.NavigateWorkspace(WorkspaceNavigation.Previous));
    }
}
