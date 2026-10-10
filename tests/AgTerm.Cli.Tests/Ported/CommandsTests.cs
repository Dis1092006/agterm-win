using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Ported 1:1 from agtermCore/Tests/agtermctlKitTests/CommandsTests.swift (upstream pin 7df70fe).
// Request equality is asserted via canonical JSON (CanonicalJson.AssertEqual), matching Swift's
// value equality on ControlRequest. Swift `ControlArgs(...)` with only-nil parameters is a PRESENT
// empty args object and ports to `new ControlArgs()`; a Swift request built with no args at all
// ports to an omitted Args (null).
public class CommandsTests
{
    /// <summary>Parse argv into a subcommand and build its `ControlRequest`. Throws if parsing or request-building fails.</summary>
    private static ControlRequest Request(params string[] argv) =>
        Agtermctl.ParseAsRoot(argv).MakeRequest();

    /// <summary>Parses argv expecting a validation failure and returns the user-facing message, or null when it parses.</summary>
    private static string? ValidationMessage(params string[] argv)
    {
        try
        {
            Agtermctl.ParseAsRoot(argv);
            return null;
        }
        catch (Exception e)
        {
            return Agtermctl.Message(e);
        }
    }

    /// <summary>The wire names of StatusShape.allCases, in declaration order (the Swift test is
    /// parameterized over `StatusShape.allCases`).</summary>
    private static readonly string[] AllShapeWireNames =
        { "circle", "square", "triangle", "diamond", "capsule", "star" };

