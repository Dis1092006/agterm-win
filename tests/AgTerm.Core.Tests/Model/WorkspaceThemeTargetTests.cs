using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

public class WorkspaceThemeTargetTests
{
    private readonly AppStore _store = AppStoreFixtures.MakeStore();

    [Fact]
    public void Nothing_changes_when_the_selected_session_is_already_in_the_workspace()
    {
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\")!;
        _store.SelectSession(session.Id);

        Assert.Null(WorkspaceThemeTarget.SessionToSelect(_store, workspace.Id));
    }

    [Fact]
    public void The_most_recently_selected_session_of_the_workspace_goes_on_screen()
    {
        var target = _store.AddWorkspace("target");
        var other = _store.AddWorkspace("other");
        var first = _store.AddSession(target.Id, "C:\\", select: false)!;
        var second = _store.AddSession(target.Id, "C:\\", select: false)!;
        var elsewhere = _store.AddSession(other.Id, "C:\\", select: false)!;
        _store.SelectSession(second.Id);
        _store.SelectSession(first.Id);
        _store.SelectSession(second.Id);
        _store.SelectSession(elsewhere.Id);

        Assert.Equal(second.Id, WorkspaceThemeTarget.SessionToSelect(_store, target.Id));
    }

    [Fact]
    public void A_workspace_whose_sessions_were_never_selected_shows_its_first_session()
    {
        var target = _store.AddWorkspace("target");
        var other = _store.AddWorkspace("other");
        var first = _store.AddSession(target.Id, "C:\\", select: false)!;
        _ = _store.AddSession(target.Id, "C:\\", select: false);
        var elsewhere = _store.AddSession(other.Id, "C:\\", select: false)!;
        _store.SelectSession(elsewhere.Id);

        Assert.Equal(first.Id, WorkspaceThemeTarget.SessionToSelect(_store, target.Id));
    }

    [Fact]
    public void An_empty_or_unknown_workspace_selects_nothing()
    {
        var empty = _store.AddWorkspace("empty");

        Assert.Null(WorkspaceThemeTarget.SessionToSelect(_store, empty.Id));
        Assert.Null(WorkspaceThemeTarget.SessionToSelect(_store, Guid.NewGuid()));
    }
}
