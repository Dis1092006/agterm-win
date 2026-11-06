using System.CommandLine;
using AgTerm.Core.Icons;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit WorkspaceCommands.swift — the exemplar for every command file: one nested
// Build per subcommand wiring System.CommandLine tokens to a plain bound command object whose
// MakeRequest/Validate mirror the Swift RequestCommand 1:1.

internal static class WorkspaceCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var workspace = new System.CommandLine.Command("workspace", "Workspace commands.");
        workspace.Subcommands.Add(BuildNew(bindings));
        workspace.Subcommands.Add(BuildRename(bindings));
        workspace.Subcommands.Add(BuildDelete(bindings));
        workspace.Subcommands.Add(BuildSelect(bindings));
        workspace.Subcommands.Add(BuildGo(bindings));
        workspace.Subcommands.Add(BuildMove(bindings));
        workspace.Subcommands.Add(BuildFocus(bindings));
        workspace.Subcommands.Add(BuildFilter(bindings));
        workspace.Subcommands.Add(BuildFlag(bindings));
        workspace.Subcommands.Add(BuildCollapse(bindings));
        workspace.Subcommands.Add(BuildExpand(bindings));
        workspace.Subcommands.Add(TintCommands.BuildWorkspaceTint(bindings));
        workspace.Subcommands.Add(BuildTheme(bindings));
        workspace.Subcommands.Add(BuildIcon(bindings));
        return workspace;
    }

    internal sealed class New : IRequestCommand
    {
        public string? Name { get; init; }
        public bool Collapsed { get; init; }
        public required TintCreateOptions Tint { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;
        public bool EchoesResultId => true;

        public void Validate() => Tint.Validate();

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceNew,
            Args = Client.WithWindow(Tint.Apply(new ControlArgs { Name = Name, Collapsed = Collapsed ? true : null })),
        };
    }

    private static System.CommandLine.Command BuildNew(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("new", "Create a workspace.");
        var name = new Argument<string?>("name")
        {
            Description = "Workspace name (defaults to the auto-generated name).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var collapsed = new Option<bool>("--collapsed")
        {
            Description = "Create the workspace collapsed in the sidebar.",
        };
        var tint = new TintCreateOptions.Binder(session: false);
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(name);
        cmd.Options.Add(collapsed);
        tint.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new New
        {
            Name = result.GetValue(name),
            Collapsed = result.GetValue(collapsed),
            Tint = tint.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Go : IRequestCommand
    {
        public required string To { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceGo,
            Args = Client.WithWindow(new ControlArgs { To = To }),
        };
    }

    // Relative to the current workspace, so it takes no --target (the shape `session go` has).
    private static System.CommandLine.Command BuildGo(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("go", "Navigate workspaces: next|prev.");
        var to = new Option<string>("--to") { Description = "Direction: next or prev.", Required = true };
        var options = new ClientOptions.Binder();
        cmd.Options.Add(to);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Go { To = result.GetValue(to)!, Client = options.Read(result) });
        return cmd;
    }

    internal sealed class Rename : IRequestCommand
    {
        public required string Name { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceRename,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Name = Name }),
        };
    }

    private static System.CommandLine.Command BuildRename(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("rename", "Rename a workspace.");
        var name = new Argument<string>("name") { Description = "New workspace name." };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(name);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Rename
        {
            Name = result.GetValue(name)!,
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Delete : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceDelete,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildDelete(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("delete", "Delete a workspace.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Delete
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Select : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceSelect,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildSelect(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("select", "Select a workspace.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Select
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Move : IRequestCommand
    {
        public required string To { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceMove,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { To = To }),
        };
    }

    private static System.CommandLine.Command BuildMove(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("move", "Reorder a workspace among its siblings.");
        var to = new Option<string>("--to")
        {
            Description = "Direction: up, down, top, or bottom.",
            Required = true,
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Options.Add(to);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Move
        {
            To = result.GetValue(to)!,
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Focus : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate()
        {
            if (ControlWorkspaceFocusModes.Parse(Mode) is null)
                throw new CliValidationException($"mode must be one of: {ControlWorkspaceFocusModes.ValidNamesPhrase}");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceFocus,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Mode = Mode }),
        };
    }

    private static System.CommandLine.Command BuildFocus(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("focus",
            $"Mark a workspace in the sidebar focus set ({ControlWorkspaceFocusModes.ValidNamesList}).");
        var mode = new Argument<string>("mode")
        {
            Description = $"Mode: {ControlWorkspaceFocusModes.HelpPhrase}.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(mode);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Focus
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    /// <summary>`agtermctl workspace filter [on|off|toggle] [--window W]` — turns the sidebar's workspace
    /// focus filter on or off for a whole window and keeps the marked set. It has no `--target`.</summary>
    internal sealed class Filter : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate()
        {
            if (Mode is not ("on" or "off" or "toggle"))
                throw new CliValidationException("mode must be on, off, or toggle");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceFilter,
            Args = Client.WithWindow(new ControlArgs { Mode = Mode }),
        };
    }

    private static System.CommandLine.Command BuildFilter(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("filter",
            "Turn the sidebar workspace focus filter on or off (on|off|toggle).");
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: on, off, or toggle (default).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(mode);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Filter
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Client = options.Read(result),
        });
        return cmd;
    }

    /// <summary>`agtermctl workspace flag [on|off|toggle] [--target W]` — flags or unflags EVERY session of
    /// one workspace as one value. Toggle unflags only when all are flagged, otherwise flags all, so it is
    /// not a per-session invert; the read-back is each session's `flagged` in `tree`.</summary>
    internal sealed class Flag : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate()
        {
            if (Mode is not ("on" or "off" or "toggle"))
                throw new CliValidationException("mode must be on, off, or toggle");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceFlag,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Mode = Mode }),
        };
    }

    private static System.CommandLine.Command BuildFlag(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("flag",
            "Flag or unflag every session in a workspace (on|off|toggle).");
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: on, off, or toggle (default; unflags only when every session is flagged).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(mode);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Flag
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Collapse : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceCollapse,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildCollapse(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("collapse", "Collapse a workspace in the sidebar tree.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Collapse
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Expand : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceExpand,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildExpand(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("expand", "Expand a workspace in the sidebar tree.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Expand
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    // Windows-only (parity/windows-only.json): upstream has no theme per workspace.
    internal sealed class Theme : IRequestCommand
    {
        public string? Name { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceTheme,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Name = Name }),
        };
    }

    private static System.CommandLine.Command BuildTheme(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("theme",
            "Set a workspace's own terminal theme, or clear it without a name (Windows only).");
        var name = new Argument<string?>("name")
        {
            Description = "Theme name from `agtermctl theme list`; omit it to show the global theme again.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(name);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Theme
        {
            Name = result.GetValue(name),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    // Windows-only (parity/windows-only.json): upstream has no icon per workspace.
    internal sealed class Icon : IRequestCommand
    {
        public string? Name { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WorkspaceIcon,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Name = WireIcon(Name) }),
        };

        // the app runs in its own directory, so a relative image path resolves here, where the user typed it
        // a blank or padded name goes as typed: the server trims it, so it clears or names the icon
        private static string? WireIcon(string? icon) =>
            icon?.Trim() is { Length: > 0 } trimmed && WorkspaceIconFile.IsFile(trimmed)
                ? Path.GetFullPath(trimmed)
                : icon;
    }

    private static System.CommandLine.Command BuildIcon(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("icon",
            "Set a workspace's own icon, or clear it without a name (Windows only).");
        var name = new Argument<string?>("name")
        {
            Description = "Icon name from `agtermctl icon list`, or the path of an .ico, .png or .svg file; omit it to show the default icon again.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(name);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Icon
        {
            Name = result.GetValue(name),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }
}