    [Fact]
    public void Tree()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Tree },
            Request("tree"));
    }

    [Fact]
    public void WorkspaceNewWithName()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceNew, Args = new ControlArgs { Name = "Work" } },
            Request("workspace", "new", "Work"));
    }

    [Fact]
    public void WorkspaceNewWithoutName()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceNew, Args = new ControlArgs() },
            Request("workspace", "new"));
    }

    [Fact]
    public void WorkspaceNewCollapsed()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceNew,
            Args = new ControlArgs { Name = "Work", Collapsed = true },
        };
        CanonicalJson.AssertEqual(expected, Request("workspace", "new", "Work", "--collapsed"));
    }

    [Fact]
    public void WorkspaceRename()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceRename, Target = "9f3c",
            Args = new ControlArgs { Name = "Renamed" },
        };
        CanonicalJson.AssertEqual(expected, Request("workspace", "rename", "Renamed", "--target", "9f3c"));
    }

    [Fact]
    public void WorkspaceDeleteDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceDelete, Target = "active" },
            Request("workspace", "delete"));
    }

    [Fact]
    public void WorkspaceSelect()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceSelect, Target = "ab" },
            Request("workspace", "select", "--target", "ab"));
    }

    [Fact]
    public void WorkspaceMove()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceMove, Target = "active",
            Args = new ControlArgs { To = "top" },
        };
        CanonicalJson.AssertEqual(expected, Request("workspace", "move", "--to", "top"));
    }

    [Fact]
    public void WorkspaceMoveRequiresToFails()
    {
        // --to has no default, so omitting it must fail to parse (the direction is validated server-side).
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "workspace", "move" }));
    }

    [Fact]
    public void WorkspaceFocusDefaultsToggle()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceFocus, Target = "active",
                Args = new ControlArgs { Mode = "toggle" },
            },
            Request("workspace", "focus"));
    }

    [Fact]
    public void WorkspaceFocusOnWithTarget()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceFocus, Target = "9f3c",
            Args = new ControlArgs { Mode = "on" },
        };
        CanonicalJson.AssertEqual(expected, Request("workspace", "focus", "on", "--target", "9f3c"));
    }

    [Fact]
    public void WorkspaceFocusRejectsBadMode()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "workspace", "focus", "sideways" }));
    }

    [Fact]
    public void WorkspaceCollapseDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceCollapse, Target = "active" },
            Request("workspace", "collapse"));
    }

    [Fact]
    public void WorkspaceExpandWithTarget()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceExpand, Target = "9f3c" },
            Request("workspace", "expand", "--target", "9f3c"));
    }

    [Fact]
    public void WorkspaceCollapseThreadsWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceCollapse, Target = "9f3c",
            Args = new ControlArgs { Window = "win" },
        };
        CanonicalJson.AssertEqual(expected, Request("workspace", "collapse", "--target", "9f3c", "--window", "win"));
    }

    [Fact]
    public void WorkspaceExpandThreadsWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceExpand, Target = "active",
            Args = new ControlArgs { Window = "win" },
        };
        CanonicalJson.AssertEqual(expected, Request("workspace", "expand", "--window", "win"));
    }

    [Fact]
    public void SessionNewWithCwdAndWorkspace()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { Cwd = "/tmp", Workspace = "ws1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "new", "--cwd", "/tmp", "--workspace", "ws1"));
    }

    [Fact]
    public void SessionNewWithWorkspaceName()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { WorkspaceName = "servers" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "new", "--workspace-name", "servers"));
    }

    [Fact]
    public void SessionNewWithCreateWorkspace()
    {
        // --create-workspace sets createWorkspace=true on the wire (omitted when the flag is absent).
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { WorkspaceName = "servers", CreateWorkspace = true },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "new", "--workspace-name", "servers", "--create-workspace"));
    }

    [Fact]
    public void SessionNewWithNoSelect()
    {
        // --no-select sets noSelect=true on the wire (omitted when the flag is absent).
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { NoSelect = true },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "new", "--no-select"));
    }

    [Fact]
    public void SessionNewWithCommandWait()
    {
        // --wait rides with --command to hold the session open after the command exits (omitted otherwise).
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { Command = "make test", Wait = true },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "new", "--command", "make test", "--wait"));
    }

    [Fact]
    public void SessionNewRejectsWaitWithoutCommand()
    {
        // --wait holds a command surface, so it is meaningless without --command — validate() rejects it.
        Assert.Equal("--wait requires --command", ValidationMessage("session", "new", "--wait"));
    }

    [Fact]
    public void SessionNewRejectsWorkspaceAndWorkspaceName()
    {
        // both addressing modes set — validate() rejects it before any request is built.
        Assert.Equal("use either --workspace or --workspace-name, not both",
            ValidationMessage("session", "new", "--workspace", "active", "--workspace-name", "servers"));
    }

    [Fact]
    public void SessionNewRequiresWorkspaceNameForCreateWorkspace()
    {
        // --create-workspace with no --workspace-name has nothing to create by id — validate() rejects it.
        Assert.Equal("--create-workspace requires --workspace-name",
            ValidationMessage("session", "new", "--create-workspace"));
    }

    [Fact]
    public void SessionNewAfter()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { After = "active" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "new", "--after", "active"));
    }

    [Fact]
    public void SessionNewBefore()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { Before = "s1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "new", "--before", "s1"));
    }

    [Fact]
    public void SessionNewRejectsAfterAndBefore()
    {
        Assert.Equal("use either --after or --before, not both",
            ValidationMessage("session", "new", "--after", "a", "--before", "b"));
    }

    [Fact]
    public void SessionNewRejectsAfterAndWorkspace()
    {
        // the anchor sid already names the workspace, so placement can't also address one.
        Assert.Equal("session.new takes --after/--before or a workspace, not both",
            ValidationMessage("session", "new", "--after", "a", "--workspace", "ws1"));
    }

    [Fact]
    public void SessionNewRejectsBeforeAndWorkspaceName()
    {
        Assert.Equal("session.new takes --after/--before or a workspace, not both",
            ValidationMessage("session", "new", "--before", "a", "--workspace-name", "servers"));
    }

    [Fact]
    public void SessionDuplicateTargetsExplicitSession()
    {
        var expected = new ControlRequest { Cmd = WireCommand.SessionDuplicate, Target = "9f3c" };
        CanonicalJson.AssertEqual(expected, Request("session", "duplicate", "--target", "9f3c"));
    }

    [Fact]
    public void SessionDuplicateDefaultsToActive()
    {
        // no options at all: the target defaults to `active`, which names its own workspace and cwd.
        var expected = new ControlRequest { Cmd = WireCommand.SessionDuplicate, Target = "active" };
        CanonicalJson.AssertEqual(expected, Request("session", "duplicate"));
    }

    [Fact]
    public void SessionClose()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionClose, Target = "x" },
            Request("session", "close", "--target", "x"));
    }

    [Fact]
    public void SessionCloseMultipleTargets()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionClose, Target = "a",
            Args = new ControlArgs { Targets = new[] { "a", "b" } },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "close", "--target", "a", "--target", "b"));
    }

    [Fact]
    public void SessionSelectDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionSelect, Target = "active" },
            Request("session", "select"));
    }

    [Fact]
    public void SessionSeenDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionSeen, Target = "active" },
            Request("session", "seen"));
    }

    [Fact]
    public void SessionSeenTargetAndWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSeen, Target = "x",
            Args = new ControlArgs { Window = "win" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "seen", "--target", "x", "--window", "win"));
    }

    [Fact]
    public void SessionRename()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionRename, Target = "active",
            Args = new ControlArgs { Name = "build" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "rename", "build"));
    }

    [Fact]
    public void SessionMove()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionMove, Target = "s1",
            Args = new ControlArgs { Workspace = "ws2" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "move", "ws2", "--target", "s1"));
    }

    [Fact]
    public void SessionMoveMultipleTargetsToWorkspace()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionMove, Target = "s1",
            Args = new ControlArgs { Targets = new[] { "s1", "s2" }, Workspace = "ws2" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "move", "ws2", "--target", "s1", "--target", "s2"));
    }

    [Fact]
    public void SessionMoveReorder()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionMove, Target = "active",
            Args = new ControlArgs { To = "up" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "move", "--to", "up"));
    }

    [Fact]
    public void SessionMoveRequiresWorkspaceOrTo()
    {
        // no placement intent at all — validate() rejects it with a usage message naming all three forms.
        Assert.Equal("provide a destination workspace, --to, or --after/--before",
            ValidationMessage("session", "move"));
    }

    [Fact]
    public void SessionMoveRejectsWorkspaceAndTo()
    {
        // both the workspace positional and --to are set — validate() rejects it with a usage message.
        Assert.Equal("provide a destination workspace or --to, not both",
            ValidationMessage("session", "move", "ws2", "--to", "up"));
    }

    [Fact]
    public void SessionMoveRejectsMultipleTargetsWithReorder()
    {
        Assert.Equal("session.move --target can be repeated only with a workspace or --after/--before",
            ValidationMessage("session", "move", "--to", "up", "--target", "s1", "--target", "s2"));
    }

    [Fact]
    public void SessionMoveAfter()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionMove, Target = "s1",
            Args = new ControlArgs { After = "s2" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "move", "--after", "s2", "--target", "s1"));
    }

    [Fact]
    public void SessionMoveAfterMultipleTargets()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionMove, Target = "s1",
            Args = new ControlArgs { Targets = new[] { "s1", "s2" }, After = "s3" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "move", "--after", "s3", "--target", "s1", "--target", "s2"));
    }

    [Fact]
    public void SessionMoveBefore()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionMove, Target = "s1",
            Args = new ControlArgs { Before = "s2" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "move", "--before", "s2", "--target", "s1"));
    }

    [Fact]
    public void SessionMoveRejectsAfterAndBefore()
    {
        Assert.Equal("use either --after or --before, not both",
            ValidationMessage("session", "move", "--after", "a", "--before", "b"));
    }

    [Fact]
    public void SessionMoveRejectsAfterAndTo()
    {
        Assert.Equal("session.move takes --after/--before or --to, not both",
            ValidationMessage("session", "move", "--after", "a", "--to", "up"));
    }

    [Fact]
    public void SessionMoveRejectsAfterAndWorkspace()
    {
        // the anchor already names the workspace, so it can't also take a destination workspace.
        Assert.Equal("session.move takes --after/--before or a workspace, not both",
            ValidationMessage("session", "move", "ws2", "--after", "a"));
    }

    [Fact]
    public void SessionTypeWithText()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionType, Target = "active",
            Args = new ControlArgs { Text = "ls\n", Select = false },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "type", "ls\n"));
    }

    [Fact]
    public void SessionTypeWithSelect()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionType, Target = "s1",
            Args = new ControlArgs { Text = "hi", Select = true },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "type", "hi", "--target", "s1", "--select"));
    }

    [Fact]
    public void SessionTypeStdinFlagParses()
    {
        // the --stdin flag parses (we don't call MakeRequest here — it would block reading stdin).
        var command = Assert.IsType<SessionCommands.TypeText>(
            Agtermctl.ParseAsRoot(new[] { "session", "type", "--stdin", "--target", "s1" }));
        Assert.True(command.Stdin);
        Assert.Null(command.Text);
        Assert.Equal("s1", command.Target.Target);
    }

    [Fact]
    public void SessionTypeWithPane()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionType, Target = "s1",
            Args = new ControlArgs { Text = "ls\n", Select = false, Pane = "right" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "type", "ls\n", "--pane", "right", "--target", "s1"));
    }

    [Fact]
    public void SessionTypeWithoutPaneOmitsIt()
    {
        var req = Request("session", "type", "ls\n");
        Assert.Null(req.Args?.Pane);
    }

    [Fact]
    public void SessionTypeWithPaneLeft()
    {
        // explicit --pane left is forwarded as "left"; the server treats nil and "left" identically (both
        // the main pane), but the CLI still passes the value through rather than dropping it.
        var req = Request("session", "type", "ls\n", "--pane", "left");
        Assert.Equal("left", req.Args?.Pane);
    }

    [Fact]
    public void SessionTypeWithPaneScratch()
    {
        var req = Request("session", "type", "ls\n", "--pane", "scratch");
        Assert.Equal("scratch", req.Args?.Pane);
    }

    [Fact]
    public void SessionTypeRejectsBadPane()
    {
        // `other` is a session.focus mode, not a typable pane — type accepts left|right|scratch only.
        Assert.Equal("--pane must be left, right, or scratch",
            ValidationMessage("session", "type", "x", "--pane", "other"));
    }

    [Fact]
    public void SessionSplitDefaultsToggle()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSplit, Target = "active",
            Args = new ControlArgs { Mode = "toggle" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "split"));
    }

    [Fact]
    public void SessionSplitOn()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSplit, Target = "active",
            Args = new ControlArgs { Mode = "on" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "split", "on"));
    }

    [Fact]
    public void SessionScratchDefaultsToggle()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionScratch, Target = "active",
            Args = new ControlArgs { Mode = "toggle" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "scratch"));
    }

    [Fact]
    public void SessionScratchOff()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionScratch, Target = "active",
            Args = new ControlArgs { Mode = "off" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "scratch", "off"));
    }

    [Fact]
    public void SessionFocusDefaultsOther()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionFocus, Target = "active",
            Args = new ControlArgs { Pane = "other" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "focus"));
    }

    [Fact]
    public void SessionFocusRight()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionFocus, Target = "active",
            Args = new ControlArgs { Pane = "right" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "focus", "right"));
    }

    [Fact]
    public void SessionResizeAbsolute()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionResize, Target = "active",
            Args = new ControlArgs { Ratio = 0.7 },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "resize", "--split-ratio", "0.7"));
    }

    [Fact]
    public void SessionResizeGrowLeftIsPositiveDelta()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionResize, Target = "active",
            Args = new ControlArgs { RatioDelta = 0.05 },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "resize", "--grow-left", "0.05"));
    }

    [Fact]
    public void SessionResizeGrowRightIsNegativeDelta()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionResize, Target = "active",
            Args = new ControlArgs { RatioDelta = -0.05 },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "resize", "--grow-right", "0.05"));
    }

    [Fact]
    public void SessionResizeRequiresExactlyOneForm()
    {
        // neither form set, and two forms set — validate() rejects both with the same usage message.
        var none = ValidationMessage("session", "resize");
        Assert.NotNull(none);
        Assert.Contains("exactly one", none);
        var both = ValidationMessage("session", "resize", "--split-ratio", "0.7", "--grow-left", "0.1");
        Assert.NotNull(both);
        Assert.Contains("exactly one", both);
    }

    [Fact]
    public void SessionResizeRejectsNonFinite()
    {
        // nan/inf parse as Double but can't JSON-encode; validate() rejects them with a clean usage error.
        // ADAPT: .NET's double parser accepts "NaN"/"Infinity" (case-insensitively) but not Swift's
        // bare "inf" spelling, so the non-finite token is "Infinity" here; "1e999" overflows to
        // Infinity on .NET Core 3.0+ just like Swift.
        var nan = ValidationMessage("session", "resize", "--split-ratio", "nan");
        Assert.NotNull(nan);
        Assert.Contains("finite", nan);
        var inf = ValidationMessage("session", "resize", "--grow-left", "Infinity");
        Assert.NotNull(inf);
        Assert.Contains("finite", inf);
        var overflow = ValidationMessage("session", "resize", "--split-ratio", "1e999");
        Assert.NotNull(overflow);
        Assert.Contains("finite", overflow);
    }

    [Fact]
    public void SessionGoNext()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionGo,
            Args = new ControlArgs { To = "next" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "go", "--to", "next"));
    }

    [Fact]
    public void SessionGoPrev()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionGo,
            Args = new ControlArgs { To = "prev" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "go", "--to", "prev"));
    }

    [Fact]
    public void SessionGoWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionGo,
            Args = new ControlArgs { Window = "w1", To = "last" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "go", "--to", "last", "--window", "w1"));
    }

    [Fact]
    public void SessionGoRequiresToFails()
    {
        // --to has no default, so omitting it must fail to parse (the direction is validated server-side).
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "session", "go" }));
    }

    [Fact]
    public void NotifyDefaultsActiveNoTitle()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.Notify, Target = "active",
            Args = new ControlArgs { Body = "hi" },
        };
        CanonicalJson.AssertEqual(expected, Request("notify", "hi"));
    }

    [Fact]
    public void NotifyWithTitleAndTarget()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.Notify, Target = "build",
            Args = new ControlArgs { Title = "Build", Body = "done" },
        };
        CanonicalJson.AssertEqual(expected, Request("notify", "done", "--title", "Build", "--target", "build"));
    }

    [Fact]
    public void SessionCopyDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionCopy, Target = "active" },
            Request("session", "copy"));
    }

    [Fact]
    public void SessionCopyWithTarget()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionCopy, Target = "9f3c" },
            Request("session", "copy", "--target", "9f3c"));
    }

    [Fact]
    public void SessionPasteDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionPaste, Target = "active" },
            Request("session", "paste"));
    }

    [Fact]
    public void SessionPasteWithTarget()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionPaste, Target = "9f3c" },
            Request("session", "paste", "--target", "9f3c"));
    }

    [Fact]
    public void SessionSelectAllDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionSelectAll, Target = "active" },
            Request("session", "select-all"));
    }

    [Fact]
    public void SessionSelectAllWithTarget()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionSelectAll, Target = "9f3c" },
            Request("session", "select-all", "--target", "9f3c"));
    }

    [Fact]
    public void SessionTextDefaultsActive()
    {
        // bare `session text` reads the visible screen of the focused pane (no args beyond the base bag).
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionText, Target = "active", Args = new ControlArgs() },
            Request("session", "text"));
    }

    [Fact]
    public void SessionTextWithAll()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionText, Target = "active",
            Args = new ControlArgs { All = true },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "text", "--all"));
    }

    [Fact]
    public void SessionTextWithLines()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionText, Target = "active",
            Args = new ControlArgs { Lines = 50 },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "text", "--lines", "50"));
    }

    [Fact]
    public void SessionTextWithPaneAndTarget()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionText, Target = "9f3c",
            Args = new ControlArgs { Pane = "right" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "text", "--pane", "right", "--target", "9f3c"));
    }

    [Fact]
    public void SessionTextWithPaneScratch()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionText, Target = "active",
            Args = new ControlArgs { Pane = "scratch" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "text", "--pane", "scratch"));
    }

    [Fact]
    public void SessionTextRejectsAllWithLines()
    {
        Assert.Equal("use either --all or --lines, not both",
            ValidationMessage("session", "text", "--all", "--lines", "10"));
    }

    [Fact]
    public void SessionTextRejectsZeroLines()
    {
        // a negative --lines (`-5`) is intercepted by ArgumentParser as a flag before validate() runs, so
        // 0 is the CLI-reachable non-positive case the validation guards against.
        Assert.Equal("--lines must be greater than 0",
            ValidationMessage("session", "text", "--lines", "0"));
    }

    [Fact]
    public void SessionTextRejectsBadPane()
    {
        Assert.Equal("--pane must be left, right, or scratch",
            ValidationMessage("session", "text", "--pane", "other"));
    }

    [Fact]
    public void SessionStatusWithBlink()
    {
        var req = Request("session", "status", "active", "--blink");
        Assert.Equal(WireCommand.SessionStatus, req.Cmd);
        Assert.Equal("active", req.Args?.Status);
        Assert.Equal(true, req.Args?.Blink);
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionStatus, Target = "active",
                Args = new ControlArgs { Status = "active", Blink = true },
            },
            req);
    }

    [Fact]
    public void SessionStatusWithoutBlink()
    {
        var req = Request("session", "status", "completed", "--target", "s1");
        Assert.Equal(WireCommand.SessionStatus, req.Cmd);
        Assert.Equal("completed", req.Args?.Status);
        Assert.Null(req.Args?.Blink);
        Assert.Null(req.Args?.AutoReset);
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionStatus, Target = "s1",
                Args = new ControlArgs { Status = "completed" },
            },
            req);
    }

    [Fact]
    public void SessionStatusWithAutoReset()
    {
        var req = Request("session", "status", "completed", "--auto-reset");
        Assert.Equal(WireCommand.SessionStatus, req.Cmd);
        Assert.Equal("completed", req.Args?.Status);
        Assert.Equal(true, req.Args?.AutoReset);
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionStatus, Target = "active",
                Args = new ControlArgs { Status = "completed", AutoReset = true },
            },
            req);
    }

    [Fact]
    public void SessionStatusWithSound()
    {
        var req = Request("session", "status", "blocked", "--sound", "Glass");
        Assert.Equal(WireCommand.SessionStatus, req.Cmd);
        Assert.Equal("blocked", req.Args?.Status);
        Assert.Equal("Glass", req.Args?.Sound);
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionStatus, Target = "active",
                Args = new ControlArgs { Status = "blocked", Sound = "Glass" },
            },
            req);
    }

    [Fact]
    public void SessionStatusWithoutSound()
    {
        var req = Request("session", "status", "active");
        Assert.Null(req.Args?.Sound);
    }

    [Fact]
    public void SessionStatusWithColor()
    {
        var req = Request("session", "status", "blocked", "--color", "#ff0000");
        Assert.Equal(WireCommand.SessionStatus, req.Cmd);
        Assert.Equal("blocked", req.Args?.Status);
        Assert.Equal("#ff0000", req.Args?.Color);
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionStatus, Target = "active",
                Args = new ControlArgs { Status = "blocked", Color = "#ff0000" },
            },
            req);
    }

    [Fact]
    public void SessionStatusWithoutColor()
    {
        var req = Request("session", "status", "active");
        Assert.Null(req.Args?.Color);
    }

    [Fact]
    public void SessionStatusRejectsBadColor()
    {
        // a non-#rrggbb color is rejected by validate() before any request is built; pin the exact message
        // so an unrelated parse failure can't masquerade as color validation (matches the sibling
        // `session background color` rejection tests).
        Assert.Equal("color must be a #rrggbb hex value",
            ValidationMessage("session", "status", "blocked", "--color", "nope"));
    }

    [Fact]
    public void SessionStatusWithShape()
    {
        var req = Request("session", "status", "blocked", "--shape", "triangle");
        Assert.Equal(WireCommand.SessionStatus, req.Cmd);
        Assert.Equal("blocked", req.Args?.Status);
        Assert.Equal("triangle", req.Args?.Shape);
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionStatus, Target = "active",
                Args = new ControlArgs { Status = "blocked", Shape = "triangle" },
            },
            req);
    }

    [Fact]
    public void SessionStatusAcceptsEveryShape()
    {
        // upstream is `@Test(arguments: StatusShape.allCases)`; the loop stands in for the parameterization.
        foreach (var shape in AllShapeWireNames)
        {
            Assert.Equal(shape, Request("session", "status", "active", "--shape", shape).Args?.Shape);
        }
    }

    [Fact]
    public void SessionStatusWithoutShape()
    {
        var req = Request("session", "status", "active");
        Assert.Null(req.Args?.Shape);
    }

    [Fact]
    public void SessionStatusRejectsUnknownShape()
    {
        // an unknown shape is rejected by validate() before any request is built, so it never reaches the
        // socket; pin the exact allCases-derived message so an unrelated parse failure can't pass for it.
        Assert.Equal("shape must be one of: circle, square, triangle, diamond, capsule, star",
            ValidationMessage("session", "status", "blocked", "--shape", "hexagon"));
    }

    [Fact]
    public void SessionStatusShapeHelpListsEveryShape()
    {
        // the --shape help is built from allCases like the validation message, so `--help` can't go stale
        // when the set changes; assert every raw value survives into the rendered help.
        // ADAPT: upstream calls ArgumentParser's `Session.Status.helpMessage(columns: 200)`;
        // System.CommandLine renders help through its invocation pipeline, so capture the console
        // output of `session status --help` instead.
        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            Agtermctl.Run(new[] { "session", "status", "--help" });
        }
        finally
        {
            Console.SetOut(original);
        }
        var help = writer.ToString();
        foreach (var shape in AllShapeWireNames)
        {
            Assert.True(help.Contains(shape), $"--shape help should list {shape}, got: {help}");
        }
    }

    [Fact]
    public void SessionStatusWithPane()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionStatus, Target = "s1",
            Args = new ControlArgs { Pane = "right", Status = "blocked" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "status", "blocked", "--pane", "right", "--target", "s1"));
    }

    [Fact]
    public void SessionStatusWithPaneScratch()
    {
        var req = Request("session", "status", "blocked", "--pane", "scratch");
        Assert.Equal("scratch", req.Args?.Pane);
    }

    [Fact]
    public void SessionStatusWithoutPaneOmitsIt()
    {
        var req = Request("session", "status", "blocked");
        Assert.Null(req.Args?.Pane);
    }

    [Fact]
    public void SessionStatusWithPaneID()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionStatus, Target = "s1",
            Args = new ControlArgs { Pane = "right", PaneId = "agent-tok", Status = "blocked" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "status", "blocked", "--pane", "right",
                "--pane-id", "agent-tok", "--target", "s1"));
    }

    [Fact]
    public void SessionStatusWithoutPaneIDOmitsIt()
    {
        var req = Request("session", "status", "blocked");
        Assert.Null(req.Args?.PaneId);
    }

    [Fact]
    public void SessionStatusRejectsBadPane()
    {
        // `other` is a session.focus mode, not a status pane — status accepts left|right|scratch only.
        Assert.Equal("--pane must be left, right, or scratch",
            ValidationMessage("session", "status", "blocked", "--pane", "other"));
    }

    [Fact]
    public void SessionRestorePinsCommand()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionRestore, Target = "s1",
            Args = new ControlArgs { Mode = "set", Command = "claude --resume abc" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "restore", "claude --resume abc", "--target", "s1"));
    }

    [Fact]
    public void SessionRestoreCarriesShellLineVerbatim()
    {
        // the pinned value is a shell line, so metacharacters must reach the wire untouched.
        var line = "cd /tmp && claude --resume abc | tee out";
        var req = Request("session", "restore", line);
        Assert.Equal(line, req.Args?.Command);
    }

    [Fact]
    public void SessionRestoreNone()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionRestore, Target = "active",
            Args = new ControlArgs { Mode = "none" },
        };
        var req = Request("session", "restore", "--none");
        CanonicalJson.AssertEqual(expected, req);
        Assert.Null(req.Args?.Command);
    }

    [Fact]
    public void SessionRestoreClear()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionRestore, Target = "active",
            Args = new ControlArgs { Mode = "clear" },
        };
        var req = Request("session", "restore", "--clear");
        CanonicalJson.AssertEqual(expected, req);
        Assert.Null(req.Args?.Command);
    }

    [Fact]
    public void SessionRestoreWithPaneAndPaneID()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionRestore, Target = "s1",
            Args = new ControlArgs
            {
                Mode = "set", Command = "claude -c",
                Pane = "right", PaneId = "pane-tok",
            },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "restore", "claude -c", "--pane", "right",
                "--pane-id", "pane-tok", "--target", "s1"));
    }

    [Fact]
    public void SessionRestoreWithoutPaneOmitsBoth()
    {
        var req = Request("session", "restore", "--clear");
        Assert.Null(req.Args?.Pane);
        Assert.Null(req.Args?.PaneId);
    }

    [Fact]
    public void SessionRestoreCarriesWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionRestore, Target = "active",
            Args = new ControlArgs { Mode = "none", Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "restore", "--none", "--window", "w1"));
    }

    [Fact]
    public void SessionRestoreRequiresOneForm()
    {
        Assert.Equal("provide exactly one of a COMMAND, --none, or --clear",
            ValidationMessage("session", "restore"));
    }

    [Fact]
    public void SessionRestoreRejectsCommandWithNone()
    {
        Assert.Equal("provide exactly one of a COMMAND, --none, or --clear",
            ValidationMessage("session", "restore", "claude", "--none"));
    }

    [Fact]
    public void SessionRestoreRejectsNoneWithClear()
    {
        Assert.Equal("provide exactly one of a COMMAND, --none, or --clear",
            ValidationMessage("session", "restore", "--none", "--clear"));
    }

    [Fact]
    public void SessionRestoreRejectsBadPane()
    {
        Assert.Equal("--pane must be left, right, or scratch",
            ValidationMessage("session", "restore", "--clear", "--pane", "other"));
    }

    [Fact]
    public void SessionSearchWithNeedle()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSearch, Target = "active",
            Args = new ControlArgs { Text = "error" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "search", "error"));
    }

    [Fact]
    public void SessionSearchOpensWithoutNeedleOrFlag()
    {
        // a bare `session search` opens the bar: no needle, no direction (an empty args bag, since the
        // command always passes a base ControlArgs to withWindow).
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSearch, Target = "active",
            Args = new ControlArgs(),
        };
        CanonicalJson.AssertEqual(expected, Request("session", "search"));
    }

    [Fact]
    public void SessionSearchNext()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSearch, Target = "active",
            Args = new ControlArgs { To = "next" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "search", "--next"));
    }

    [Fact]
    public void SessionSearchPrev()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSearch, Target = "active",
            Args = new ControlArgs { To = "prev" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "search", "--prev"));
    }

    [Fact]
    public void SessionSearchClose()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSearch, Target = "s1",
            Args = new ControlArgs { To = "close" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "search", "--close", "--target", "s1"));
    }

    [Fact]
    public void SessionSearchNeedleWithNext()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSearch, Target = "active",
            Args = new ControlArgs { Text = "foo", To = "next" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "search", "foo", "--next"));
    }

    [Fact]
    public void SessionSearchRejectsFlagCombos()
    {
        // the three navigation flags are mutually exclusive — validate() rejects combining any two.
        // upstream is `@Test(arguments: [...])`; the loop stands in for the parameterization.
        foreach (var flags in new[]
                 {
                     new[] { "--next", "--close" },
                     new[] { "--next", "--prev" },
                     new[] { "--prev", "--close" },
                 })
        {
            Assert.Equal("--next, --prev, and --close are mutually exclusive",
                ValidationMessage(new[] { "session", "search" }.Concat(flags).ToArray()));
        }
    }

    [Fact]
    public void SessionSearchRejectsNeedleWithClose()
    {
        // --close ignores the needle, so the combo is a usage error rather than a silent no-op.
        Assert.Equal("--close cannot be combined with a needle",
            ValidationMessage("session", "search", "foo", "--close"));
    }

    [Fact]
    public void SessionSearchWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSearch, Target = "active",
            Args = new ControlArgs { Text = "foo", Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "search", "foo", "--window", "w1"));
    }

    [Fact]
    public void SessionOverlayOpenWithCommandAndCwd()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "9f3c",
            Args = new ControlArgs { Cwd = "/b", Command = "revdiff" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "overlay", "open", "revdiff", "--cwd", "/b", "--target", "9f3c"));
    }

    [Fact]
    public void SessionOverlayOpenDefaultsActiveNoCwd()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "revdiff" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "overlay", "open", "revdiff"));
    }

    [Fact]
    public void SessionOverlayOpenWithWait()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "revdiff", Wait = true },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "overlay", "open", "revdiff", "--wait"));
    }

    [Fact]
    public void SessionOverlayOpenFloating()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "htop", SizePercent = 70 },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "overlay", "open", "htop", "--size-percent", "70"));
    }

    [Fact]
    public void SessionOverlayOpenWithBackgroundColor()
    {
        // --background-color maps to ControlArgs.color (shared field, disambiguated by the command).
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "revdiff", Color = "#2a1a3a" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "overlay", "open", "revdiff", "--background-color", "#2a1a3a"));
    }

    [Fact]
    public void SessionOverlayOpenRejectsBadBackgroundColor()
    {
        // validate() rejects a malformed hex at parse time (before any connection).
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "session", "overlay", "open", "cmd", "--background-color", "purple" }));
    }

    [Fact]
    public void SessionOverlayOpenWithFollow()
    {
        // --follow maps to ControlArgs.follow = true; the server selects the target after opening.
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "revdiff", Follow = true },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "overlay", "open", "revdiff", "--follow"));
    }

    [Fact]
    public void SessionOverlayOpenWithoutFollow()
    {
        // omitting --follow leaves follow nil (omitted-when-nil on the wire = the no-switch default).
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "revdiff" },
        };
        var built = Request("session", "overlay", "open", "revdiff");
        CanonicalJson.AssertEqual(expected, built);
        Assert.Null(built.Args?.Follow);
    }

    [Fact]
    public void SessionOverlayOpenFollowWithBlockAndSizePercentAndTarget()
    {
        // --follow composes with --block, --size-percent, and --target; follow rides through makeRequest.
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "9f3c",
            Args = new ControlArgs { Command = "htop", SizePercent = 60, Follow = true },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "overlay", "open", "htop", "--follow", "--block",
                "--size-percent", "60", "--target", "9f3c"));
    }

    [Fact]
    public void SessionOverlayClose()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionOverlayClose, Target = "active" },
            Request("session", "overlay", "close"));
    }

    [Fact]
    public void SessionOverlayOpenWithBlockParses()
    {
        // --block changes run() (open → poll result), not makeRequest, so the built request is the plain open.
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "revdiff" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "overlay", "open", "revdiff", "--block"));
    }

    [Fact]
    public void SessionOverlayOpenWithBlockAndSizePercentParses()
    {
        // --block composes with --size-percent; the block run() opens via makeRequest, so sizePercent rides through.
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayOpen, Target = "active",
            Args = new ControlArgs { Command = "htop", SizePercent = 60 },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "overlay", "open", "htop", "--block", "--size-percent", "60"));
    }

    [Fact]
    public void SessionOverlayResult()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionOverlayResult, Target = "9f3c" },
            Request("session", "overlay", "result", "--target", "9f3c"));
    }

    [Fact]
    public void SessionOverlayResizeWithSizePercent()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayResize, Target = "9f3c",
            Args = new ControlArgs { SizePercent = 40 },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "overlay", "resize", "--size-percent", "40", "--target", "9f3c"));
    }

    [Fact]
    public void SessionOverlayResizeFull()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionOverlayResize, Target = "active",
            Args = new ControlArgs { Full = true },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "overlay", "resize", "--full"));
    }

    [Fact]
    public void SessionOverlayResizeRejectsBadArgs()
    {
        // validate() enforces exactly-one-of --size-percent/--full and the 1...100 range at parse time.
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "session", "overlay", "resize" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "session", "overlay", "resize", "--full", "--size-percent", "50" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "session", "overlay", "resize", "--size-percent", "150" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "session", "overlay", "resize", "--size-percent", "0" }));
    }

    [Fact]
    public void SessionOverlayOpenRejectsBadSizePercent()
    {
        Assert.Equal("--size-percent must be between 1 and 100",
            ValidationMessage("session", "overlay", "open", "cmd", "--size-percent", "0"));
        Assert.Equal("--size-percent must be between 1 and 100",
            ValidationMessage("session", "overlay", "open", "cmd", "--size-percent", "150"));
        Assert.Null(ValidationMessage("session", "overlay", "open", "cmd", "--size-percent", "100"));
    }

    [Fact]
    public void SessionOverlayBlockRejectsWait()
    {
        // validate() enforces the mutually-exclusive flags at parse time (before any connection).
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "session", "overlay", "open", "cmd", "--block", "--wait" }));
    }

    [Fact]
    public void QuickDefaultsToggle()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Quick, Args = new ControlArgs { Mode = "toggle" } },
            Request("quick"));
    }

    [Fact]
    public void QuickShow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Quick, Args = new ControlArgs { Mode = "show" } },
            Request("quick", "show"));
    }

    [Fact]
    public void QuickTypeWithText()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.QuickType, Args = new ControlArgs { Text = "ls\n" } },
            Request("quick", "type", "ls\n"));
    }

    [Fact]
    public void QuickTypeStdinFlagParses()
    {
        // the --stdin flag parses (we don't call MakeRequest here — it would block reading stdin).
        var command = Assert.IsType<QuickCommands.TypeText>(
            Agtermctl.ParseAsRoot(new[] { "quick", "type", "--stdin" }));
        Assert.True(command.Stdin);
        Assert.Null(command.Text);
    }

    [Fact]
    public void QuickTypeWithoutTextOrStdinThrows()
    {
        // "provide TEXT or --stdin" is raised in makeRequest (not validate), so it's reached via Request(),
        // which calls MakeRequest, rather than ParseAsRoot alone.
        Assert.ThrowsAny<Exception>(() => Request("quick", "type"));
    }

    [Fact]
    public void QuickTextDefaultsToVisibleScreen()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.QuickText, Args = new ControlArgs() },
            Request("quick", "text"));
    }

    [Fact]
    public void QuickTextWithAll()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.QuickText, Args = new ControlArgs { All = true } },
            Request("quick", "text", "--all"));
    }

    [Fact]
    public void QuickTextWithLines()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.QuickText, Args = new ControlArgs { Lines = 50 } },
            Request("quick", "text", "--lines", "50"));
    }

    [Fact]
    public void QuickTextRejectsAllWithLines()
    {
        Assert.Equal("use either --all or --lines, not both",
            ValidationMessage("quick", "text", "--all", "--lines", "10"));
    }

    [Fact]
    public void QuickTextRejectsZeroLines()
    {
        Assert.Equal("--lines must be greater than 0",
            ValidationMessage("quick", "text", "--lines", "0"));
    }

    [Fact]
    public void SurfaceZoomDefaultsToggleActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SurfaceZoom, Target = "active",
                Args = new ControlArgs { Mode = "toggle" },
            },
            Request("surface", "zoom"));
    }

    [Fact]
    public void SurfaceZoomTargetsSurfaceID()
    {
        var id = "surface:5E5B1C5B-75C5-49E6-8806-2C61D8D6BBA9:right";

        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SurfaceZoom, Target = id,
                Args = new ControlArgs { Mode = "show" },
            },
            Request("surface", "zoom", "show", "--target", id));
    }

    [Fact]
    public void SurfaceZoomTargetsWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SurfaceZoom, Target = "active",
                Args = new ControlArgs { Mode = "hide", Window = "win" },
            },
            Request("surface", "zoom", "hide", "--window", "win"));
    }

    // MARK: - dashboard

    [Fact]
    public void DashboardOpenWithIdsAndFontSize()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.Dashboard,
            Args = new ControlArgs { Targets = new[] { "s1", "s2" }, FontSize = 12 },
        };
        CanonicalJson.AssertEqual(expected, Request("dashboard", "s1", "s2", "--font-size", "12"));
    }

    [Fact]
    public void DashboardOpenWithAutoSize()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.Dashboard,
            Args = new ControlArgs { Targets = new[] { "s1", "s2" }, AutoSize = true },
        };
        CanonicalJson.AssertEqual(expected, Request("dashboard", "s1", "s2", "--auto-size"));
    }

    [Fact]
    public void DashboardOpenTargetsWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.Dashboard,
            Args = new ControlArgs { Targets = new[] { "s1" }, Window = "win" },
        };
        CanonicalJson.AssertEqual(expected, Request("dashboard", "s1", "--window", "win"));
    }

    [Fact]
    public void DashboardClose()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Dashboard, Args = new ControlArgs { Close = true } },
            Request("dashboard", "--close"));
    }

    [Fact]
    public void DashboardRejectsFontSizeWithAutoSize()
    {
        Assert.Equal("--font-size is mutually exclusive with --auto-size",
            ValidationMessage("dashboard", "s1", "--font-size", "12", "--auto-size"));
    }

    [Fact]
    public void DashboardRejectsNonPositiveFontSize()
    {
        Assert.Equal("--font-size must be a positive number",
            ValidationMessage("dashboard", "s1", "--font-size", "0"));
    }

    [Fact]
    public void DashboardRejectsCloseWithIds()
    {
        Assert.Equal("--close takes no ids, --mru, or font options",
            ValidationMessage("dashboard", "s1", "--close"));
    }

    [Fact]
    public void DashboardRejectsCloseWithAutoSize()
    {
        Assert.Equal("--close takes no ids, --mru, or font options",
            ValidationMessage("dashboard", "--close", "--auto-size"));
    }

    [Fact]
    public void DashboardRejectsEmptyIdsWithoutClose()
    {
        Assert.Equal("dashboard requires at least one session id (or --mru, or --close)",
            ValidationMessage("dashboard"));
    }

    [Fact]
    public void DashboardMruAlone()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Dashboard, Args = new ControlArgs { Mru = true } },
            Request("dashboard", "--mru"));
    }

    [Fact]
    public void DashboardMruWithAutoSizeAndWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.Dashboard,
            Args = new ControlArgs { Window = "win", AutoSize = true, Mru = true },
        };
        CanonicalJson.AssertEqual(expected, Request("dashboard", "--mru", "--auto-size", "--window", "win"));
    }

    [Fact]
    public void DashboardRejectsMruWithIds()
    {
        Assert.Equal("--mru cannot be combined with session ids",
            ValidationMessage("dashboard", "s1", "--mru"));
    }

    [Fact]
    public void DashboardRejectsMruWithClose()
    {
        Assert.Equal("--close takes no ids, --mru, or font options",
            ValidationMessage("dashboard", "--mru", "--close"));
    }

    [Fact]
    public void DashboardRejectsNegativeFontSize()
    {
        Assert.Equal("--font-size must be a positive number",
            ValidationMessage("dashboard", "s1", "--font-size=-3"));
    }

    [Fact]
    public void DashboardRejectsInfiniteFontSize()
    {
        // ADAPT: upstream passes "--font-size=inf"; .NET's double parser accepts "Infinity" but not
        // the bare "inf" spelling, so the non-finite token is "Infinity" here.
        Assert.Equal("--font-size must be a positive number",
            ValidationMessage("dashboard", "s1", "--font-size=Infinity"));
    }

    [Fact]
    public void SidebarDefaultsToggle()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Sidebar, Args = new ControlArgs { Mode = "toggle" } },
            Request("sidebar"));
    }

    [Fact]
    public void SidebarHide()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Sidebar, Args = new ControlArgs { Mode = "hide" } },
            Request("sidebar", "hide"));
    }

    [Fact]
    public void SidebarModeDefaultsToggle()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SidebarMode, Args = new ControlArgs { Mode = "toggle" } },
            Request("sidebar", "mode"));
    }

    [Fact]
    public void SidebarModeFlagged()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SidebarMode, Args = new ControlArgs { Mode = "flagged" } },
            Request("sidebar", "mode", "flagged"));
    }

    [Fact]
    public void SidebarModeRejectsBadMode()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "sidebar", "mode", "sideways" }));
    }

    [Fact]
    public void SidebarExpand()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SidebarExpand },
            Request("sidebar", "expand"));
    }

    [Fact]
    public void SidebarCollapse()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SidebarCollapse },
            Request("sidebar", "collapse"));
    }

    [Fact]
    public void SidebarExpandWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SidebarExpand, Args = new ControlArgs { Window = "abc" } },
            Request("sidebar", "expand", "--window", "abc"));
    }

    [Fact]
    public void SidebarCollapseWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SidebarCollapse, Args = new ControlArgs { Window = "abc" } },
            Request("sidebar", "collapse", "--window", "abc"));
    }

    [Fact]
    public void SessionFlagDefaultsToggle()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionFlag, Target = "active",
                Args = new ControlArgs { Mode = "toggle" },
            },
            Request("session", "flag"));
    }

    [Fact]
    public void SessionFlagOnWithTarget()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionFlag, Target = "9f3c",
            Args = new ControlArgs { Mode = "on" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "flag", "on", "--target", "9f3c"));
    }

    [Fact]
    public void SessionFlagClear()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionFlag, Target = "active",
                Args = new ControlArgs { Mode = "clear" },
            },
            Request("session", "flag", "clear"));
    }

    [Fact]
    public void SessionFlagRejectsBadMode()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "session", "flag", "bogus" }));
    }

    [Fact]
    public void FontInc()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.FontInc, Target = "s1" },
            Request("font", "inc", "--target", "s1"));
    }

    [Fact]
    public void FontDec()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.FontDec, Target = "active" },
            Request("font", "dec"));
    }

    [Fact]
    public void FontReset()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.FontReset, Target = "active" },
            Request("font", "reset"));
    }

    [Fact]
    public void FontIncWithPane()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.FontInc, Target = "s1",
                Args = new ControlArgs { Pane = "right" },
            },
            Request("font", "inc", "--pane", "right", "--target", "s1"));
    }

    [Fact]
    public void FontDecWithPaneScratch()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.FontDec, Target = "active",
                Args = new ControlArgs { Pane = "scratch" },
            },
            Request("font", "dec", "--pane", "scratch"));
    }

    [Fact]
    public void FontResetWithPaneAndWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.FontReset, Target = "active",
                Args = new ControlArgs { Window = "w1", Pane = "left" },
            },
            Request("font", "reset", "--pane", "left", "--window", "w1"));
    }

    [Fact]
    public void FontRejectsInvalidPane()
    {
        Assert.Equal("--pane must be left, right, or scratch",
            ValidationMessage("font", "inc", "--pane", "other"));
    }

    [Fact]
    public void KeymapReload()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.KeymapReload },
            Request("keymap", "reload"));
    }

    [Fact]
    public void KeymapReloadRejectsWindowSelector()
    {
        // keymap.reload is app-global, so --window is meaningless and must not be accepted.
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "keymap", "reload", "--window", "w1" }));
    }

    [Fact]
    public void ConfigReload()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.ConfigReload },
            Request("config", "reload"));
    }

    [Fact]
    public void ConfigReloadRejectsWindowSelector()
    {
        // config.reload is app-global, so --window is meaningless and must not be accepted.
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "config", "reload", "--window", "w1" }));
    }

    // MARK: - theme subcommands

    [Fact]
    public void ThemeSetWithName()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.ThemeSet, Args = new ControlArgs { Name = "Dracula" } },
            Request("theme", "set", "Dracula"));
    }

    [Fact]
    public void ThemeSetWithoutNameSelectsDefault()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.ThemeSet, Args = new ControlArgs() },
            Request("theme", "set"));
    }

    [Fact]
    public void ThemeSetSyncWithBothSides()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.ThemeSet,
                Args = new ControlArgs { Light = "Builtin Light", Dark = "agterm" },
            },
            Request("theme", "set", "--light", "Builtin Light", "--dark", "agterm"));
    }

    [Fact]
    public void ThemeSetOneSlotAlone()
    {
        // theme.set is per-slot: either side alone is a valid request (the server keeps/seeds the other).
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.ThemeSet, Args = new ControlArgs { Light = "Builtin Light" } },
            Request("theme", "set", "--light", "Builtin Light"));
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.ThemeSet, Args = new ControlArgs { Dark = "Nord" } },
            Request("theme", "set", "--dark", "Nord"));
        // the reserved 'none' keyword (clear the dark slot) travels as a plain value.
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.ThemeSet, Args = new ControlArgs { Dark = "none" } },
            Request("theme", "set", "--dark", "none"));
    }

    [Fact]
    public void ThemeSetRejectsNamePlusLight()
    {
        // a positional NAME plus --light targets the same slot twice — validate() rejects it.
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "theme", "set", "Dracula", "--light", "Builtin Light" }));
    }

    [Fact]
    public void ThemeList()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.ThemeList },
            Request("theme", "list"));
    }

    [Fact]
    public void ThemeRejectsWindowSelector()
    {
        // theme is app-global (one settings model), so --window is meaningless and must not be accepted.
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "theme", "set", "Nord", "--window", "w1" }));
    }

    // MARK: - window subcommands

    [Fact]
    public void WindowNewWithName()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowNew, Args = new ControlArgs { Name = "Work" } },
            Request("window", "new", "Work"));
    }

    [Fact]
    public void WindowNewWithoutName()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowNew, Args = new ControlArgs() },
            Request("window", "new"));
    }

    [Fact]
    public void WindowList()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowList },
            Request("window", "list"));
    }

    [Fact]
    public void WindowSelect()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowSelect, Target = "9f3c" },
            Request("window", "select", "9f3c"));
    }

    [Fact]
    public void WindowSelectDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowSelect, Target = "active" },
            Request("window", "select"));
    }

    [Fact]
    public void WindowClose()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowClose, Target = "ab" },
            Request("window", "close", "ab"));
    }

    [Fact]
    public void WindowRename()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WindowRename, Target = "9f3c",
            Args = new ControlArgs { Name = "Renamed" },
        };
        CanonicalJson.AssertEqual(expected, Request("window", "rename", "9f3c", "Renamed"));
    }

    [Fact]
    public void WindowDelete()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowDelete, Target = "9f3c" },
            Request("window", "delete", "9f3c"));
    }

    [Fact]
    public void WindowResize()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WindowResize, Target = "9f3c",
            Args = new ControlArgs { Width = 1200, Height = 800 },
        };
        CanonicalJson.AssertEqual(expected,
            Request("window", "resize", "9f3c", "--width", "1200", "--height", "800"));
    }

    [Fact]
    public void WindowResizeDefaultsToActive()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WindowResize, Target = "active",
            Args = new ControlArgs { Width = 1000, Height = 700 },
        };
        CanonicalJson.AssertEqual(expected,
            Request("window", "resize", "--width", "1000", "--height", "700"));
    }

    [Fact]
    public void WindowMoveWithDisplay()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WindowMove, Target = "9f3c",
            Args = new ControlArgs { X = 100, Y = 50, Display = 1 },
        };
        CanonicalJson.AssertEqual(expected,
            Request("window", "move", "9f3c", "--x", "100", "--y", "50", "--display", "1"));
    }

    [Fact]
    public void WindowMoveDefaultsActiveAndCurrentDisplay()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WindowMove, Target = "active",
            Args = new ControlArgs { X = 100, Y = 50 },
        };
        CanonicalJson.AssertEqual(expected, Request("window", "move", "--x", "100", "--y", "50"));
    }

    [Fact]
    public void WindowZoom()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowZoom, Target = "9f3c" },
            Request("window", "zoom", "9f3c"));
    }

    [Fact]
    public void WindowFullscreen()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowFullscreen, Target = "9f3c" },
            Request("window", "fullscreen", "9f3c"));
    }

    [Fact]
    public void WindowFullscreenDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowFullscreen, Target = "active" },
            Request("window", "fullscreen"));
    }

    [Fact]
    public void WindowDeleteDefaultsActive()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowDelete, Target = "active" },
            Request("window", "delete"));
    }

    [Fact]
    public void WindowRenameRequiresBothArgsFails()
    {
        // rename needs an id AND a name; only one positional must fail to parse.
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "window", "rename", "9f3c" }));
    }

    [Fact]
    public void WindowCommandsRejectWindowSelector()
    {
        // window.* target via the positional id, so --window is meaningless and must not be accepted.
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "window", "list", "--window", "w1" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "window", "select", "9f3c", "--window", "w1" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "quick", "--window", "w1" }));
    }

    [Fact]
    public void WindowCommandsKeepSocketAndJSON()
    {
        // --socket / --json stay available on window.* (they share the connection/print surface).
        var command = Assert.IsType<WindowCommands.List>(
            Agtermctl.ParseAsRoot(new[] { "window", "list", "--socket", "/tmp/x.sock", "--json" }));
        Assert.True(command.Options.Json);
        Assert.Equal("/tmp/x.sock", command.Options.SocketPath(_ => null));
    }

    // MARK: - global --window selector

    [Fact]
    public void SessionNewWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionNew,
            Args = new ControlArgs { Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "new", "--window", "w1"));
    }

    [Fact]
    public void SessionSelectWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionSelect, Target = "active",
            Args = new ControlArgs { Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "select", "--window", "w1"));
    }

    [Fact]
    public void WorkspaceNewWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceNew,
            Args = new ControlArgs { Name = "Work", Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("workspace", "new", "Work", "--window", "w1"));
    }

    [Fact]
    public void TreeWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Tree, Args = new ControlArgs { Window = "w1" } },
            Request("tree", "--window", "w1"));
    }

    [Fact]
    public void TreeWithoutWindowOmitsArgs()
    {
        // no --window keeps tree in its compact form (args nil), matching the no-window request value.
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.Tree },
            Request("tree"));
    }

    // --window must populate args.window AND leave the command's own args intact (the merge folds it
    // into the existing bag rather than replacing it).

    [Fact]
    public void SessionTypeWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionType, Target = "active",
            Args = new ControlArgs { Text = "ls\n", Select = false, Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "type", "ls\n", "--window", "w1"));
    }

    [Fact]
    public void SessionMoveWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionMove, Target = "s1",
            Args = new ControlArgs { Workspace = "ws2", Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "move", "ws2", "--target", "s1", "--window", "w1"));
    }

    [Fact]
    public void SessionRenameWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionRename, Target = "active",
            Args = new ControlArgs { Name = "build", Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "rename", "build", "--window", "w1"));
    }

    [Fact]
    public void SessionRevealWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionReveal, Target = "s1",
            Args = new ControlArgs { Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "reveal", "--target", "s1", "--window", "w1"));
    }

    [Fact]
    public void SessionCloseWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionClose, Target = "x",
                Args = new ControlArgs { Window = "w1" },
            },
            Request("session", "close", "--target", "x", "--window", "w1"));
    }

    [Fact]
    public void WorkspaceRenameWithWindow()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.WorkspaceRename, Target = "9f3c",
            Args = new ControlArgs { Name = "Renamed", Window = "w1" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("workspace", "rename", "Renamed", "--target", "9f3c", "--window", "w1"));
    }

    [Fact]
    public void FontIncWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.FontInc, Target = "active",
                Args = new ControlArgs { Window = "w1" },
            },
            Request("font", "inc", "--window", "w1"));
    }

    [Fact]
    public void FontDecWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.FontDec, Target = "s1",
                Args = new ControlArgs { Window = "w1" },
            },
            Request("font", "dec", "--target", "s1", "--window", "w1"));
    }

    [Fact]
    public void FontResetWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.FontReset, Target = "active",
                Args = new ControlArgs { Window = "w1" },
            },
            Request("font", "reset", "--window", "w1"));
    }

    [Fact]
    public void InvalidSubcommandFailsToParse()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "bogus" }));
    }

    [Fact]
    public void SessionTypeWithoutTextOrStdinFails()
    {
        // parses fine (text is optional), but makeRequest validates it needs TEXT or --stdin.
        var command = Agtermctl.ParseAsRoot(new[] { "session", "type" });
        Assert.ThrowsAny<Exception>(() => command.MakeRequest());
    }

    // MARK: - socket-path precedence
    //
    // ADAPT (see PLATFORM-NOTES): upstream resolves $HOME → "$HOME/Library/Application Support/
    // agterm/agterm.sock" with a "/tmp/agterm/agterm.sock" no-HOME fallback; this port resolves
    // the home dir → "<HOME>\.agterm-win\agterm.sock" with a
    // "<temp>\.agterm-win\agterm.sock" fallback. --socket and AGTERM_STATE_DIR precedence
    // is identical.

    [Fact]
    public void SocketPathExplicitFlagWins()
    {
        var command = Agtermctl.ParseAsRoot(new[] { "tree", "--socket", "/tmp/explicit.sock" });
        var env = new Dictionary<string, string?>
        {
            ["AGTERM_STATE_DIR"] = "/tmp/state",
            ["USERPROFILE"] = @"C:\Users\x", // upstream: HOME=/Users/x
        };
        Assert.Equal("/tmp/explicit.sock", command.Options.SocketPath(key => env.GetValueOrDefault(key)));
    }

    [Fact]
    public void SocketPathStateDirOverHome()
    {
        var command = Agtermctl.ParseAsRoot(new[] { "tree" });
        var env = new Dictionary<string, string?>
        {
            ["AGTERM_STATE_DIR"] = "/tmp/state",
            ["USERPROFILE"] = @"C:\Users\x", // upstream: HOME=/Users/x
        };
        Assert.Equal(Path.Combine("/tmp/state", "agterm.sock"),
            command.Options.SocketPath(key => env.GetValueOrDefault(key)));
    }

    [Fact]
    public void SocketPathFallsBackToHome()
    {
        // upstream: HOME → "$HOME/Library/Application Support/agterm/agterm.sock"; here the
        // per-user root is the home directory and the app dir is .agterm-win.
        var command = Agtermctl.ParseAsRoot(new[] { "tree" });
        var home = @"C:\Users\x";
        var env = new Dictionary<string, string?> { ["USERPROFILE"] = home };
        Assert.Equal(Path.Combine(home, ".agterm-win", "agterm.sock"),
            command.Options.SocketPath(key => env.GetValueOrDefault(key)));
    }

    [Fact]
    public void SocketPathFallsBackToTmpWithoutHome()
    {
        // upstream: no HOME → "/tmp/agterm/agterm.sock"; here no home dir → the temp dir.
        var command = Agtermctl.ParseAsRoot(new[] { "tree" });
        Assert.Equal(Path.Combine(Path.GetTempPath(), ".agterm-win", "agterm.sock"),
            command.Options.SocketPath(_ => null));
    }

    // MARK: - session background

    [Fact]
    public void SessionBackgroundImage()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionBackground, Target = "active",
            Args = new ControlArgs { Mode = "image", Path = "/tmp/bg.png" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "background", "image", "/tmp/bg.png"));
    }

    [Fact]
    public void SessionBackgroundImageWithOptions()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionBackground, Target = "s1",
            Args = new ControlArgs
            {
                Mode = "image", Path = "/tmp/bg.png", Opacity = 0.2,
                Fit = "cover", Position = "top-left", Repeats = true,
            },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "background", "image", "/tmp/bg.png", "--opacity", "0.2",
                "--fit", "cover", "--position", "top-left", "--repeat", "--target", "s1"));
    }

    [Fact]
    public void SessionBackgroundText()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionBackground, Target = "active",
            Args = new ControlArgs { Text = "DRAFT", Mode = "text", Color = "#ff0000", Opacity = 0.15 },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "background", "text", "DRAFT", "--color", "#ff0000", "--opacity", "0.15"));
    }

    [Fact]
    public void SessionBackgroundColor()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionBackground, Target = "s1",
            Args = new ControlArgs { Mode = "color", Color = "#112233" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "background", "color", "#112233", "--target", "s1"));
    }

    [Fact]
    public void SessionBackgroundColorRejectsBadColor()
    {
        // assert the color validation (not some unrelated parse error) fired.
        var red = ValidationMessage("session", "background", "color", "red");
        Assert.NotNull(red);
        Assert.Contains("color", red);
        var shortHex = ValidationMessage("session", "background", "color", "#fff");
        Assert.NotNull(shortHex);
        Assert.Contains("color", shortHex);
    }

    [Fact]
    public void SessionBackgroundClear()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionBackground, Target = "active",
            Args = new ControlArgs { Mode = "clear" },
        };
        CanonicalJson.AssertEqual(expected, Request("session", "background", "clear"));
    }

    [Fact]
    public void SessionBackgroundRejectsBadFit()
    {
        Assert.NotNull(ValidationMessage("session", "background", "image", "/tmp/bg.png", "--fit", "fill"));
    }

    [Fact]
    public void SessionBackgroundRejectsBadPosition()
    {
        Assert.NotNull(ValidationMessage("session", "background", "text", "X", "--position", "middle"));
    }

    [Fact]
    public void SessionBackgroundRejectsOutOfRangeOpacity()
    {
        Assert.NotNull(ValidationMessage("session", "background", "image", "/tmp/bg.png", "--opacity", "1.5"));
        Assert.NotNull(ValidationMessage("session", "background", "text", "X", "--opacity", "-0.2"));
    }

    [Fact]
    public void SessionBackgroundRejectsBadColor()
    {
        Assert.NotNull(ValidationMessage("session", "background", "text", "X", "--color", "red"));
        Assert.NotNull(ValidationMessage("session", "background", "text", "X", "--color", "#fff"));
    }

    [Fact]
    public void SessionBackgroundAcceptsValidColor()
    {
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionBackground, Target = "active",
            Args = new ControlArgs { Text = "X", Mode = "text", Color = "#ff8800" },
        };
        CanonicalJson.AssertEqual(expected,
            Request("session", "background", "text", "X", "--color", "#ff8800"));
    }

    [Fact]
    public void SessionBackgroundRejectsEmptyAndTooLongText()
    {
        Assert.NotNull(ValidationMessage("session", "background", "text", ""));
        Assert.NotNull(ValidationMessage("session", "background", "text",
            new string('A', WatermarkConfig.MaxTextLength + 1)));
    }

    [Fact]
    public void SessionBackgroundImageRejectsControlCharPath()
    {
        // the config-injection vector: a newline in the path would smuggle an extra ghostty key into the
        // per-surface overlay. Caught CLI-side (matching the server boundary) before any round-trip.
        Assert.NotNull(ValidationMessage("session", "background", "image",
            "x.png\nclipboard-read = allow\ny.png"));
    }
}
