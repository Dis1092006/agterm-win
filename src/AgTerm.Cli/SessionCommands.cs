using System.CommandLine;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit SessionCommands.swift — the `session` command group.

internal static class SessionCommands
{
    /// <summary>Shared `--pane` validation for the commands that accept `left|right|scratch` (session
    /// type, text, status, and font). Rejects any other value with a clean usage error before the
    /// socket round-trip, matching the server-side switch (so the CLI and server can't drift, and a
    /// raw socket client still hits the same check server-side). These commands intentionally reuse
    /// `StatusPane`'s `left|right|scratch` value set as the shared pane-addressing vocabulary — the
    /// enum is named for agent status but its cases are the pane names.</summary>
    internal static void ValidatePaneArgument(string? pane)
    {
        if (pane is { } value && StatusPanes.FromWire(value) is null)
            throw new CliValidationException("--pane must be left, right, or scratch");
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var session = new System.CommandLine.Command("session", "Session commands.");
        session.Subcommands.Add(BuildNew(bindings));
        session.Subcommands.Add(BuildDuplicate(bindings));
        session.Subcommands.Add(BuildClose(bindings));
        session.Subcommands.Add(BuildSelect(bindings));
        session.Subcommands.Add(BuildGo(bindings));
        session.Subcommands.Add(BuildRename(bindings));
        session.Subcommands.Add(BuildReveal(bindings));
        session.Subcommands.Add(BuildMove(bindings));
        session.Subcommands.Add(BuildType(bindings));
        session.Subcommands.Add(BuildSplit(bindings));
        session.Subcommands.Add(BuildScratch(bindings));
        session.Subcommands.Add(BuildFocus(bindings));
        session.Subcommands.Add(BuildResize(bindings));
        session.Subcommands.Add(BuildCopy(bindings));
        session.Subcommands.Add(BuildPaste(bindings));
        session.Subcommands.Add(BuildSelectAll(bindings));
        session.Subcommands.Add(BuildText(bindings));
        session.Subcommands.Add(BuildStatus(bindings));
        session.Subcommands.Add(BuildRestore(bindings));
        session.Subcommands.Add(BuildFlag(bindings));
        session.Subcommands.Add(BuildSeen(bindings));
        session.Subcommands.Add(BuildSearch(bindings));
        session.Subcommands.Add(BuildBackground(bindings));
        session.Subcommands.Add(BuildOverlay(bindings));
        return session;
    }

    internal sealed class New : IRequestCommand
    {
        public string? Cwd { get; init; }
        public string? Workspace { get; init; }
        public string? WorkspaceName { get; init; }
        public bool CreateWorkspace { get; init; }
        public string? Command { get; init; }
        public bool Wait { get; init; }
        public string? Name { get; init; }
        public string? After { get; init; }
        public string? Before { get; init; }
        public bool NoSelect { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;
        public bool EchoesResultId => true;

        public void Validate()
        {
            if (After is not null && Before is not null)
                throw new CliValidationException("use either --after or --before, not both");
            // the anchor sid already names the workspace, so placement can't also address one.
            if ((After is not null || Before is not null) && (Workspace is not null || WorkspaceName is not null))
                throw new CliValidationException("session.new takes --after/--before or a workspace, not both");
            if (Workspace is not null && WorkspaceName is not null)
                throw new CliValidationException("use either --workspace or --workspace-name, not both");
            if (CreateWorkspace && WorkspaceName is null)
                throw new CliValidationException("--create-workspace requires --workspace-name");
            if (Wait && Command is null)
                throw new CliValidationException("--wait requires --command");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionNew,
            Args = Client.WithWindow(new ControlArgs
            {
                Name = Name, Cwd = Cwd, Workspace = Workspace, WorkspaceName = WorkspaceName,
                CreateWorkspace = CreateWorkspace ? true : null, NoSelect = NoSelect ? true : null,
                Command = Command, Wait = Wait ? true : null, After = After, Before = Before,
            }),
        };
    }

    private static System.CommandLine.Command BuildNew(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("new", "Create a session.");
        var cwd = new Option<string?>("--cwd")
        {
            Description = "Working directory (defaults to $HOME).",
        };
        var workspace = new Option<string?>("--workspace")
        {
            Description = "Target workspace by id/prefix/active (defaults to the current one). Mutually exclusive with --workspace-name.",
        };
        var workspaceName = new Option<string?>("--workspace-name")
        {
            Description = "Target workspace by name; errors if not found unless --create-workspace. Mutually exclusive with --workspace.",
        };
        var createWorkspace = new Option<bool>("--create-workspace")
        {
            Description = "With --workspace-name, create the workspace when it does not exist (reuse it otherwise).",
        };
        var command = new Option<string?>("--command")
        {
            Description = "Run this command as the session's process instead of the login shell (no echoed command line; the session closes when it exits).",
        };
        var wait = new Option<bool>("--wait")
        {
            Description = "With --command, hold the session open after the command exits (press any key to close) instead of closing immediately.",
        };
        var name = new Option<string?>("--name")
        {
            Description = "Initial session name (defaults to the auto basename).",
        };
        var after = new Option<string?>("--after")
        {
            Description = "Place the new session right AFTER this anchor session (id/prefix/active); the anchor carries its own workspace, replacing --workspace.",
        };
        var before = new Option<string?>("--before")
        {
            Description = "Place the new session right BEFORE this anchor session (id/prefix/active); mirror of --after.",
        };
        var noSelect = new Option<bool>("--no-select")
        {
            Description = "Create the session in the background without selecting or focusing it (leaves the current selection untouched).",
        };
        var options = new ClientOptions.Binder();
        cmd.Options.Add(cwd);
        cmd.Options.Add(workspace);
        cmd.Options.Add(workspaceName);
        cmd.Options.Add(createWorkspace);
        cmd.Options.Add(command);
        cmd.Options.Add(wait);
        cmd.Options.Add(name);
        cmd.Options.Add(after);
        cmd.Options.Add(before);
        cmd.Options.Add(noSelect);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new New
        {
            Cwd = result.GetValue(cwd),
            Workspace = result.GetValue(workspace),
            WorkspaceName = result.GetValue(workspaceName),
            CreateWorkspace = result.GetValue(createWorkspace),
            Command = result.GetValue(command),
            Wait = result.GetValue(wait),
            Name = result.GetValue(name),
            After = result.GetValue(after),
            Before = result.GetValue(before),
            NoSelect = result.GetValue(noSelect),
            Client = options.Read(result),
        });
        return cmd;
    }

