using System.CommandLine;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit MiscCommands.swift — the keymap, config, restore, theme, quick, surface,
// dashboard, sidebar, notify, and font command groups. Groups appear in the upstream file order;
// each Swift `defaultSubcommand` is mirrored by binding the group command itself to the default
// subcommand's shape, so the bare verb keeps working (`agtermctl quick show` == `quick visibility show`).

// MARK: - keymap

internal static class KeymapCommands
{
    internal sealed class Reload : IRequestCommand
    {
        // keymap.reload is app-global (the frontmost window's settings model), so no `--window` selector.
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.KeymapReload };
    }

    internal sealed class List : IRequestCommand
    {
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.KeymapList };
    }

    internal sealed class Run : IRequestCommand
    {
        public required string Name { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.KeymapRun,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Name = Name }),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var keymap = new System.CommandLine.Command("keymap", "Keymap commands.");
        keymap.Subcommands.Add(BuildReload(bindings));
        keymap.Subcommands.Add(BuildList(bindings));
        keymap.Subcommands.Add(BuildRun(bindings));
        return keymap;
    }

    private static System.CommandLine.Command BuildReload(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("reload",
            "Re-read and apply keymap.conf (prints the diagnostic count).");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Reload { Basic = options.Read(result) });
        return cmd;
    }

    private static System.CommandLine.Command BuildList(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("list",
            "Print the resolved keymap: every action's chord, the custom commands and the diagnostics.");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new List { Basic = options.Read(result) });
        return cmd;
    }

    private static System.CommandLine.Command BuildRun(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("run",
            "Run one of your custom commands by name against a session.\n\n"
            + "NAME is a custom command's name exactly as `keymap list` prints it. The command runs as it does "
            + "from the custom command palette, with the target session's focused pane, primary or split and "
            + "never its scratch or an overlay, supplying the working directory, the selection and the context "
            + "tokens. A name no command carries is refused.\n\n"
            + "The reply says the command started. Its exit status and output are not reported.");
        var name = new Argument<string>("name")
        {
            Description = "Custom command name, from `keymap list`.",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(name);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Run
        {
            Name = result.GetValue(name)!,
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }
}

// MARK: - config

internal static class ConfigCommands
{
    internal sealed class Reload : IRequestCommand
    {
        // config.reload is app-global (one settings model), so no `--window` selector.
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.ConfigReload };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var config = new System.CommandLine.Command("config", "Config commands.");
        config.Subcommands.Add(BuildReload(bindings));
        return config;
    }

    private static System.CommandLine.Command BuildReload(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("reload",
            "Re-read settings.json and apply it to every pane (prints ok).");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Reload { Basic = options.Read(result) });
        return cmd;
    }
}

// MARK: - restore

internal static class RestoreCommands
{
    // Upstream discussion (kept out of the one-line help):
    //
    //   This is app-global and CAPTURE-scoped: it drops the foreground commands agterm captured at
    //   quit, across every open window, and leaves per-session restore-command overrides alone.
    //
    //   Not to be confused with `agtermctl session restore --clear`, which is per-session and
    //   OVERRIDE-scoped: it drops one pane's pinned command so that pane goes back to auto-capture.
    internal sealed class Clear : IRequestCommand
    {
        // restore.clear is app-global (clears every open window), so no `--window` selector.
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.RestoreClear };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var restore = new System.CommandLine.Command("restore", "Restore-running-command commands.");
        restore.Subcommands.Add(BuildClear(bindings));
        return restore;
    }

    private static System.CommandLine.Command BuildClear(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("clear",
            "Clear every session's saved foreground command so the next restart restores plain shells.");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Clear { Basic = options.Read(result) });
        return cmd;
    }
}

// MARK: - theme

internal static class ThemeCommands
{
    // Upstream discussion (kept out of the one-line help):
    //
    //   theme set NAME            set the light/single theme (a dark theme, if set, is kept)
    //   theme set --dark NAME     set the dark theme — the terminal then tracks Windows
    //   light/dark appearance (the light side seeds from the current theme)
    //   theme set --dark none     clear the dark theme (stop tracking the appearance)
    //   theme set                 built-in terminal colors (clears everything)
    internal sealed class Set : IRequestCommand
    {
        public string? Name { get; init; }
        public string? Light { get; init; }
        public string? Dark { get; init; }
        // theme is app-global (one settings model), so no `--window` selector.
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public void Validate()
        {
            if (Name is not null && Light is not null)
                throw new CliValidationException("Pass either a NAME or --light, not both.");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.ThemeSet,
            Args = new ControlArgs { Name = Name, Light = Light, Dark = Dark },
        };
    }

