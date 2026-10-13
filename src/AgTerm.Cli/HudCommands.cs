using System.CommandLine;
using System.Text;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit SessionHudCommands.swift (upstream ed831ab): the `session hud` group.
// Message length and control characters are the dispatcher's to refuse; only what needs no socket
// is checked here.
internal static class HudCommands
{
    internal const string GroupDescription = "Post, update, or close a passive message panel over a session.";
    internal const string OpenDescription = "Post a message panel over the session; the session keeps focus and stays typable.";
    internal const string UpdateDescription = "Replace the panel's text in place (no re-spawn, no blink).";
    internal const string CloseDescription = "Take the message panel down (a program overlay in the same slot is left alone).";

    internal const string StickyHelp =
        "Put the panel flush against the edge or corner --position names, with no margin; center has no edge and ignores it. Add --size-percent 100 for a strip across the pane.";

    internal const string NoFrameHelp = "Draw no border, no rounded corners and no blank row above and below the text.";

    // help and validation derive from the same Core constants, so a changed limit reaches both.
    // Ctrl+click replaces upstream's command-click, the click that opens a HUD link on Windows.
    private static readonly string OpenMarkdownHelp =
        $"Render the message as markdown, up to {HudSpec.MaxMarkdownLength} characters. A single newline inside a paragraph is a soft break; end a line with two spaces or a backslash to break it. A link is underlined and opens on a Ctrl+click.";

    private static readonly string FontSizeHelp =
        $"The panel's own font size in points, {(int)HudSpec.MinFontSize}-{(int)HudSpec.MaxFontSize}; omit to use the session's. Fixed for the panel's life.";

    private static readonly string OpenSpinnerStyleHelp =
        $"Spinner style: {HudSpinners.AcceptedNamesPhrase} (default: {HudSpinners.Wire(HudSpinners.Default)}). Implies --spinner; {HudSpinners.NoneName} leaves the panel static.";

    private static readonly string UpdateSpinnerStyleHelp =
        $"Switch the spinner to {HudSpinners.AcceptedNamesPhrase}; implies --spinner, and repaints the live panel without a re-spawn. {HudSpinners.NoneName} stops it.";

    // the canonical nine are what session background shares; the aliases are this command's own
    private static readonly string OpenPositionHelp =
        $"Placement in the pane: {HudPositions.ValidNamesPhrase} (default: center), the same anchors session background takes. Every anchor off center holds a fixed margin at that edge. Here top and bottom are also accepted, for top-center and bottom-center.";

    private static readonly string UpdatePositionHelp = $"Move the panel to {HudPositions.AcceptedNamesPhrase} (default: center).";

    private static readonly string OpenHideAfterHelp =
        $"Take the panel down by itself after SECONDS, 0...{(int)HudSpec.MaxHideAfter}; omit or 0 to leave it up until something closes it. The clock runs whether or not the session is on screen.";

    private static readonly string UpdateHideAfterHelp =
        $"Restart the panel's auto-hide at SECONDS, 0...{(int)HudSpec.MaxHideAfter}; omit or 0 to cancel it, like every other option an update replaces rather than patches.";

    private static string WidthHelp(string verb) =>
        $"{verb} the panel's WIDTH to PERCENT (1-100) of the pane instead of measuring the message; bounded to {HudLayout.MinSizePercent}-{HudLayout.MaxSizePercent}, or up to 100 with --sticky off center. Height always follows the message.";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Accepts the top and bottom aliases as the dispatcher does: refusing one here would
    /// fail a value the identical raw-socket request takes.</summary>
    internal static void ValidatePosition(string? position)
    {
        if (position is not null && HudPositions.Parse(position) is null)
            throw new CliValidationException("position must be one of: " + HudPositions.AcceptedNamesPhrase);
    }

    internal static void ValidateTextColor(string? textColor)
    {
        if (textColor is not null && !WatermarkConfig.IsValidColorHex(textColor))
            throw new CliValidationException("text-color must be a #rrggbb hex value");
    }

    /// <summary>Accepts `none` beside the styles: it is what tree reports for a static panel, and
    /// refusing it would fail a value tree just handed the caller.</summary>
    internal static void ValidateSpinnerStyle(string? style)
    {
        if (style is not null && style != HudSpinners.NoneName && HudSpinners.FromWire(style) is null)
            throw new CliValidationException("spinner style must be one of: " + HudSpinners.AcceptedNamesPhrase);
    }