    /// <summary>No options: the target session names both the destination workspace and the cwd, so a
    /// duplicate is fully described by `--target` (the GUI half is the sidebar row's "Duplicate").</summary>
    internal sealed class Duplicate : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;
        public bool EchoesResultId => true;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionDuplicate,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildDuplicate(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("duplicate",
            "Duplicate a session: a fresh shell in its directory, placed right after it.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Duplicate
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Close : IRequestCommand
    {
        public required BatchTargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest()
        {
            var batchArgs = Target.BatchTargets is { } batch ? new ControlArgs { Targets = batch } : null;
            return new ControlRequest
            {
                Cmd = WireCommand.SessionClose,
                Target = Target.Targets.Count > 0 ? Target.Targets[0] : "active",
                Args = Client.WithWindow(batchArgs),
            };
        }
    }

    private static System.CommandLine.Command BuildClose(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("close", "Close a session.");
        var target = new BatchTargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Close
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
            Cmd = WireCommand.SessionSelect,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildSelect(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("select", "Select a session.");
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

    internal sealed class Go : IRequestCommand
    {
        public required string To { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionGo,
            Args = Client.WithWindow(new ControlArgs { To = To }),
        };
    }

    private static System.CommandLine.Command BuildGo(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("go",
            "Navigate sessions: next|prev|first|last|next-attention|prev-attention.");
        var to = new Option<string>("--to")
        {
            Description = "Direction: next, prev, first, last, next-attention, or prev-attention (attention = blocked/completed).",
            Required = true,
        };
        var options = new ClientOptions.Binder();
        cmd.Options.Add(to);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Go
        {
            To = result.GetValue(to)!,
            Client = options.Read(result),
        });
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
            Cmd = WireCommand.SessionRename,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Name = Name }),
        };
    }