    internal sealed class List : IRequestCommand
    {
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.ThemeList };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var theme = new System.CommandLine.Command("theme", "Theme commands.");
        theme.Subcommands.Add(BuildSet(bindings));
        theme.Subcommands.Add(BuildList(bindings));
        return theme;
    }

    private static System.CommandLine.Command BuildSet(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("set", "Set + persist the terminal theme, per slot.");
        var name = new Argument<string?>("name")
        {
            Description = "Light/single theme name (a bundled theme); omit for built-in terminal colors.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var light = new Option<string?>("--light")
        {
            Description = "Light-appearance theme (same slot as NAME).",
        };
        var dark = new Option<string?>("--dark")
        {
            Description = "Dark-appearance theme, or 'none' to clear it.",
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(name);
        cmd.Options.Add(light);
        cmd.Options.Add(dark);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Set
        {
            Name = result.GetValue(name),
            Light = result.GetValue(light),
            Dark = result.GetValue(dark),
            Basic = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildList(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("list", "List bundled themes (the current one marked).");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new List { Basic = options.Read(result) });
        return cmd;
    }
}

// MARK: - quick

internal static class QuickCommands
{
    /// <summary>`agtermctl quick [show|hide|toggle]` — the default, so the bare verb keeps working.
    /// Shows/hides the frontmost window's quick terminal.</summary>
    internal sealed class Visibility : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        // the quick terminal is always the frontmost window's, so this carries no `--window` selector.
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.Quick,
            Args = new ControlArgs { Mode = Mode },
        };
    }

    /// <summary>`agtermctl quick type TEXT` — inject literal keystrokes into the frontmost window's
    /// quick terminal (the quick-terminal twin of `session type`). No `--target`/`--window`: it's
    /// always the frontmost window's quick terminal.</summary>
    internal sealed class TypeText : IRequestCommand
    {
        public string? Text { get; init; }
        public bool Stdin { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        /// <summary>Where --stdin reads from; injectable for tests. Mirrors the Swift, which reads
        /// stdin inside makeRequest (not validate), so the read happens only at request-build time.</summary>
        public Func<Stream> StdinStream { get; init; } = Console.OpenStandardInput;

        public ControlRequest MakeRequest() =>
            MakeRequest(Stdin ? TypedStdin.ReadAll(StdinStream()) : null);

        internal ControlRequest MakeRequest(byte[]? stdin)
        {
            string payload;
            if (Stdin)
                payload = TypedStdin.Decode(stdin ?? Array.Empty<byte>());
            else if (Text is not null)
                payload = Text;
            else
                throw new CliValidationException("provide TEXT or --stdin");
            return new ControlRequest
            {
                Cmd = WireCommand.QuickType,
                Args = new ControlArgs { Text = payload },
            };
        }
    }

    /// <summary>`agtermctl quick text` — print the frontmost window's quick-terminal buffer as plain
    /// text (the read-back for `quick type`; does not touch the system clipboard). No `--pane`: the
    /// quick terminal has a single surface.</summary>
    internal sealed class Text : IRequestCommand
    {
        public bool All { get; init; }
        public int? Lines { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public void Validate()
        {
            if (All && Lines is not null)
                throw new CliValidationException("use either --all or --lines, not both");
            if (Lines is { } lines && lines <= 0)
                throw new CliValidationException("--lines must be greater than 0");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.QuickText,
            Args = new ControlArgs { All = All ? true : null, Lines = Lines },
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var quick = new System.CommandLine.Command("quick",
            "Quick terminal: visibility, type into it, read its text.");
        var visibility = new System.CommandLine.Command("visibility",
            "Quick terminal visibility (show|hide|toggle).");
        BindVisibility(bindings, visibility);
        quick.Subcommands.Add(visibility);
        quick.Subcommands.Add(BuildType(bindings));
        quick.Subcommands.Add(BuildText(bindings));
        // defaultSubcommand: Visibility — the bare `quick [show|hide|toggle]` binds like
        // `quick visibility …` (a token matching a subcommand name still routes to that subcommand).
        BindVisibility(bindings, quick);
        return quick;
    }

    private static void BindVisibility(CommandBindings bindings, System.CommandLine.Command cmd)
    {
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: show, hide, or toggle (default).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(mode);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Visibility
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Basic = options.Read(result),
        });
    }

    private static System.CommandLine.Command BuildType(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("type", "Inject text into the quick terminal.");
        var text = new Argument<string?>("text")
        {
            Description = "Text to inject (omit with --stdin).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var stdin = new Option<bool>("--stdin")
        {
            Description = "Read the text from stdin instead of an argument.",
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(text);
        cmd.Options.Add(stdin);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new TypeText
        {
            Text = result.GetValue(text),
            Stdin = result.GetValue(stdin),
            Basic = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildText(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("text",
            "Print the quick terminal's buffer as plain text.");
        var all = new Option<bool>("--all")
        {
            Description = "Read the full screen + scrollback instead of just the visible screen.",
        };
        var lines = new Option<int?>("--lines")
        {
            Description = "Keep only the last N lines of the full buffer.",
        };
        var options = new BasicOptions.Binder();
        cmd.Options.Add(all);
        cmd.Options.Add(lines);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Text
        {
            All = result.GetValue(all),
            Lines = result.GetValue(lines),
            Basic = options.Read(result),
        });
        return cmd;
    }
}

// MARK: - surface

internal static class SurfaceCommands
{
    internal sealed class Zoom : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        public required SurfaceTargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SurfaceZoom,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Mode = Mode }),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var surface = new System.CommandLine.Command("surface", "Terminal surface commands.");
        surface.Subcommands.Add(BuildZoom(bindings));
        return surface;
    }

    private static System.CommandLine.Command BuildZoom(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("zoom", "Zoom a terminal surface (show|hide|toggle).");
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: show, hide, or toggle (default).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var target = new SurfaceTargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(mode);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Zoom
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }
}

// MARK: - dashboard

internal static class DashboardCommand
{
    /// <summary>`agtermctl dashboard <ids…> [--font-size N | --auto-size] [--window W]` opens a
    /// view-only grid of the named sessions (max 9); `agtermctl dashboard --mru [--font-size N |
    /// --auto-size] [--window W]` opens one of the window's most-recently-used sessions (up to 9)
    /// instead of naming ids; `agtermctl dashboard --close [--window W]` closes the open one. The
    /// positional ids map to `ControlArgs.targets`; the dispatcher caps them at 9, dedups, and
    /// reports any drop. The CLI re-checks the flag combinations `validate()`-style so a bad
    /// invocation is a clean usage error without a socket round-trip (the dispatcher enforces the
    /// same rules server-side).</summary>
    //
    // Upstream discussion (kept out of the one-line help):
    //
    //   dashboard S1 S2 S3                 open a grid of the named sessions (ids or unique prefixes, max 9)
    //   dashboard S1 S2 --font-size 12     open with an absolute cell font size (points)
    //   dashboard S1 S2 --auto-size        open sizing cells relative to the Settings default font
    //   dashboard --mru                    open a grid of the window's most-recently-used sessions (up to 9)
    //   dashboard --mru --auto-size        the same, sizing cells relative to the Settings default font
    //   dashboard S1 --window W            open in a specific window (defaults to the frontmost)
    //   dashboard --close                  close the open dashboard
    internal sealed class Dashboard : IRequestCommand
    {
        public IReadOnlyList<string> Ids { get; init; } = Array.Empty<string>();
        public double? FontSize { get; init; }
        public bool AutoSize { get; init; }
        public bool Mru { get; init; }
        public bool Close { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        // reject the invalid flag combinations at parse time (before any connection) so they are
        // clean usage errors, unit-testable without a socket; the dispatcher re-checks the same
        // rules server-side.
        public void Validate()
        {
            if (Close)
            {
                if (Ids.Count != 0 || Mru || FontSize is not null || AutoSize)
                    throw new CliValidationException("--close takes no ids, --mru, or font options");
                return;
            }
            if (Mru && Ids.Count != 0)
                throw new CliValidationException("--mru cannot be combined with session ids");
            // an open needs explicit ids OR --mru (which supplies them from the window's recency).
            if (Ids.Count == 0 && !Mru)
                throw new CliValidationException(
                    "dashboard requires at least one session id (or --mru, or --close)");
            if (FontSize is not null && AutoSize)
                throw new CliValidationException("--font-size is mutually exclusive with --auto-size");
            // nan/inf parse as Double but aren't a valid size; reject non-finite/non-positive here
            // with a clean error.
            if (FontSize is { } fontSize && (!double.IsFinite(fontSize) || fontSize <= 0))
                throw new CliValidationException("--font-size must be a positive number");
        }

        public ControlRequest MakeRequest()
        {
            var args = new ControlArgs
            {
                Targets = Ids.Count == 0 ? null : Ids.ToArray(),
                Close = Close ? true : null,
                FontSize = FontSize,
                AutoSize = AutoSize ? true : null,
                Mru = Mru ? true : null,
            };
            return new ControlRequest { Cmd = WireCommand.Dashboard, Args = Client.WithWindow(args) };
        }
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("dashboard",
            "Open a view-only grid of live sessions, or --close the open one.");
        var ids = new Argument<string[]>("ids")
        {
            Description = "Session ids (or unique prefixes) to show, max 9. Omit only with --mru or --close.",
            Arity = ArgumentArity.ZeroOrMore,
            DefaultValueFactory = _ => Array.Empty<string>(),
        };
        var fontSize = CliOptions.LenientDouble("--font-size",
            "Absolute cell font size in points (mutually exclusive with --auto-size).");
        var autoSize = new Option<bool>("--auto-size")
        {
            Description = "Size cells relative to the Settings default font, shrinking as the grid grows.",
        };
        var mru = new Option<bool>("--mru")
        {
            Description = "Populate the grid from the window's most-recently-used sessions (up to 9).",
        };
        var close = new Option<bool>("--close")
        {
            Description = "Close the open dashboard (takes no ids, --mru, or font options).",
        };
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(ids);
        cmd.Options.Add(fontSize);
        cmd.Options.Add(autoSize);
        cmd.Options.Add(mru);
        cmd.Options.Add(close);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Dashboard
        {
            Ids = result.GetValue(ids) ?? Array.Empty<string>(),
            FontSize = result.GetValue(fontSize),
            AutoSize = result.GetValue(autoSize),
            Mru = result.GetValue(mru),
            Close = result.GetValue(close),
            Client = options.Read(result),
        });
        return cmd;
    }
}

// MARK: - sidebar

internal static class SidebarCommands
{
    /// <summary>`agtermctl sidebar [show|hide|toggle]` — the default, so the bare verb keeps
    /// working. Toggles the frontmost window's sidebar visibility.</summary>
    internal sealed class Visibility : IRequestCommand
    {
        public string Mode { get; init; } = "toggle";
        // the sidebar is always the frontmost window's, so this carries no `--window` selector.
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.Sidebar,
            Args = new ControlArgs { Mode = Mode },
        };
    }

    /// <summary>`agtermctl sidebar mode [tree|flagged|toggle]` — flips the frontmost window's
    /// sidebar view between the workspace tree and the flat flagged working-set list.</summary>
    internal sealed class Mode : IRequestCommand
    {
        public string ViewMode { get; init; } = "toggle";
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public void Validate()
        {
            if (ViewMode is not ("tree" or "flagged" or "toggle"))
                throw new CliValidationException("mode must be tree, flagged, or toggle");
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SidebarMode,
            Args = new ControlArgs { Mode = ViewMode },
        };
    }

    /// <summary>`agtermctl sidebar expand [--window W]` — expand every workspace in a window's
    /// sidebar tree (defaults to the frontmost). Unlike `visibility`/`mode`, this carries the
    /// `--window` selector so a script can expand a background window's tree.</summary>
    internal sealed class Expand : IRequestCommand
    {
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SidebarExpand,
            Args = Client.WithWindow(),
        };
    }

    /// <summary>`agtermctl sidebar collapse [--window W]` — collapse every workspace except the
    /// active one (it stays expanded) in a window's sidebar (defaults to the frontmost).</summary>
    internal sealed class Collapse : IRequestCommand
    {
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SidebarCollapse,
            Args = Client.WithWindow(),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var sidebar = new System.CommandLine.Command("sidebar", "Sidebar visibility and view mode.");
        var visibility = new System.CommandLine.Command("visibility",
            "Sidebar visibility (show|hide|toggle).");
        BindVisibility(bindings, visibility);
        sidebar.Subcommands.Add(visibility);
        sidebar.Subcommands.Add(BuildMode(bindings));
        sidebar.Subcommands.Add(BuildExpand(bindings));
        sidebar.Subcommands.Add(BuildCollapse(bindings));
        // defaultSubcommand: Visibility — the bare `sidebar [show|hide|toggle]` binds like
        // `sidebar visibility …` (a token matching a subcommand name still routes to that subcommand).
        BindVisibility(bindings, sidebar);
        return sidebar;
    }

    private static void BindVisibility(CommandBindings bindings, System.CommandLine.Command cmd)
    {
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: show, hide, or toggle (default).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(mode);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Visibility
        {
            Mode = result.GetValue(mode) ?? "toggle",
            Basic = options.Read(result),
        });
    }

    private static System.CommandLine.Command BuildMode(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("mode", "Sidebar view mode (tree|flagged|toggle).");
        var mode = new Argument<string>("mode")
        {
            Description = "Mode: tree, flagged, or toggle (default).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "toggle",
        };
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(mode);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Mode
        {
            ViewMode = result.GetValue(mode) ?? "toggle",
            Basic = options.Read(result),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildExpand(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("expand", "Expand every workspace in the sidebar.");
        var options = new ClientOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Expand { Client = options.Read(result) });
        return cmd;
    }

    private static System.CommandLine.Command BuildCollapse(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("collapse",
            "Collapse all workspaces except the active one.");
        var options = new ClientOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Collapse { Client = options.Read(result) });
        return cmd;
    }
}

