using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported from agtermCore ControlRecentTests.swift (upstream 6b564749, #744).</summary>
public class ControlRecentTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly SessionSnapshot _first = new() { Id = Guid.NewGuid(), CustomName = "a", Cwd = "/a" };
    private readonly SessionSnapshot _second = new() { Id = Guid.NewGuid(), CustomName = "b", Cwd = "/b" };

    private static string Wire(Guid id) => id.ToString("D").ToUpperInvariant();

    private RecentClosedItem SessionItem(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Kind = RecentClosedKind.Session,
        Title = "a",
        Subtitle = "project",
        ClosedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
        Session = new RecentClosedSession
        {
            WorkspaceId = _workspaceId, WorkspaceName = "project", WorkspaceIndex = 0, SessionIndex = 1,
            Snapshot = _first,
        },
    };

    private RecentClosedItem WorkspaceItem(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Kind = RecentClosedKind.Workspace,
        Title = "project",
        Subtitle = "2 sessions",
        ClosedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_500),
        Workspace = new RecentClosedWorkspace
        {
            Snapshot = new WorkspaceSnapshot
            {
                Id = _workspaceId, Name = "project", Sessions = new[] { _first, _second },
            },
            SelectedSessionId = _second.Id,
        },
    };

    [Fact]
    public void ASessionEntryProjectsItsParentWorkspaceAndOneSession()
    {
        var item = SessionItem();

        var projected = ControlRecentItem.From(item);
        Assert.Equal(Wire(item.Id), projected.Id);
        Assert.Equal("session", projected.Kind);
        Assert.Equal("a", projected.Title);
        Assert.Equal("project", projected.Subtitle);
        Assert.Equal(1_700_000_000, projected.ClosedAt);
        Assert.Equal(Wire(_workspaceId), projected.WorkspaceId);
        Assert.Equal(new[] { Wire(_first.Id) }, projected.SessionIds);
    }

    [Fact]
    public void AWorkspaceEntryProjectsItselfAndEverySnapshotSession()
    {
        var item = WorkspaceItem();

        var projected = ControlRecentItem.From(item);

        Assert.Equal("workspace", projected.Kind);
        Assert.Equal("2 sessions", projected.Subtitle);
        Assert.Equal(1_700_000_500, projected.ClosedAt);
        Assert.Equal(Wire(_workspaceId), projected.WorkspaceId);
        Assert.Equal(new[] { Wire(_first.Id), Wire(_second.Id) }, projected.SessionIds);
    }

    [Fact]
    public void PayloadsRoundTripThroughTheWire()
    {
        var item = ControlRecentItem.From(SessionItem());
        var result = new ControlResult
        {
            Recent = new[] { item },
            RecentOpen = new ControlRecentOpen { Item = item, Window = "W1", Complete = false },
        };

        var decoded = ControlJson.Deserialize<ControlResult>(ControlJson.Serialize(result))!;

        Assert.Equal(item.Id, decoded.Recent![0].Id);
        Assert.Equal(item.SessionIds, decoded.Recent![0].SessionIds);
        Assert.Equal("W1", decoded.RecentOpen!.Window);
        Assert.False(decoded.RecentOpen.Complete);
        Assert.Equal(item.Id, decoded.RecentOpen.Item.Id);
    }

    [Fact]
    public void ThePayloadUsesUpstreamFieldNamesAndOmitsAnAbsentSubtitle()
    {
        var workspace = WorkspaceItem() with { Subtitle = null };

        var json = ControlJson.Serialize(new ControlResult { Recent = new[] { ControlRecentItem.From(workspace) } });

        Assert.Contains("\"workspaceID\":", json);
        Assert.Contains("\"sessionIDs\":[", json);
        Assert.Contains("\"closedAt\":1700000500", json);
        Assert.DoesNotContain("subtitle", json);
    }

    [Fact]
    public void NoTargetResolvesTheNewestEntry()
    {
        var newest = SessionItem();
        var older = WorkspaceItem();

        Assert.Equal(newest.Id, RecentClosedTarget.Resolve(null, new[] { newest, older }).Id);
    }

    [Fact]
    public void ATargetResolvesByExactIdOrUniquePrefix()
    {
        var a = SessionItem(Guid.Parse("AAAA0000-0000-0000-0000-000000000001"));
        var b = WorkspaceItem(Guid.Parse("BBBB0000-0000-0000-0000-000000000002"));

        Assert.Equal(b.Id, RecentClosedTarget.Resolve("bbbb", new[] { a, b }).Id);
        Assert.Equal(a.Id, RecentClosedTarget.Resolve(Wire(a.Id).ToLowerInvariant(), new[] { a, b }).Id);
    }

    [Fact]
    public void AnEmptyListIsRefusedWhateverTheTarget()
    {
        Assert.Equal("no recently closed items", RecentClosedTarget.Resolve(null, Array.Empty<RecentClosedItem>()).Error);
        Assert.Equal("no recently closed items", RecentClosedTarget.Resolve("ab", Array.Empty<RecentClosedItem>()).Error);
    }

    [Fact]
    public void UnknownAmbiguousAndActiveTargetsAreRefused()
    {
        var a = SessionItem(Guid.Parse("AAAA0000-0000-0000-0000-000000000001"));
        var b = WorkspaceItem(Guid.Parse("AAAA0000-0000-0000-0000-000000000002"));
        var items = new[] { a, b };

        Assert.Equal("no such recent item: cccc", RecentClosedTarget.Resolve("cccc", items).Error);
        Assert.Equal("no such recent item: active", RecentClosedTarget.Resolve("active", items).Error);
        Assert.Equal("ambiguous recent item prefix 'aaaa' → AAAA0000, AAAA0000",
            RecentClosedTarget.Resolve("aaaa", items).Error);
    }

    [Fact]
    public void TheCommandsHaveTheirUpstreamWireNames()
    {
        Assert.Equal("recent.list", Command.RecentList.ToWire());
        Assert.Equal("recent.open", Command.RecentOpen.ToWire());
        Assert.Equal("recent.clear", Command.RecentClear.ToWire());
    }
}
