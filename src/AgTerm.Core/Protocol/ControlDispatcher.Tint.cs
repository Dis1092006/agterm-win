using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

// session.tint, workspace.tint and the creation tint of session.new / workspace.new (upstream
// f3fea57b). The dispatcher parses and validates, so a rejected request never reaches the host.
public sealed partial class ControlDispatcher
{
    private ControlResponse DispatchTint(ControlRequest request)
    {
        if (TintUpdate(request.Cmd.ToWire(), request.Args, Placements(request.Cmd == Command.SessionTint), out var update) is { } refusal)
            return refusal;
        return request.Cmd == Command.SessionTint
            ? _actions.SetSessionTint(request.Target, request.Args?.Window, update!)
            : _actions.SetWorkspaceTint(request.Target, request.Args?.Window, update!);
    }

    /// <summary>Mode set|clear. Set needs a color and may carry a placement; clear takes neither.</summary>
    private static ControlResponse? TintUpdate(string cmd, ControlArgs? args, TintPlacements placements, out ControlTintUpdate? update)
    {
        update = null;
        switch (args?.Mode ?? "")
        {
            case "set":
            {
                if (args?.Tint is not { } raw)
                    return Err($"{cmd} set requires a color");
                if (ParseTintColor(raw, out var color) is { } badColor)
                    return badColor;
                TintParts? parts = null;
                if (args.TintOn is { } rawParts)
                {
                    if (placements.Parse(rawParts) is not { } parsed)
                        return placements.Invalid(rawParts);
                    parts = parsed;
                }
                update = ControlTintUpdate.Set(color, parts);
                return null;
            }
            case "clear":
                if (args?.Tint is not null || args?.TintOn is not null)
                    return Err($"{cmd} clear takes no color or placement");
                update = ControlTintUpdate.Clear;
                return null;
            default:
                return Err($"invalid tint mode: {args?.Mode ?? ""} (set|clear)");
        }
    }

    /// <summary>The tint session.new or workspace.new creates the row with: none without --tint, the
    /// icon when --tint has no --tint-on.</summary>
    private static ControlResponse? CreationTint(ControlArgs? args, bool session, out SidebarTint? tint)
    {
        tint = null;
        if (args?.Tint is not { } raw)
            return args?.TintOn is not null ? Err("--tint-on requires --tint") : null;
        if (ParseTintColor(raw, out var color) is { } badColor)
            return badColor;
        var parts = TintParts.Icon;
        var placements = Placements(session);
        if (args.TintOn is { } rawParts)
        {
            if (placements.Parse(rawParts) is not { } parsed)
                return placements.Invalid(rawParts);
            parts = parsed;
        }
        tint = new SidebarTint(color, parts);
        return null;
    }

    private static ControlResponse? ParseTintColor(string raw, out TintColor color) =>
        TintColors.TryParse(raw.ToLowerInvariant(), out color)
            ? null
            : Err($"invalid tint: {raw} ({TintColors.ValidNamesList})");

    /// <summary>The parts a row kind takes: a session all three, a workspace no flag (Windows-only).</summary>
    private static TintPlacements Placements(bool session) => session
        ? new(SidebarTint.SessionParts, SidebarTint.ValidPartNamesList)
        : new(SidebarTint.WorkspaceParts, SidebarTint.WorkspacePartNamesList);

    private sealed record TintPlacements(TintParts Allowed, string NamesList)
    {
        public TintParts? Parse(string[] raw) => SidebarTint.ParseParts(raw, Allowed);

        public ControlResponse Invalid(string[] raw) =>
            Err($"invalid tint placement: {string.Join(",", raw)} ({NamesList})");
    }
}
