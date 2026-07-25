using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore/Tests/agtermCoreTests/ControlProtocolTests.swift.
/// Value equality (Swift Codable ==) is asserted via canonical JSON.</summary>
public class ControlProtocolTests
{
    // round-trip a value through JSON and back, asserting equality with the original.
    private static T RoundTrip<T>(T value)
    {
        var decoded = ControlJson.Deserialize<T>(ControlJson.Serialize(value))!;
        Assert.Equal(CanonicalJson.Of(value), CanonicalJson.Of(decoded));
        return decoded;
    }

    private static string Encoded<T>(T value) => ControlJson.Serialize(value);

    private static ControlSessionNode Node(string id = "s1", string name = "shell", string cwd = "/tmp",
        bool active = true, bool split = false) => new()
    {
        Id = id, Name = name, Cwd = cwd, Active = active, Split = split,
    };

    private static ControlResponse TreeResponse(ControlSessionNode session) => new()
    {
        Ok = true,
        Result = new ControlResult
        {
            Tree = new ControlTree
            {
                Workspaces = new[]
                {
                    new ControlWorkspaceNode { Id = "w1", Name = "work", Active = true, Sessions = new[] { session } },
                },
            },
        },
    };

    private static ControlSessionNode FirstSession(ControlResponse response) =>
        response.Result!.Tree!.Workspaces[0].Sessions[0];

    [Fact]
    public void TreeRequestRoundTrips()
    {
        RoundTrip(new ControlRequest { Cmd = Command.Tree });
    }

