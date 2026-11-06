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
        Id = SessionId, CustomName = "api", Cwd = "/home/u/proj", IsSplit = true, SplitAxis = SplitAxis.TopBottom, FontSize = 14.0,
        SplitCwd = "/home/u/other", SplitRatio = 0.4, Flagged = true, Context = "PR #517",
        Tint = new SidebarTint(TintColor.Red, TintParts.Name),
        ForegroundCommand = new[] { "ssh", "host" }, SplitForegroundCommand = new[] { "top" },
        InitialCommand = "ssh host", CommandWait = true,
        SplitInitialCommand = "top", SplitCommandWait = true,
        BackgroundWatermark = new BackgroundWatermark { Kind = WatermarkKind.Text, Text = "wip" },
        RestoreCommand = "make watch", SplitRestoreCommand = "", LastSelectedAt = 1_700_000_000.5,
    };

    [Fact]
    public void SessionSnapshotFieldNamesMatchParitySurfaceInOrder()
    {
        using var doc = JsonDocument.Parse(ControlJson.Serialize(FullSessionSnapshot()));
        // the pin plus what adopted.json takes, in upstream order (context: 73b63b71)
        var wanted = SurfaceFieldNames("sessionSnapshot").Concat(ParitySurface.Adopted("sessionSnapshot"))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(ParitySurface.LatestUpstreamPersistence("sessionSnapshot").Where(wanted.Contains).ToList(),
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public void ContextIsOmittedWhenUnsetAndRoundTripsWhenSet()
    {
        var bare = ControlJson.Serialize(new SessionSnapshot { Id = SessionId, Cwd = "/h" });
        Assert.DoesNotContain("context", bare);

        var json = "{\"id\":\"" + SessionId.ToString().ToUpperInvariant() + "\",\"cwd\":\"/h\",\"context\":\"PR #517: reap ordering\"}";
        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;
        Assert.Equal("PR #517: reap ordering", session.Context);
        Assert.Equal(json, ControlJson.Serialize(session));
    }

    [Fact]
    public void LastSelectedAtIsOmittedWhenUnsetAndRoundTripsWhenSet()
    {
        Assert.DoesNotContain("lastSelectedAt", ControlJson.Serialize(new SessionSnapshot { Id = SessionId, Cwd = "/h" }));

        var json = "{\"id\":\"" + SessionId.ToString().ToUpperInvariant() + "\",\"cwd\":\"/h\",\"lastSelectedAt\":1700000000.5}";
        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;
        Assert.Equal(1_700_000_000.5, session.LastSelectedAt);
        Assert.Equal(json, ControlJson.Serialize(session));
    }

    [Fact]
    public void SnapshotWithoutLastSelectedAtDecodesNull()
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\"}";
        Assert.Null(ControlJson.Deserialize<SessionSnapshot>(json)!.LastSelectedAt);
    }

    [Fact]
    public void AMistypedLastSelectedAtDropsWithoutFailingTheDecode()
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\",\"lastSelectedAt\":\"yesterday\",\"customName\":\"keep\"}";
        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;
        Assert.Null(session.LastSelectedAt);
        Assert.Equal("keep", session.CustomName);
    }

    // upstream anUnknownTintColorDropsOnlyTheTint (f3fea57b)
    [Fact]
    public void AnUnknownTintColorDropsOnlyTheTint()
    {
        var session = ControlJson.Deserialize<SessionSnapshot>(
            "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\",\"flagged\":true,\"tint\":{\"color\":\"teal\",\"on\":[\"name\"]}}")!;
        Assert.Null(session.Tint);
        Assert.True(session.Flagged);

        var workspace = ControlJson.Deserialize<WorkspaceSnapshot>(
            "{\"id\":\"" + WorkspaceId + "\",\"name\":\"w\",\"sessions\":[],\"collapsed\":true,\"tint\":\"red\"}")!;
        Assert.Null(workspace.Tint);
        Assert.True(workspace.Collapsed);
    }

    [Fact]
    public void TintRoundTripsInBothSnapshots()
    {
        var sessionJson = "{\"id\":\"" + SessionId.ToString().ToUpperInvariant() +
                          "\",\"cwd\":\"/h\",\"tint\":{\"color\":\"blue\",\"on\":[\"icon\",\"name\"]}}";
        Assert.Equal(sessionJson, ControlJson.Serialize(ControlJson.Deserialize<SessionSnapshot>(sessionJson)));

        var workspaceJson = "{\"id\":\"" + WorkspaceId.ToString().ToUpperInvariant() +
                            "\",\"name\":\"w\",\"sessions\":[],\"tint\":{\"color\":\"gray\",\"on\":[\"name\"]}}";
        Assert.Equal(workspaceJson, ControlJson.Serialize(ControlJson.Deserialize<WorkspaceSnapshot>(workspaceJson)));
    }

    [Fact]
    public void SnapshotWithoutContextDecodesNull()
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\"}";
        Assert.Null(ControlJson.Deserialize<SessionSnapshot>(json)!.Context);
    }

    // stored is the JSON-ESCAPED body: an unescaped control byte is invalid JSON, so embedding one
    // would test the parser instead of the context rule (upstream handEditedInvalidContextDrops...)
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PR\\n517")]
    [InlineData("PR\\u2028517")]
    [InlineData("PR\\t517")]
    [InlineData("PR517\\n")]
    public void HandEditedInvalidContextDropsWithoutFailingTheDecode(string stored)
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\",\"context\":\"" + stored + "\",\"customName\":\"keep\"}";
        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;
        Assert.Null(session.Context);
        Assert.Equal("keep", session.CustomName);
    }

    [Fact]
    public void HandEditedOversizedContextDropsWithoutFailingTheDecode()
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\",\"context\":\"" + new string('a', Session.ContextByteLimit + 1) +
                   "\",\"customName\":\"keep\"}";
        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;
        Assert.Null(session.Context);
        Assert.Equal("keep", session.CustomName);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(true)]
    public void ContextOfAWrongJsonTypeDecodesNull(object value)
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\",\"context\":" + value.ToString()!.ToLowerInvariant() + "}";
        Assert.Null(ControlJson.Deserialize<SessionSnapshot>(json)!.Context);
    }

    [Fact]
    public void HandEditedContextIsTrimmedOnDecode()
    {
        var json = "{\"id\":\"" + SessionId + "\",\"cwd\":\"/tmp\",\"context\":\"  PR #517  \"}";
        Assert.Equal("PR #517", ControlJson.Deserialize<SessionSnapshot>(json)!.Context);
    }

    [Fact]
    public void SnapshotFieldNamesMatchParitySurfaceInOrder()
    {
        var snapshot = new Snapshot
        {
            Version = Snapshot.CurrentVersion, SelectedSessionId = SessionId,
            Workspaces = Array.Empty<WorkspaceSnapshot>(), SidebarWidth = 240.0,
            SidebarVisible = true, SidebarMode = SidebarMode.Tree,
            FocusedWorkspaceIds = new[] { WorkspaceId }, FocusEnabled = true, SessionRecency = new[] { SessionId },
            FlaggedOrder = new[] { SessionId },
        };
        using var doc = JsonDocument.Parse(ControlJson.Serialize(snapshot));
        // the pin's single focusedWorkspaceID became a set plus a filter flag (upstream 6722755a).
        var pin = SurfaceFieldNames("snapshot")
            .SelectMany(name => name == "focusedWorkspaceID"
                ? new[] { "focusedWorkspaceIDs", "focusEnabled" }
                : new[] { name })
            .ToList();
        Assert.Equal(ParitySurface.UpstreamPersistence("snapshot"), pin);
        // the pin plus what adopted.json takes, in upstream order (flaggedOrder: 8088e146)
        var wanted = pin.Concat(ParitySurface.Adopted("snapshot")).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(ParitySurface.LatestUpstreamPersistence("snapshot").Where(wanted.Contains).ToList(),
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public void WorkspaceSnapshotFieldNamesMatchParitySurfacePlusWindowsOnlyInOrder()
    {
        var workspace = new WorkspaceSnapshot
        {
            Id = WorkspaceId, Name = "w", Sessions = Array.Empty<SessionSnapshot>(),
            Collapsed = true, Tint = new SidebarTint(TintColor.Red), Theme = "Nord", Icon = "MapPin",
        };
        using var doc = JsonDocument.Parse(ControlJson.Serialize(workspace));
        // the pin plus what adopted.json takes, in upstream order, then the Windows-only fields
        var wanted = SurfaceFieldNames("workspaceSnapshot").Concat(ParitySurface.Adopted("workspaceSnapshot"))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            ParitySurface.LatestUpstreamPersistence("workspaceSnapshot").Where(wanted.Contains)
                .Concat(ParitySurface.WindowsOnly("workspaceSnapshot")).ToList(),
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public void WorkspaceSnapshotWritesTheIconAndOmitsItWhenAbsent()
    {
        var workspace = new WorkspaceSnapshot
        {
            Id = WorkspaceId, Name = "w", Sessions = Array.Empty<SessionSnapshot>(), Icon = "MapPin",
        };

        Assert.Contains("\"icon\":\"MapPin\"", ControlJson.Serialize(workspace));
        Assert.DoesNotContain("icon", ControlJson.Serialize(workspace with { Icon = null }));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("\"  \"")]
    public void AnIconThatIsNotANameReadsAsNoneAndKeepsTheWorkspace(string value)
    {
        var json = $$"""{"id":"{{WorkspaceId.ToString().ToUpperInvariant()}}","name":"w","sessions":[],"icon":{{value}}}""";

        var workspace = Assert.IsType<WorkspaceSnapshot>(ControlJson.Deserialize<WorkspaceSnapshot>(json));

        Assert.Null(workspace.Icon);
        Assert.Equal("w", workspace.Name);
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
            FocusedWorkspaceIds = new[] { WorkspaceId }, FocusEnabled = true, SessionRecency = new[] { SessionId },
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

    private static string SnapshotWith(string field) => "{\"version\":1,\"workspaces\":[]," + field + "}";

    [Theory]
    [InlineData("\"sidebarMode\":\"grid\"")] // unknown raw value from a newer build
    [InlineData("\"sidebarMode\":5")]
    public void UnknownSidebarModeDecodesLossyToNull(string field)
    {
        Assert.Null(ControlJson.Deserialize<Snapshot>(SnapshotWith(field))!.SidebarMode);
    }

    [Theory]
    [InlineData("\"selectedSessionID\":\"nope\"")]
    [InlineData("\"selectedSessionID\":7")]
    public void BadSelectedSessionIdDecodesLossyToNull(string field)
    {
        Assert.Null(ControlJson.Deserialize<Snapshot>(SnapshotWith(field))!.SelectedSessionId);
    }

    [Theory]
    [InlineData("\"sidebarWidth\":\"wide\"")]
    [InlineData("\"sidebarVisible\":\"yes\"")]
    [InlineData("\"focusEnabled\":\"yes\"")]
    public void BadTopLevelScalarsDecodeLossyAndKeepTheTree(string field)
    {
        var snapshot = ControlJson.Deserialize<Snapshot>(SnapshotWith(field))!;
        Assert.Null(snapshot.SidebarWidth);
        Assert.Null(snapshot.SidebarVisible);
        Assert.Null(snapshot.FocusEnabled);
        Assert.Equal(1, snapshot.Version);
    }

    [Fact]
    public void FocusSetAndFlagRoundTripInTheirOwnKeys()
    {
        var json = SnapshotWith("\"focusedWorkspaceIDs\":[\"" + WorkspaceId.ToString().ToUpperInvariant() + "\"],\"focusEnabled\":true");
        var snapshot = ControlJson.Deserialize<Snapshot>(json)!;

        Assert.Equal(new[] { WorkspaceId }, snapshot.FocusedWorkspaceIds);
        Assert.True(snapshot.FocusEnabled);
        Assert.Equal(json, ControlJson.Serialize(snapshot));
    }

    [Fact]
    public void LegacyFocusedWorkspaceIdMigratesToAnEnabledOneMemberSet()
    {
        var snapshot = ControlJson.Deserialize<Snapshot>(
            SnapshotWith("\"focusedWorkspaceID\":\"" + WorkspaceId.ToString().ToUpperInvariant() + "\""))!;

        Assert.Equal(new[] { WorkspaceId }, snapshot.FocusedWorkspaceIds);
        Assert.True(snapshot.FocusEnabled);
        // the legacy key is never written back
        var written = ControlJson.Serialize(snapshot);
        Assert.DoesNotContain("\"focusedWorkspaceID\"", written);
        Assert.Contains("\"focusedWorkspaceIDs\"", written);
    }

    [Fact]
    public void LegacyFocusIsIgnoredWhenTheSetKeyIsPresent()
    {
        var other = Guid.Parse("AAAA1111-0000-0000-0000-000000000002");
        var snapshot = ControlJson.Deserialize<Snapshot>(SnapshotWith(
            "\"focusedWorkspaceID\":\"" + WorkspaceId + "\",\"focusedWorkspaceIDs\":[\"" + other + "\"],\"focusEnabled\":false"))!;

        Assert.Equal(new[] { other }, snapshot.FocusedWorkspaceIds);
        Assert.False(snapshot.FocusEnabled);
    }

    [Fact]
    public void LegacyFocusDoesNotOverrideAnExplicitFalseWhenTheSetIsMalformed()
    {
        // a malformed set is present-but-unusable, not absent: the migration must not fire over an explicit false.
        var snapshot = ControlJson.Deserialize<Snapshot>(SnapshotWith(
            "\"focusedWorkspaceID\":\"" + WorkspaceId + "\",\"focusedWorkspaceIDs\":[\"not-a-uuid\"],\"focusEnabled\":false"))!;

        Assert.Empty(snapshot.FocusedWorkspaceIds ?? []);
        Assert.False(snapshot.FocusEnabled);
    }

    [Theory]
    [InlineData("[\"not-a-uuid\"]")]
    [InlineData("\"wrong type\"")]
    [InlineData("[42]")]
    public void BadFocusSetDecodesLossyAndKeepsTheTree(string idsJson)
    {
        var snapshot = ControlJson.Deserialize<Snapshot>(SnapshotWith("\"focusedWorkspaceIDs\":" + idsJson))!;

        Assert.Empty(snapshot.FocusedWorkspaceIds ?? []);
        Assert.Equal(1, snapshot.Version);
    }

    [Fact]
    public void BadWorkspaceAndSessionOptionalsDecodeLossyAndKeepTheTree()
    {
        var json = "{\"version\":1,\"workspaces\":[{\"id\":\"" + WorkspaceId + "\",\"name\":\"w\",\"collapsed\":\"yes\"," +
                   "\"sessions\":[{\"id\":\"" + SessionId + "\",\"cwd\":\"/h\",\"fontSize\":\"big\",\"isSplit\":3," +
                   "\"customName\":9,\"splitRatio\":\"half\",\"flagged\":\"x\",\"foregroundCommand\":\"ls\"," +
                   "\"splitForegroundCommand\":[1],\"initialCommand\":5,\"commandWait\":\"w\",\"restoreCommand\":1," +
                   "\"splitRestoreCommand\":[],\"splitCwd\":2}]}]}";
        var snapshot = ControlJson.Deserialize<Snapshot>(json)!;

        var workspace = snapshot.Workspaces[0];
        Assert.Null(workspace.Collapsed);
        var session = workspace.Sessions[0];
        Assert.Equal("/h", session.Cwd);
        Assert.Null(session.FontSize);
        Assert.Null(session.IsSplit);
        Assert.Null(session.CustomName);
        Assert.Null(session.SplitRatio);
        Assert.Null(session.Flagged);
        Assert.Null(session.ForegroundCommand);
        Assert.Null(session.SplitForegroundCommand);
        Assert.Null(session.InitialCommand);
        Assert.Null(session.CommandWait);
        Assert.Null(session.RestoreCommand);
        Assert.Null(session.SplitRestoreCommand);
        Assert.Null(session.SplitCwd);
    }

    [Fact]
    public void IdentityStaysStrict()
    {
        Assert.Throws<JsonException>(() => ControlJson.Deserialize<Snapshot>(
            "{\"version\":1,\"workspaces\":[{\"id\":\"" + WorkspaceId + "\",\"name\":\"w\"," +
            "\"sessions\":[{\"id\":\"" + SessionId + "\",\"cwd\":5}]}]}"));
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
