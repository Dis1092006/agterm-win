using System.CommandLine;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit WindowCommands.swift — the window group. Unlike the session/workspace
// commands there is no --window selector: each subcommand targets via its positional id
// (default "active"), mirroring the Swift @Argument defaults exactly.

internal static class WindowCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var window = new System.CommandLine.Command("window", "Window commands.");
        window.Subcommands.Add(BuildNew(bindings));
        window.Subcommands.Add(BuildList(bindings));
        window.Subcommands.Add(BuildSelect(bindings));
        window.Subcommands.Add(BuildClose(bindings));
        window.Subcommands.Add(BuildRename(bindings));
        window.Subcommands.Add(BuildDelete(bindings));
        window.Subcommands.Add(BuildResize(bindings));
        window.Subcommands.Add(BuildMove(bindings));
        window.Subcommands.Add(BuildZoom(bindings));
        window.Subcommands.Add(BuildFullscreen(bindings));
        return window;
    }

    /// <summary>The positional window selector shared by the id-targeting subcommands — the port of
    /// the Swift `@Argument var id: String = "active"`.</summary>
    private static Argument<string> OptionalIdArgument() => new("id")
    {
        Description = "Window id, unique prefix, or 'active'.",
        Arity = ArgumentArity.ZeroOrOne,
        DefaultValueFactory = _ => "active",
    };

    internal sealed class New : IRequestCommand
    {
        public string? Name { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;
        public bool EchoesResultId => true;

        // The Swift always builds ControlArgs(name:) here, so args encodes even when name is nil.
        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WindowNew,
            Args = new ControlArgs { Name = Name },
        };
    }

    private static System.CommandLine.Command BuildNew(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("new", "Create and open a window.");
        var name = new Argument<string?>("name")
        {
            Description = "Window name (defaults to the auto-generated name).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(name);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new New
        {
            Name = result.GetValue(name),
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class List : IRequestCommand
    {
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.WindowList };
    }

    private static System.CommandLine.Command BuildList(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("list", "List windows (id, name, open, active).");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new List { Basic = options.Read(result) });
        return cmd;
    }

    internal sealed class Select : IRequestCommand
    {
        public string Id { get; init; } = "active";
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.WindowSelect, Target = Id };
    }

    private static System.CommandLine.Command BuildSelect(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("select", "Select (raise or open) a window.");
        var id = OptionalIdArgument();
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Select
        {
            Id = result.GetValue(id) ?? "active",
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Close : IRequestCommand
    {
        public string Id { get; init; } = "active";
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.WindowClose, Target = Id };
    }

    private static System.CommandLine.Command BuildClose(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("close", "Close a window (its bundle is kept).");
        var id = OptionalIdArgument();
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Close
        {
            Id = result.GetValue(id) ?? "active",
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Rename : IRequestCommand
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WindowRename,
            Target = Id,
            Args = new ControlArgs { Name = Name },
        };
    }

    private static System.CommandLine.Command BuildRename(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("rename", "Rename a window.");
        var id = new Argument<string>("id") { Description = "Window id, unique prefix, or 'active'." };
        var name = new Argument<string>("name") { Description = "New window name." };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        cmd.Arguments.Add(name);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Rename
        {
            Id = result.GetValue(id)!,
            Name = result.GetValue(name)!,
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Delete : IRequestCommand
    {
        public string Id { get; init; } = "active";
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.WindowDelete, Target = Id };
    }

    private static System.CommandLine.Command BuildDelete(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("delete", "Delete a window (keeps at least one).");
        var id = OptionalIdArgument();
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Delete
        {
            Id = result.GetValue(id) ?? "active",
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Resize : IRequestCommand
    {
        public string Id { get; init; } = "active";
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WindowResize,
            Target = Id,
            Args = new ControlArgs { Width = Width, Height = Height },
        };
    }

    private static System.CommandLine.Command BuildResize(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("resize", "Resize a window (frame size in points).");
        var id = OptionalIdArgument();
        var width = new Option<int>("--width")
        {
            Description = "New width in points.",
            Required = true,
        };
        var height = new Option<int>("--height")
        {
            Description = "New height in points.",
            Required = true,
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        cmd.Options.Add(width);
        cmd.Options.Add(height);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Resize
        {
            Id = result.GetValue(id) ?? "active",
            Width = result.GetValue(width),
            Height = result.GetValue(height),
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Move : IRequestCommand
    {
        public string Id { get; init; } = "active";
        public required int X { get; init; }
        public required int Y { get; init; }
        public int? Display { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.WindowMove,
            Target = Id,
            Args = new ControlArgs { X = X, Y = Y, Display = Display },
        };
    }

    private static System.CommandLine.Command BuildMove(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("move",
            "Move a window (top-left x,y in points, relative to a display).");
        var id = OptionalIdArgument();
        var x = new Option<int>("--x")
        {
            Description = "Left edge x in points, from the display's left.",
            Required = true,
        };
        var y = new Option<int>("--y")
        {
            Description = "Top edge y in points, from the display's top.",
            Required = true,
        };
        var display = new Option<int?>("--display")
        {
            Description = "Display index (default: the window's current display).",
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        cmd.Options.Add(x);
        cmd.Options.Add(y);
        cmd.Options.Add(display);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Move
        {
            Id = result.GetValue(id) ?? "active",
            X = result.GetValue(x),
            Y = result.GetValue(y),
            Display = result.GetValue(display),
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Zoom : IRequestCommand
    {
        public string Id { get; init; } = "active";
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.WindowZoom, Target = Id };
    }

    private static System.CommandLine.Command BuildZoom(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("zoom", "Zoom a window (maximize-to-screen toggle).");
        var id = OptionalIdArgument();
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Zoom
        {
            Id = result.GetValue(id) ?? "active",
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Fullscreen : IRequestCommand
    {
        public string Id { get; init; } = "active";
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.WindowFullscreen, Target = Id };
    }

    private static System.CommandLine.Command BuildFullscreen(CommandBindings bindings)
    {
        // Upstream help says "native macOS full screen"; the Windows analogue is documented in
        // PLATFORM-NOTES. The wire command and semantics are unchanged.
        var cmd = new System.CommandLine.Command("fullscreen", "Toggle native full screen for a window.");
        var id = OptionalIdArgument();
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(id);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Fullscreen
        {
            Id = result.GetValue(id) ?? "active",
            Basic = options.Read(result),
        });
        return cmd;
    }
}
