using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore ControlDispatcherTests.swift (names preserved).</summary>
public class ControlDispatcherTests
{
    private static ControlResponse Ok(ControlResult? result = null) => new() { Ok = true, Result = result };
    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static void ExpectResponse(ControlResponse expected, ControlResponse? actual)
        => Assert.Equal(CanonicalJson.Of(expected), CanonicalJson.Of(actual!));

    [Fact]
    public async Task TreeRoutesThroughActionsWithWindowArgument()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var tree = new ControlTree
        {
            Workspaces = new[]
            {
                new ControlWorkspaceNode
                {
                    Id = "workspace", Name = "Workspace", Active = true,
                    Sessions = Array.Empty<ControlSessionNode>(),
                },
            },
        };
        actions.NextTreeResponse = Ok(new ControlResult { Tree = tree });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Tree, Args = new ControlArgs { Window = "abc" },
        });

        ExpectResponse(Ok(new ControlResult { Tree = tree }), response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.Tree("abc") }, actions.Calls);
    }

    [Fact]
    public async Task SidebarVisibilityParsesModesAndKeepsExactResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSidebarVisibilityResponse = Ok();

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Sidebar, Args = new ControlArgs { Mode = "hide" },
        });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SidebarVisibility(ControlToggleMode.Off),
        }, actions.Calls);
    }

    [Fact]
    public async Task SidebarVisibilityDefaultsToToggle()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        _ = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.Sidebar });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SidebarVisibility(ControlToggleMode.Toggle),
        }, actions.Calls);
    }

    [Fact]
    public async Task SidebarVisibilityRejectsInvalidModeWithoutCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Sidebar, Args = new ControlArgs { Mode = "yes" },
        });

        ExpectResponse(Err("invalid sidebar mode: yes"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SidebarViewModeParsesModesAndKeepsExactResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSidebarViewModeResponse = Ok();

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SidebarMode, Args = new ControlArgs { Mode = "flagged" },
        });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SidebarViewMode(ControlSidebarViewMode.Flagged),
        }, actions.Calls);
    }

    [Fact]
    public async Task SidebarViewModeDefaultsToToggle()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        _ = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.SidebarMode });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SidebarViewMode(ControlSidebarViewMode.Toggle),
        }, actions.Calls);
    }

    [Fact]
    public async Task SidebarViewModeRejectsInvalidModeWithoutCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SidebarMode, Args = new ControlArgs { Mode = "wide" },
        });

        ExpectResponse(Err("invalid sidebar mode: wide"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SidebarExpandAndCollapseRouteWithWindowArgument()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextExpandResponse = Ok();
        actions.NextCollapseResponse = Err("window not open - window.select it first");

        var expand = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SidebarExpand, Args = new ControlArgs { Window = "win" },
        });
        var collapse = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SidebarCollapse, Args = new ControlArgs { Window = "win" },
        });

        ExpectResponse(Ok(), expand);
        ExpectResponse(Err("window not open - window.select it first"), collapse);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Expand("win"),
            new MockControlActions.Call.Collapse("win"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionNewRoutesValidatedOptions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionNewResponse = Ok(new ControlResult { Id = "new-session" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs
            {
                Name = "api", Cwd = "/tmp", WorkspaceName = "servers", CreateWorkspace = true,
                Command = "top", Window = "win",
            },
        });

        var options = new ControlSessionCreateOptions(Window: "win", Cwd: "/tmp", Workspace: null,
            WorkspaceName: "servers", CreateWorkspace: true, Command: "top", Name: "api");
        ExpectResponse(Ok(new ControlResult { Id = "new-session" }), response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.SessionNew(options) }, actions.Calls);
    }

    [Fact]
    public async Task SessionNewRoutesNoSelect()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionNewResponse = Ok(new ControlResult { Id = "bg-session" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { NoSelect = true },
        });

        var options = new ControlSessionCreateOptions(Window: null, Cwd: null, Workspace: null,
            WorkspaceName: null, CreateWorkspace: null, Command: null, Name: null, NoSelect: true);
        ExpectResponse(Ok(new ControlResult { Id = "bg-session" }), response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.SessionNew(options) }, actions.Calls);
    }

    [Fact]
    public async Task SessionNewRejectsAmbiguousWorkspaceArguments()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Workspace = "active", WorkspaceName = "servers" },
        });

        ExpectResponse(Err("use either --workspace or --workspace-name, not both"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionNewThreadsWaitWithCommand()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionNewResponse = Ok(new ControlResult { Id = "held" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Command = "make test", Wait = true },
        });

        var options = new ControlSessionCreateOptions(Window: null, Cwd: null, Workspace: null,
            WorkspaceName: null, CreateWorkspace: null, Command: "make test", Wait: true, Name: null);
        ExpectResponse(Ok(new ControlResult { Id = "held" }), response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.SessionNew(options) }, actions.Calls);
    }

    [Fact]
    public async Task SessionNewRejectsWaitWithoutCommand()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { Wait = true },
        });

        ExpectResponse(Err("--wait requires --command"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionNewRejectsCreateWorkspaceWithoutName()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { CreateWorkspace = true },
        });

        ExpectResponse(Err("--create-workspace requires --workspace-name"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionDuplicateRoutesTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionDuplicateResponse = Ok(new ControlResult { Id = "dupe" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionDuplicate, Target = "abc", Args = new ControlArgs { Window = "win" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "dupe" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionDuplicate("abc", "win"),
        }, actions.Calls);
    }

    /// <summary>session.duplicate takes no options at all — the target names its own workspace and cwd —
    /// so a bare request must still route (the host defaults the target to active).</summary>
    [Fact]
    public async Task SessionDuplicateRoutesWithoutArgs()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.SessionDuplicate });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionDuplicate(null, null),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionNewRoutesPlacementOptions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionNewResponse = Ok(new ControlResult { Id = "new-session" });

        var after = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { After = "active" },
        });
        var before = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { Before = "anchor" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "new-session" }), after);
        ExpectResponse(Ok(new ControlResult { Id = "new-session" }), before);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionNew(new ControlSessionCreateOptions(Window: null, Cwd: null,
                Workspace: null, WorkspaceName: null, CreateWorkspace: null, Command: null, Name: null,
                After: "active")),
            new MockControlActions.Call.SessionNew(new ControlSessionCreateOptions(Window: null, Cwd: null,
                Workspace: null, WorkspaceName: null, CreateWorkspace: null, Command: null, Name: null,
                Before: "anchor")),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionNewRejectsConflictingPlacementArguments()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var bothAnchors = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { After = "a", Before = "b" },
        });
        var anchorAndWorkspace = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { Workspace = "dest", After = "a" },
        });
        var anchorAndWorkspaceName = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionNew, Args = new ControlArgs { WorkspaceName = "servers", Before = "a" },
        });

        ExpectResponse(Err("use either --after or --before, not both"), bothAnchors);
        ExpectResponse(Err("session.new takes --after/--before or a workspace, not both"), anchorAndWorkspace);
        ExpectResponse(Err("session.new takes --after/--before or a workspace, not both"), anchorAndWorkspaceName);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionMoveRoutesPlacementForms()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var after = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "session",
            Args = new ControlArgs { Window = "win", After = "anchor" },
        });
        var before = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "session",
            Args = new ControlArgs { Before = "anchor" },
        });

        ExpectResponse(Ok(), after);
        ExpectResponse(Ok(), before);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionMove("session", "win",
                new ControlSessionMove.Place("anchor", After: true)),
            new MockControlActions.Call.SessionMove("session", null,
                new ControlSessionMove.Place("anchor", After: false)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionMoveRejectsConflictingPlacementForms()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var bothAnchors = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "active",
            Args = new ControlArgs { After = "a", Before = "b" },
        });
        var anchorAndTo = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "active",
            Args = new ControlArgs { To = "up", After = "a" },
        });
        var anchorAndWorkspace = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "active",
            Args = new ControlArgs { Workspace = "dest", Before = "a" },
        });

        ExpectResponse(Err("use either --after or --before, not both"), bothAnchors);
        ExpectResponse(Err("session.move takes --after/--before or --to, not both"), anchorAndTo);
        ExpectResponse(Err("session.move takes --after/--before or a workspace, not both"), anchorAndWorkspace);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionSelectGoCloseAndRenameRouteThroughActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var selected = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionSelect, Target = "session", Args = new ControlArgs { Window = "win" },
        });
        var navigated = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionGo, Args = new ControlArgs { Window = "win", To = "next-attention" },
        });
        var closed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionClose, Target = "session",
        });
        var renamed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRename, Target = "session", Args = new ControlArgs { Name = "api" },
        });
        var revealed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionReveal, Target = "session", Args = new ControlArgs { Window = "win" },
        });

        ExpectResponse(Ok(), selected);
        ExpectResponse(Ok(), navigated);
        ExpectResponse(Ok(), closed);
        ExpectResponse(Ok(), renamed);
        ExpectResponse(Ok(), revealed);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionSelect("session", "win"),
            new MockControlActions.Call.SessionGo("win", SessionNavigation.NextAttention),
            new MockControlActions.Call.SessionClose("session", null),
            new MockControlActions.Call.SessionRename("session", null, "api"),
            new MockControlActions.Call.SessionReveal("session", "win"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionCloseRoutesBatchTargets()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var closed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionClose,
            Args = new ControlArgs { Targets = new[] { "a", "b" }, Window = "win" },
        });
        var empty = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionClose, Args = new ControlArgs { Targets = Array.Empty<string>() },
        });

        ExpectResponse(Ok(), closed);
        ExpectResponse(Err("session.close requires at least one --target"), empty);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionCloseBatch(new[] { "a", "b" }, "win"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionGoAndRenameRejectInvalidInputsWithoutCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missingDirection = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.SessionGo });
        var badDirection = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionGo, Args = new ControlArgs { To = "sideways" },
        });
        var missingName = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRename, Target = "session",
        });

        ExpectResponse(Err("session.go requires --to next|prev|first|last|next-attention|prev-attention"),
            missingDirection);
        ExpectResponse(Err("session.go requires --to next|prev|first|last|next-attention|prev-attention"),
            badDirection);
        ExpectResponse(Err("session.rename requires a name"), missingName);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task WorkspaceCommandsRouteThroughActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var created = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceNew, Args = new ControlArgs { Name = "api", Window = "win" },
        });
        var selected = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceSelect, Target = "workspace", Args = new ControlArgs { Window = "win" },
        });
        var renamed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceRename, Target = "workspace", Args = new ControlArgs { Name = "  renamed  " },
        });
        var deleted = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceDelete, Target = "workspace",
        });

        ExpectResponse(Ok(), created);
        ExpectResponse(Ok(), selected);
        ExpectResponse(Ok(), renamed);
        ExpectResponse(Ok(), deleted);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WorkspaceNew("win", "api", Collapsed: false),
            new MockControlActions.Call.WorkspaceSelect("workspace", "win"),
            new MockControlActions.Call.WorkspaceRename("workspace", null, "renamed"),
            new MockControlActions.Call.WorkspaceDelete("workspace", null),
        }, actions.Calls);
    }

    [Fact]
    public async Task WorkspaceRenameRejectsMissingOrBlankNameWithoutCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missing = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceRename, Target = "workspace",
        });
        var blank = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceRename, Target = "workspace", Args = new ControlArgs { Name = "   " },
        });

        ExpectResponse(Err("workspace.rename requires a name"), missing);
        ExpectResponse(Err("workspace.rename requires a name"), blank);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionMoveRoutesReorderAndWorkspaceForms()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var reorder = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "session",
            Args = new ControlArgs { Window = "win", To = "top" },
        });
        var workspace = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "session",
            Args = new ControlArgs { Workspace = "dest" },
        });

        ExpectResponse(Ok(), reorder);
        ExpectResponse(Ok(), workspace);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionMove("session", "win",
                new ControlSessionMove.Reorder(ReorderDirection.Top)),
            new MockControlActions.Call.SessionMove("session", null,
                new ControlSessionMove.Workspace("dest")),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionMoveRoutesBatchWorkspaceAndPlacementForms()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var workspace = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove,
            Args = new ControlArgs { Targets = new[] { "a", "b" }, Workspace = "dest", Window = "win" },
        });
        var after = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove,
            Args = new ControlArgs { Targets = new[] { "a", "b" }, After = "anchor" },
        });
        var reorder = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove,
            Args = new ControlArgs { Targets = new[] { "a", "b" }, To = "up" },
        });

        ExpectResponse(Ok(), workspace);
        ExpectResponse(Ok(), after);
        ExpectResponse(Err("session.move --target can be repeated only with a workspace or --after/--before"),
            reorder);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionMoveBatch(new[] { "a", "b" }, "win",
                new ControlSessionMove.Workspace("dest")),
            new MockControlActions.Call.SessionMoveBatch(new[] { "a", "b" }, null,
                new ControlSessionMove.Place("anchor", After: true)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionMoveNormalizesOneArrayTargetToSingularAction()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var workspace = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove,
            Args = new ControlArgs { Targets = new[] { "a" }, Workspace = "dest", Window = "win" },
        });
        var after = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove,
            Args = new ControlArgs { Targets = new[] { "b" }, After = "anchor" },
        });

        ExpectResponse(Ok(), workspace);
        ExpectResponse(Ok(), after);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionMove("a", "win", new ControlSessionMove.Workspace("dest")),
            new MockControlActions.Call.SessionMove("b", null, new ControlSessionMove.Place("anchor", After: true)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionMoveRejectsInvalidForms()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missing = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "active",
        });
        var both = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "active",
            Args = new ControlArgs { Workspace = "active", To = "up" },
        });
        var badDirection = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionMove, Target = "active", Args = new ControlArgs { To = "sideways" },
        });

        ExpectResponse(Err("session.move requires --to or a workspace"), missing);
        ExpectResponse(Err("session.move takes either --to or a workspace, not both"), both);
        ExpectResponse(Err("session.move --to must be up|down|top|bottom"), badDirection);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task WorkspaceMoveRoutesDirectionAndRejectsInvalidForms()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var moved = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceMove, Target = "workspace",
            Args = new ControlArgs { Window = "win", To = "bottom" },
        });
        var missing = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceMove, Target = "workspace",
        });
        var bad = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceMove, Target = "workspace", Args = new ControlArgs { To = "sideways" },
        });

        ExpectResponse(Ok(), moved);
        ExpectResponse(Err("workspace.move requires --to"), missing);
        ExpectResponse(Err("workspace.move --to must be up|down|top|bottom"), bad);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WorkspaceMove("workspace", "win", ReorderDirection.Bottom),
        }, actions.Calls);
    }

    [Fact]
    public async Task WorkspaceFocusRoutesModeForHostSideValidation()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var focused = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceFocus, Target = "workspace",
            Args = new ControlArgs { Mode = "on", Window = "win" },
        });

        ExpectResponse(Ok(), focused);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WorkspaceFocus("workspace", "win", "on"),
        }, actions.Calls);
    }

    [Fact]
    public async Task WorkspaceCollapseAndExpandRouteExpandedFlag()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var collapsed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceCollapse, Target = "workspace", Args = new ControlArgs { Window = "win" },
        });
        var expanded = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceExpand, Target = "active",
        });

        ExpectResponse(Ok(), collapsed);
        ExpectResponse(Ok(), expanded);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WorkspaceExpansion("workspace", "win", Expanded: false),
            new MockControlActions.Call.WorkspaceExpansion("active", null, Expanded: true),
        }, actions.Calls);
    }

    [Fact]
    public async Task WorkspaceNewRoutesCollapsedFlag()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var created = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceNew,
            Args = new ControlArgs { Name = "api", Collapsed = true, Window = "win" },
        });

        ExpectResponse(Ok(), created);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WorkspaceNew("win", "api", Collapsed: true),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionFlagRoutesModeForHostSideValidation()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var flagged = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionFlag, Target = "session",
            Args = new ControlArgs { Mode = "on", Window = "win" },
        });
        var cleared = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionFlag, Args = new ControlArgs { Mode = "clear" },
        });

        ExpectResponse(Ok(), flagged);
        ExpectResponse(Ok(), cleared);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionFlag("session", "win", "on"),
            new MockControlActions.Call.SessionFlag(null, null, "clear"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionSeenRoutesTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var seen = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionSeen, Target = "session", Args = new ControlArgs { Window = "win" },
        });
        var active = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.SessionSeen });

        ExpectResponse(Ok(), seen);
        ExpectResponse(Ok(), active);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.MarkSessionSeen("session", "win"),
            new MockControlActions.Call.MarkSessionSeen(null, null),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionStatusRoutesParsedStatusAndRejectsInvalidStatus()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var status = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs
            {
                Window = "win", Status = "blocked", Blink = true,
                AutoReset = true, Sound = "default", Color = "#ff0000",
            },
        });
        var bad = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session", Args = new ControlArgs { Status = "bogus" },
        });
        var badColor = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Status = "blocked", Color = "nope" },
        });

        ExpectResponse(Ok(), status);
        ExpectResponse(Err("invalid status"), bad);
        ExpectResponse(Err("invalid color (expected #rrggbb)"), badColor);
        // the bad-color request errors before reaching the actions, so only the good one is recorded.
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionStatus("session", "win",
                new ControlSessionStatusUpdate(AgentStatus.Blocked, Blink: true,
                    AutoReset: true, Sound: "default", Color: "#ff0000", Pane: null)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionStatusRevertsColorWhenOmitted()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // set a per-call color, then set again with NO color: the second update must carry color nil,
        // proving the "next call without --color discards it" contract at the dispatch/update layer.
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Status = "blocked", Color = "#ff0000" },
        });
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session", Args = new ControlArgs { Status = "blocked" },
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionStatus("session", null,
                new ControlSessionStatusUpdate(AgentStatus.Blocked, Blink: null, AutoReset: null,
                    Sound: null, Color: "#ff0000")),
            new MockControlActions.Call.SessionStatus("session", null,
                new ControlSessionStatusUpdate(AgentStatus.Blocked, Blink: null, AutoReset: null,
                    Sound: null, Color: null)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionStatusCarriesValidPaneAndRejectsInvalidPane()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // the opaque --pane-id rides through untouched alongside a valid role --pane (the app-side arm,
        // not the dispatcher, resolves the token against the session's live surfaces).
        var tagged = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Pane = "right", PaneId = "agent-tok", Status = "blocked" },
        });
        var badPane = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Pane = "middle", Status = "blocked" },
        });

        ExpectResponse(Ok(), tagged);
        ExpectResponse(Err("--pane must be left, right, or scratch"), badPane);
        // the invalid pane never reaches actions (status unchanged), only the valid one is recorded.
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionStatus("session", null,
                new ControlSessionStatusUpdate(AgentStatus.Blocked, Blink: null, AutoReset: null,
                    Sound: null, Pane: StatusPane.Right, PaneId: "agent-tok")),
        }, actions.Calls);
    }

    [Theory]
    [InlineData("circle", StatusShape.Circle)]
    [InlineData("square", StatusShape.Square)]
    [InlineData("triangle", StatusShape.Triangle)]
    [InlineData("diamond", StatusShape.Diamond)]
    [InlineData("capsule", StatusShape.Capsule)]
    [InlineData("star", StatusShape.Star)]
    public async Task SessionStatusCarriesEveryValidShape(string rawValue, StatusShape shape)
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Status = "blocked", Shape = rawValue },
        });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionStatus("session", null,
                new ControlSessionStatusUpdate(AgentStatus.Blocked, Blink: null, AutoReset: null,
                    Sound: null, Shape: shape)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionStatusForwardsShapeOnlyWhenTheArgIsPresent()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // the dispatcher carries the arg through verbatim: present → the parsed shape, absent → nil.
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Status = "blocked", Shape = "triangle" },
        });
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session", Args = new ControlArgs { Status = "blocked" },
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionStatus("session", null,
                new ControlSessionStatusUpdate(AgentStatus.Blocked, Blink: null, AutoReset: null,
                    Sound: null, Shape: StatusShape.Triangle)),
            new MockControlActions.Call.SessionStatus("session", null,
                new ControlSessionStatusUpdate(AgentStatus.Blocked, Blink: null, AutoReset: null,
                    Sound: null, Shape: null)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionStatusRejectsInvalidShapeWithoutMutating()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Status = "blocked", Shape = "hexagon" },
        });

        // the accepted set in the message is derived from the valid-name list, so it tracks the enum.
        var accepted = StatusShapes.ValidNamesList;
        ExpectResponse(Err($"invalid shape: hexagon ({accepted})"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionStatusAcceptsShapeOnIdle()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // idle renders no glyph, so a shape is accepted and simply carries nothing to draw — same as --color.
        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Status = "idle", Shape = "star" },
        });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionStatus("session", null,
                new ControlSessionStatusUpdate(AgentStatus.Idle, Blink: null, AutoReset: null,
                    Sound: null, Shape: StatusShape.Star)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionStatusColorErrorWinsOverInvalidPane()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // both --color and --pane are invalid; color is validated first, so the color error wins.
        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Pane = "middle", Status = "blocked", Color = "nope" },
        });

        ExpectResponse(Err("invalid color (expected #rrggbb)"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionStatusValidatesColorThenShapeThenPane()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var accepted = StatusShapes.ValidNamesList;

        // the shape check was inserted BETWEEN the existing color and pane checks; pin both boundaries so
        // reordering the three guards can't change which error a caller sees without failing here.
        var colorOverShape = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Status = "blocked", Color = "nope", Shape = "hexagon" },
        });
        var shapeOverPane = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "session",
            Args = new ControlArgs { Pane = "middle", Status = "blocked", Shape = "hexagon" },
        });

        ExpectResponse(Err("invalid color (expected #rrggbb)"), colorOverShape);
        ExpectResponse(Err($"invalid shape: hexagon ({accepted})"), shapeOverPane);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreRoutesEachModeToTheHost()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionRestoreResponse = Ok(new ControlResult { Id = "session" });

        var set = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = "claude --resume abc", Window = "win" },
        });
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session", Args = new ControlArgs { Mode = "none" },
        });
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session", Args = new ControlArgs { Mode = "clear" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), set);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionRestore("session", "win",
                new ControlSessionRestoreUpdate(new ControlRestoreOverride.Pin("claude --resume abc"))),
            new MockControlActions.Call.SessionRestore("session", null,
                new ControlSessionRestoreUpdate(new ControlRestoreOverride.PinNone())),
            new MockControlActions.Call.SessionRestore("session", null,
                new ControlSessionRestoreUpdate(new ControlRestoreOverride.Unpin())),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreCarriesPaneAndPaneIDThrough()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // the opaque --pane-id rides through untouched (the app-side arm resolves it against the live
        // surfaces); the role --pane is parsed to StatusPane here.
        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = "htop", Pane = "right", PaneId = "pane-tok" },
        });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionRestore("session", null,
                new ControlSessionRestoreUpdate(new ControlRestoreOverride.Pin("htop"),
                    Pane: StatusPane.Right, PaneId: "pane-tok")),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreKeepsShellMetacharactersVerbatim()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // a pinned value is a SHELL LINE: operators, quotes, and variables are the point and must reach
        // the host unmodified (never re-quoted).
        var line = "cd \"$HOME/dev\" && claude --resume abc | tee /tmp/x; echo done";
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = line },
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionRestore("session", null,
                new ControlSessionRestoreUpdate(new ControlRestoreOverride.Pin(line))),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreRejectsBadModeAndMissingCommand()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var unknown = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session", Args = new ControlArgs { Mode = "pin" },
        });
        var missingMode = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
        });
        var noCommand = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session", Args = new ControlArgs { Mode = "set" },
        });

        ExpectResponse(Err("invalid restore mode: pin (set|none|clear)"), unknown);
        ExpectResponse(Err("invalid restore mode:  (set|none|clear)"), missingMode);
        ExpectResponse(Err("session.restore set requires a command"), noCommand);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreSetWithAnEmptyCommandPinsNothing()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        // an empty command is a present-but-empty pin, which IS the tri-state's "pinned to nothing" value
        // — the same state `none` writes. It reaches the host as Pin("") rather than being rejected.
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = "" },
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionRestore("session", null,
                new ControlSessionRestoreUpdate(new ControlRestoreOverride.Pin(""))),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreRejectsControlCharactersAndBadPane()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var newline = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = "echo one\necho two" },
        });
        var tab = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = "echo\tone" },
        });
        var badPane = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = "htop", Pane = "middle" },
        });

        // the rejection covers every control scalar, tab included.
        ExpectResponse(Err("command must not contain control characters"), newline);
        ExpectResponse(Err("command must not contain control characters"), tab);
        ExpectResponse(Err("--pane must be left, right, or scratch"), badPane);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreRejectsInvalidPaneOnNonSetModes()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var none = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "none", Pane = "middle" },
        });
        var clear = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "clear", Pane = "middle" },
        });

        ExpectResponse(Err("--pane must be left, right, or scratch"), none);
        ExpectResponse(Err("--pane must be left, right, or scratch"), clear);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionRestoreCapsCommandAtByteLimitNotGraphemeCount()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var cap = ControlRestoreOverride.MaxCommandBytes;

        var exact = new string('a', cap);
        var over = new string('a', cap + 1);
        // 400 four-byte scalars = 1600 UTF-8 bytes but only 400 characters: well under the cap by
        // grapheme count, over it by BYTES — the cap is a storage bound, so this must be rejected.
        var multiByte = string.Concat(Enumerable.Repeat("🌍", 400));

        var exactResponse = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = exact },
        });
        var overResponse = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = over },
        });
        var multiByteResponse = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionRestore, Target = "session",
            Args = new ControlArgs { Mode = "set", Command = multiByte },
        });

        Assert.True(new System.Globalization.StringInfo(multiByte).LengthInTextElements < cap);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(multiByte) > cap);
        ExpectResponse(Ok(), exactResponse);
        ExpectResponse(Err($"command too long (max {cap} bytes)"), overResponse);
        ExpectResponse(Err($"command too long (max {cap} bytes)"), multiByteResponse);
        // only the exactly-at-cap command reached the host.
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionRestore("session", null,
                new ControlSessionRestoreUpdate(new ControlRestoreOverride.Pin(exact))),
        }, actions.Calls);
    }

    [Fact]
    public async Task SplitScratchFocusAndResizeRouteParsedInputs()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var split = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionSplit, Target = "session",
            Args = new ControlArgs { Mode = "off", Window = "win" },
        });
        var scratch = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionScratch, Target = "session",
            Args = new ControlArgs { Mode = "on", Command = "htop" },
        });
        var focus = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionFocus, Target = "session", Args = new ControlArgs { Pane = "right" },
        });
        var resize = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionResize, Target = "session",
            Args = new ControlArgs { Window = "win", RatioDelta = -0.1 },
        });

        ExpectResponse(Ok(), split);
        ExpectResponse(Ok(), scratch);
        ExpectResponse(Ok(), focus);
        ExpectResponse(Ok(), resize);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionSplit("session", "win", "off"),
            new MockControlActions.Call.SessionScratch("session", null, "on", "htop"),
            new MockControlActions.Call.SessionFocus("session", null, "right"),
            new MockControlActions.Call.SessionResize("session", "win", new ControlSplitResize.Delta(-0.1)),
        }, actions.Calls);
    }

    [Fact]
    public async Task ResizeRejectsInvalidInputs()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missingResize = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionResize, Args = new ControlArgs(),
        });
        var bothResize = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionResize, Args = new ControlArgs { Ratio = 0.7, RatioDelta = 0.1 },
        });

        ExpectResponse(Err("session.resize requires --split-ratio, --grow-left, or --grow-right"),
            missingResize);
        ExpectResponse(Err("session.resize: --split-ratio is mutually exclusive with --grow-left/--grow-right"),
            bothResize);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task FontCommandsRouteActionsWithTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextFontResponse = Ok(new ControlResult { Id = "session" });

        var inc = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.FontInc, Target = "session", Args = new ControlArgs { Window = "win" },
        });
        var dec = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.FontDec, Target = "session" });
        var reset = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.FontReset, Target = "session" });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), inc);
        ExpectResponse(Ok(new ControlResult { Id = "session" }), dec);
        ExpectResponse(Ok(new ControlResult { Id = "session" }), reset);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Font("session", "win", null, "increase_font_size:1"),
            new MockControlActions.Call.Font("session", null, null, "decrease_font_size:1"),
            new MockControlActions.Call.Font("session", null, null, "reset_font_size"),
        }, actions.Calls);
    }

    [Fact]
    public async Task FontCommandsThreadPane()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextFontResponse = Ok(new ControlResult { Id = "session" });

        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.FontDec, Target = "session", Args = new ControlArgs { Pane = "right" },
        });
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.FontInc, Target = "session",
            Args = new ControlArgs { Window = "win", Pane = "scratch" },
        });
        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.FontReset, Target = "session", Args = new ControlArgs { Pane = "left" },
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Font("session", null, "right", "decrease_font_size:1"),
            new MockControlActions.Call.Font("session", "win", "scratch", "increase_font_size:1"),
            new MockControlActions.Call.Font("session", null, "left", "reset_font_size"),
        }, actions.Calls);
    }

    [Fact]
    public async Task KeymapAndConfigReloadWrapDiagnosticCounts()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextKeymapResponse = Ok(new ControlResult { Count = 2 });
        actions.NextConfigResponse = Ok(new ControlResult { Count = 3 });

        var keymap = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.KeymapReload });
        var config = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.ConfigReload });

        ExpectResponse(Ok(new ControlResult { Count = 2 }), keymap);
        ExpectResponse(Ok(new ControlResult { Count = 3 }), config);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.KeymapReload(),
            new MockControlActions.Call.ConfigReload(),
        }, actions.Calls);
    }

    [Fact]
    public async Task NotifyRequiresBodyBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missing = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.Notify, Target = "session" });
        var empty = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Notify, Target = "session", Args = new ControlArgs { Body = "" },
        });

        ExpectResponse(Err("notify requires a body"), missing);
        ExpectResponse(Err("notify requires a body"), empty);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task NotifyRoutesBodyTitleTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextNotifyResponse = Ok(new ControlResult { Id = "session" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Notify, Target = "session",
            Args = new ControlArgs { Window = "win", Title = "Build", Body = "done" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Notify("session", "win", "Build", "done"),
        }, actions.Calls);
    }

    [Fact]
    public async Task ThemeSetRoutesAndEchoesActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextThemeSetResponse = Ok(new ControlResult { Theme = "Dracula" });

        var set = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.ThemeSet, Args = new ControlArgs { Name = "Dracula" },
        });

        ExpectResponse(Ok(new ControlResult { Theme = "Dracula" }), set);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.ThemeSet("Dracula"),
        }, actions.Calls);
    }

    [Fact]
    public async Task ThemeSetKeepsExactActionErrorResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextThemeSetResponse = Err("unknown theme: NotARealTheme");

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.ThemeSet, Args = new ControlArgs { Name = "NotARealTheme" },
        });

        ExpectResponse(Err("unknown theme: NotARealTheme"), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.ThemeSet("NotARealTheme"),
        }, actions.Calls);
    }

    [Fact]
    public async Task ThemeListReturnsCurrentThemeAndAvailableThemes()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextThemeListResponse = Ok(new ControlResult
        {
            Theme = "Dracula", Themes = new[] { "Dracula", "Nord" },
        });

        var response = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.ThemeList });

        ExpectResponse(Ok(new ControlResult { Theme = "Dracula", Themes = new[] { "Dracula", "Nord" } }),
            response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.ThemeList(),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionTypeRequiresTextBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionType, Target = "session",
        });

        ExpectResponse(Err("session.type requires text"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionTypeRejectsNulBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionType, Target = "session",
            Args = new ControlArgs { Text = "safe\0danger\n" },
        });

        ExpectResponse(Err("text must not contain a NUL byte"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task PaneTakingCommandsRejectAnUnknownPaneWithoutCallingTheAction()
    {
        var requests = new[]
        {
            new ControlRequest { Cmd = Command.SessionType, Target = "s", Args = new ControlArgs { Text = "x", Pane = "middle" } },
            new ControlRequest { Cmd = Command.SessionText, Target = "s", Args = new ControlArgs { Pane = "middle" } },
            new ControlRequest { Cmd = Command.FontInc, Target = "s", Args = new ControlArgs { Pane = "middle" } },
            new ControlRequest { Cmd = Command.FontDec, Target = "s", Args = new ControlArgs { Pane = "middle" } },
            new ControlRequest { Cmd = Command.FontReset, Target = "s", Args = new ControlArgs { Pane = "middle" } },
        };
        foreach (var request in requests)
        {
            var actions = new MockControlActions();
            var dispatcher = new ControlDispatcher(actions);

            var response = await dispatcher.Dispatch(request);

            ExpectResponse(Err("invalid pane: middle"), response);
            Assert.Empty(actions.Calls);
        }
    }

    [Fact]
    public async Task SessionTypeReportsNulBeforeAnUnknownPane()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionType, Target = "s",
            Args = new ControlArgs { Text = "a\0b", Pane = "middle" },
        });

        ExpectResponse(Err("text must not contain a NUL byte"), response);
    }

    [Fact]
    public async Task SessionTextReportsAnExtentErrorBeforeAPaneOne()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionText, Target = "s",
            Args = new ControlArgs { Pane = "middle", All = true, Lines = 5 },
        });

        ExpectResponse(Err("use either --all or --lines, not both"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionTypeRoutesParsedOptionsAndEchoesActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionTypeResponse = Err("session not realized");

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionType, Target = "session",
            Args = new ControlArgs { Text = "ls\n", Select = true, Window = "win", Pane = "scratch" },
        });

        ExpectResponse(Err("session not realized"), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionType("session", "win",
                new ControlSessionTypeOptions("ls\n", Select: true, Pane: "scratch")),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionCopyRoutesTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionCopyResponse = Ok(new ControlResult { Text = "selected" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionCopy, Target = "session", Args = new ControlArgs { Window = "win" },
        });

        ExpectResponse(Ok(new ControlResult { Text = "selected" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionCopy("session", "win"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionCopyKeepsExactActionErrorResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionCopyResponse = Err("no selection");

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionCopy, Target = "session",
        });

        ExpectResponse(Err("no selection"), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionCopy("session", null),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionPasteRoutesTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionPasteResponse = Ok(new ControlResult { Id = "session" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionPaste, Target = "session", Args = new ControlArgs { Window = "win" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionPaste("session", "win"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionSelectAllRoutesTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionSelectAllResponse = Ok(new ControlResult { Id = "session" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionSelectAll, Target = "session", Args = new ControlArgs { Window = "win" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionSelectAll("session", "win"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayOpenRejectsInvalidInputsBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missing = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session",
        });
        var empty = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session", Args = new ControlArgs { Command = "" },
        });
        var badColor = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session",
            Args = new ControlArgs { Command = "cat", Color = "purple" },
        });

        ExpectResponse(Err("session.overlay.open requires a command"), missing);
        ExpectResponse(Err("session.overlay.open requires a command"), empty);
        ExpectResponse(Err("invalid color: purple (#rrggbb)"), badColor);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayOpenRejectsOutOfRangeSizePercent()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var tooBig = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session",
            Args = new ControlArgs { Command = "cat", SizePercent = 101 },
        });
        var tooSmall = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session",
            Args = new ControlArgs { Command = "cat", SizePercent = 0 },
        });
        var badColorFirst = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session",
            Args = new ControlArgs { Command = "cat", SizePercent = 500, Color = "nope" },
        });

        ExpectResponse(Err("session.overlay.open: --size-percent must be 1...100"), tooBig);
        ExpectResponse(Err("session.overlay.open: --size-percent must be 1...100"), tooSmall);
        ExpectResponse(Err("invalid color: nope (#rrggbb)"), badColorFirst);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayOpenRoutesOptionsAndEchoesActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextOverlayOpenResponse = Err("overlay already open");

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session",
            Args = new ControlArgs
            {
                Cwd = "/tmp", Command = "cat", Wait = true,
                SizePercent = 70, Follow = true, Window = "win", Color = "#2a1a3a",
            },
        });

        ExpectResponse(Err("overlay already open"), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.OverlayOpen("session", "win",
                new ControlSessionOverlayOpenOptions("cat", Cwd: "/tmp", Wait: true,
                    SizePercent: 70, BackgroundColor: "#2a1a3a", Follow: true)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayOpenDefaultsFollowToFalseWhenOmitted()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextOverlayOpenResponse = Ok(new ControlResult { Id = "session" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen, Target = "session", Args = new ControlArgs { Command = "cat" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.OverlayOpen("session", null,
                new ControlSessionOverlayOpenOptions("cat", Cwd: null, Wait: false,
                    SizePercent: null, BackgroundColor: null, Follow: false)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayCloseAndResultRouteTargetAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextOverlayCloseResponse = Ok(new ControlResult { Id = "session" });
        actions.NextOverlayResultResponse = Ok(new ControlResult { Id = "session", ExitCode = 7 });

        var close = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayClose, Target = "session", Args = new ControlArgs { Window = "win" },
        });
        var result = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResult, Target = "session", Args = new ControlArgs { Window = "win" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), close);
        ExpectResponse(Ok(new ControlResult { Id = "session", ExitCode = 7 }), result);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.OverlayClose("session", "win"),
            new MockControlActions.Call.OverlayResult("session", "win"),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayResultKeepsExactActionErrorResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextOverlayResultResponse = Err(OverlayResultError.StillRunning);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResult, Target = "session",
        });

        ExpectResponse(Err(OverlayResultError.StillRunning), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.OverlayResult("session", null),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayResizeRoutesSizePercentAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextOverlayResizeResponse = Ok(new ControlResult { Id = "session" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResize, Target = "session",
            Args = new ControlArgs { SizePercent = 60, Window = "win" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "session" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.OverlayResize("session", "win", 60),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayResizeFullRoutesNilSizePercent()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResize, Target = "session", Args = new ControlArgs { Full = true },
        });

        Assert.True(response?.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.OverlayResize("session", null, null),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionOverlayResizeRejectsMissingConflictingAndOutOfRange()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missing = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResize, Target = "session",
        });
        var both = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResize, Target = "session",
            Args = new ControlArgs { SizePercent = 50, Full = true },
        });
        var tooBig = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResize, Target = "session",
            Args = new ControlArgs { SizePercent = 101 },
        });
        var tooSmall = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionOverlayResize, Target = "session",
            Args = new ControlArgs { SizePercent = 0 },
        });

        ExpectResponse(Err("session.overlay.resize requires --size-percent or --full"), missing);
        ExpectResponse(Err("session.overlay.resize: --full is mutually exclusive with --size-percent"), both);
        ExpectResponse(Err("session.overlay.resize: --size-percent must be 1...100"), tooBig);
        ExpectResponse(Err("session.overlay.resize: --size-percent must be 1...100"), tooSmall);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionBackgroundRoutesParsedTextImageColorAndClearForms()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionBackgroundResponse = Ok(new ControlResult { Id = "session" });

        var text = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Target = "session",
            Args = new ControlArgs
            {
                Text = "DRAFT", Mode = "text", Window = "win", Color = "#ff0000",
                Opacity = 0.15, Fit = "contain", Position = "top-left",
            },
        });
        var image = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Target = "session",
            Args = new ControlArgs
            {
                Mode = "image", Path = "/tmp/bg.png", Fit = "cover",
                Position = "bottom-right", Repeats = true,
            },
        });
        var color = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Target = "session",
            Args = new ControlArgs { Mode = "color", Color = "#102030" },
        });
        var clear = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Target = "session",
        });

        var textWatermark = new BackgroundWatermark
        {
            Kind = WatermarkKind.Text, Text = "DRAFT", ColorHex = "#ff0000",
            Opacity = 0.15, Fit = WatermarkFit.Contain, Position = WatermarkPosition.TopLeft,
        };
        var imageWatermark = new BackgroundWatermark
        {
            Kind = WatermarkKind.Image, ImagePath = "/tmp/bg.png",
            Fit = WatermarkFit.Cover, Position = WatermarkPosition.BottomRight, Repeats = true,
        };
        var colorWatermark = new BackgroundWatermark { Kind = WatermarkKind.Color, ColorHex = "#102030" };
        ExpectResponse(Ok(new ControlResult { Id = "session" }), text);
        ExpectResponse(Ok(new ControlResult { Id = "session" }), image);
        ExpectResponse(Ok(new ControlResult { Id = "session" }), color);
        ExpectResponse(Ok(new ControlResult { Id = "session" }), clear);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionBackground("session", "win",
                new ControlSessionBackgroundOptions(textWatermark)),
            new MockControlActions.Call.SessionBackground("session", null,
                new ControlSessionBackgroundOptions(imageWatermark)),
            new MockControlActions.Call.SessionBackground("session", null,
                new ControlSessionBackgroundOptions(colorWatermark)),
            new MockControlActions.Call.SessionBackground("session", null,
                new ControlSessionBackgroundOptions(null)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionBackgroundRejectsInvalidInputsBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var tooLong = new string('x', WatermarkConfig.MaxTextLength + 1);

        var badFit = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground,
            Args = new ControlArgs { Mode = "image", Path = "/tmp/bg.png", Fit = "wide" },
        });
        var badPosition = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground,
            Args = new ControlArgs { Mode = "image", Path = "/tmp/bg.png", Position = "middle" },
        });
        var badOpacity = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground,
            Args = new ControlArgs { Mode = "image", Path = "/tmp/bg.png", Opacity = 1.5 },
        });
        var missingPath = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Args = new ControlArgs { Mode = "image" },
        });
        var controlPath = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground,
            Args = new ControlArgs { Mode = "image", Path = "/tmp/bg\n.png" },
        });
        var missingText = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Args = new ControlArgs { Mode = "text" },
        });
        var longText = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Args = new ControlArgs { Text = tooLong, Mode = "text" },
        });
        var badTextColor = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground,
            Args = new ControlArgs { Text = "DRAFT", Mode = "text", Color = "red" },
        });
        var missingColor = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Args = new ControlArgs { Mode = "color" },
        });
        var badColor = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Args = new ControlArgs { Mode = "color", Color = "blue" },
        });
        var badMode = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionBackground, Args = new ControlArgs { Mode = "pattern" },
        });

        ExpectResponse(Err("invalid fit: wide (contain|cover|stretch|none)"), badFit);
        ExpectResponse(Err("invalid position: middle"), badPosition);
        ExpectResponse(Err("invalid opacity: 1.5 (0.0-1.0)"), badOpacity);
        ExpectResponse(Err("session.background image requires a path"), missingPath);
        ExpectResponse(Err("image path must not contain control characters"), controlPath);
        ExpectResponse(Err("session.background text requires text"), missingText);
        ExpectResponse(Err($"session.background text too long (max {WatermarkConfig.MaxTextLength} characters)"),
            longText);
        ExpectResponse(Err("invalid color: red (#rrggbb)"), badTextColor);
        ExpectResponse(Err("session.background color requires a color"), missingColor);
        ExpectResponse(Err("invalid color: blue (#rrggbb)"), badColor);
        ExpectResponse(Err("invalid background mode: pattern (image|text|color|clear)"), badMode);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionTextRoutesOptionsAndKeepsExactActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionTextResponse = Ok(new ControlResult { Text = "line\n" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionText, Target = "session",
            Args = new ControlArgs { Window = "win", Pane = "scratch", Lines = 10 },
        });

        ExpectResponse(Ok(new ControlResult { Text = "line\n" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionText("session", "win",
                new ControlSessionTextOptions("scratch", All: false, Lines: 10)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionTextRejectsInvalidLineOptionsBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var both = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionText, Args = new ControlArgs { All = true, Lines = 5 },
        });
        var zero = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionText, Args = new ControlArgs { Lines = 0 },
        });

        ExpectResponse(Err("use either --all or --lines, not both"), both);
        ExpectResponse(Err("--lines must be greater than 0"), zero);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task RestoreClearRoutesThroughActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextRestoreClearResponse = Ok();

        var response = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.RestoreClear });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.RestoreClear(),
        }, actions.Calls);
    }

    [Fact]
    public async Task QuickRoutesRawModeAndKeepsActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextQuickResponse = Err("invalid quick mode: maybe");

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Quick, Args = new ControlArgs { Mode = "maybe" },
        });

        ExpectResponse(Err("invalid quick mode: maybe"), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Quick("maybe"),
        }, actions.Calls);
    }

    [Fact]
    public async Task QuickTypeRoutesTextAndKeepsActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextQuickTypeResponse = Err("quick terminal not open");

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.QuickType, Args = new ControlArgs { Text = "hello" },
        });

        ExpectResponse(Err("quick terminal not open"), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.QuickType("hello"),
        }, actions.Calls);
    }

    [Fact]
    public async Task QuickTypeRequiresTextBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.QuickType });

        ExpectResponse(Err("quick.type requires text"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task QuickTypeRejectsNulBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.QuickType,
            Args = new ControlArgs { Text = "safe\0danger\n" },
        });

        ExpectResponse(Err("text must not contain a NUL byte"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task QuickTextRoutesOptionsAndKeepsExactActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextQuickTextResponse = Ok(new ControlResult { Text = "line\n" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.QuickText, Args = new ControlArgs { Lines = 10 },
        });

        ExpectResponse(Ok(new ControlResult { Text = "line\n" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.QuickText(All: false, Lines: 10),
        }, actions.Calls);
    }

    [Fact]
    public async Task QuickTextRoutesAllFlagOnTheSuccessPath()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextQuickTextResponse = Ok(new ControlResult { Text = "full\n" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.QuickText, Args = new ControlArgs { All = true },
        });

        ExpectResponse(Ok(new ControlResult { Text = "full\n" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.QuickText(All: true, Lines: null),
        }, actions.Calls);
    }

    [Fact]
    public async Task QuickTextRejectsInvalidLineOptionsBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var both = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.QuickText, Args = new ControlArgs { All = true, Lines = 5 },
        });
        var zero = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.QuickText, Args = new ControlArgs { Lines = 0 },
        });

        ExpectResponse(Err("use either --all or --lines, not both"), both);
        ExpectResponse(Err("--lines must be greater than 0"), zero);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SurfaceZoomParsesModeAndRoutesTargetWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSurfaceZoomResponse = Ok(new ControlResult { Id = "surface:s1:right" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SurfaceZoom, Target = "surface:s1:right",
            Args = new ControlArgs { Mode = "show", Window = "win" },
        });

        ExpectResponse(Ok(new ControlResult { Id = "surface:s1:right" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SurfaceZoom("surface:s1:right", "win", ControlToggleMode.On),
        }, actions.Calls);
    }

    [Fact]
    public async Task SurfaceZoomDefaultsToToggle()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        _ = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.SurfaceZoom });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SurfaceZoom(null, null, ControlToggleMode.Toggle),
        }, actions.Calls);
    }

    [Fact]
    public async Task SurfaceZoomRejectsInvalidModeWithoutCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SurfaceZoom, Args = new ControlArgs { Mode = "zoom" },
        });

        ExpectResponse(Err("invalid surface zoom mode: zoom"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task DashboardOpenRoutesTargetsWindowAndFixedFontMode()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextDashboardResponse = Ok(new ControlResult { Id = "win" });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard,
            Args = new ControlArgs { Targets = new[] { "a", "b" }, Window = "win", FontSize = 14 },
        });

        ExpectResponse(Ok(new ControlResult { Id = "win" }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Dashboard(new[] { "a", "b" }, "win", Close: false,
                new DashboardFontMode.Fixed(14), Mru: false),
        }, actions.Calls);
    }

    [Fact]
    public async Task DashboardOpenBuildsAutoAndUntouchedFontModes()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var auto = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a" }, AutoSize = true },
        });
        var untouched = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a", "b" } },
        });

        ExpectResponse(Ok(), auto);
        ExpectResponse(Ok(), untouched);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Dashboard(new[] { "a" }, null, Close: false,
                new DashboardFontMode.Auto(), Mru: false),
            new MockControlActions.Call.Dashboard(new[] { "a", "b" }, null, Close: false,
                new DashboardFontMode.Untouched(), Mru: false),
        }, actions.Calls);
    }

    [Fact]
    public async Task DashboardCloseRoutesWithCloseTrueAndNoFontMode()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Window = "win", Close = true },
        });

        ExpectResponse(Ok(), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Dashboard(Array.Empty<string>(), "win", Close: true,
                new DashboardFontMode.Untouched(), Mru: false),
        }, actions.Calls);
    }

    [Fact]
    public async Task DashboardForwardsAllTargetsWithoutCapping()
    {
        // the 9-cell cap now counts PANES and lives app-side (a split session expands to two cells), so
        // the dispatcher forwards ALL raw ids untouched and appends no drop text.
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var ids = Enumerable.Range(1, 11).Select(i => $"s{i}").ToArray();

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = ids },
        });
        Assert.True(response?.Ok);
        Assert.Null(response!.Result?.Text);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Dashboard(ids, null, Close: false,
                new DashboardFontMode.Untouched(), Mru: false),
        }, actions.Calls);

        // whatever the app-side response is (unresolved / dropped-pane text), the dispatcher passes it
        // through verbatim — it no longer post-processes the message.
        actions.NextDashboardResponse = Ok(new ControlResult { Text = "unresolved: s3" });
        var passed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = ids },
        });
        Assert.Equal("unresolved: s3", passed?.Result?.Text);
    }

    [Fact]
    public async Task DashboardRejectsInvalidInputsBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var emptyOpen = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.Dashboard });
        var bothFonts = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard,
            Args = new ControlArgs { Targets = new[] { "a" }, FontSize = 12, AutoSize = true },
        });
        var zeroFont = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a" }, FontSize = 0 },
        });
        var negativeFont = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a" }, FontSize = -3 },
        });
        var nonFiniteFont = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a" }, FontSize = double.NaN },
        });
        var closeWithIds = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a" }, Close = true },
        });
        var closeWithFont = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Close = true, FontSize = 12 },
        });
        var closeWithAutoSize = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Close = true, AutoSize = true },
        });

        ExpectResponse(Err("dashboard requires at least one session id"), emptyOpen);
        ExpectResponse(Err("dashboard: --font-size is mutually exclusive with --auto-size"), bothFonts);
        ExpectResponse(Err("dashboard --font-size must be a positive number"), zeroFont);
        ExpectResponse(Err("dashboard --font-size must be a positive number"), negativeFont);
        ExpectResponse(Err("dashboard --font-size must be a positive number"), nonFiniteFont);
        ExpectResponse(Err("dashboard --close takes no ids, --mru, or font options"), closeWithIds);
        ExpectResponse(Err("dashboard --close takes no ids, --mru, or font options"), closeWithFont);
        ExpectResponse(Err("dashboard --close takes no ids, --mru, or font options"), closeWithAutoSize);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SessionSearchRoutesRawInputsAndKeepsActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextSessionSearchResponse = Ok(new ControlResult { Text = "2 of 5", Count = 5 });

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionSearch, Target = "session",
            Args = new ControlArgs { Text = "needle", Window = "win", To = "next" },
        });

        ExpectResponse(Ok(new ControlResult { Text = "2 of 5", Count = 5 }), response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionSearch("session", "win", "needle", "next"),
        }, actions.Calls);
    }

    [Fact]
    public async Task WindowLifecycleAndListCommandsRouteThroughActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var windows = new[]
        {
            new ControlWindowNode { Id = "win-a", Name = "Main", Open = true, Active = true },
            new ControlWindowNode { Id = "win-b", Name = "Build", Open = false, Active = false },
        };
        actions.NextWindowNewResponse = Ok(new ControlResult { Id = "win-b" });
        actions.NextWindowListResponse = Ok(new ControlResult { Windows = windows });
        actions.NextWindowSelectResponse = Ok(new ControlResult { Id = "win-b" });
        actions.NextWindowCloseResponse = Ok(new ControlResult { Id = "win-b" });
        actions.NextWindowDeleteResponse = Err("cannot delete last window");

        var created = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowNew, Args = new ControlArgs { Name = "Build" },
        });
        var listed = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.WindowList });
        var selected = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowSelect, Target = "win-b",
        });
        var closed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowClose, Target = "win-b",
        });
        var deleted = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowDelete, Target = "win-b",
        });

        ExpectResponse(Ok(new ControlResult { Id = "win-b" }), created);
        ExpectResponse(Ok(new ControlResult { Windows = windows }), listed);
        ExpectResponse(Ok(new ControlResult { Id = "win-b" }), selected);
        ExpectResponse(Ok(new ControlResult { Id = "win-b" }), closed);
        ExpectResponse(Err("cannot delete last window"), deleted);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WindowNew("Build"),
            new MockControlActions.Call.WindowList(),
            new MockControlActions.Call.WindowSelect("win-b"),
            new MockControlActions.Call.WindowClose("win-b"),
            new MockControlActions.Call.WindowDelete("win-b"),
        }, actions.Calls);
    }

    [Fact]
    public async Task WindowCommandsRouteParsedInputsAndKeepActionResponses()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextWindowRenameResponse = Ok(new ControlResult { Id = "win" });
        actions.NextWindowResizeResponse = Ok(new ControlResult { Id = "win" });
        actions.NextWindowMoveResponse = Ok(new ControlResult { Id = "win" });
        actions.NextWindowZoomResponse = Err("window not open — window.select it first");

        var renamed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowRename, Target = "9f3c", Args = new ControlArgs { Name = "  Renamed  " },
        });
        var resized = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowResize, Target = "9f3c", Args = new ControlArgs { Width = 1200, Height = 800 },
        });
        var moved = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowMove, Target = "9f3c", Args = new ControlArgs { X = 100, Y = 50, Display = 1 },
        });
        var zoomed = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowZoom, Target = "9f3c",
        });
        actions.NextWindowFullscreenResponse = Ok(new ControlResult { Id = "win" });
        var fullscreen = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowFullscreen, Target = "9f3c",
        });

        ExpectResponse(Ok(new ControlResult { Id = "win" }), renamed);
        ExpectResponse(Ok(new ControlResult { Id = "win" }), resized);
        ExpectResponse(Ok(new ControlResult { Id = "win" }), moved);
        ExpectResponse(Err("window not open — window.select it first"), zoomed);
        ExpectResponse(Ok(new ControlResult { Id = "win" }), fullscreen);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WindowRename("9f3c", "Renamed"),
            new MockControlActions.Call.WindowResize("9f3c", 1200, 800),
            new MockControlActions.Call.WindowMove("9f3c", 100, 50, 1),
            new MockControlActions.Call.WindowZoom("9f3c"),
            new MockControlActions.Call.WindowFullscreen("9f3c"),
        }, actions.Calls);
    }

    [Fact]
    public async Task WindowCommandsRejectInvalidInputsBeforeCallingActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var missingName = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowRename, Target = "win",
        });
        var blankName = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowRename, Target = "win", Args = new ControlArgs { Name = "   " },
        });
        var missingResize = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowResize, Target = "win", Args = new ControlArgs { Width = 1200 },
        });
        var badResize = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowResize, Target = "win", Args = new ControlArgs { Width = 0, Height = 800 },
        });
        var missingMoveY = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowMove, Target = "win", Args = new ControlArgs { X = 100 },
        });

        ExpectResponse(Err("window.rename requires a name"), missingName);
        ExpectResponse(Err("window.rename requires a name"), blankName);
        ExpectResponse(Err("window.resize requires positive width and height"), missingResize);
        ExpectResponse(Err("window.resize requires positive width and height"), badResize);
        ExpectResponse(Err("window.move requires x and y"), missingMoveY);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task WindowCommandsKeepHostSideLookupAndPlatformErrors()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        actions.NextWindowRenameResponse = Err("no such window: missing");
        actions.NextWindowMoveResponse = Err("display 3 out of range (have 1)");

        var missingWindow = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowRename, Target = "missing", Args = new ControlArgs { Name = "Renamed" },
        });
        var badDisplay = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WindowMove, Target = "win", Args = new ControlArgs { X = 100, Y = 50, Display = 3 },
        });

        ExpectResponse(Err("no such window: missing"), missingWindow);
        ExpectResponse(Err("display 3 out of range (have 1)"), badDisplay);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WindowRename("missing", "Renamed"),
            new MockControlActions.Call.WindowMove("win", 100, 50, 3),
        }, actions.Calls);
    }
}
