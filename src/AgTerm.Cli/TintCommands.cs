using System.CommandLine;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit TintCommands.swift (upstream f3fea57b): `session tint`, `workspace tint` and
// the --tint/--tint-on creation options `session new` and `workspace new` share.

/// <summary>Builds the shared session.tint / workspace.tint wire form from the CLI spelling, where
/// the one argument is either a color or `clear`.</summary>
internal static class TintRequest
{
    public static readonly string ColorHelp = $"A color ({TintColors.ValidNamesList}) or 'clear'.";

    public static string OnHelp(bool session) => session
        ? "Where the color shows: a comma list of icon, name, flag (the flag of a flagged session). Omitted keeps the current placement, else icon."
        : "Where the color shows: icon, name, or icon,name. Omitted keeps the current placement, else icon.";

    public static void Validate(string value, string? on, bool session)
    {
        if (value == "clear")
        {
            if (on is not null) throw new CliValidationException("--on does not apply to clear");
            return;
        }
        if (!TintColors.TryParse(value.ToLowerInvariant(), out _))
            throw new CliValidationException($"tint must be one of: {TintColors.ValidNamesList}, or clear");
        ValidateParts(on, "--on", session);
    }

    /// <summary>A session tint can also show on the flag (Windows-only); a workspace row has none.</summary>
    public static void ValidateParts(string? on, string flag, bool session)
    {
        if (on is null) return;
        var allowed = session ? SidebarTint.SessionParts : SidebarTint.WorkspaceParts;
        if (SidebarTint.ParseParts(Parts(on) ?? [], allowed) is null)
            throw new CliValidationException(session
                ? $"{flag} must list icon, name and/or flag, comma-separated"
                : $"{flag} must list icon and/or name, comma-separated");
    }

    public static ControlArgs Args(string value, string? on) =>
        value == "clear"
            ? new ControlArgs { Mode = "clear" }
            : new ControlArgs { Mode = "set", Tint = value.ToLowerInvariant(), TintOn = Parts(on) };

    /// <summary>Splits the comma list keeping empty items, so "icon," reaches validation as a bad part.</summary>
    public static string[]? Parts(string? on) => on?.Split(',');
}

/// <summary>The creation tint `session new` and `workspace new` share.</summary>
internal sealed class TintCreateOptions
{
    public string? Tint { get; init; }
    public string? TintOn { get; init; }
    public bool Session { get; init; }

    public void Validate()
    {
        if (Tint is null)
        {
            if (TintOn is not null) throw new CliValidationException("--tint-on requires --tint");
            return;
        }
        if (!TintColors.TryParse(Tint.ToLowerInvariant(), out _))
            throw new CliValidationException($"--tint must be one of: {TintColors.ValidNamesList}");
        TintRequest.ValidateParts(TintOn, "--tint-on", Session);
    }

    /// <summary>Copies the creation tint into a session.new / workspace.new argument bag.</summary>
    public ControlArgs Apply(ControlArgs args) =>
        args with { Tint = Tint?.ToLowerInvariant(), TintOn = TintRequest.Parts(TintOn) };

    internal sealed class Binder(bool session)
    {
        private readonly Option<string?> _tint = new("--tint")
        {
            Description = $"Create it tinted this color ({TintColors.ValidNamesList}).",
        };
        private readonly Option<string?> _tintOn = new("--tint-on")
        {
            Description = session
                ? "With --tint: a comma list of icon, name, flag (default icon)."
                : "With --tint: icon, name, or icon,name (default icon).",
        };

        public void AddTo(System.CommandLine.Command command)
        {
            command.Options.Add(_tint);
            command.Options.Add(_tintOn);
        }

        public TintCreateOptions Read(ParseResult result) => new()
        {
            Tint = result.GetValue(_tint),
            TintOn = result.GetValue(_tintOn),
            Session = session,
        };
    }
}

internal static class TintCommands
{
    /// <summary>`session tint` and `workspace tint`: one color-or-clear argument, an optional --on list,
    /// a target and the client options.</summary>
    internal sealed class Tint : IRequestCommand
    {
        public required WireCommand Cmd { get; init; }
        public required string Value { get; init; }
        public string? On { get; init; }
        public required TargetOptions Target { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public void Validate() => TintRequest.Validate(Value, On, Cmd == WireCommand.SessionTint);

        public ControlRequest MakeRequest() => new()
        {
            Cmd = Cmd,
            Target = Target.Target,
            Args = Client.WithWindow(TintRequest.Args(Value, On)),
        };
    }

    public static System.CommandLine.Command BuildSessionTint(CommandBindings bindings) =>
        Build(bindings, WireCommand.SessionTint,
            "Tint a session's sidebar row (a color, or clear).\n\n"
            + "session tint red                 tint the icon red (or recolor, keeping the placement)\n"
            + "session tint green --on name     color the name instead\n"
            + "session tint blue --on icon,name color both\n"
            + "session tint red --on flag       color the flag of a flagged session\n"
            + "session tint clear               remove the tint\n\n"
            + "Persists across a relaunch; read back as the tree's tint and tintOn.");

    public static System.CommandLine.Command BuildWorkspaceTint(CommandBindings bindings) =>
        Build(bindings, WireCommand.WorkspaceTint,
            "Tint a workspace's own sidebar row (a color, or clear); its sessions keep their own.\n\n"
            + "workspace tint red --on icon,name\n"
            + "workspace tint clear\n\n"
            + "Persists across a relaunch; read back as the workspace node's tint and tintOn.");

    private static System.CommandLine.Command Build(CommandBindings bindings, WireCommand wire, string description)
    {
        var cmd = new System.CommandLine.Command("tint", description);
        var value = new Argument<string>("value") { Description = TintRequest.ColorHelp };
        var on = new Option<string?>("--on") { Description = TintRequest.OnHelp(wire == WireCommand.SessionTint) };
        var target = new TargetOptions.Binder();
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(value);
        cmd.Options.Add(on);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Tint
        {
            Cmd = wire,
            Value = result.GetValue(value)!,
            On = result.GetValue(on),
            Target = target.Read(result),
            Client = options.Read(result),
        });
        return cmd;
    }
}
