using System.CommandLine;
using System.Globalization;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

internal static class AskRunner
{
    /// <summary>A No button is still an answer, so it exits 0; escaped and cancelled differ so a
    /// script can tell the user's dismissal from an administrative cancel.</summary>
    public static int ExitCode(ControlAskOutcome outcome) => outcome switch
    {
        ControlAskOutcome.Answered => 0,
        ControlAskOutcome.Cancelled => 2,
        ControlAskOutcome.Escaped => 3,
        _ => 1,
    };

    public static int RunOpen(CliEnvironment env, IConnectionOptions options, ControlRequest open, bool noBlock) =>
        ModalRunner.RunOpen(env, options, ModalFamilies.Ask, open, noBlock);

    public static int PrintResult(CliEnvironment env, IConnectionOptions options, ControlRequest request) =>
        ModalRunner.PrintResult(env, options, ModalFamilies.Ask, request);
}

internal static class AskCommands
{
    /// <summary>`agtermctl ask [open] TITLE --button ...`: ask a question and wait for its button answer.</summary>
    internal sealed class Open : IRequestCommand
    {
        public required string Title { get; init; }
        public string? Message { get; init; }
        public IReadOnlyList<string> Buttons { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Hotkeys { get; init; } = Array.Empty<string>();
        public string? Default { get; init; }
        public string? Destructive { get; init; }
        public string Style { get; init; } = "terminal";
        public string Align { get; init; } = "right";
        public string? Width { get; init; }
        public string? Target { get; init; }
        public string? Pane { get; init; }
        public string? PaneId { get; init; }
        public bool Follow { get; init; }
        public bool NoBlock { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Title))
                throw new CliValidationException("ask.open requires a title");
            if (Style == "gui" && (Pane is not null || PaneId is not null) && Target is null)
                throw new CliValidationException("--pane requires a session");
            if (Pane is not null && OverlayPanes.FromControlName(Pane) is null)
                throw new CliValidationException("--pane must be left or right");
            if (ControlAskStyles.FromWire(Style) is null)
                throw new CliValidationException("unknown style");
            if (ControlAskAlignments.FromWire(Align) is null)
                throw new CliValidationException("unknown align");
            _ = ParsedWidth();
            _ = ParsedButtons();
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.AskOpen,
            Target = Target,
            Args = Client.WithWindow(new ControlArgs
            {
                Follow = Follow ? true : null,
                Message = Message,
                Buttons = ParsedButtons(),
                DefaultButton = Default,
                Style = Style,
                Align = Align,
                DestructiveButton = Destructive,
                Pane = Pane,
                PaneId = PaneId,
                Title = Title,
                Width = ParsedWidth(),
            }),
        };

        public int Execute(CliEnvironment env) => AskRunner.RunOpen(env, Options, MakeRequest(), NoBlock);

        /// <summary>Read as text so a non-number gets upstream's message, not a parser error.</summary>
        private int? ParsedWidth()
        {
            if (Width is null) return null;
            if (!int.TryParse(Width, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
                || value < 10 || value > 100)
                throw new CliValidationException("width must be 10 to 100");
            return value;
        }

        private ControlAskButton[] ParsedButtons()
        {
            if (Buttons.Count == 0)
                throw new CliValidationException("ask requires at least one --button");
            var choices = Buttons.Select(ParseButton).ToArray();
            var assigned = new HashSet<int>();
            foreach (var token in Hotkeys)
            {
                var equals = token.IndexOf('=');
                if (equals < 0)
                    throw new CliValidationException("--hotkey requires ID=LETTER");
                var id = token[..equals];
                var index = Array.FindIndex(choices, choice => AskIds.Same(choice.Id, id));
                if (index < 0)
                    throw new CliValidationException($"unknown hotkey button: {id}");
                if (!assigned.Add(index))
                    throw new CliValidationException($"hotkey already assigned to button: {id}");
                choices[index] = choices[index] with { Hotkey = token[(equals + 1)..] };
            }
            return choices;
        }

        private static ControlAskButton ParseButton(string token)
        {
            var equals = token.IndexOf('=');
            return equals < 0
                ? new ControlAskButton { Id = token, Label = token }
                : new ControlAskButton { Id = token[..equals], Label = token[(equals + 1)..] };
        }
    }

    /// <summary>`agtermctl ask result ID`: print a dialog's current or final result.</summary>
    internal sealed class Result : IRequestCommand
    {
        public required string Id { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.AskResult, Target = Id, Args = Client.WithWindow() };

        public int Execute(CliEnvironment env) => AskRunner.PrintResult(env, Options, MakeRequest());
    }

