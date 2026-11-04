using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// the tree fields of A3: context (73b63b71), realized (8e67daa4), splitCwd (16af9dc3)
public class AppStoreTreeSessionFieldsTests
{
    private readonly AppStore _store = AppStoreFixtures.MakeStore();
    private readonly Session _session;

    public AppStoreTreeSessionFieldsTests()
    {
        _session = _store.AddSession(_store.AddWorkspace("work").Id, "/primary")!;
    }

    private ControlSessionNode Node(Func<Session, bool>? realized = null) =>
        _store.ControlTree(realized: realized).Workspaces[0].Sessions[0];

    [Fact]
    public void TheTreeProjectsSessionContext()
    {
        var other = _store.AddSession(_store.Workspaces[0].Id, "/repo/b")!;
        _session.Context = "PR #517: restore reap ordering";

        var sessions = _store.ControlTree().Workspaces[0].Sessions;

        Assert.Equal("PR #517: restore reap ordering", sessions[0].Context);
        Assert.Null(sessions[1].Context);
        Assert.Null(other.Context);
    }

    [Fact]
    public void ContextIsOmittedFromJsonWhenUnset()
    {
        Assert.DoesNotContain("\"context\"", ControlJson.Serialize(_store.ControlTree()));

        _session.Context = "PR #517";

        Assert.Contains("\"context\":\"PR #517\"", ControlJson.Serialize(_store.ControlTree()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitCwdReportsEachFallbackEvenWhenHidden(bool shown)
    {
        _session.HasSplit = true;
        _session.IsSplit = shown;
        foreach (var (initial, reported, expected) in new (string?, string?, string)[]
                 {
                     (null, null, "/primary"), ("/restored", null, "/restored"), ("/restored", "/live", "/live"),
                 })
        {
            _session.InitialSplitCwd = initial;
            _session.SplitCwd = reported;

            var node = Node();

            Assert.Equal(expected, node.SplitCwd);
            Assert.Equal("/primary", node.Cwd);
            Assert.Contains($"\"splitCwd\":\"{expected}\"", ControlJson.Serialize(node));
        }
    }

    [Fact]
    public void NoSplitOmitsTheDirectoryEvenWithStaleSplitFields()
    {
        _session.SplitCwd = "/stale";
        _session.InitialSplitCwd = "/restored";

        var node = Node();

        Assert.Null(node.SplitCwd);
        Assert.DoesNotContain("splitCwd", ControlJson.Serialize(node));
    }

    [Fact]
    public void RealizedIsFalseWithoutAHostAndFollowsTheHostCallback()
    {
        Assert.False(Node().Realized);
        Assert.True(Node(session => session.Id == _session.Id).Realized);
        Assert.False(Node(_ => false).Realized);
    }

    [Fact]
    public void RealizedIsAlwaysWrittenToJson()
    {
        Assert.Contains("\"realized\":false", ControlJson.Serialize(Node()));
        Assert.Contains("\"realized\":true", ControlJson.Serialize(Node(_ => true)));
    }
}