    /// <summary>Refuses what cannot be scheduled instead of clamping it, so the same value fails
    /// the same way over a raw socket.</summary>
    internal static void ValidateHideAfter(double? seconds)
    {
        if (seconds is { } value && !HudSpec.IsValidHideAfter(value))
            throw new CliValidationException($"hide-after must be 0...{(int)HudSpec.MaxHideAfter} seconds");
    }

    internal static void ValidateMessageSource(string? message, string? file)
    {
        if (message is null && file is null)
            throw new CliValidationException("provide MESSAGE or --file");
        if (message is not null && file is not null)
            throw new CliValidationException("MESSAGE and --file are mutually exclusive");
    }

    internal static void ValidateFontSize(double? points)
    {
        if (points is { } value && !HudSpec.IsValidFontSize(value))
            throw new CliValidationException($"font-size must be {(int)HudSpec.MinFontSize}...{(int)HudSpec.MaxFontSize} points");
    }

    /// <summary>The one spinner value the socket carries. A named style turns the spinner on by
    /// itself and an explicit `none` beats a bare --spinner: naming a value is the more specific
    /// instruction. Null is the static panel.</summary>
    public static string? SpinnerValue(bool spinner, string? style)
    {
        if (style == HudSpinners.NoneName)
            return null;
        return style ?? (spinner ? HudSpinners.Wire(HudSpinners.Default) : null);
    }