    /// <summary>`agtermctl ask cancel ID`: close a pending dialog as cancelled.</summary>
    internal sealed class Cancel : IRequestCommand
    {
        public required string Id { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.AskCancel, Target = Id, Args = Client.WithWindow() };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var ask = new System.CommandLine.Command("ask",
            "Open, poll, or cancel a themed question dialog. Use ask open TITLE when the title is 'open', 'result', or 'cancel'.");
        var open = new System.CommandLine.Command("open", "Ask a question and wait for its button answer.");
        BindOpen(bindings, open);
        ask.Subcommands.Add(open);
        ask.Subcommands.Add(BuildIdCommand(bindings, "result", "Print a dialog's current or terminal result as JSON.",
            (id, client) => new Result { Id = id, Client = client }));
        ask.Subcommands.Add(BuildIdCommand(bindings, "cancel", "Cancel a dialog without answering a button.",
            (id, client) => new Cancel { Id = id, Client = client }));
        // defaultSubcommand: Open, so `agtermctl ask TITLE ...` opens a dialog
        BindOpen(bindings, ask);
        return ask;
    }

    private static void BindOpen(CommandBindings bindings, System.CommandLine.Command command)
    {
        var title = new Argument<string>("title") { Description = "Dialog title." };
        var message = new Option<string?>("--message") { Description = "Optional message below the title." };
        var buttons = new Option<string[]>("--button")
        {
            Description = "Button ID=LABEL, or one token for both. Repeat for each button (max 6).",
            DefaultValueFactory = _ => Array.Empty<string>(),
        };
        var hotkeys = new Option<string[]>("--hotkey")
        {
            Description = "Button ID=LETTER. Repeat for more buttons.",
            DefaultValueFactory = _ => Array.Empty<string>(),
        };
        var defaultButton = new Option<string?>("--default") { Description = "ID of the initially highlighted button." };
        var style = new Option<string>("--style") { Description = "Dialog style: terminal or gui.", DefaultValueFactory = _ => "terminal" };
        var align = new Option<string>("--align") { Description = "Button block alignment: left, center, or right.", DefaultValueFactory = _ => "right" };
        var width = new Option<string?>("--width") { Description = "Panel width as an integer percent of the anchor (10...100); omit for automatic sizing." };
        var destructive = new Option<string?>("--destructive") { Description = "ID of the destructive button." };
        var target = new Option<string?>("--target") { Description = "Session id, prefix, or 'active'. Default: selected session for terminal, window-centered for gui." };
        var pane = new Option<string?>("--pane") { Description = "Anchor to the session's left or right pane. GUI style requires --target." };
        var paneId = new Option<string?>("--pane-id") { Description = "Stable pane token; overrides --pane. GUI style requires --target." };
        var follow = new Option<bool>("--follow") { Description = "Raise the owning window." };
        var noBlock = new Option<bool>("--no-block") { Description = "Print the dialog id and return without waiting." };
        var client = new ClientOptions.Binder();
        client.AddTo(command);
        command.Arguments.Add(title);
        foreach (var option in new Option[] { message, buttons, hotkeys, defaultButton, style, align, width, destructive, target, pane, paneId, follow, noBlock })
            command.Options.Add(option);
        bindings.Register(command, result => new Open
        {
            Title = result.GetValue(title)!,
            Message = result.GetValue(message),
            Buttons = result.GetValue(buttons) ?? Array.Empty<string>(),
            Hotkeys = result.GetValue(hotkeys) ?? Array.Empty<string>(),
            Default = result.GetValue(defaultButton),
            Style = result.GetValue(style) ?? "terminal",
            Align = result.GetValue(align) ?? "right",
            Width = result.GetValue(width),
            Destructive = result.GetValue(destructive),
            Target = result.GetValue(target),
            Pane = result.GetValue(pane),
            PaneId = result.GetValue(paneId),
            Follow = result.GetValue(follow),
            NoBlock = result.GetValue(noBlock),
            Client = client.Read(result),
        });
    }

    private static System.CommandLine.Command BuildIdCommand(
        CommandBindings bindings, string name, string description,
        Func<string, ClientOptions, IRequestCommand> create)
    {
        var command = new System.CommandLine.Command(name, description);
        var id = new Argument<string>("id") { Description = "Exact dialog id returned by ask open." };
        command.Arguments.Add(id);
        var client = new ClientOptions.Binder();
        client.AddTo(command);
        bindings.Register(command, result => create(result.GetValue(id)!, client.Read(result)));
        return command;
    }
}
