using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// Ported from agtermCore AppStoreSessionStateTests.swift (upstream 14afbfd5, flag a whole workspace):
/// AllSessionsFlagged and ApplyWorkspaceFlag.
/// </summary>
public class AppStoreWorkspaceFlagTests
{
    private static (AppStore Store, Guid TargetId, Session[] Sessions, Session Other) FlagStore()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("target");
        var sessions = new[] { "/a", "/b", "/c" }.Select(cwd => store.AddSession(workspace.Id, cwd)!).ToArray();
        var otherWorkspace = store.AddWorkspace("other");
        var other = store.AddSession(otherWorkspace.Id, "/other")!;
        return (store, workspace.Id, sessions, other);
    }

    [Theory]
    [InlineData(new[] { false, false, false }, ControlToggleMode.On, true)]
    [InlineData(new[] { true, false, true }, ControlToggleMode.On, true)]
    [InlineData(new[] { true, true, true }, ControlToggleMode.Off, false)]
    [InlineData(new[] { true, false, true }, ControlToggleMode.Off, false)]
    [InlineData(new[] { false, false, false }, ControlToggleMode.Toggle, true)]
    [InlineData(new[] { true, false, true }, ControlToggleMode.Toggle, true)]
    [InlineData(new[] { true, true, true }, ControlToggleMode.Toggle, false)]
    public void WorkspaceFlagSetsEverySessionToOneValue(bool[] initial, ControlToggleMode mode, bool want)
    {
        var (store, targetId, sessions, other) = FlagStore();
        foreach (var (session, flagged) in sessions.Zip(initial)) store.SetFlag(flagged, session.Id);
        store.SetFlag(true, other.Id);

        store.ApplyWorkspaceFlag(mode, targetId);

        Assert.Equal(new[] { want, want, want }, sessions.Select(s => s.Flagged));
        Assert.Equal(want, store.AllSessionsFlagged(targetId));
        Assert.True(other.Flagged, "another workspace's sessions are not touched");
    }

    [Fact]
    public void WorkspaceFlagOnAnEmptyOrUnknownWorkspaceChangesNothing()
    {
        var (store, _, _, other) = FlagStore();
        var empty = store.AddWorkspace("empty");

        store.ApplyWorkspaceFlag(ControlToggleMode.On, empty.Id);
        store.ApplyWorkspaceFlag(ControlToggleMode.On, Guid.NewGuid());

        Assert.False(store.AllSessionsFlagged(empty.Id));
        Assert.False(other.Flagged);
    }

    [Fact]
    public void WorkspaceFlagFlagsSessionsTheFlaggedViewHides()
    {
        var (store, targetId, sessions, _) = FlagStore();
        store.SetFlag(true, sessions[0].Id);
        store.SetSidebarMode(SidebarMode.Flagged);

        store.ApplyWorkspaceFlag(ControlToggleMode.Toggle, targetId);

        Assert.Equal(sessions.Select(s => s.Id), store.FlaggedSessions.Select(s => s.Id));
    }

    [Fact]
    public void WorkspaceUnflagInFlaggedModeReselectsAVisibleSession()
    {
        var (store, targetId, sessions, other) = FlagStore();
        store.ApplyWorkspaceFlag(ControlToggleMode.On, targetId);
        store.SetFlag(true, other.Id);
        store.SelectSession(sessions[^1].Id);
        store.SetSidebarMode(SidebarMode.Flagged);

        store.ApplyWorkspaceFlag(ControlToggleMode.Toggle, targetId);

        Assert.Equal(other.Id, store.SelectedSessionId);
    }
}