    private static System.CommandLine.Command BuildRename(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("rename", "Rename a session.");
        var name = new Argument<string>("name") { Description = "New session name." };
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

    internal sealed class Reveal : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionReveal,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildReveal(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("reveal",
            "Reveal a session's focused working directory in Finder.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Reveal
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Move : IRequestCommand
    {
        public string? Workspace { get; init; }
        public string? To { get; init; }
        public string? After { get; init; }
        public string? Before { get; init; }
        public required BatchTargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        // exactly one placement intent among {workspace positional (relocate), --to (reorder),
        // --after/--before (anchor-relative place)}; reject the empty/conflicting cases at parse time so
        // it's a clean usage error, unit-testable without a socket. The anchor carries its own workspace,
        // so placement is mutually exclusive with both --to and a destination workspace.
        public void Validate()
        {
            if (After is not null && Before is not null)
                throw new CliValidationException("use either --after or --before, not both");
            if (After is not null || Before is not null)
            {
                if (To is not null)
                    throw new CliValidationException("session.move takes --after/--before or --to, not both");
                if (Workspace is not null)
                    throw new CliValidationException("session.move takes --after/--before or a workspace, not both");
                return;
            }
            if (Target.Targets.Count > 1 && To is not null)
                throw new CliValidationException("session.move --target can be repeated only with a workspace or --after/--before");
            switch (Workspace, To)
            {
                case (null, null):
                    throw new CliValidationException("provide a destination workspace, --to, or --after/--before");
                case (not null, not null):
                    throw new CliValidationException("provide a destination workspace or --to, not both");
            }
        }

        public ControlRequest MakeRequest()
        {
            ControlArgs args;
            if (After is { } after)
                args = new ControlArgs { After = after };
            else if (Before is { } before)
                args = new ControlArgs { Before = before };
            else if (Workspace is { } workspace)
                args = new ControlArgs { Workspace = workspace };
            else
                args = new ControlArgs { To = To };
            var withTargets = args with { Targets = Target.BatchTargets };
            return new ControlRequest
            {
                Cmd = WireCommand.SessionMove,
                Target = Target.Targets.Count > 0 ? Target.Targets[0] : "active",
                Args = Client.WithWindow(withTargets),
            };
        }
    }

    private static System.CommandLine.Command BuildMove(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("move",
            "Move a session: to another workspace, reorder with --to, or place relative to an anchor with --after/--before.");
        var workspace = new Argument<string?>("workspace")
        {
            Description = "Destination workspace id/prefix (relocate). Omit with --to or --after/--before.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var to = new Option<string?>("--to")
        {
            Description = "Reorder within the workspace: up, down, top, or bottom.",
        };
        var after = new Option<string?>("--after")
        {
            Description = "Place right AFTER this anchor session (id/prefix/active); the anchor carries its own workspace (relocates + positions in one shot).",
        };
        var before = new Option<string?>("--before")
        {
            Description = "Place right BEFORE this anchor session (id/prefix/active); mirror of --after.",
        };
        var target = new BatchTargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(workspace);
        cmd.Options.Add(to);
        cmd.Options.Add(after);
        cmd.Options.Add(before);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Move
        {
            Workspace = result.GetValue(workspace),
            To = result.GetValue(to),
            After = result.GetValue(after),
            Before = result.GetValue(before),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class TypeText : IRequestCommand
    {
        public string? Text { get; init; }
        public bool Stdin { get; init; }
        public bool Select { get; init; }
        public string? Pane { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        /// <summary>Stdin source. Upstream reads FileHandle.standardInput inside makeRequest, so the
        /// read happens here too; injectable for tests.</summary>
        public Func<Stream> StdinStream { get; init; } = Console.OpenStandardInput;

        public void Validate() => ValidatePaneArgument(Pane);

        public ControlRequest MakeRequest() =>
            MakeRequest(Stdin ? TypedStdin.ReadAll(StdinStream()) : null);

        internal ControlRequest MakeRequest(byte[]? stdin)
        {
            string payload;
            if (Stdin)
                payload = TypedStdin.Decode(stdin ?? Array.Empty<byte>());
            else if (Text is { } text)
                payload = text;
            else
                throw new CliValidationException("provide TEXT or --stdin");
            return new ControlRequest
            {
                Cmd = WireCommand.SessionType,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs { Text = payload, Select = Select, Pane = Pane }),
            };
        }
    }

    private static System.CommandLine.Command BuildType(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("type", "Inject text into a session.");
        var text = new Argument<string?>("text")
        {
            Description = "Text to inject (omit with --stdin).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var stdin = new Option<bool>("--stdin")
        {
            Description = "Read the text from stdin instead of an argument.",
        };
        var select = new Option<bool>("--select")
        {
            Description = "Select the session first if its surface is not ready (main pane only; a split pane must already exist).",
        };
        var pane = new Option<string?>("--pane")
        {
            Description = "Which pane to type into: left (main), right (split), or scratch (the session's scratch terminal, even when hidden). Defaults to the left pane.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(text);
        cmd.Options.Add(stdin);
        cmd.Options.Add(select);
        cmd.Options.Add(pane);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new TypeText
        {
            Text = result.GetValue(text),
            Stdin = result.GetValue(stdin),
            Select = result.GetValue(select),
            Pane = result.GetValue(pane),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Split : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionSplit,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Mode = Mode }),
        };
    }

    private static System.CommandLine.Command BuildSplit(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("split", "Show or hide a session split (on|off|toggle).");
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: on (show), off (hide), or toggle (default). Hidden panes stay alive.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(mode);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Split
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Scratch : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        public string? Command { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionScratch,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Mode = Mode, Command = Command }),
        };
    }

    private static System.CommandLine.Command BuildScratch(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("scratch",
            "Show or hide a session scratch terminal (on|off|toggle).");
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: on (show), off (hide), or toggle (default). The hidden scratch shell stays alive.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var command = new Option<string?>("--command")
        {
            Description = "When showing, run this command as the scratch's process instead of a login shell (run-once; respawns the scratch if one is already open).",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(mode);
        cmd.Options.Add(command);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Scratch
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Command = result.GetValue(command),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Focus : IRequestCommand
    {
        public string Pane { get; init; } = "other";
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionFocus,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Pane = Pane }),
        };
    }

    private static System.CommandLine.Command BuildFocus(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("focus", "Focus a split session's pane (left|right|other).");
        var pane = new Argument<string>("pane")
        {
            Description = "Pane: left, right, or other (toggle, default).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "other",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(pane);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Focus
        {
            Pane = result.GetValue(pane) ?? "other",
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Resize : IRequestCommand
    {
        public double? SplitRatio { get; init; }
        public double? GrowLeft { get; init; }
        public double? GrowRight { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        // exactly one of the three forms must be set; reject neither/multiple at parse time so it's a clean
        // usage error, unit-testable without a socket. Prints the applied (clamped) fraction.
        public void Validate()
        {
            var values = new[] { SplitRatio, GrowLeft, GrowRight }
                .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            if (values.Length != 1)
                throw new CliValidationException("provide exactly one of --split-ratio, --grow-left, or --grow-right");
            // nan/inf parse as Double but fail to JSON-encode (a generic error after the socket opens), so
            // reject non-finite input here with a clean usage error.
            if (!double.IsFinite(values[0]))
                throw new CliValidationException("the resize value must be a finite number");
        }

        public ControlRequest MakeRequest()
        {
            // grow-left/grow-right map to a signed wire delta (+ grows the left pane); split-ratio is absolute.
            ControlArgs args;
            if (SplitRatio is { } splitRatio)
                args = new ControlArgs { Ratio = splitRatio };
            else if (GrowLeft is { } growLeft)
                args = new ControlArgs { RatioDelta = growLeft };
            else
                args = new ControlArgs { RatioDelta = -(GrowRight ?? 0) };
            return new ControlRequest
            {
                Cmd = WireCommand.SessionResize,
                Target = Target.Target,
                Args = Client.WithWindow(args),
            };
        }
    }

    private static System.CommandLine.Command BuildResize(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("resize",
            "Resize a split session's divider (set or nudge the left-pane fraction).");
        var splitRatio = CliOptions.LenientDouble("--split-ratio",
            "Absolute left-pane fraction 0..1 (e.g. 0.7). Clamped to 0.05..0.95.");
        var growLeft = CliOptions.LenientDouble("--grow-left",
            "Grow the left pane by this fraction (e.g. 0.05); shrinks the right.");
        var growRight = CliOptions.LenientDouble("--grow-right",
            "Grow the right pane by this fraction (e.g. 0.05); shrinks the left.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Options.Add(splitRatio);
        cmd.Options.Add(growLeft);
        cmd.Options.Add(growRight);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Resize
        {
            SplitRatio = result.GetValue(splitRatio),
            GrowLeft = result.GetValue(growLeft),
            GrowRight = result.GetValue(growRight),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Copy : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionCopy,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildCopy(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("copy",
            "Print a session's selected text (does not touch the system clipboard).");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Copy
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Paste : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionPaste,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildPaste(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("paste",
            "Paste the system clipboard into a session (like ⌘V).");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Paste
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class SelectAll : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionSelectAll,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildSelectAll(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("select-all",
            "Select a session's entire terminal buffer (like ⌘A).");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new SelectAll
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Text : IRequestCommand
    {
        public bool All { get; init; }
        public int? Lines { get; init; }
        public string? Pane { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate()
        {
            if (All && Lines is not null)
                throw new CliValidationException("use either --all or --lines, not both");
            if (Lines is { } lines && lines <= 0)
                throw new CliValidationException("--lines must be greater than 0");
            ValidatePaneArgument(Pane);
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionText,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Pane = Pane, All = All ? true : null, Lines = Lines }),
        };
    }

    private static System.CommandLine.Command BuildText(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("text",
            "Print a session's terminal buffer as plain text (does not touch the system clipboard).");
        var all = new Option<bool>("--all")
        {
            Description = "Read the full screen + scrollback instead of just the visible screen.",
        };
        var lines = new Option<int?>("--lines")
        {
            Description = "Keep only the last N lines of the full buffer.",
        };
        var pane = new Option<string?>("--pane")
        {
            Description = "Which pane to read: left (main), right (split), or scratch (the session's scratch terminal, even when hidden). Defaults to the on-screen pane.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Options.Add(all);
        cmd.Options.Add(lines);
        cmd.Options.Add(pane);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Text
        {
            All = result.GetValue(all),
            Lines = result.GetValue(lines),
            Pane = result.GetValue(pane),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Status : IRequestCommand
    {
        public required string State { get; init; }
        public bool Blink { get; init; }
        public bool AutoReset { get; init; }
        public string? Sound { get; init; }
        public string? Color { get; init; }
        public string? Shape { get; init; }
        public string? Pane { get; init; }
        public string? PaneId { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate()
        {
            if (Color is { } color && !WatermarkConfig.IsValidColorHex(color))
                throw new CliValidationException("color must be a #rrggbb hex value");
            if (Shape is { } shape && StatusShapes.FromWire(shape) is null)
                throw new CliValidationException($"shape must be one of: {StatusShapes.ValidNamesPhrase}");
            ValidatePaneArgument(Pane);
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionStatus,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs
            {
                Pane = Pane, PaneId = PaneId, Status = State,
                Blink = Blink ? true : null,
                AutoReset = AutoReset ? true : null, Sound = Sound,
                Color = Color, Shape = Shape,
            }),
        };
    }

    private static System.CommandLine.Command BuildStatus(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("status", "Set a session's agent status indicator.");
        var state = new Argument<string>("state")
        {
            Description = "State: idle, active, completed, or blocked.",
        };
        var blink = new Option<bool>("--blink")
        {
            Description = "Pulse the indicator for attention.",
        };
        var autoReset = new Option<bool>("--auto-reset")
        {
            Description = "Reset the indicator to idle once the session is visited.",
        };
        var sound = new Option<string?>("--sound")
        {
            Description = "Play a sound when set: 'default' (or 'beep') for the system alert sound, or a "
                + "system sound name (Basso, Blow, Bottle, Frog, Funk, Glass, Hero, Morse, Ping, Pop, Purr, "
                + "Sosumi, Submarine, Tink).",
        };
        var color = new Option<string?>("--color")
        {
            Description = "Override the glyph tint for this call only (#rrggbb); reverts on the next status set without it.",
        };
        var shape = new Option<string?>("--shape")
        {
            Description = "Override the glyph silhouette for this call only: "
                + $"{StatusShapes.ValidNamesPhrase}; reverts on the next status set without it.",
        };
        var pane = new Option<string?>("--pane")
        {
            Description = "Which pane set this status: left (main), right (split), or scratch. Records the blocked pane so nav lands on it. Defaults to the left pane.",
        };
        var paneId = new Option<string?>("--pane-id")
        {
            Description = "A surface's stable token (the shell's $AGTERM_PANE_ID) — the agent-status hook "
                + "forwards it so a status set from a promoted-then-re-split pane lands on the pane's "
                + "current slot. Overrides --pane when it resolves; falls back to --pane otherwise.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(state);
        cmd.Options.Add(blink);
        cmd.Options.Add(autoReset);
        cmd.Options.Add(sound);
        cmd.Options.Add(color);
        cmd.Options.Add(shape);
        cmd.Options.Add(pane);
        cmd.Options.Add(paneId);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Status
        {
            State = result.GetValue(state)!,
            Blink = result.GetValue(blink),
            AutoReset = result.GetValue(autoReset),
            Sound = result.GetValue(sound),
            Color = result.GetValue(color),
            Shape = result.GetValue(shape),
            Pane = result.GetValue(pane),
            PaneId = result.GetValue(paneId),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    /// <summary>The per-session, per-pane restore-command override. Nested under `session`, so it is a
    /// different verb from the top-level `restore clear` in MiscCommands — that one is app-global and
    /// capture-scoped, this one is per-session and override-scoped.</summary>
    internal sealed class Restore : IRequestCommand
    {
        public string? Command { get; init; }
        public bool None { get; init; }
        public bool Clear { get; init; }
        public string? Pane { get; init; }
        public string? PaneId { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        // exactly one of the three forms; reject neither/multiple at parse time so it's a clean usage
        // error, unit-testable without a socket (the dispatcher enforces the same modes server-side).
        public void Validate()
        {
            if (new[] { Command is not null, None, Clear }.Count(set => set) != 1)
                throw new CliValidationException("provide exactly one of a COMMAND, --none, or --clear");
            ValidatePaneArgument(Pane);
        }

        public ControlRequest MakeRequest()
        {
            // Validate() guarantees the forms are exclusive, so Command is null for none/clear.
            var mode = None ? "none" : Clear ? "clear" : "set";
            return new ControlRequest
            {
                Cmd = WireCommand.SessionRestore,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs
                {
                    Mode = mode, Command = Command, Pane = Pane, PaneId = PaneId,
                }),
            };
        }
    }

    private static System.CommandLine.Command BuildRestore(CommandBindings bindings)
    {
        // the abstract plus the upstream discussion, so `--help` carries the same guidance.
        var cmd = new System.CommandLine.Command("restore",
            "Pin the command a session's pane re-runs on the next launch.\n\n"
            + "session restore \"claude --resume ID\"   pin a shell line for the next launch\n"
            + "session restore --none                 pin to nothing (the pane restores a plain shell)\n"
            + "session restore --clear                drop the override, back to auto-capture\n\n"
            + "The override is written now and consumed on the NEXT launch — it never touches the running "
            + "session. It wins over the pane's captured foreground command, is gated on the "
            + "restore-running-command setting, and reads back on `tree` as restoreCommand (main pane) or "
            + "splitRestoreCommand (split pane). It is STICKY: it fires again on every launch until cleared.\n\n"
            + "COMMAND is shell code, stored verbatim in the window's state file and readable via `tree`, so "
            + "it must not carry secrets.\n\n"
            + "Not to be confused with `agtermctl restore clear`, which is app-global and clears every "
            + "session's CAPTURED foreground command; this one is per-session and clears only the override.");
        var command = new Argument<string?>("command")
        {
            Description = "Shell line to run on the next launch (omit with --none or --clear).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var none = new Option<bool>("--none")
        {
            Description = "Pin the pane to nothing: it restores a plain shell, suppressing the captured command.",
        };
        var clear = new Option<bool>("--clear")
        {
            Description = "Drop the override so the pane goes back to restoring its captured foreground command.",
        };
        var pane = new Option<string?>("--pane")
        {
            Description = "Which pane to pin: left (main), right (split), or scratch (rejected — the scratch is never restored). Defaults to the left pane.",
        };
        var paneId = new Option<string?>("--pane-id")
        {
            Description = "A surface's stable token (the shell's $AGTERM_PANE_ID) — resolves to the pane's "
                + "CURRENT slot, so a hook in a promoted-then-re-split pane still pins the right one. Unlike "
                + "`session status`, a token that does not resolve is an error unless --pane is also given "
                + "as the fallback.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(command);
        cmd.Options.Add(none);
        cmd.Options.Add(clear);
        cmd.Options.Add(pane);
        cmd.Options.Add(paneId);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Restore
        {
            Command = result.GetValue(command),
            None = result.GetValue(none),
            Clear = result.GetValue(clear),
            Pane = result.GetValue(pane),
            PaneId = result.GetValue(paneId),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    // named `FlagCommand` upstream (not `Flag`) so it doesn't shadow ArgumentParser's `@Flag` wrapper
    // within the `Session` namespace; the name is kept 1:1 and the user-facing verb stays `flag`.
    internal sealed class FlagCommand : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate()
        {
            if (Mode is not ("on" or "off" or "toggle" or "clear"))
                throw new CliValidationException("mode must be on, off, toggle, or clear");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionFlag,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Mode = Mode }),
        };
    }

    private static System.CommandLine.Command BuildFlag(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("flag",
            "Flag a session for the flagged working-set view (on|off|toggle|clear).");
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: on, off, toggle (default), or clear (unflag all; ignores --target).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(mode);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new FlagCommand
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Seen : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionSeen,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildSeen(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("seen",
            "Clear a session's unseen-notification badge without changing the selection or focus (idempotent).");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Seen
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Search : IRequestCommand
    {
        public string? Needle { get; init; }
        public bool Next { get; init; }
        public bool Prev { get; init; }
        public bool Close { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        // the three navigation flags are mutually exclusive; reject 2+ at parse time so it's a clean
        // usage error, unit-testable without a socket. a needle alongside --close is also rejected: close
        // ignores the needle, so the combo is a usage error rather than a silent no-op.
        public void Validate()
        {
            if (new[] { Next, Prev, Close }.Count(set => set) > 1)
                throw new CliValidationException("--next, --prev, and --close are mutually exclusive");
            if (Close && Needle is not null)
                throw new CliValidationException("--close cannot be combined with a needle");
        }

        public ControlRequest MakeRequest()
        {
            var to = Next ? "next" : Prev ? "prev" : Close ? "close" : null;
            return new ControlRequest
            {
                Cmd = WireCommand.SessionSearch,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs { Text = Needle, To = to }),
            };
        }
    }

    private static System.CommandLine.Command BuildSearch(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("search",
            "Search a session's terminal output (open the bar, set a needle, or step matches).");
        var needle = new Argument<string?>("needle")
        {
            Description = "Needle to search for (omit to just open the bar).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var next = new Option<bool>("--next")
        {
            Description = "Step to the next match.",
        };
        var prev = new Option<bool>("--prev")
        {
            Description = "Step to the previous match.",
        };
        var close = new Option<bool>("--close")
        {
            Description = "Close the search bar.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(needle);
        cmd.Options.Add(next);
        cmd.Options.Add(prev);
        cmd.Options.Add(close);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Search
        {
            Needle = result.GetValue(needle),
            Next = result.GetValue(next),
            Prev = result.GetValue(prev),
            Close = result.GetValue(close),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    // MARK: - session background

    internal static class Background
    {
        /// <summary>Shared input validation against the host-free `WatermarkConfig`, so a bad value is a
        /// clean parse error before any socket round-trip, matching the server's rejection exactly. The
        /// enum checks reject `""` too, so no separate empty-string special-case is needed.</summary>
        internal static void Validate(string? fit = null, string? position = null, double? opacity = null,
                                      string? color = null, string? text = null, string? path = null)
        {
            if (fit is { } f && !WatermarkConfig.IsValidFit(f))
                throw new CliValidationException(
                    "fit must be one of: " + string.Join(", ", WatermarkConfig.ValidFits));
            if (position is { } p && !WatermarkConfig.IsValidPosition(p))
                throw new CliValidationException(
                    "position must be one of: " + string.Join(", ", WatermarkConfig.ValidPositions));
            if (opacity is { } o && !WatermarkConfig.IsValidOpacity(o))
                throw new CliValidationException("opacity must be between 0.0 and 1.0");
            if (color is { } c && !WatermarkConfig.IsValidColorHex(c))
                throw new CliValidationException("color must be a #rrggbb hex value");
            if (text is { } t && !WatermarkConfig.IsValidText(t))
                throw new CliValidationException($"text must be 1–{WatermarkConfig.MaxTextLength} characters");
            if (path is { } pa && !WatermarkConfig.IsValidImagePath(pa))
                throw new CliValidationException("image path must not contain control characters");
        }

        internal sealed class Image : IRequestCommand
        {
            public required string Path { get; init; }
            public double? Opacity { get; init; }
            public string? Fit { get; init; }
            public string? Position { get; init; }
            public bool RepeatImage { get; init; }
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            public void Validate() =>
                Background.Validate(fit: Fit, position: Position, opacity: Opacity, path: Path);

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionBackground,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs
                {
                    Mode = "image", Path = Path, Opacity = Opacity,
                    Fit = Fit, Position = Position,
                    Repeats = RepeatImage ? true : null,
                }),
            };
        }

        internal sealed class Text : IRequestCommand
        {
            public required string WatermarkText { get; init; }
            public string? Color { get; init; }
            public double? Opacity { get; init; }
            public string? Fit { get; init; }
            public string? Position { get; init; }
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            public void Validate() =>
                Background.Validate(fit: Fit, position: Position, opacity: Opacity, color: Color,
                                    text: WatermarkText);

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionBackground,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs
                {
                    Text = WatermarkText, Mode = "text", Color = Color,
                    Opacity = Opacity, Fit = Fit, Position = Position,
                }),
            };
        }

        internal sealed class Color : IRequestCommand
        {
            public required string BackgroundColor { get; init; }
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            public void Validate() => Background.Validate(color: BackgroundColor);

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionBackground,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs { Mode = "color", Color = BackgroundColor }),
            };
        }

        internal sealed class Clear : IRequestCommand
        {
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionBackground,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs { Mode = "clear" }),
            };
        }
    }

    private static System.CommandLine.Command BuildBackground(CommandBindings bindings)
    {
        var background = new System.CommandLine.Command("background",
            "Set or clear a session's background (image, rasterized text, or solid color).");
        background.Subcommands.Add(BuildBackgroundImage(bindings));
        background.Subcommands.Add(BuildBackgroundText(bindings));
        background.Subcommands.Add(BuildBackgroundColor(bindings));
        background.Subcommands.Add(BuildBackgroundClear(bindings));
        return background;
    }

    private static System.CommandLine.Command BuildBackgroundImage(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("image",
            "Show a PNG or JPEG image behind the terminal (auto-fits the window).");
        var path = new Argument<string>("path")
        {
            Description = "Path to a PNG or JPEG image file.",
        };
        var opacity = new Option<double?>("--opacity")
        {
            Description = "Image opacity 0.0-1.0 (default 1.0).",
        };
        var fit = new Option<string?>("--fit")
        {
            Description = "Fit: contain (default), cover, stretch, or none.",
        };
        var position = new Option<string?>("--position")
        {
            Description = "Position: center (default) or an edge/corner anchor (top-left, bottom-right, …).",
        };
        var repeatImage = new Option<bool>("--repeat")
        {
            Description = "Tile the image to fill blank space.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(path);
        cmd.Options.Add(opacity);
        cmd.Options.Add(fit);
        cmd.Options.Add(position);
        cmd.Options.Add(repeatImage);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Background.Image
        {
            Path = result.GetValue(path)!,
            Opacity = result.GetValue(opacity),
            Fit = result.GetValue(fit),
            Position = result.GetValue(position),
            RepeatImage = result.GetValue(repeatImage),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildBackgroundText(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("text",
            "Render TEXT as a watermark behind the terminal (auto-fits the window).");
        var text = new Argument<string>("text")
        {
            Description = "Watermark text.",
        };
        var color = new Option<string?>("--color")
        {
            Description = "Text color as #rrggbb (default: the terminal foreground color).",
        };
        var opacity = new Option<double?>("--opacity")
        {
            Description = "Opacity 0.0-1.0 (default 1.0).",
        };
        var fit = new Option<string?>("--fit")
        {
            Description = "Fit: contain (default), cover, stretch, or none.",
        };
        var position = new Option<string?>("--position")
        {
            Description = "Position: center (default) or an edge/corner anchor (top-left, bottom-right, …).",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(text);
        cmd.Options.Add(color);
        cmd.Options.Add(opacity);
        cmd.Options.Add(fit);
        cmd.Options.Add(position);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Background.Text
        {
            WatermarkText = result.GetValue(text)!,
            Color = result.GetValue(color),
            Opacity = result.GetValue(opacity),
            Fit = result.GetValue(fit),
            Position = result.GetValue(position),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildBackgroundColor(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("color",
            "Set a solid background color for the terminal (honors the Settings window translucency).");
        var color = new Argument<string>("color")
        {
            Description = "Background color as #rrggbb.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(color);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Background.Color
        {
            BackgroundColor = result.GetValue(color)!,
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildBackgroundClear(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("clear",
            "Remove the session's background (watermark or solid color).");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Background.Clear
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    // MARK: - session overlay

    internal static class Overlay
    {
        internal sealed class Open : IRequestCommand
        {
            public required string Command { get; init; }
            public string? Cwd { get; init; }
            public bool Wait { get; init; }
            public bool Block { get; init; }
            public bool Follow { get; init; }
            public int? SizePercent { get; init; }
            public string? BackgroundColor { get; init; }
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            // reject the mutually-exclusive combo + a malformed color at parse time (before any connection),
            // so it's a clean usage error and is unit-testable without a socket.
            public void Validate()
            {
                if (Block && Wait)
                    throw new CliValidationException("--block cannot be combined with --wait");
                if (BackgroundColor is { } backgroundColor && !WatermarkConfig.IsValidColorHex(backgroundColor))
                    throw new CliValidationException("background-color must be a #rrggbb hex value");
                if (SizePercent is { } sizePercent && sizePercent is < 1 or > 100)
                    throw new CliValidationException("--size-percent must be between 1 and 100");
            }

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionOverlayOpen,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs
                {
                    Cwd = Cwd, Command = Command, Wait = Wait ? true : null,
                    SizePercent = SizePercent, Follow = Follow ? true : null,
                    Color = BackgroundColor,
                }),
            };

            public int Execute(CliEnvironment env)
            {
                if (!Block) return this.DefaultRun(env);
                var pipeName = Options.PipeName(env.GetEnv);
                // open via the same `MakeRequest()` the non-block path uses (DRY): in block mode `Validate()`
                // guarantees `!Wait`, so its `wait` is null — identical to opening non-wait, and the floating
                // `--size-percent` is carried through the single source instead of a duplicated ControlArgs.
                var opened = env.Send(pipeName, MakeRequest());
                if (!opened.Ok || opened.Result?.Id is not { } id)
                {
                    ClientOutput.PrintResponse(env, opened, Options.Json);
                    return 1;
                }
                // poll session.overlay.result until the program exits. target the returned id with NO
                // window scope: the id is globally unique and resolves cross-window, so a frontmost-window
                // change during the run can't make the poll miss the session.
                while (true)
                {
                    var res = env.Send(pipeName, new ControlRequest
                    {
                        Cmd = WireCommand.SessionOverlayResult,
                        Target = id,
                    });
                    if (res.Ok)
                    {
                        if (Options.Json) ClientOutput.PrintResponse(env, res, json: true);
                        // a successful result must carry the status; its absence is a protocol violation, not success.
                        if (res.Result?.ExitCode is not { } code)
                        {
                            env.Error.WriteLine("error: result missing exit code");
                            return 1;
                        }
                        return code;
                    }
                    if (res.Error == OverlayResultError.StillRunning)
                    {
                        env.Sleep(TimeSpan.FromSeconds(0.1));
                        continue;
                    }
                    ClientOutput.PrintResponse(env, res, Options.Json);
                    return 1;
                }
            }
        }

        internal sealed class Close : IRequestCommand
        {
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionOverlayClose,
                Target = Target.Target,
                Args = Client.WithWindow(),
            };
        }

        internal sealed class Resize : IRequestCommand
        {
            public int? SizePercent { get; init; }
            public bool Full { get; init; }
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            // require exactly one of --size-percent / --full at parse time (before any connection), so it is a
            // clean usage error and unit-testable without a socket; the dispatcher re-checks the same rules.
            public void Validate()
            {
                if (Full && SizePercent is not null)
                    throw new CliValidationException("--full cannot be combined with --size-percent");
                if (!Full && SizePercent is null)
                    throw new CliValidationException("provide --size-percent PERCENT or --full");
                if (SizePercent is { } sizePercent && sizePercent is < 1 or > 100)
                    throw new CliValidationException("--size-percent must be between 1 and 100");
            }

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionOverlayResize,
                Target = Target.Target,
                Args = Client.WithWindow(new ControlArgs { SizePercent = SizePercent, Full = Full ? true : null }),
            };
        }

        internal sealed class Result : IRequestCommand
        {
            public required TargetOptions Target { get; init; }
            public required ClientOptions Client { get; init; }
            public IConnectionOptions Options => Client;

            public ControlRequest MakeRequest() => new()
            {
                Cmd = WireCommand.SessionOverlayResult,
                Target = Target.Target,
                Args = Client.WithWindow(),
            };
        }
    }

    private static System.CommandLine.Command BuildOverlay(CommandBindings bindings)
    {
        var overlay = new System.CommandLine.Command("overlay",
            "Open, resize, or close an ephemeral overlay terminal on a session.");
        overlay.Subcommands.Add(BuildOverlayOpen(bindings));
        overlay.Subcommands.Add(BuildOverlayClose(bindings));
        overlay.Subcommands.Add(BuildOverlayResize(bindings));
        overlay.Subcommands.Add(BuildOverlayResult(bindings));
        return overlay;
    }

    private static System.CommandLine.Command BuildOverlayOpen(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("open",
            "Open an overlay running COMMAND; it closes when COMMAND exits.");
        var command = new Argument<string>("command")
        {
            Description = "Program to run in the overlay (e.g. revdiff).",
        };
        var cwd = new Option<string?>("--cwd")
        {
            Description = "Working directory (default: the session's current directory).",
        };
        var wait = new Option<bool>("--wait")
        {
            Description = "Keep the overlay open after COMMAND exits (press any key to close).",
        };
        var block = new Option<bool>("--block")
        {
            Description = "Block until COMMAND exits and exit with its status (the program renders normally; capture its output via the program's own output file).",
        };
        var follow = new Option<bool>("--follow")
        {
            Description = "Select (switch to) the target session after opening the overlay (default: open without switching).",
        };
        var sizePercent = new Option<int?>("--size-percent")
        {
            Description = "Render a floating, framed panel at PERCENT (1-100) of the pane instead of full-size.",
        };
        var backgroundColor = new Option<string?>("--background-color")
        {
            Description = "Solid background color (#rrggbb) for the overlay pane, independent of the session's own.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(command);
        cmd.Options.Add(cwd);
        cmd.Options.Add(wait);
        cmd.Options.Add(block);
        cmd.Options.Add(follow);
        cmd.Options.Add(sizePercent);
        cmd.Options.Add(backgroundColor);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Overlay.Open
        {
            Command = result.GetValue(command)!,
            Cwd = result.GetValue(cwd),
            Wait = result.GetValue(wait),
            Block = result.GetValue(block),
            Follow = result.GetValue(follow),
            SizePercent = result.GetValue(sizePercent),
            BackgroundColor = result.GetValue(backgroundColor),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildOverlayClose(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("close", "Close the overlay terminal (destroys it).");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Overlay.Close
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildOverlayResize(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("resize",
            "Resize an open overlay: floating at a percent, or back to full-pane.");
        var sizePercent = new Option<int?>("--size-percent")
        {
            Description = "Resize to a floating, framed panel at PERCENT (1-100) of the pane.",
        };
        var full = new Option<bool>("--full")
        {
            Description = "Resize to full-pane (translucent, hides the session).",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Options.Add(sizePercent);
        cmd.Options.Add(full);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Overlay.Resize
        {
            SizePercent = result.GetValue(sizePercent),
            Full = result.GetValue(full),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildOverlayResult(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("result",
            "Print the overlay program's exit status (errors if it is still running or never ran).");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Overlay.Result
        {
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }
}