// MARK: - notify

internal static class NotifyCommand
{
    internal sealed class Notify : IRequestCommand
    {
        public required string Body { get; init; }
        public string? Title { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.Notify,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs { Title = Title, Body = Body }),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("notify",
            "Post a desktop notification (default: the active session of the frontmost window).");
        var body = new Argument<string>("body") { Description = "Notification body." };
        var title = new Option<string?>("--title")
        {
            Description = "Notification title (defaults to the session name).",
        };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(body);
        cmd.Options.Add(title);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Notify
        {
            Body = result.GetValue(body)!,
            Title = result.GetValue(title),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }
}

// MARK: - font

internal static class FontCommands
{
    /// <summary>Help text for the shared `--pane` option on the font subcommands. Reuses the
    /// `left|right|scratch` vocabulary of `session type`/`session text`; omitted defaults to the
    /// main pane.</summary>
    private const string PaneHelp = "Which pane's font to change: left (main), right (split), or scratch (the "
        + "session's scratch terminal, even when hidden). Defaults to the left pane.";

    /// <summary>Port of the file-scoped validatePaneArgument in SessionCommands.swift (a private
    /// copy here so this file stays self-contained): a present --pane must parse as a StatusPane.</summary>
    private static void ValidatePaneArgument(string? pane)
    {
        if (pane is not null && StatusPanes.FromWire(pane) is null)
            throw new CliValidationException("--pane must be left, right, or scratch");
    }

