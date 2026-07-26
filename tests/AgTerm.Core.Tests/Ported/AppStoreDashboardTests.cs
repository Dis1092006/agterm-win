using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore AppStore+DashboardTests.swift.</summary>
public sealed class AppStoreDashboardTests
{
    [Fact]
    public void DashboardMembersExpandsSessionsToPaneCells()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        b.HasSplit = true; // a split session expands into two cells (primary + split)
        var (members, dropped) = store.DashboardMembers(new[] { a.Id, b.Id }, 9);
        Assert.Equal(0, dropped);
        Assert.Equal(new List<DashboardMember>
        {
            new(a.Id, TerminalZoomSurface.Primary),
            new(b.Id, TerminalZoomSurface.Primary),
            new(b.Id, TerminalZoomSurface.Split),
        }, members);
        // control refs: a non-split session is one `:left` cell, a split session is `:left` + `:right`.
        Assert.Equal(new[]
        {
            $"{a.Id.ToString("D").ToUpperInvariant()}:left",
            $"{b.Id.ToString("D").ToUpperInvariant()}:left",
            $"{b.Id.ToString("D").ToUpperInvariant()}:right",
        }, members.Select(m => m.ControlRef));
    }

    [Fact]
    public void DashboardMembersCapsAtLimitAndReportsDropped()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var ids = Enumerable.Range(0, 5).Select(i => store.AddSession(ws.Id, $"/{i}")!.Id).ToList();
        var (members, dropped) = store.DashboardMembers(ids, 3);
        // first 3 kept
        Assert.Equal(ids.Take(3).Select(id => new DashboardMember(id, TerminalZoomSurface.Primary)),
            members);
        Assert.Equal(2, dropped); // two panes past the cap
    }

    [Fact]
    public void DashboardMembersSkipsUnresolvedIDs()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var (members, dropped) = store.DashboardMembers(new[] { Guid.NewGuid(), a.Id, Guid.NewGuid() }, 9);
        Assert.Equal(0, dropped);
        // only the real id yields a cell
        Assert.Equal(new List<DashboardMember> { new(a.Id, TerminalZoomSurface.Primary) }, members);
    }

    [Fact]
    public void DashboardMRUMembersFollowsRecencyOrderAndExpands()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        b.HasSplit = true;
        store.SelectSession(a.Id);
        store.SelectSession(b.Id); // most-recent-first recency: [b, a]
        Assert.Equal(new List<DashboardMember>
        {
            new(b.Id, TerminalZoomSurface.Primary),
            new(b.Id, TerminalZoomSurface.Split),
            new(a.Id, TerminalZoomSurface.Primary),
        }, store.DashboardMruMembers(9));
    }

    [Fact]
    public void DashboardMRUMembersEmptyWhenNoSessions()
    {
        var store = AppStoreFixtures.MakeStore();
        Assert.Empty(store.DashboardMruMembers(9)); // no sessions -> no recent members
    }
}