    [Fact]
    public void WorkspaceCommandsRoundTrip()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.WorkspaceNew, Args = new ControlArgs { Name = "work" } },
            new ControlRequest { Cmd = Command.WorkspaceRename, Target = "active", Args = new ControlArgs { Name = "renamed" } },
            new ControlRequest { Cmd = Command.WorkspaceDelete, Target = "9f3c" },
            new ControlRequest { Cmd = Command.WorkspaceSelect, Target = "9f3c" },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void SessionCommandsRoundTrip()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { Cwd = "/tmp", Workspace = "active" } },
            new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { Cwd = "/tmp", Command = "ssh host -p 22" } },
            new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { Name = "myhost", Command = "ssh host" } },
            new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { WorkspaceName = "servers", CreateWorkspace = true } },
            new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { NoSelect = true } },
            new ControlRequest { Cmd = Command.SessionDuplicate, Target = "9f3c" },
            new ControlRequest { Cmd = Command.SessionDuplicate, Target = "active", Args = new ControlArgs { Window = "win" } },
            new ControlRequest { Cmd = Command.SessionClose, Target = "9f3c" },
            new ControlRequest { Cmd = Command.SessionClose, Args = new ControlArgs { Targets = new[] { "9f3c", "abcd" } } },
            new ControlRequest { Cmd = Command.SessionSelect, Target = "9f3c" },
            new ControlRequest { Cmd = Command.SessionRename, Target = "active", Args = new ControlArgs { Name = "build" } },
            new ControlRequest { Cmd = Command.SessionReveal, Target = "active" },
            new ControlRequest { Cmd = Command.SessionMove, Target = "9f3c", Args = new ControlArgs { Workspace = "other" } },
            new ControlRequest { Cmd = Command.SessionMove, Args = new ControlArgs { Targets = new[] { "9f3c", "abcd" }, Workspace = "other" } },
            new ControlRequest { Cmd = Command.SessionMove, Args = new ControlArgs { Targets = new[] { "9f3c", "abcd" }, After = "anchor" } },
            new ControlRequest { Cmd = Command.SessionCopy, Target = "9f3c" },
            new ControlRequest { Cmd = Command.SessionPaste, Target = "9f3c" },
            new ControlRequest { Cmd = Command.SessionSelectAll, Target = "9f3c" },
            new ControlRequest { Cmd = Command.SessionOverlayOpen, Target = "9f3c", Args = new ControlArgs { Cwd = "/b", Command = "revdiff" } },
            new ControlRequest { Cmd = Command.SessionOverlayOpen, Target = "9f3c", Args = new ControlArgs { Command = "htop", SizePercent = 70 } },
            new ControlRequest { Cmd = Command.SessionOverlayOpen, Target = "9f3c", Args = new ControlArgs { Command = "revdiff", Color = "#2a1a3a" } },
            new ControlRequest { Cmd = Command.SessionOverlayClose, Target = "9f3c" },
            new ControlRequest { Cmd = Command.SessionOverlayResize, Target = "9f3c", Args = new ControlArgs { SizePercent = 60 } },
            new ControlRequest { Cmd = Command.SessionOverlayResize, Target = "9f3c", Args = new ControlArgs { Full = true } },
            new ControlRequest { Cmd = Command.SessionOverlayResult, Target = "9f3c" },
            new ControlRequest
            {
                Cmd = Command.SurfaceZoom, Target = "surface:5E5B1C5B-75C5-49E6-8806-2C61D8D6BBA9:right",
                Args = new ControlArgs { Mode = "show", Window = "win" },
            },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void SessionBatchTargetsOmitWhenNilAndRoundTripWhenSet()
    {
        var single = new ControlRequest { Cmd = Command.SessionClose, Target = "9f3c" };
        Assert.DoesNotContain("targets", Encoded(single));

        var batch = new ControlRequest { Cmd = Command.SessionClose, Args = new ControlArgs { Targets = new[] { "a", "b" } } };
        var decoded = RoundTrip(batch);
        Assert.Equal(new[] { "a", "b" }, decoded.Args!.Targets);
    }

    [Fact]
    public void SessionOverlayResizeOmitsFullWhenNil()
    {
        var request = new ControlRequest { Cmd = Command.SessionOverlayResize, Target = "9f3c", Args = new ControlArgs { SizePercent = 60 } };
        var decoded = RoundTrip(request);
        Assert.Null(decoded.Args!.Full);
        // omit-when-nil WIRE contract for `full`: a percent resize must not emit `full` at all.
        Assert.DoesNotContain("full", Encoded(request));
    }

    [Fact]
    public void SessionOverlayOpenRoundTripsWithFollow()
    {
        var follow = new ControlRequest { Cmd = Command.SessionOverlayOpen, Target = "9f3c", Args = new ControlArgs { Command = "revdiff", Follow = true } };
        Assert.True(RoundTrip(follow).Args!.Follow);

        var noFollow = new ControlRequest { Cmd = Command.SessionOverlayOpen, Target = "9f3c", Args = new ControlArgs { Command = "revdiff", Follow = false } };
        Assert.False(RoundTrip(noFollow).Args!.Follow);
    }

    [Fact]
    public void SessionOverlayOpenOmitsFollowWhenNil()
    {
        var request = new ControlRequest { Cmd = Command.SessionOverlayOpen, Target = "9f3c", Args = new ControlArgs { Command = "revdiff" } };
        var decoded = RoundTrip(request);
        Assert.Null(decoded.Args!.Follow);
        // a nil follow must not be encoded at all, not emitted as null.
        Assert.DoesNotContain("follow", Encoded(request));
    }

    [Fact]
    public void SessionTextRoundTripsWithAllLinesAndPane()
    {
        var request = new ControlRequest { Cmd = Command.SessionText, Target = "9f3c", Args = new ControlArgs { Pane = "left", All = true, Lines = 50 } };
        var decoded = RoundTrip(request);
        Assert.Equal(Command.SessionText, decoded.Cmd);
        Assert.True(decoded.Args!.All);
        Assert.Equal(50, decoded.Args.Lines);
        Assert.Equal("left", decoded.Args.Pane);
    }

    [Fact]
    public void SessionTextBareRoundTrips()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionText });
        Assert.Null(decoded.Args?.All);
        Assert.Null(decoded.Args?.Lines);
        Assert.Null(decoded.Args?.Pane);
    }

    [Fact]
    public void SessionTypeWithSelectRoundTrips()
    {
        RoundTrip(new ControlRequest { Cmd = Command.SessionType, Target = "9f3c", Args = new ControlArgs { Text = "ls\n", Select = true } });
    }

    [Fact]
    public void SessionTypeWithoutSelectRoundTrips()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionType, Target = "active", Args = new ControlArgs { Text = "pwd\n" } });
        Assert.Null(decoded.Args!.Select);
    }

    [Fact]
    public void SessionTypeWithPaneRoundTrips()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionType, Target = "9f3c", Args = new ControlArgs { Text = "ls\n", Pane = "right" } });
        Assert.Equal("right", decoded.Args!.Pane);
    }

    [Fact]
    public void SessionSeenRoundTrips()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionSeen, Target = "9f3c", Args = new ControlArgs { Window = "win" } });
        Assert.Equal(Command.SessionSeen, decoded.Cmd);
    }

    [Fact]
    public void SessionRestoreRoundTripsEachMode()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.SessionRestore, Target = "9f3c", Args = new ControlArgs { Mode = "set", Command = "claude --resume abc" } },
            new ControlRequest { Cmd = Command.SessionRestore, Target = "9f3c", Args = new ControlArgs { Mode = "none" } },
            new ControlRequest { Cmd = Command.SessionRestore, Target = "9f3c", Args = new ControlArgs { Mode = "clear" } },
        };
        foreach (var request in cases)
            Assert.Equal(Command.SessionRestore, RoundTrip(request).Cmd);
    }

    [Fact]
    public void SessionRestoreRoundTripsWithPaneAndWindow()
    {
        var request = new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "9f3c",
            Args = new ControlArgs
            {
                Mode = "set", Command = "cd repo && claude -r xyz",
                Window = "win", Pane = "right", PaneId = "surface-token-abc",
            },
        };
        var decoded = RoundTrip(request);
        Assert.Equal("cd repo && claude -r xyz", decoded.Args!.Command);
        Assert.Equal("right", decoded.Args.Pane);
        Assert.Equal("surface-token-abc", decoded.Args.PaneId);
        Assert.Equal("win", decoded.Args.Window);
    }

    [Fact]
    public void SessionRestoreOmitsCommandWhenNil()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionRestore, Target = "9f3c", Args = new ControlArgs { Mode = "clear" } });
        Assert.Null(decoded.Args!.Command);
        Assert.Null(decoded.Args.Pane);
        Assert.Null(decoded.Args.PaneId);
    }

    [Fact]
    public void SessionStatusRoundTripsWithStateAndBlink()
    {
        var decoded = RoundTrip(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "9f3c",
            Args = new ControlArgs { Status = "active", Blink = true, AutoReset = true },
        });
        Assert.Equal(Command.SessionStatus, decoded.Cmd);
        Assert.Equal("active", decoded.Args!.Status);
        Assert.True(decoded.Args.Blink);
        Assert.True(decoded.Args.AutoReset);
    }

    [Fact]
    public void SessionStatusRoundTripsWithSound()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { Status = "blocked", Sound = "Glass" } });
        Assert.Equal("Glass", decoded.Args!.Sound);
    }

    [Fact]
    public void SessionStatusOmitsSoundWhenNil()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { Status = "active" } });
        Assert.Null(decoded.Args!.Sound);
    }

    [Fact]
    public void SessionStatusRoundTripsWithColor()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { Status = "blocked", Color = "#ff0000" } });
        Assert.Equal("#ff0000", decoded.Args!.Color);
    }

    [Fact]
    public void SessionStatusOmitsColorWhenNil()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { Status = "active" } });
        Assert.Null(decoded.Args!.Color);
    }

    [Fact]
    public void SessionStatusRoundTripsWithShape()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { Status = "blocked", Shape = "triangle" } });
        Assert.Equal("triangle", decoded.Args!.Shape);
    }

    [Fact]
    public void SessionStatusOmitsShapeWhenNil()
    {
        var request = new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { Status = "active" } };
        Assert.DoesNotContain("shape", Encoded(request));
        Assert.Null(RoundTrip(request).Args!.Shape);
    }

    [Fact]
    public void SessionStatusRoundTripsWithPaneID()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { PaneId = "surface-token-abc", Status = "blocked" } });
        Assert.Equal("surface-token-abc", decoded.Args!.PaneId);
    }

    [Fact]
    public void SessionStatusOmitsPaneIDWhenNil()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionStatus, Target = "9f3c", Args = new ControlArgs { Status = "active" } });
        Assert.Null(decoded.Args!.PaneId);
    }

    [Fact]
    public void SessionStatusDecodesSound()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.status\",\"args\":{\"status\":\"blocked\",\"sound\":\"default\"}}")!;
        Assert.Equal(Command.SessionStatus, decoded.Cmd);
        Assert.Equal("blocked", decoded.Args!.Status);
        Assert.Equal("default", decoded.Args.Sound);
    }

    [Fact]
    public void SessionStatusRawStringMapsToCommandAndArgs()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.status\",\"args\":{\"status\":\"blocked\"}}")!;
        Assert.Equal(Command.SessionStatus, decoded.Cmd);
        Assert.Equal("blocked", decoded.Args!.Status);
        Assert.Null(decoded.Args.Blink);
        Assert.Null(decoded.Args.AutoReset);
    }

    [Fact]
    public void SessionStatusDecodesAutoReset()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.status\",\"args\":{\"status\":\"completed\",\"autoReset\":true}}")!;
        Assert.Equal(Command.SessionStatus, decoded.Cmd);
        Assert.Equal("completed", decoded.Args!.Status);
        Assert.True(decoded.Args.AutoReset);
    }

    [Fact]
    public void SessionStatusUnknownStateDecodesForServerToReject()
    {
        // an unknown status string decodes fine; the server rejects it when parsing to AgentStatus.
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.status\",\"args\":{\"status\":\"bogus\"}}")!;
        Assert.Equal(Command.SessionStatus, decoded.Cmd);
        Assert.Equal("bogus", decoded.Args!.Status);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            ControlJson.Deserialize<AgentStatus>($"\"{decoded.Args.Status}\""));
    }

    [Fact]
    public void ModeBearingCommandsRoundTrip()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.SessionSplit, Target = "active", Args = new ControlArgs { Mode = "toggle" } },
            new ControlRequest { Cmd = Command.SessionScratch, Target = "active", Args = new ControlArgs { Mode = "toggle" } },
            new ControlRequest { Cmd = Command.SessionScratch, Target = "9f3c", Args = new ControlArgs { Mode = "on" } },
            new ControlRequest { Cmd = Command.SessionScratch, Target = "active", Args = new ControlArgs { Mode = "on", Command = "htop" } },
            new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "show" } },
            new ControlRequest { Cmd = Command.Sidebar, Args = new ControlArgs { Mode = "hide" } },
            new ControlRequest { Cmd = Command.SessionFlag, Target = "active", Args = new ControlArgs { Mode = "toggle" } },
            new ControlRequest { Cmd = Command.SessionFlag, Target = "9f3c", Args = new ControlArgs { Mode = "on" } },
            new ControlRequest { Cmd = Command.SessionFlag, Args = new ControlArgs { Mode = "clear" } },
            new ControlRequest { Cmd = Command.SidebarMode, Args = new ControlArgs { Mode = "flagged" } },
            new ControlRequest { Cmd = Command.SidebarMode, Args = new ControlArgs { Mode = "toggle" } },
            new ControlRequest { Cmd = Command.WorkspaceFocus, Target = "active", Args = new ControlArgs { Mode = "on" } },
            new ControlRequest { Cmd = Command.WorkspaceFocus, Target = "9f3c", Args = new ControlArgs { Mode = "off" } },
            new ControlRequest { Cmd = Command.WorkspaceFocus, Target = "active", Args = new ControlArgs { Mode = "toggle" } },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void QuickTypeAndTextCommandsRoundTrip()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.QuickType, Args = new ControlArgs { Text = "hello\n" } },
            new ControlRequest { Cmd = Command.QuickText },
            new ControlRequest { Cmd = Command.QuickText, Args = new ControlArgs { All = true } },
            new ControlRequest { Cmd = Command.QuickText, Args = new ControlArgs { Lines = 50 } },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void SessionScratchRoundTripsWithCommand()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionScratch, Target = "active", Args = new ControlArgs { Mode = "on", Command = "htop" } });
        Assert.Equal("on", decoded.Args!.Mode);
        Assert.Equal("htop", decoded.Args.Command);
    }

    [Fact]
    public void SessionResizeRoundTrips()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.SessionResize, Target = "active", Args = new ControlArgs { Ratio = 0.7 } },
            new ControlRequest { Cmd = Command.SessionResize, Target = "9f3c", Args = new ControlArgs { RatioDelta = 0.05 } },
            new ControlRequest { Cmd = Command.SessionResize, Args = new ControlArgs { RatioDelta = -0.05 } },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void SessionResizeRawStringMapsToCommandAndArgs()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.resize\",\"target\":\"active\",\"args\":{\"ratio\":0.7}}")!;
        Assert.Equal(Command.SessionResize, decoded.Cmd);
        Assert.Equal(0.7, decoded.Args!.Ratio);
        Assert.Null(decoded.Args.RatioDelta);
    }

    [Fact]
    public void SessionResizeResultRoundTripsRatio()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c", Ratio = 0.85 } };
        Assert.Equal(0.85, RoundTrip(response).Result!.Ratio);
    }

    [Fact]
    public void SessionFlagRawStringMapsToCommandAndMode()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.flag\",\"target\":\"active\",\"args\":{\"mode\":\"on\"}}")!;
        Assert.Equal(Command.SessionFlag, decoded.Cmd);
        Assert.Equal("on", decoded.Args!.Mode);
    }

    [Fact]
    public void SidebarModeRawStringMapsToCommand()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"sidebar.mode\",\"args\":{\"mode\":\"flagged\"}}")!;
        Assert.Equal(Command.SidebarMode, decoded.Cmd);
        Assert.Equal("flagged", decoded.Args!.Mode);
    }

    [Fact]
    public void WorkspaceFocusRawStringMapsToCommandAndMode()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"workspace.focus\",\"target\":\"active\",\"args\":{\"mode\":\"on\"}}")!;
        Assert.Equal(Command.WorkspaceFocus, decoded.Cmd);
        Assert.Equal("on", decoded.Args!.Mode);
        Assert.Equal("active", decoded.Target);
    }

    [Fact]
    public void SessionBackgroundRoundTrips()
    {
        var cases = new[]
        {
            new ControlRequest
            {
                Cmd = Command.SessionBackground, Target = "active",
                Args = new ControlArgs { Mode = "image", Path = "/tmp/bg.png", Opacity = 0.2, Fit = "cover", Position = "top-left", Repeats = true },
            },
            new ControlRequest
            {
                Cmd = Command.SessionBackground, Target = "9f3c",
                Args = new ControlArgs { Text = "DRAFT", Mode = "text", Color = "#ff0000", Opacity = 0.15, Fit = "contain", Position = "center" },
            },
            new ControlRequest { Cmd = Command.SessionBackground, Target = "active", Args = new ControlArgs { Mode = "color", Color = "#112233" } },
            new ControlRequest { Cmd = Command.SessionBackground, Target = "active", Args = new ControlArgs { Mode = "clear" } },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void SessionBackgroundRawStringMapsToCommandAndArgs()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.background\",\"target\":\"active\",\"args\":{\"mode\":\"text\",\"text\":\"DRAFT\",\"color\":\"#ff0000\",\"opacity\":0.15}}")!;
        Assert.Equal(Command.SessionBackground, decoded.Cmd);
        Assert.Equal("text", decoded.Args!.Mode);
        Assert.Equal("DRAFT", decoded.Args.Text);
        Assert.Equal("#ff0000", decoded.Args.Color);
        Assert.Equal(0.15, decoded.Args.Opacity);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithFlagged()
    {
        var session = Node() with { Flagged = true };
        Assert.True(FirstSession(RoundTrip(TreeResponse(session))).Flagged);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithTitle()
    {
        var session = Node(name: "build") with { Title = "user@web1: ~" };
        Assert.Equal("user@web1: ~", FirstSession(RoundTrip(TreeResponse(session))).Title);
    }

    [Fact]
    public void TreeSessionNodeOmitsTitleWhenNil()
    {
        // a session with no reported title must omit the key entirely, not emit null.
        var json = Encoded(Node());
        Assert.DoesNotContain("title", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.Title);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithForeground()
    {
        var session = Node(split: true) with { Foreground = new[] { "ssh", "gate" }, SplitForeground = new[] { "tail", "-f", "/x" } };
        var node = FirstSession(RoundTrip(TreeResponse(session)));
        Assert.Equal(new[] { "ssh", "gate" }, node.Foreground);
        Assert.Equal(new[] { "tail", "-f", "/x" }, node.SplitForeground);
    }

    [Fact]
    public void TreeSessionNodeOmitsForegroundWhenNil()
    {
        // a pane at its prompt has no foreground command — the keys must be omitted, not null.
        Assert.DoesNotContain("foreground", Encoded(Node()));
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithFontSizes()
    {
        var session = Node(split: true) with { FontSize = 13, SplitFontSize = 9.5, ScratchFontSize = 11 };
        var node = FirstSession(RoundTrip(TreeResponse(session)));
        Assert.Equal(13, node.FontSize);
        Assert.Equal(9.5, node.SplitFontSize);
        Assert.Equal(11, node.ScratchFontSize);
    }

    [Fact]
    public void TreeSessionNodeOmitsFontSizesWhenNil()
    {
        // an unrealized pane has no live font size — all three keys must be omitted, not null.
        var json = Encoded(Node());
        Assert.DoesNotContain("fontSize", json);
        Assert.DoesNotContain("FontSize", json);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithStatus()
    {
        var session = Node() with { Status = "blocked" };
        Assert.Equal("blocked", FirstSession(RoundTrip(TreeResponse(session))).Status);
    }

    [Fact]
    public void TreeSessionNodeOmitsStatusWhenNil()
    {
        var json = Encoded(Node());
        Assert.DoesNotContain("status", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.Status);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithStatusPane()
    {
        var session = Node(split: true) with { Status = "blocked", StatusPane = "right" };
        Assert.Equal("right", FirstSession(RoundTrip(TreeResponse(session))).StatusPane);
    }

    [Fact]
    public void TreeSessionNodeOmitsStatusPaneWhenNil()
    {
        var json = Encoded(Node());
        Assert.DoesNotContain("statusPane", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.StatusPane);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithStatusBlinkAndColor()
    {
        var session = Node() with { Status = "blocked", StatusBlink = true, StatusColor = "#ff8800" };
        var node = FirstSession(RoundTrip(TreeResponse(session)));
        Assert.True(node.StatusBlink);
        Assert.Equal("#ff8800", node.StatusColor);
    }

    [Fact]
    public void TreeSessionNodeOmitsStatusBlinkAndColorWhenNil()
    {
        var json = Encoded(Node() with { Status = "blocked" });
        Assert.DoesNotContain("statusBlink", json);
        Assert.DoesNotContain("statusColor", json);
        var decoded = ControlJson.Deserialize<ControlSessionNode>(json)!;
        Assert.Null(decoded.StatusBlink);
        Assert.Null(decoded.StatusColor);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithStatusShape()
    {
        var session = Node() with { Status = "blocked", StatusShape = "triangle" };
        Assert.Equal("triangle", FirstSession(RoundTrip(TreeResponse(session))).StatusShape);
    }

    [Fact]
    public void TreeSessionNodeOmitsStatusShapeWhenNil()
    {
        var json = Encoded(Node() with { Status = "blocked" });
        Assert.DoesNotContain("statusShape", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.StatusShape);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithBackground()
    {
        // the read side of session.background: the watermark spec rides the tree node.
        var watermark = new BackgroundWatermark
        {
            Kind = WatermarkKind.Text, Text = "PROD", ColorHex = "#ff0000",
            Opacity = 0.2, Fit = WatermarkFit.Cover, Position = WatermarkPosition.TopRight,
        };
        var session = Node() with { Background = watermark };
        var node = FirstSession(RoundTrip(TreeResponse(session)));
        Assert.Equal(watermark, node.Background);
        Assert.Equal(WatermarkFit.Cover, node.Background!.Fit); // the typed enum survives the wire round-trip
        Assert.Equal(WatermarkPosition.TopRight, node.Background.Position);
    }

    [Fact]
    public void TreeSessionNodeOmitsBackgroundWhenNil()
    {
        var json = Encoded(Node());
        Assert.DoesNotContain("background", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.Background);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithUnseen()
    {
        var session = Node(active: false) with { Unseen = 3 };
        Assert.Equal(3, FirstSession(RoundTrip(TreeResponse(session))).Unseen);
    }

    [Fact]
    public void TreeSessionNodeOmitsUnseenWhenNil()
    {
        var json = Encoded(Node());
        Assert.DoesNotContain("unseen", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.Unseen);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithCommandWait()
    {
        var session = Node(name: "build", active: false) with { CommandWait = true };
        Assert.True(FirstSession(RoundTrip(TreeResponse(session))).CommandWait);
    }

    [Fact]
    public void TreeSessionNodeOmitsCommandWaitWhenNil()
    {
        var json = Encoded(Node());
        Assert.DoesNotContain("commandWait", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.CommandWait);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithOverlaySizePercent()
    {
        var session = Node() with { Overlay = true, OverlaySizePercent = 95 };
        Assert.Equal(95, FirstSession(RoundTrip(TreeResponse(session))).OverlaySizePercent);
    }

    [Fact]
    public void TreeSessionNodeOmitsOverlaySizePercentWhenNil()
    {
        // no overlay, or a FULL-pane overlay — the key must be omitted, not emitted as null.
        var json = Encoded(Node() with { Overlay = true });
        Assert.DoesNotContain("overlaySizePercent", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.OverlaySizePercent);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithRestoreCommand()
    {
        var session = Node(split: true) with { RestoreCommand = "claude --resume abc", SplitRestoreCommand = "tail -f log" };
        var node = FirstSession(RoundTrip(TreeResponse(session)));
        Assert.Equal("claude --resume abc", node.RestoreCommand);
        Assert.Equal("tail -f log", node.SplitRestoreCommand);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsRestoreCommandPinnedToNothing()
    {
        // "" = pinned to nothing (a plain shell). The key must be PRESENT and empty — distinct from
        // the omitted no-override state, which is the whole tri-state.
        var session = Node(split: true) with { RestoreCommand = "", SplitRestoreCommand = "" };
        var json = Encoded(session);
        Assert.Contains("\"restoreCommand\":\"\"", json);
        Assert.Contains("\"splitRestoreCommand\":\"\"", json);
        var decoded = ControlJson.Deserialize<ControlSessionNode>(json)!;
        Assert.Equal("", decoded.RestoreCommand);
        Assert.Equal("", decoded.SplitRestoreCommand);
    }

    [Fact]
    public void TreeSessionNodeOmitsRestoreCommandWhenNil()
    {
        // no override — the keys must be omitted so a script can tell "auto-capture" from
        // "pinned to nothing" ("").
        var json = Encoded(Node());
        Assert.DoesNotContain("restoreCommand", json);
        Assert.DoesNotContain("splitRestoreCommand", json);
        var decoded = ControlJson.Deserialize<ControlSessionNode>(json)!;
        Assert.Null(decoded.RestoreCommand);
        Assert.Null(decoded.SplitRestoreCommand);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithSplitRatio()
    {
        var session = Node(split: true) with { SplitRatio = 0.35 };
        Assert.Equal(0.35, FirstSession(RoundTrip(TreeResponse(session))).SplitRatio);
    }

    [Fact]
    public void TreeSessionNodeOmitsSplitRatioWhenNil()
    {
        var json = Encoded(Node());
        Assert.DoesNotContain("splitRatio", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.SplitRatio);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithSplitFocused()
    {
        // false = the main (left) pane, true = the split (right) pane.
        var session = Node(split: true) with { SplitFocused = false };
        Assert.False(FirstSession(RoundTrip(TreeResponse(session))).SplitFocused);
    }

    [Fact]
    public void TreeSessionNodeOmitsSplitFocusedWhenNil()
    {
        // no split — the key must be omitted (a false value IS emitted, distinct from "no split").
        var json = Encoded(Node());
        Assert.DoesNotContain("splitFocused", json);
        Assert.Null(ControlJson.Deserialize<ControlSessionNode>(json)!.SplitFocused);
    }

    [Fact]
    public void TreeSessionNodeRoundTripsWithSurfaces()
    {
        var surfaces = new[]
        {
            new ControlSurfaceNode { Id = "surface:s1:left", Kind = "left", Active = true, Visible = true },
            new ControlSurfaceNode { Id = "surface:s1:right", Kind = "right", Active = false, Visible = false },
        };
        var session = Node(split: true) with { Surfaces = surfaces };
        Assert.Equal(surfaces, FirstSession(RoundTrip(TreeResponse(session))).Surfaces);
    }

    [Fact]
    public void TreeSessionNodeToleratesMissingSurfaces()
    {
        // a pre-surface.zoom server omits the key — it must decode as null, and a node built
        // without surfaces must omit the key from the JSON.
        var raw = "{\"id\":\"s1\",\"name\":\"shell\",\"cwd\":\"/tmp\",\"active\":true,\"split\":false," +
                  "\"overlay\":false,\"scratch\":false,\"flagged\":false}";
        var decoded = ControlJson.Deserialize<ControlSessionNode>(raw)!;
        Assert.Null(decoded.Surfaces);
        Assert.DoesNotContain("surfaces", Encoded(decoded));
    }

    [Fact]
    public void TreeRoundTripsWithLiveWindowFields()
    {
        var tree = new ControlTree
        {
            Workspaces = new[] { new ControlWorkspaceNode { Id = "w1", Name = "work", Active = true, Sessions = new[] { Node() } } },
            IdleMs = 4200, AutoFollowMs = 30_000, SidebarVisible = false,
        };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Tree = tree } };
        var decoded = RoundTrip(response);
        Assert.Equal(4200, decoded.Result!.Tree!.IdleMs);
        Assert.Equal(30_000, decoded.Result.Tree.AutoFollowMs);
        Assert.False(decoded.Result.Tree.SidebarVisible);
    }

    [Fact]
    public void TreeOmitsLiveWindowFieldsWhenNil()
    {
        var tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() };
        var json = Encoded(tree);
        Assert.DoesNotContain("idleMs", json);
        Assert.DoesNotContain("autoFollowMs", json);
        Assert.DoesNotContain("sidebarVisible", json);
        var decoded = ControlJson.Deserialize<ControlTree>(json)!;
        Assert.Null(decoded.IdleMs);
        Assert.Null(decoded.AutoFollowMs);
        Assert.Null(decoded.SidebarVisible);
    }

    [Fact]
    public void WindowNodeRoundTripsWithPerWindowFields()
    {
        var node = new ControlWindowNode { Id = "w1", Name = "work", Open = true, Active = true, AutoFollowMs = 5000, SidebarVisible = true };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Windows = new[] { node } } };
        var decoded = RoundTrip(response);
        Assert.Equal(5000, decoded.Result!.Windows![0].AutoFollowMs);
        Assert.True(decoded.Result.Windows[0].SidebarVisible);
    }

    [Fact]
    public void WindowNodeOmitsPerWindowFieldsWhenNil()
    {
        var node = new ControlWindowNode { Id = "w1", Name = "work", Open = true, Active = false };
        var json = Encoded(node);
        Assert.DoesNotContain("autoFollowMs", json);
        Assert.DoesNotContain("sidebarVisible", json);
        var decoded = ControlJson.Deserialize<ControlWindowNode>(json)!;
        Assert.Null(decoded.AutoFollowMs);
        Assert.Null(decoded.SidebarVisible);
    }

    [Fact]
    public void WindowNodeRoundTripsWithGeometry()
    {
        var node = new ControlWindowNode
        {
            Id = "w1", Name = "work", Open = true, Active = true,
            Geometry = new ControlWindowFrame { X = 100, Y = 40, Width = 1200, Height = 800, Display = 1 },
        };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Windows = new[] { node } } };
        var frame = RoundTrip(response).Result!.Windows![0].Geometry;
        Assert.Equal(new ControlWindowFrame { X = 100, Y = 40, Width = 1200, Height = 800, Display = 1 }, frame);
    }

    [Fact]
    public void WindowNodeOmitsGeometryWhenNil()
    {
        var node = new ControlWindowNode { Id = "w1", Name = "work", Open = false, Active = false };
        var json = Encoded(node);
        Assert.DoesNotContain("geometry", json);
        Assert.Null(ControlJson.Deserialize<ControlWindowNode>(json)!.Geometry);
    }

    [Fact]
    public void WindowNodeRoundTripsWithFullscreenAndZoom()
    {
        var node = new ControlWindowNode { Id = "w1", Name = "work", Open = true, Active = true, Fullscreen = true, Zoomed = false };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Windows = new[] { node } } };
        var decoded = RoundTrip(response);
        Assert.True(decoded.Result!.Windows![0].Fullscreen);
        Assert.False(decoded.Result.Windows[0].Zoomed);
    }

    [Fact]
    public void WindowNodeOmitsFullscreenAndZoomWhenNil()
    {
        var node = new ControlWindowNode { Id = "w1", Name = "work", Open = false, Active = false };
        var json = Encoded(node);
        Assert.DoesNotContain("fullscreen", json);
        Assert.DoesNotContain("zoomed", json);
        var decoded = ControlJson.Deserialize<ControlWindowNode>(json)!;
        Assert.Null(decoded.Fullscreen);
        Assert.Null(decoded.Zoomed);
    }

    [Fact]
    public void WorkspaceNodeRoundTripsWithFocused()
    {
        var ws = new ControlWorkspaceNode { Id = "w1", Name = "work", Active = true, Focused = true, Sessions = Array.Empty<ControlSessionNode>() };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Tree = new ControlTree { Workspaces = new[] { ws } } } };
        Assert.True(RoundTrip(response).Result!.Tree!.Workspaces[0].Focused);
    }

    [Fact]
    public void WorkspaceNodeOmitsFocusedWhenNil()
    {
        var ws = new ControlWorkspaceNode { Id = "w1", Name = "work", Active = true, Sessions = Array.Empty<ControlSessionNode>() };
        var json = Encoded(ws);
        Assert.DoesNotContain("focused", json);
        Assert.Null(ControlJson.Deserialize<ControlWorkspaceNode>(json)!.Focused);
    }

    [Fact]
    public void WorkspaceNodeRoundTripsWithCollapsed()
    {
        var ws = new ControlWorkspaceNode { Id = "w1", Name = "work", Active = true, Collapsed = true, Sessions = Array.Empty<ControlSessionNode>() };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Tree = new ControlTree { Workspaces = new[] { ws } } } };
        Assert.True(RoundTrip(response).Result!.Tree!.Workspaces[0].Collapsed);
    }

    [Fact]
    public void WorkspaceNodeOmitsCollapsedWhenNil()
    {
        // an expanded workspace (the default) omits the key, so an all-expanded tree stays
        // byte-compatible with a legacy response.
        var ws = new ControlWorkspaceNode { Id = "w1", Name = "work", Active = true, Sessions = Array.Empty<ControlSessionNode>() };
        var json = Encoded(ws);
        Assert.DoesNotContain("collapsed", json);
        Assert.Null(ControlJson.Deserialize<ControlWorkspaceNode>(json)!.Collapsed);
    }

    [Fact]
    public void WorkspaceCollapseExpandRawStringsMapToCommands()
    {
        var collapse = ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"workspace.collapse\",\"target\":\"9f3c\"}")!;
        Assert.Equal(Command.WorkspaceCollapse, collapse.Cmd);
        Assert.Equal("9f3c", collapse.Target);
        var expand = ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"workspace.expand\",\"target\":\"active\"}")!;
        Assert.Equal(Command.WorkspaceExpand, expand.Cmd);
        Assert.Equal("active", expand.Target);
    }

    [Fact]
    public void TreeRoundTripsWithSidebarMode()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult { Tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>(), SidebarMode = "flagged" } },
        };
        Assert.Equal("flagged", RoundTrip(response).Result!.Tree!.SidebarMode);
    }

    [Fact]
    public void TreeRoundTripsWithQuickVisible()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult { Tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>(), QuickVisible = true } },
        };
        Assert.True(RoundTrip(response).Result!.Tree!.QuickVisible);
    }

    [Fact]
    public void TreeOmitsQuickVisibleWhenNil()
    {
        var tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() };
        var json = Encoded(tree);
        Assert.DoesNotContain("quickVisible", json);
        Assert.Null(ControlJson.Deserialize<ControlTree>(json)!.QuickVisible);
    }

    [Fact]
    public void TreeRoundTripsWithZoomedSurface()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Tree = new ControlTree
                {
                    Workspaces = Array.Empty<ControlWorkspaceNode>(),
                    ZoomedSurface = "surface:5E5B1C5B-75C5-49E6-8806-2C61D8D6BBA9:right",
                },
            },
        };
        Assert.Equal("surface:5E5B1C5B-75C5-49E6-8806-2C61D8D6BBA9:right",
            RoundTrip(response).Result!.Tree!.ZoomedSurface);
    }

    [Fact]
    public void TreeOmitsZoomedSurfaceWhenNil()
    {
        var tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() };
        var json = Encoded(tree);
        Assert.DoesNotContain("zoomedSurface", json);
        Assert.Null(ControlJson.Deserialize<ControlTree>(json)!.ZoomedSurface);
    }

    [Fact]
    public void DashboardRequestRoundTrips()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "9f3c", "abcd" } } },
            new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "9f3c" }, Window = "win", FontSize = 12 } },
            new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "9f3c", "abcd" }, AutoSize = true } },
            new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Close = true } },
            new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Window = "win", AutoSize = true, Mru = true } },
        };
        foreach (var request in cases) RoundTrip(request);

        var opened = RoundTrip(new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a", "b" }, FontSize = 14 } });
        Assert.Equal(new[] { "a", "b" }, opened.Args!.Targets);
        Assert.Equal(14, opened.Args.FontSize);
        Assert.Null(opened.Args.AutoSize);
        Assert.Null(opened.Args.Mru);
        var closed = RoundTrip(new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Close = true } });
        Assert.True(closed.Args!.Close);
        var mru = RoundTrip(new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Mru = true } });
        Assert.True(mru.Args!.Mru);
        Assert.Null(mru.Args.Targets);
    }

    [Fact]
    public void DashboardArgsOmitFieldsWhenNil()
    {
        // an open request with only targets must not emit close/fontSize/autoSize/mru at all.
        var request = new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "9f3c" } } };
        var json = Encoded(request);
        Assert.DoesNotContain("close", json);
        Assert.DoesNotContain("fontSize", json);
        Assert.DoesNotContain("autoSize", json);
        Assert.DoesNotContain("mru", json);
    }

    [Fact]
    public void TreeRoundTripsWithDashboardFields()
    {
        // members/highlighted are pane refs (<uuid>:left/:right) — a split session appears as both cells.
        var members = new[] { "9f3c:left", "9f3c:right", "abcd:left" };
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Tree = new ControlTree
                {
                    Workspaces = Array.Empty<ControlWorkspaceNode>(),
                    DashboardMembers = members, DashboardHighlighted = "9f3c:right",
                    DashboardFontSize = 12, DashboardFontMode = "auto",
                },
            },
        };
        var tree = RoundTrip(response).Result!.Tree!;
        Assert.Equal(members, tree.DashboardMembers);
        Assert.Equal("9f3c:right", tree.DashboardHighlighted);
        Assert.Equal(12, tree.DashboardFontSize);
        Assert.Equal("auto", tree.DashboardFontMode);
    }

    [Fact]
    public void TreeOmitsDashboardFieldsWhenNil()
    {
        var tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() };
        var json = Encoded(tree);
        Assert.DoesNotContain("dashboardMembers", json);
        Assert.DoesNotContain("dashboardHighlighted", json);
        Assert.DoesNotContain("dashboardFontSize", json);
        Assert.DoesNotContain("dashboardFontMode", json);
        var decoded = ControlJson.Deserialize<ControlTree>(json)!;
        Assert.Null(decoded.DashboardMembers);
        Assert.Null(decoded.DashboardHighlighted);
        Assert.Null(decoded.DashboardFontSize);
        Assert.Null(decoded.DashboardFontMode);
    }

    [Fact]
    public void BackgroundWatermarkFitPositionSerializeAsRawStrings()
    {
        // the Fit/Position enums must serialize to the engine's exact key strings.
        var watermark = new BackgroundWatermark
        {
            Kind = WatermarkKind.Image, ImagePath = "/a.png",
            Fit = WatermarkFit.Stretch, Position = WatermarkPosition.BottomCenter,
        };
        var json = Encoded(watermark);
        Assert.Contains("\"fit\":\"stretch\"", json);
        Assert.Contains("\"position\":\"bottom-center\"", json);
        // a decoded-back value equals the original (rawValue mapping is lossless).
        Assert.Equal(watermark, ControlJson.Deserialize<BackgroundWatermark>(json));
    }

    [Fact]
    public void BackgroundWatermarkColorKindSerializes()
    {
        // the color kind serializes as "color" and carries only the hex. Round-trip through
        // ControlSessionNode too, so the actual tree wire path covers the color read-back.
        var watermark = new BackgroundWatermark { Kind = WatermarkKind.Color, ColorHex = "#112233" };
        var json = Encoded(watermark);
        Assert.Contains("\"kind\":\"color\"", json);
        Assert.Equal(watermark, ControlJson.Deserialize<BackgroundWatermark>(json));

        var session = Node() with { Background = watermark };
        var node = FirstSession(RoundTrip(TreeResponse(session)));
        Assert.Equal(watermark, node.Background);
        Assert.Equal(WatermarkKind.Color, node.Background!.Kind);
        Assert.Equal("#112233", node.Background.ColorHex);
    }

    [Fact]
    public void RestoreClearRoundTrips()
    {
        RoundTrip(new ControlRequest { Cmd = Command.RestoreClear });
    }

    [Fact]
    public void SessionFocusRoundTripsWithPane()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionFocus, Target = "active", Args = new ControlArgs { Pane = "right" } });
        Assert.Equal("right", decoded.Args!.Pane);
    }

    [Fact]
    public void SessionGoRoundTripsWithDirection()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionGo, Args = new ControlArgs { To = "next" } });
        Assert.Equal(Command.SessionGo, decoded.Cmd);
        Assert.Equal("next", decoded.Args!.To);
    }

    [Fact]
    public void SessionGoRoundTripsWithAttentionDirection()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionGo, Args = new ControlArgs { To = "next-attention" } });
        Assert.Equal("next-attention", decoded.Args!.To);
        Assert.Equal(SessionNavigation.NextAttention, SessionNavigations.FromWire(decoded.Args.To!));
    }

    [Fact]
    public void SessionMoveReorderRoundTripsWithDirection()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionMove, Target = "9f3c", Args = new ControlArgs { To = "up" } });
        Assert.Equal(Command.SessionMove, decoded.Cmd);
        Assert.Equal("up", decoded.Args!.To);
        Assert.Null(decoded.Args.Workspace);
    }

    [Fact]
    public void SessionMoveRoundTripsWithAfterAnchor()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionMove, Target = "9f3c", Args = new ControlArgs { After = "active" } });
        Assert.Equal("active", decoded.Args!.After);
        Assert.Null(decoded.Args.Before);
        Assert.Null(decoded.Args.To);
        Assert.Null(decoded.Args.Workspace);
    }

    [Fact]
    public void SessionMoveRoundTripsWithBeforeAnchor()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionMove, Target = "9f3c", Args = new ControlArgs { Before = "1a2b" } });
        Assert.Equal("1a2b", decoded.Args!.Before);
        Assert.Null(decoded.Args.After);
    }

    [Fact]
    public void SessionNewRoundTripsWithAfterAnchor()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { After = "active" } });
        Assert.Equal("active", decoded.Args!.After);
        Assert.Null(decoded.Args.Before);
        Assert.Null(decoded.Args.Workspace);
    }

    [Fact]
    public void SessionNewRoundTripsWithBeforeAnchor()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { Before = "1a2b" } });
        Assert.Equal("1a2b", decoded.Args!.Before);
        Assert.Null(decoded.Args.After);
    }

    [Fact]
    public void WorkspaceMoveRoundTripsWithDirection()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.WorkspaceMove, Target = "active", Args = new ControlArgs { To = "top" } });
        Assert.Equal(Command.WorkspaceMove, decoded.Cmd);
        Assert.Equal("top", decoded.Args!.To);
    }

    [Fact]
    public void WorkspaceMoveRawStringMapsToCommand()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"workspace.move\",\"target\":\"active\",\"args\":{\"to\":\"bottom\"}}")!;
        Assert.Equal(Command.WorkspaceMove, decoded.Cmd);
        Assert.Equal("bottom", decoded.Args!.To);
    }

    [Fact]
    public void SessionSearchRoundTripsWithNeedleAndDirection()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionSearch, Target = "active", Args = new ControlArgs { Text = "foo", To = "next" } });
        Assert.Equal(Command.SessionSearch, decoded.Cmd);
        Assert.Equal("foo", decoded.Args!.Text);
        Assert.Equal("next", decoded.Args.To);
    }

    [Theory]
    [InlineData("next")]
    [InlineData("prev")]
    [InlineData("close")]
    public void SessionSearchRoundTripsEachDirection(string to)
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionSearch, Target = "active", Args = new ControlArgs { To = to } });
        Assert.Equal(Command.SessionSearch, decoded.Cmd);
        Assert.Equal(to, decoded.Args!.To);
    }

    [Fact]
    public void SessionSearchResultRoundTripsWithCountAndText()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Text = "3 of 12", Count = 12 } };
        var decoded = RoundTrip(response);
        Assert.Equal(12, decoded.Result!.Count);
        Assert.Equal("3 of 12", decoded.Result.Text);
    }

    [Fact]
    public void SessionSearchRawStringMapsToCommand()
    {
        Assert.Equal(Command.SessionSearch,
            ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"session.search\"}")!.Cmd);
    }

    [Fact]
    public void NotifyRoundTripsWithTitleAndBody()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.Notify, Target = "active", Args = new ControlArgs { Title = "Build", Body = "done" } });
        Assert.Equal("Build", decoded.Args!.Title);
        Assert.Equal("done", decoded.Args.Body);
    }

    [Fact]
    public void FontCommandsRoundTrip()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.FontInc, Target = "active" },
            new ControlRequest { Cmd = Command.FontDec, Target = "active" },
            new ControlRequest { Cmd = Command.FontReset, Target = "active" },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void WindowCommandsRoundTrip()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.WindowNew, Args = new ControlArgs { Name = "work" } },
            new ControlRequest { Cmd = Command.WindowList },
            new ControlRequest { Cmd = Command.WindowSelect, Target = "9f3c" },
            new ControlRequest { Cmd = Command.WindowClose, Target = "9f3c" },
            new ControlRequest { Cmd = Command.WindowRename, Target = "active", Args = new ControlArgs { Name = "renamed" } },
            new ControlRequest { Cmd = Command.WindowDelete, Target = "9f3c" },
            new ControlRequest { Cmd = Command.WindowZoom, Target = "9f3c" },
            new ControlRequest { Cmd = Command.WindowFullscreen, Target = "9f3c" },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void KeymapReloadRequestRoundTrips()
    {
        Assert.Equal(Command.KeymapReload, RoundTrip(new ControlRequest { Cmd = Command.KeymapReload }).Cmd);
    }

    [Fact]
    public void KeymapReloadRawStringMapsToCommand()
    {
        Assert.Equal(Command.KeymapReload,
            ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"keymap.reload\"}")!.Cmd);
    }

    [Fact]
    public void ConfigReloadRequestRoundTrips()
    {
        Assert.Equal(Command.ConfigReload, RoundTrip(new ControlRequest { Cmd = Command.ConfigReload }).Cmd);
    }

    [Fact]
    public void ConfigReloadRawStringMapsToCommand()
    {
        Assert.Equal(Command.ConfigReload,
            ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"config.reload\"}")!.Cmd);
    }

    [Fact]
    public void SidebarExpandCollapseRequestsRoundTrip()
    {
        var cases = new[]
        {
            new ControlRequest { Cmd = Command.SidebarExpand },
            new ControlRequest { Cmd = Command.SidebarCollapse },
            new ControlRequest { Cmd = Command.SidebarExpand, Args = new ControlArgs { Window = "abc" } },
            new ControlRequest { Cmd = Command.SidebarCollapse, Args = new ControlArgs { Window = "abc" } },
        };
        foreach (var request in cases) RoundTrip(request);
    }

    [Fact]
    public void SidebarExpandRawStringMapsToCommand()
    {
        Assert.Equal(Command.SidebarExpand,
            ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"sidebar.expand\"}")!.Cmd);
    }

    [Fact]
    public void SidebarCollapseRawStringMapsToCommand()
    {
        Assert.Equal(Command.SidebarCollapse,
            ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"sidebar.collapse\"}")!.Cmd);
    }

    [Fact]
    public void ResponseOkWithCountRoundTrips()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Count = 3 } };
        Assert.Equal(3, RoundTrip(response).Result!.Count);
    }

    [Fact]
    public void ResponseOkWithAffectedRoundTrips()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Affected = 2 } };
        var decoded = RoundTrip(response);
        Assert.Equal(2, decoded.Result!.Affected);
        Assert.Null(decoded.Result.Count);
    }

    [Fact]
    public void ThemeSetRequestRoundTrips()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.ThemeSet, Args = new ControlArgs { Name = "Dracula" } });
        Assert.Equal(Command.ThemeSet, decoded.Cmd);
        Assert.Equal("Dracula", decoded.Args!.Name);
    }

    [Fact]
    public void ThemeSetRawStringMapsToCommand()
    {
        var decoded = ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"theme.set\",\"args\":{\"name\":\"Nord\"}}")!;
        Assert.Equal(Command.ThemeSet, decoded.Cmd);
        Assert.Equal("Nord", decoded.Args!.Name);
    }

    [Fact]
    public void ThemeListRequestRoundTrips()
    {
        Assert.Equal(Command.ThemeList, RoundTrip(new ControlRequest { Cmd = Command.ThemeList }).Cmd);
    }

    [Fact]
    public void ThemeListResponseRoundTrips()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Theme = "Nord", Themes = new[] { "Dracula", "Nord" } } };
        var decoded = RoundTrip(response);
        Assert.Equal("Nord", decoded.Result!.Theme);
        Assert.Equal(new[] { "Dracula", "Nord" }, decoded.Result.Themes);
    }

    [Fact]
    public void ThemeSetResponseEchoesAppliedTheme()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Theme = "Dracula" } };
        var decoded = RoundTrip(response);
        Assert.Equal("Dracula", decoded.Result!.Theme);
        Assert.Null(decoded.Result.Themes);
    }

    [Fact]
    public void ThemeSetSyncRequestRoundTrips()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.ThemeSet, Args = new ControlArgs { Light = "Builtin Light", Dark = "agterm" } });
        Assert.Equal(Command.ThemeSet, decoded.Cmd);
        Assert.Equal("Builtin Light", decoded.Args!.Light);
        Assert.Equal("agterm", decoded.Args.Dark);
        Assert.Null(decoded.Args.Name);
    }

    [Fact]
    public void ThemeListResponseCarriesSyncState()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Theme = "agterm", Themes = new[] { "agterm", "Builtin Light" },
                Sync = true, Light = "Builtin Light", Dark = "agterm",
            },
        };
        var decoded = RoundTrip(response);
        Assert.True(decoded.Result!.Sync);
        Assert.Equal("Builtin Light", decoded.Result.Light);
        Assert.Equal("agterm", decoded.Result.Dark);
    }

    [Fact]
    public void SessionCommandWithWindowArgRoundTrips()
    {
        var decoded = RoundTrip(new ControlRequest { Cmd = Command.SessionSelect, Target = "9f3c", Args = new ControlArgs { Window = "main" } });
        Assert.Equal("main", decoded.Args!.Window);
    }

    [Fact]
    public void RequestUsesExpectedWireFieldNames()
    {
        var request = new ControlRequest { Cmd = Command.SessionType, Target = "9f3c", Args = new ControlArgs { Text = "ls\n", Select = true } };
        var json = Encoded(request);
        Assert.Contains("\"cmd\":\"session.type\"", json);
        Assert.Contains("\"target\":\"9f3c\"", json);
        Assert.Contains("\"args\":", json);
        Assert.Contains("\"text\":\"ls\\n\"", json);
        Assert.Contains("\"select\":true", json);
    }

    [Fact]
    public void ResponseOkWithIDRoundTrips()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } };
        Assert.Equal("9f3c", RoundTrip(response).Result!.Id);
    }

    [Fact]
    public void ResponseOkWithTreeRoundTrips()
    {
        Assert.Equal("shell", FirstSession(RoundTrip(TreeResponse(Node()))).Name);
    }

    [Fact]
    public void ResponseOkWithTextRoundTrips()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Text = "selected\nlines" } };
        Assert.Equal("selected\nlines", RoundTrip(response).Result!.Text);
    }

    [Fact]
    public void ResponseOkWithExitCodeRoundTrips()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c", ExitCode = 10 } };
        Assert.Equal(10, RoundTrip(response).Result!.ExitCode);
    }

    [Fact]
    public void ResponseOkWithWindowsRoundTrips()
    {
        var windows = new[]
        {
            new ControlWindowNode { Id = "w1", Name = "work", Open = true, Active = true },
            new ControlWindowNode { Id = "w2", Name = "personal", Open = false, Active = false },
        };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Windows = windows } };
        var decoded = RoundTrip(response);
        Assert.Equal(2, decoded.Result!.Windows!.Length);
        Assert.Equal("work", decoded.Result.Windows[0].Name);
        Assert.True(decoded.Result.Windows[0].Open);
        Assert.True(decoded.Result.Windows[0].Active);
        Assert.False(decoded.Result.Windows[1].Open);
    }

    [Fact]
    public void WindowsResultUsesExpectedWireFieldNames()
    {
        var windows = new[] { new ControlWindowNode { Id = "w1", Name = "work", Open = true, Active = false } };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Windows = windows } };
        var json = Encoded(response);
        Assert.Contains("\"windows\":", json);
        Assert.Contains("\"id\":\"w1\"", json);
        Assert.Contains("\"name\":\"work\"", json);
        Assert.Contains("\"open\":true", json);
        Assert.Contains("\"active\":false", json);
    }

    [Fact]
    public void ResponseErrorRoundTrips()
    {
        var response = new ControlResponse { Ok = false, Error = "ambiguous prefix '9f'" };
        var decoded = RoundTrip(response);
        Assert.False(decoded.Ok);
        Assert.Equal("ambiguous prefix '9f'", decoded.Error);
    }

    [Fact]
    public void ResponseUsesExpectedWireFieldNames()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } };
        var json = Encoded(response);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"result\":", json);
        Assert.Contains("\"id\":\"9f3c\"", json);
    }

    [Fact]
    public void UnknownCommandFailsToDecode()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"bogus.command\"}"));
    }
}