    internal sealed class Inc : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public string? Pane { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate() => ValidatePaneArgument(Pane);

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.FontInc,
            Target = Target.Target,
            Args = Client.WithWindow(Pane is null ? null : new ControlArgs { Pane = Pane }),
        };
    }

    internal sealed class Dec : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public string? Pane { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate() => ValidatePaneArgument(Pane);

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.FontDec,
            Target = Target.Target,
            Args = Client.WithWindow(Pane is null ? null : new ControlArgs { Pane = Pane }),
        };
    }

    internal sealed class Reset : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public string? Pane { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate() => ValidatePaneArgument(Pane);

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.FontReset,
            Target = Target.Target,
            Args = Client.WithWindow(Pane is null ? null : new ControlArgs { Pane = Pane }),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var font = new System.CommandLine.Command("font", "Font size commands.");
        font.Subcommands.Add(BuildInc(bindings));
        font.Subcommands.Add(BuildDec(bindings));
        font.Subcommands.Add(BuildReset(bindings));
        return font;
    }

    private static System.CommandLine.Command BuildInc(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("inc", "Increase font size.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        var pane = new Option<string?>("--pane") { Description = PaneHelp };
        target.AddTo(cmd);
        options.AddTo(cmd);
        cmd.Options.Add(pane);
        bindings.Register(cmd, result => new Inc
        {
            Target = target.Read(result),
            Client = options.Read(result),
            Pane = result.GetValue(pane),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildDec(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("dec", "Decrease font size.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        var pane = new Option<string?>("--pane") { Description = PaneHelp };
        target.AddTo(cmd);
        options.AddTo(cmd);
        cmd.Options.Add(pane);
        bindings.Register(cmd, result => new Dec
        {
            Target = target.Read(result),
            Client = options.Read(result),
            Pane = result.GetValue(pane),
        });
        return cmd;
    }

    private static System.CommandLine.Command BuildReset(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("reset", "Reset font size.");
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        var pane = new Option<string?>("--pane") { Description = PaneHelp };
        target.AddTo(cmd);
        options.AddTo(cmd);
        cmd.Options.Add(pane);
        bindings.Register(cmd, result => new Reset
        {
            Target = target.Read(result),
            Client = options.Read(result),
            Pane = result.GetValue(pane),
        });
        return cmd;
    }
}