    /// <summary>The argument, or the file's UTF-8 text with a leading byte order mark dropped
    /// (Windows editors write one), CRLF made LF and one trailing newline dropped: files end with
    /// one and a plain panel refuses newlines. A lone CR stays, so the server refuses it as
    /// upstream's does. Throws CliValidationException, so Validate is the place to call it.</summary>
    public static string MessageText(string? message, string? file)
    {
        if (file is null)
            return message ?? "";
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CliValidationException($"cannot read --file {file}: {e.Message}");
        }
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new CliValidationException($"--file {file} is not valid UTF-8");
        }
        if (text.StartsWith('\uFEFF'))
            text = text[1..];
        text = text.Replace("\r\n", "\n");
        return text.EndsWith('\n') ? text[..^1] : text;
    }

    /// <summary>`agtermctl session hud [open] MESSAGE`: post a panel over the session.</summary>
    internal sealed class Open : IRequestCommand
    {
        public string? Message { get; init; }
        public string? FilePath { get; init; }
        public bool Markdown { get; init; }
        public double? FontSize { get; init; }
        public string? Detail { get; init; }
        public bool Spinner { get; init; }
        public string? SpinnerStyle { get; init; }
        public string? Position { get; init; }
        public string? BackgroundColor { get; init; }
        public string? TextColor { get; init; }
        public int? SizePercent { get; init; }
        public bool Sticky { get; init; }
        public bool NoFrame { get; init; }
        public string? Pane { get; init; }
        public string? PaneId { get; init; }
        public double? HideAfter { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        // read once in Validate, so the text sent is the text that passed, even if the file changes
        private string? _messageText;

        public void Validate()
        {
            if (BackgroundColor is not null && !WatermarkConfig.IsValidColorHex(BackgroundColor))
                throw new CliValidationException("background-color must be a #rrggbb hex value");
            ValidateTextColor(TextColor);
            ValidatePosition(Position);
            ValidateSpinnerStyle(SpinnerStyle);
            ValidateHideAfter(HideAfter);
            ValidateMessageSource(Message, FilePath);
            ValidateFontSize(FontSize);
            SessionCommands.ValidateSizePercent(SizePercent);
            SessionCommands.ValidateOverlayPane(Pane);
            _messageText = MessageText(Message, FilePath);
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionHudOpen,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs
            {
                SizePercent = SizePercent,
                Message = _messageText ?? MessageText(Message, FilePath),
                Detail = Detail,
                Spinner = SpinnerValue(Spinner, SpinnerStyle),
                HideAfter = HideAfter,
                Markdown = Markdown ? true : null,
                Sticky = Sticky ? true : null,
                Frame = NoFrame ? false : null,
                Pane = Pane,
                PaneId = PaneId,
                Color = BackgroundColor,
                TextColor = TextColor,
                Position = Position,
                FontSize = FontSize,
            }),
        };
    }

    /// <summary>`agtermctl session hud update MESSAGE`: replace the whole panel spec in place. No
    /// --background-color and no --font-size: the panel keeps the open's values for both.</summary>
    internal sealed class Update : IRequestCommand
    {
        public string? Message { get; init; }
        public string? FilePath { get; init; }
        public bool Markdown { get; init; }
        public string? Detail { get; init; }
        public bool Spinner { get; init; }
        public string? SpinnerStyle { get; init; }
        public string? Position { get; init; }
        public string? TextColor { get; init; }
        public int? SizePercent { get; init; }
        public bool Sticky { get; init; }
        public bool NoFrame { get; init; }
        public string? Pane { get; init; }
        public string? PaneId { get; init; }
        public double? HideAfter { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        private string? _messageText;

        public void Validate()
        {
            ValidateTextColor(TextColor);
            ValidatePosition(Position);
            ValidateSpinnerStyle(SpinnerStyle);
            ValidateHideAfter(HideAfter);
            ValidateMessageSource(Message, FilePath);
            SessionCommands.ValidateSizePercent(SizePercent);
            SessionCommands.ValidateOverlayPane(Pane);
            _messageText = MessageText(Message, FilePath);
        }

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionHudUpdate,
            Target = Target.Target,
            Args = Client.WithWindow(new ControlArgs
            {
                SizePercent = SizePercent,
                Message = _messageText ?? MessageText(Message, FilePath),
                Detail = Detail,
                Spinner = SpinnerValue(Spinner, SpinnerStyle),
                HideAfter = HideAfter,
                Markdown = Markdown ? true : null,
                Sticky = Sticky ? true : null,
                Frame = NoFrame ? false : null,
                Pane = Pane,
                PaneId = PaneId,
                TextColor = TextColor,
                Position = Position,
            }),
        };
    }

    /// <summary>`agtermctl session hud close`: take the panel down.</summary>
    internal sealed class Close : IRequestCommand
    {
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.SessionHudClose,
            Target = Target.Target,
            Args = Client.WithWindow(),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var hud = new System.CommandLine.Command("hud", GroupDescription);
        var open = new System.CommandLine.Command("open", OpenDescription);
        BindOpen(bindings, open);
        hud.Subcommands.Add(open);
        hud.Subcommands.Add(BuildUpdate(bindings));
        hud.Subcommands.Add(BuildClose(bindings));
        // upstream's defaultSubcommand: the group binds as open too, so `session hud MESSAGE` posts a panel
        BindOpen(bindings, hud);
        return hud;
    }

    private static void BindOpen(CommandBindings bindings, System.CommandLine.Command command)
    {
        var message = new Argument<string?>("message")
        {
            Description = "Message shown in the panel (omit with --file).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var file = new Option<string?>("--file") { Description = "Read the message from FILE instead of the argument." };
        var markdown = new Option<bool>("--markdown") { Description = OpenMarkdownHelp };
        var fontSize = CliOptions.LenientDouble("--font-size", FontSizeHelp);
        var detail = new Option<string?>("--detail") { Description = "Dim second line under the message (e.g. what the caller is waiting on)." };
        var spinner = new Option<bool>("--spinner") { Description = "Animate a spinner glyph in the panel, in the default style." };
        var spinnerStyle = new Option<string?>("--spinner-style") { Description = OpenSpinnerStyleHelp };
        var position = new Option<string?>("--position") { Description = OpenPositionHelp };
        var backgroundColor = new Option<string?>("--background-color") { Description = "Solid background color (#rrggbb) for the panel, independent of the session's own." };
        var textColor = new Option<string?>("--text-color") { Description = "Color (#rrggbb) for the panel's text; omit to keep the terminal foreground." };
        var sizePercent = new Option<int?>("--size-percent") { Description = WidthHelp("Set") };
        var sticky = new Option<bool>("--sticky") { Description = StickyHelp };
        var noFrame = new Option<bool>("--no-frame") { Description = NoFrameHelp };
        var pane = new Option<string?>("--pane") { Description = "Anchor inside primary/left/top or split/right/bottom; omit for the whole session." };
        var paneId = new Option<string?>("--pane-id") { Description = "Stable pane token ($AGTERM_PANE_ID); overrides --pane when it resolves." };
        var hideAfter = CliOptions.LenientDouble("--hide-after", OpenHideAfterHelp);
        var target = new TargetOptions.Binder();
        var client = new ClientOptions.Binder();
        command.Arguments.Add(message);
        foreach (var option in new Option[] { file, markdown, fontSize, detail, spinner, spinnerStyle, position, backgroundColor, textColor, sizePercent, sticky, noFrame, pane, paneId, hideAfter })
            command.Options.Add(option);
        target.AddTo(command);
        client.AddTo(command);
        bindings.Register(command, result => new Open
        {
            Message = result.GetValue(message),
            FilePath = result.GetValue(file),
            Markdown = result.GetValue(markdown),
            FontSize = result.GetValue(fontSize),
            Detail = result.GetValue(detail),
            Spinner = result.GetValue(spinner),
            SpinnerStyle = result.GetValue(spinnerStyle),
            Position = result.GetValue(position),
            BackgroundColor = result.GetValue(backgroundColor),
            TextColor = result.GetValue(textColor),
            SizePercent = result.GetValue(sizePercent),
            Sticky = result.GetValue(sticky),
            NoFrame = result.GetValue(noFrame),
            Pane = result.GetValue(pane),
            PaneId = result.GetValue(paneId),
            HideAfter = result.GetValue(hideAfter),
            Target = target.Read(result),
            Client = client.Read(result),
        });
    }

    private static System.CommandLine.Command BuildUpdate(CommandBindings bindings)
    {
        var command = new System.CommandLine.Command("update", UpdateDescription);
        var message = new Argument<string?>("message")
        {
            Description = "New message; it replaces the old one entirely (omit with --file).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var file = new Option<string?>("--file") { Description = "Read the new message from FILE instead of the argument." };
        var markdown = new Option<bool>("--markdown") { Description = "Render the message as markdown; omit to return the panel to plain text." };
        var detail = new Option<string?>("--detail") { Description = "Dim second line under the message; omit to drop the old one." };
        var spinner = new Option<bool>("--spinner") { Description = "Keep (or start) the spinner in the default style; omit to stop it." };
        var spinnerStyle = new Option<string?>("--spinner-style") { Description = UpdateSpinnerStyleHelp };
        var position = new Option<string?>("--position") { Description = UpdatePositionHelp };
        var textColor = new Option<string?>("--text-color") { Description = "Recolor the panel's text (#rrggbb); omit to return it to the terminal foreground." };
        var sizePercent = new Option<int?>("--size-percent") { Description = WidthHelp("Resize") };
        var sticky = new Option<bool>("--sticky") { Description = StickyHelp + " Omit to return the panel to its edge margin." };
        var noFrame = new Option<bool>("--no-frame") { Description = NoFrameHelp + " Omit to bring the frame back." };
        var pane = new Option<string?>("--pane") { Description = "Anchor inside primary/left/top or split/right/bottom; omit to return to whole-session placement." };
        var paneId = new Option<string?>("--pane-id") { Description = "Stable pane token ($AGTERM_PANE_ID); repeat it on update to keep pane scope." };
        var hideAfter = CliOptions.LenientDouble("--hide-after", UpdateHideAfterHelp);
        var target = new TargetOptions.Binder();
        var client = new ClientOptions.Binder();
        command.Arguments.Add(message);
        foreach (var option in new Option[] { file, markdown, detail, spinner, spinnerStyle, position, textColor, sizePercent, sticky, noFrame, pane, paneId, hideAfter })
            command.Options.Add(option);
        target.AddTo(command);
        client.AddTo(command);
        bindings.Register(command, result => new Update
        {
            Message = result.GetValue(message),
            FilePath = result.GetValue(file),
            Markdown = result.GetValue(markdown),
            Detail = result.GetValue(detail),
            Spinner = result.GetValue(spinner),
            SpinnerStyle = result.GetValue(spinnerStyle),
            Position = result.GetValue(position),
            TextColor = result.GetValue(textColor),
            SizePercent = result.GetValue(sizePercent),
            Sticky = result.GetValue(sticky),
            NoFrame = result.GetValue(noFrame),
            Pane = result.GetValue(pane),
            PaneId = result.GetValue(paneId),
            HideAfter = result.GetValue(hideAfter),
            Target = target.Read(result),
            Client = client.Read(result),
        });
        return command;
    }

    private static System.CommandLine.Command BuildClose(CommandBindings bindings)
    {
        var command = new System.CommandLine.Command("close", CloseDescription);
        var target = new TargetOptions.Binder();
        var client = new ClientOptions.Binder();
        target.AddTo(command);
        client.AddTo(command);
        bindings.Register(command, result => new Close { Target = target.Read(result), Client = client.Read(result) });
        return command;
    }
}
