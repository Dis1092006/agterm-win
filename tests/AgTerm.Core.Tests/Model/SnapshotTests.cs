using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Model;

public class SnapshotTests
{
    private static readonly Guid SessionId = Guid.Parse("5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792");
    private static readonly Guid WorkspaceId = Guid.Parse("AAAA1111-0000-0000-0000-000000000001");

    private static List<string> SurfaceFieldNames(string section)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.File("parity/surface.json")));
        return doc.RootElement.GetProperty("persistence").GetProperty(section)
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList();
    }

    private static SessionSnapshot FullSessionSnapshot() => new()
    {
        Id = SessionId, CustomName = "api", Cwd = "/home/u/proj", IsSplit = true, FontSize = 14.0,
        SplitCwd = "/home/u/other", SplitRatio = 0.4, Flagged = true,
        ForegroundCommand = new[] { "ssh", "host" }, SplitForegroundCommand = new[] { "top" },
        InitialCommand = "ssh host", CommandWait = true,
        BackgroundWatermark = new BackgroundWatermark { Kind = WatermarkKind.Text, Text = "wip" },
        RestoreCommand = "make watch", SplitRestoreCommand = "",
    };

    [Fact]
    public void SessionSnapshotFieldNamesMatchParitySurfaceInOrder()
    {
        using var doc = JsonDocument.Parse(ControlJson.Serialize(FullSessionSnapshot()));
        Assert.Equal(SurfaceFieldNames("sessionSnapshot"),
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public void SnapshotFieldNamesMatchParitySurfaceInOrder()
    {
        var snapshot = new Snapshot
        {
            Version = Snapshot.CurrentVersion, SelectedSessionId = SessionId,
            Workspaces = Array.Empty<WorkspaceSnapshot>(), SidebarWidth = 240.0,
            SidebarVisible = true, SidebarMode = SidebarMode.Tree,
            FocusedWorkspaceId = WorkspaceId, SessionRecency = new[] { SessionId },
        };
        using var doc = JsonDocument.Parse(ControlJson.Serialize(snapshot));
        Assert.Equal(SurfaceFieldNames("snapshot"),
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public void UuidsSerializeUppercaseLikeSwiftFoundation()
    {
        var json = ControlJson.Serialize(new Snapshot
        {
            Version = 1, SelectedSessionId = SessionId, Workspaces = Array.Empty<WorkspaceSnapshot>(),
        });
        Assert.Contains("\"selectedSessionID\":\"5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792\"", json);
    }

    [Fact]
    public void FullSnapshotRoundTrips()
    {
        var snapshot = new Snapshot
        {
            Version = 1, SelectedSessionId = SessionId,
            Workspaces = new[]
            {
                new WorkspaceSnapshot
                {
                    Id = WorkspaceId, Name = "work",
                    Sessions = new[] { FullSessionSnapshot() }, Collapsed = true,
                },
            },
            SidebarWidth = 240.0, SidebarVisible = false, SidebarMode = SidebarMode.Flagged,
            FocusedWorkspaceId = WorkspaceId, SessionRecency = new[] { SessionId },
        };
        var json = ControlJson.Serialize(snapshot);
        Assert.Equal(json, ControlJson.Serialize(ControlJson.Deserialize<Snapshot>(json)));
    }

    [Fact]
    public void LegacyMinimalSnapshotStillDecodes()
    {
        // A file written before any of the optional fields existed must decode instead of
        // failing the load and wiping the saved tree.
        var json = "{\"version\":1,\"workspaces\":[{\"id\":\"" + WorkspaceId + "\",\"name\":\"w\"," +
                   "\"sessions\":[{\"id\":\"" + SessionId + "\",\"cwd\":\"/home\"}]}]}";
        var snapshot = ControlJson.Deserialize<Snapshot>(json)!;

        var session = snapshot.Workspaces[0].Sessions[0];
        Assert.Equal("/home", session.Cwd);
        Assert.Null(session.CustomName);
        Assert.Null(session.IsSplit);
        Assert.Null(snapshot.SidebarWidth);
        Assert.Null(snapshot.SidebarMode);
        Assert.Null(snapshot.SessionRecency);
    }

    [Fact]
    public void MissingVersionFailsTheDecode()
    {
        // Swift decodes version strictly; the loader turns the failure into a fresh snapshot.
        Assert.Throws<JsonException>(() => ControlJson.Deserialize<Snapshot>("{\"workspaces\":[]}"));
    }

    [Theory]
    [InlineData("{\"kind\":\"hologram\"}")] // unknown kind (downgrade after a newer release)
    [InlineData("{\"kind\":\"image\",\"fit\":\"zoom\"}")] // unknown fit
    [InlineData("{\"kind\":\"text\",\"position\":\"middle\"}")] // unknown position
    [InlineData("42")] // wrong JSON type entirely
    [InlineData("{}")] // no kind at all
    public void BadWatermarkDecodesLossyToNullInsteadOfFailingTheSession(string watermarkJson)
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/home\",\"backgroundWatermark\":" + watermarkJson + "}";
        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;

        Assert.Null(session.BackgroundWatermark);
        Assert.Equal("/home", session.Cwd); // the rest of the snapshot survives
    }

    [Fact]
    public void ValidWatermarkRoundTripsInsideTheSessionSnapshot()
    {
        // uppercase in the fixture: serialization writes canonical uppercase UUIDs like Swift.
        var json = "{\"id\":\"" + SessionId.ToString().ToUpperInvariant() + "\",\"cwd\":\"/h\",\"backgroundWatermark\":" +
                   "{\"kind\":\"image\",\"imagePath\":\"/tmp/w.png\",\"opacity\":0.15," +
                   "\"fit\":\"cover\",\"position\":\"top-right\",\"repeats\":true}}";
        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;

        Assert.Equal(WatermarkKind.Image, session.BackgroundWatermark!.Kind);
        Assert.Equal(WatermarkFit.Cover, session.BackgroundWatermark.Fit);
        Assert.Equal(WatermarkPosition.TopRight, session.BackgroundWatermark.Position);
        Assert.Equal(json, ControlJson.Serialize(session));
    }

    [Theory]
    [InlineData("[\"not-a-uuid\"]")]
    [InlineData("\"wrong type\"")]
    [InlineData("[42]")]
    public void BadSessionRecencyDecodesLossyToNull(string recencyJson)
    {
        var json = "{\"version\":1,\"workspaces\":[],\"sessionRecency\":" + recencyJson + "}";
        var snapshot = ControlJson.Deserialize<Snapshot>(json)!;

        Assert.Null(snapshot.SessionRecency);
        Assert.Equal(1, snapshot.Version); // the rest of the snapshot survives
    }

    [Fact]
    public void UnknownSidebarModeFailsStrictly()
    {
        // Unlike the lossy fields, sidebarMode keeps Swift's strict decodeIfPresent behavior.
        Assert.Throws<JsonException>(() => ControlJson.Deserialize<Snapshot>(
            "{\"version\":1,\"workspaces\":[],\"sidebarMode\":\"grid\"}"));
    }

    [Fact]
    public void CollapsedIsOmittedForAnExpandedWorkspace()
    {
        // an all-expanded tree serializes byte-identically to a legacy snapshot.
        var workspace = new WorkspaceSnapshot
        {
            Id = WorkspaceId, Name = "w", Sessions = Array.Empty<SessionSnapshot>(),
        };
        Assert.DoesNotContain("collapsed", ControlJson.Serialize(workspace));
    }
}
