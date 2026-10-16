using System.Globalization;
using System.Text;
using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

/// <summary>
/// Routes control commands through a host-provided action seam. The dispatcher owns command parsing
/// and response shape; host actions keep target resolution, window state, and terminal-surface side
/// effects. The 1:1 port of the Swift ControlDispatcher.
/// </summary>
public sealed class ControlDispatcher
{
    private readonly IControlActions _actions;

    public ControlDispatcher(IControlActions actions)
    {
        _actions = actions;
    }

    public async Task<ControlResponse?> Dispatch(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.Tree:
                return _actions.ControlTree(request.Args?.Window);
            case Command.EventsRead:
                return DispatchEventsRead(request);
            case Command.SessionNew or Command.SessionDuplicate or Command.SessionSelect
                or Command.SessionGo or Command.SessionClose or Command.SessionRename
                or Command.SessionReveal or Command.SessionMove or Command.SessionFlag
                or Command.SessionSeen or Command.SessionStatus or Command.SessionRestore:
                return DispatchSessionCommand(request);
            case Command.SessionSplit or Command.SessionScratch or Command.SessionFocus
                or Command.SessionResize or Command.SurfaceZoom or Command.SessionType
                or Command.SessionCopy or Command.SessionPaste or Command.SessionSelectAll
                or Command.SessionSearch or Command.SessionOverlayOpen or Command.SessionOverlayClose
                or Command.SessionOverlayResize or Command.SessionOverlayResult
                or Command.SessionBackground or Command.SessionText:
                return await DispatchSessionSurfaceCommand(request);
            case Command.WorkspaceNew or Command.WorkspaceSelect or Command.WorkspaceRename
                or Command.WorkspaceDelete or Command.WorkspaceMove or Command.WorkspaceFocus
                or Command.WorkspaceCollapse or Command.WorkspaceExpand or Command.WorkspaceTheme:
                return DispatchWorkspaceCommand(request);
            case Command.Quick or Command.FontInc or Command.FontDec or Command.FontReset
                or Command.KeymapReload or Command.ConfigReload or Command.Notify
                or Command.ThemeSet or Command.ThemeList or Command.Sidebar or Command.SidebarMode
                or Command.SidebarExpand or Command.SidebarCollapse or Command.RestoreClear:
                return DispatchAppCommand(request);
            case Command.QuickType or Command.QuickText:
                return await DispatchQuickCommand(request);
            case Command.WindowNew or Command.WindowList or Command.WindowSelect or Command.WindowClose
                or Command.WindowRename or Command.WindowDelete or Command.WindowResize
                or Command.WindowMove or Command.WindowZoom or Command.WindowFullscreen:
                return await DispatchWindowCommand(request);
            case Command.Dashboard:
                return DispatchDashboard(request);
            case Command.PickOpen or Command.PickResult or Command.PickCancel:
                return DispatchPickCommand(request);
            case Command.AskOpen or Command.AskResult or Command.AskCancel:
                return DispatchAskCommand(request);
            case Command.SessionHudOpen or Command.SessionHudUpdate or Command.SessionHudClose:
                return DispatchHudCommand(request);
            case Command.DebugAppearance:
                // UI-test-only seam handled host-side (out of the parity surface on Windows).
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), $"unexpected command: {request.Cmd}");
        }
    }

    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static ControlResponse? NulRejection(string text) =>
        text.Contains('\0') ? Err("text must not contain a NUL byte") : null;

    /// <summary>The --pane check of the commands that act on one terminal (session.type,
    /// session.text, font.*). It runs before the action, so a bad pane wins over target errors.</summary>
    private static ControlResponse? SurfacePaneRejection(string? raw) =>
        raw is not null && StatusPanes.FromWire(raw) is null ? Err($"invalid pane: {raw}") : null;

    private const string InvalidPickItem = "invalid request: pick item requires id and label";

    /// <summary>Port of upstream ControlDispatcher+Pick: host-free validation in upstream's order,
    /// then the window-scoped state goes to the host.</summary>
    private ControlResponse DispatchPickCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.PickOpen:
            {
                if (request.Args?.Items is not { } items)
                    return Err("pick.open requires items");
                var allowCustom = request.Args?.AllowCustom == true;
                // an empty list is a text prompt, which only makes sense when a custom answer is accepted
                if (items.Length == 0 && !allowCustom)
                    return Err("pick.open requires at least one item");
                if (items.Length > ControlPickItem.MaxItems)
                    return Err($"too many items (max {ControlPickItem.MaxItems})");
                // System.Text.Json binds an explicit null to a required string; Swift fails the decode
                if (items.Any(item => item is null || item.Id is null || item.Label is null))
                    return Err(InvalidPickItem);
                if (items.Any(item => item.Label.Length == 0))
                    return Err("pick item label must not be empty");
                // Swift String equality treats canonically equivalent strings as equal
                var ids = items.Select(item => item.Id.Normalize(NormalizationForm.FormC)).ToArray();
                if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                    return Err("pick item ids must be unique");
                if (items.Any(item => CommandRestore.HasControlCharacter(item.Label)
                        || (item.Subtitle is { } subtitle && CommandRestore.HasControlCharacter(subtitle))))
                    return Err("item text must not contain control characters");
                string? selection = null;
                if (request.Args?.Selection is { } wanted)
                {
                    var index = Array.IndexOf(ids, wanted.Normalize(NormalizationForm.FormC));
                    if (index < 0)
                        return Err("pick select must name an item id");
                    selection = items[index].Id;
                }
                var pick = new PendingPick(
                    Guid.NewGuid().ToString("D").ToUpperInvariant(),
                    items,
                    request.Args?.Prompt,
                    request.Args?.Query,
                    allowCustom,
                    selection);
                return _actions.OpenPick(pick, request.Args?.Window, request.Args?.Follow == true);
            }
            case Command.PickResult:
                return request.Target is { } resultTarget
                    ? _actions.PickResult(resultTarget, request.Args?.Window)
                    : Err("pick.result requires a pick id");
            case Command.PickCancel:
                return request.Target is { } cancelTarget
                    ? _actions.CancelPick(cancelTarget, request.Args?.Window)
                    : Err("pick.cancel requires a pick id");
            default:
                throw new InvalidOperationException($"unexpected pick command: {request.Cmd}");
        }
    }

    private const string InvalidAskButton = "invalid request: ask button requires id and label";

    private ControlResponse DispatchAskCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.AskOpen:
                return DispatchAskOpen(request);
            case Command.AskResult:
                return request.Target is { } resultTarget
                    ? _actions.AskResult(resultTarget, request.Args?.Window)
                    : Err("ask.result requires an ask id");
            case Command.AskCancel:
                return request.Target is { } cancelTarget
                    ? _actions.CancelAsk(cancelTarget, request.Args?.Window)
                    : Err("ask.cancel requires an ask id");
            default:
                throw new InvalidOperationException($"unexpected ask command: {request.Cmd}");
        }
    }

    /// <summary>Port of upstream ControlDispatcher+Ask: host-free validation in upstream's order,
    /// before the host resolves placement or touches a slot.</summary>
    private ControlResponse DispatchAskOpen(ControlRequest request)
    {
        var args = request.Args;
        if (args?.Title is not { } title || string.IsNullOrWhiteSpace(title))
            return Err("ask.open requires a title");
        if (args.Buttons is not { } buttons)
            return Err("ask.open requires buttons");
        if (buttons.Length == 0)
            return Err("ask.open requires at least one button");
        if (buttons.Length > ControlAskButton.MaxButtons)
            return Err($"too many buttons (max {ControlAskButton.MaxButtons})");
        // System.Text.Json binds an explicit null to a required string; Swift fails the decode
        if (buttons.Any(button => button is null || button.Id is null || button.Label is null))
            return Err(InvalidAskButton);
        if (buttons.Any(button => button.Label.Length == 0))
            return Err("ask button label must not be empty");
        var ids = buttons.Select(button => button.Id.Normalize(NormalizationForm.FormC)).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            return Err("ask button ids must be unique");
        if (CommandRestore.HasControlCharacter(title) || CommandRestore.HasControlCharacter(args.Message ?? "")
            || buttons.Any(button => CommandRestore.HasControlCharacter(button.Label)))
            return Err("ask text must not contain control characters");
        foreach (var (role, id) in new[] { ("default", args.DefaultButton), ("destructive", args.DestructiveButton) })
        {
            if (id is not null && Array.IndexOf(ids, id.Normalize(NormalizationForm.FormC)) < 0)
                return Err($"unknown {role} button: {id}");
        }
        if (args.DestructiveButton is not null && AskIds.Same(args.DefaultButton, args.DestructiveButton))
            return Err("default button must not be destructive");
        if (ControlAskStyles.FromWire(args.Style ?? "terminal") is not { } style)
            return Err("unknown style");
        if (ControlAskAlignments.FromWire(args.Align ?? "right") is not { } align)
            return Err("unknown align");
        if (args.Width is { } width && (width < 10 || width > 100))
            return Err("width must be 10 to 100");
        if (HotkeyRejection(buttons) is { } hotkeyError)
            return Err(hotkeyError);
        if (style == ControlAskStyle.Gui && (args.Pane is not null || args.PaneId is not null) && request.Target is null)
            return Err("--pane requires a session");
        OverlayPane? pane = null;
        if (args.Pane is { } rawPane && (pane = OverlayPanes.FromControlName(rawPane)) is null)
            return Err("--pane must be left or right");
        var ask = new PendingAsk(
            Guid.NewGuid().ToString("D").ToUpperInvariant(),
            title,
            args.Message,
            buttons.Select(button => button with { Hotkey = button.Hotkey?.ToLowerInvariant() }).ToArray(),
            args.DefaultButton,
            args.DestructiveButton,
            style,
            align,
            args.Width);
        return _actions.OpenAsk(ask, request.Target, args.Window, new ControlAskPlacement(pane, args.PaneId), args.Follow == true);
    }

    private static string? HotkeyRejection(IEnumerable<ControlAskButton> buttons)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var button in buttons)
        {
            if (button.Hotkey is not { } hotkey) continue;
            if (hotkey.Length != 1 || !char.IsAsciiLetter(hotkey[0]))
                return "ask button hotkey must be one ASCII letter";
            if (!seen.Add(hotkey.ToLowerInvariant()))
                return "ask button hotkeys must be unique";
        }
        return null;
    }

    /// <summary>Port of upstream ControlDispatcher+Hud: host-free validation in upstream's order.
    /// Slot occupancy, pane identity and sizing need the store and stay with the host.</summary>
    private ControlResponse DispatchHudCommand(ControlRequest request)
    {
        if (request.Cmd == Command.SessionHudClose)
            return _actions.CloseHud(request.Target, request.Args?.Window);
        var args = request.Args;
        var cmd = request.Cmd.ToWire();
        OverlayPane? pane = null;
        if (args?.Pane is { } rawPane && (pane = OverlayPanes.FromControlName(rawPane)) is null)
            return Err("--pane must be left or right");
        // blank joins absent: Wrap drops whitespace-only text, so the panel would paint an empty
        // frame while tree reported a live HUD
        if (args?.Message is not { } message || string.IsNullOrWhiteSpace(message))
            return Err($"{cmd} requires a message");
        var markdown = args.Markdown == true;
        var messageRefused = markdown ? HudText.HasMarkdownControlCharacters(message) : HudText.HasControlCharacters(message);
        if (messageRefused || HudText.HasControlCharacters(args.Detail ?? ""))
            return Err(HudErrors.TextControlCharacters);
        var cap = markdown ? HudSpec.MaxMarkdownLength : HudSpec.MaxTextLength;
        if (HudLayout.TextLength(message) > cap)
            return Err($"hud message too long (max {cap} characters)");
        // spec 1.2 rule 5: a markdown source that lays out to nothing is no message (upstream ControlDispatcher+Hud.swift)
        if (markdown && !HudMarkdown.RendersVisibleText(message))
            return Err($"{cmd} requires a message");
        if (HudLayout.TextLength(args.Detail ?? "") > HudSpec.MaxTextLength)
            return Err($"hud detail too long (max {HudSpec.MaxTextLength} characters)");
        if (args.Color is { } color && !WatermarkConfig.IsValidColorHex(color))
            return Err($"invalid color: {color} (#rrggbb)");
        if (args.TextColor is { } textColor && !WatermarkConfig.IsValidColorHex(textColor))
            return Err($"invalid text color: {textColor} (#rrggbb)");
        if (args.FontSize is { } fontSize)
        {
            if (request.Cmd != Command.SessionHudOpen)
                return Err("session.hud.update: --font-size is fixed at open; reopen the hud to change it");
            if (!HudSpec.IsValidFontSize(fontSize))
                return Err("session.hud.open: --font-size must be 6...72 points");
        }
        if (args.SizePercent is { } percent && (percent < 1 || percent > 100))
            return Err($"{cmd}: --size-percent must be 1...100");
        // refused rather than clamped, so a caller who asked for a duration nothing can schedule is told
        if (args.HideAfter is { } hideAfter && !HudSpec.IsValidHideAfter(hideAfter))
            return Err($"{cmd}: --hide-after must be 0...86400 seconds");
        var position = HudPositions.Default;
        if (args.Position is { } rawPosition)
        {
            if (HudPositions.Parse(rawPosition) is not { } parsedPosition)
                return Err($"invalid position: {rawPosition} ({HudPositions.AcceptedNamesList})");
            position = parsedPosition;
        }
        HudSpinner? spinner = null;
        // none is tree's spelling of a static panel, so a caller echoing tree back must not be refused
        if (args.Spinner is { } rawSpinner && rawSpinner != HudSpinners.NoneName)
        {
            if (HudSpinners.FromWire(rawSpinner) is not { } parsedSpinner)
                return Err($"invalid spinner: {rawSpinner} ({HudSpinners.AcceptedNamesList})");
            spinner = parsedSpinner;
        }
        var spec = new HudSpec(
            message,
            Detail: args.Detail,
            Spinner: spinner,
            BackgroundColor: args.Color,
            TextColor: args.TextColor,
            SizePercent: args.SizePercent,
            Position: position,
            HideAfter: args.HideAfter,
            Markdown: markdown,
            FontSize: args.FontSize,
            Sticky: args.Sticky == true,
            Frame: args.Frame ?? true);
        var placement = new ControlHudPlacement(pane, args.PaneId);
        return request.Cmd == Command.SessionHudOpen
            ? _actions.OpenHud(request.Target, args.Window, spec, placement)
            : _actions.UpdateHud(request.Target, args.Window, spec, placement);
    }

    private ControlResponse DispatchEventsRead(ControlRequest request)
    {
        var args = request.Args;
        ControlEventCursor? cursor;
        switch (args?.Run, args?.After)
        {
            case (null, null):
                cursor = null;
                break;
            case (not null, null) or (null, not null):
                return Err(ControlEventRequestError.CursorPair);
            case ({ } runText, { } afterText):
                if (!Guid.TryParseExact(runText, "D", out var run))
                    return Err(ControlEventRequestError.InvalidRun);
                if (!ulong.TryParse(afterText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                        out var after))
                    return Err(ControlEventRequestError.InvalidCursor);
                cursor = new ControlEventCursor(run, after);
                break;
        }

        var limit = args?.Limit ?? 100;
        if (limit is < 1 or > 1_000)
            return Err(ControlEventRequestError.InvalidLimit);

        var parsedKinds = new HashSet<ControlEventKind>();
        foreach (var field in args?.Kinds ?? Array.Empty<string>())
        {
            foreach (var component in field.Split(','))
            {
                var rawKind = component.Trim();
                var kind = ControlEventKinds.FromWire(rawKind);
                if (kind is null)
                    return Err(ControlEventRequestError.InvalidKind(rawKind));
                parsedKinds.Add(kind.Value);
            }
        }
        var kinds = parsedKinds.Count == 0 ? null : parsedKinds;
        return _actions.ReadEvents(new ControlEventReadOptions(cursor, kinds, limit));
    }

    private ControlResponse DispatchSessionCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.SessionNew:
            {
                var args = request.Args;
                if (args?.After is not null && args?.Before is not null)
                    return Err("use either --after or --before, not both");
                // The anchor sid carries its own workspace, so placement can't also name one.
                if ((args?.After is not null || args?.Before is not null)
                    && (args?.Workspace is not null || args?.WorkspaceName is not null))
                    return Err("session.new takes --after/--before or a workspace, not both");
                if (args?.Workspace is not null && args?.WorkspaceName is not null)
                    return Err("use either --workspace or --workspace-name, not both");
                if (args?.CreateWorkspace == true && args?.WorkspaceName is null)
                    return Err("--create-workspace requires --workspace-name");
                // --wait holds the surface after the command exits, so it is meaningless without a command.
                if (args?.Wait == true && args?.Command is null)
                    return Err("--wait requires --command");
                return _actions.CreateSession(new ControlSessionCreateOptions(
                    Window: args?.Window,
                    Cwd: args?.Cwd,
                    Workspace: args?.Workspace,
                    WorkspaceName: args?.WorkspaceName,
                    CreateWorkspace: args?.CreateWorkspace,
                    Command: args?.Command,
                    Wait: args?.Wait,
                    Name: args?.Name,
                    After: args?.After,
                    Before: args?.Before,
                    NoSelect: args?.NoSelect == true));
            }
            case Command.SessionDuplicate:
                // no options: the source session names its own workspace AND its cwd, so a duplicate is
                // fully described by the target.
                return _actions.DuplicateSession(request.Target, request.Args?.Window);
            case Command.SessionSelect:
                return _actions.SelectSession(request.Target, request.Args?.Window);
            case Command.SessionGo:
            {
                // unknown/missing to is a structured error.
                var dir = request.Args?.To is { } to ? SessionNavigations.FromWire(to) : null;
                if (dir is null)
                    return Err("session.go requires --to next|prev|first|last|next-attention|prev-attention");
                return _actions.GoSession(request.Args?.Window, dir.Value);
            }
            case Command.SessionClose:
            {
                if (request.Args?.Targets is { } targets)
                {
                    if (targets.Length == 0)
                        return Err("session.close requires at least one --target");
                    return _actions.CloseSessions(targets, request.Args?.Window);
                }
                return _actions.CloseSession(request.Target, request.Args?.Window);
            }
            case Command.SessionRename:
            {
                if (request.Args?.Name is not { } name)
                    return Err("session.rename requires a name");
                return _actions.RenameSession(request.Target, request.Args?.Window, name);
            }
            case Command.SessionReveal:
                return _actions.RevealSession(request.Target, request.Args?.Window);
            case Command.SessionMove:
                return DispatchSessionMoveCommand(request);
            case Command.SessionFlag:
                return _actions.SetSessionFlag(request.Target, request.Args?.Window, request.Args?.Mode);
            case Command.SessionSeen:
                return _actions.MarkSessionSeen(request.Target, request.Args?.Window);
            case Command.SessionStatus:
                return DispatchSessionStatus(request);
            case Command.SessionRestore:
                return DispatchSessionRestore(request);
            default:
                throw new InvalidOperationException($"unexpected session command: {request.Cmd}");
        }
    }

    private ControlResponse DispatchSessionMoveCommand(ControlRequest request)
    {
        var args = request.Args;
        if (args?.After is not null && args?.Before is not null)
            return Err("use either --after or --before, not both");
        // Placement mode: the anchor sid self-identifies the destination workspace, so it's mutually
        // exclusive with --to and with a workspace parameter.
        if ((args?.After ?? args?.Before) is { } anchor)
        {
            if (args?.To is not null)
                return Err("session.move takes --after/--before or --to, not both");
            if (args?.Workspace is not null)
                return Err("session.move takes --after/--before or a workspace, not both");
            var placeMove = new ControlSessionMove.Place(anchor, After: args?.After is not null);
            if (args?.Targets is { } placeTargets)
                return DispatchSessionMove(placeTargets, args?.Window, placeMove);
            return _actions.MoveSession(request.Target, args?.Window, placeMove);
        }
        if (args?.To is not null && args?.Workspace is not null)
            return Err("session.move takes either --to or a workspace, not both");
        if (args?.To is { } to)
        {
            var direction = ReorderDirections.FromWire(to);
            if (direction is null)
                return Err("session.move --to must be up|down|top|bottom");
            if (args?.Targets is not null)
                return Err("session.move --target can be repeated only with a workspace or --after/--before");
            return _actions.MoveSession(request.Target, args?.Window,
                new ControlSessionMove.Reorder(direction.Value));
        }
        if (args?.Workspace is not { } workspace)
            return Err("session.move requires --to or a workspace");
        var move = new ControlSessionMove.Workspace(workspace);
        if (args?.Targets is { } workspaceTargets)
            return DispatchSessionMove(workspaceTargets, args?.Window, move);
        return _actions.MoveSession(request.Target, args?.Window, move);
    }

    private ControlResponse DispatchSessionStatus(ControlRequest request)
    {
        var status = AgentStatuses.FromWire(request.Args?.Status ?? "");
        if (status is null)
            return Err("invalid status");
        if (request.Args?.Color is { } color && !WatermarkConfig.IsValidColorHex(color))
            return Err("invalid color (expected #rrggbb)");
        StatusShape? shape = null;
        if (request.Args?.Shape is { } rawShape)
        {
            var parsed = StatusShapes.FromWire(rawShape);
            if (parsed is null)
                return Err($"invalid shape: {rawShape} ({StatusShapes.ValidNamesList})");
            shape = parsed;
        }
        if (!TryParsePane(request.Args?.Pane, out var pane, out var rejection))
            return rejection;
        var update = new ControlSessionStatusUpdate(status.Value, Blink: request.Args?.Blink,
            AutoReset: request.Args?.AutoReset,
            Sound: request.Args?.Sound, Color: request.Args?.Color,
            Shape: shape,
            Pane: pane, PaneId: request.Args?.PaneId);
        return _actions.SetSessionStatus(request.Target, request.Args?.Window, update);
    }

    /// <summary>
    /// session.restore: parse the set|none|clear mode into a ControlRestoreOverride and the pane
    /// selector into a StatusPane, then hand both to the host. A pinned command is validated but NEVER
    /// rewritten — it is a shell line, so metacharacters are the point; it is rejected only for being
    /// absent, carrying control characters, or exceeding the storage cap. An EMPTY command is the same
    /// pinned-to-nothing state as none. paneID rides through opaquely (the dispatcher has no session
    /// to resolve it against).
    /// </summary>
    private ControlResponse DispatchSessionRestore(ControlRequest request)
    {
        var args = request.Args;
        ControlRestoreOverride pin;
        switch (args?.Mode ?? "")
        {
            case "set":
            {
                if (args?.Command is not { } command)
                    return Err("session.restore set requires a command");
                // a control character would smuggle an extra line (or an escape sequence) into the
                // shell the override is typed into, so the whole class is rejected — tab included.
                if (CommandRestore.HasControlCharacter(command))
                    return Err("command must not contain control characters");
                if (System.Text.Encoding.UTF8.GetByteCount(command) > ControlRestoreOverride.MaxCommandBytes)
                    return Err($"command too long (max {ControlRestoreOverride.MaxCommandBytes} bytes)");
                pin = new ControlRestoreOverride.Pin(command);
                break;
            }
            case "none":
                pin = new ControlRestoreOverride.PinNone();
                break;
            case "clear":
                pin = new ControlRestoreOverride.Unpin();
                break;
            default:
                return Err($"invalid restore mode: {args?.Mode ?? ""} (set|none|clear)");
        }
        if (!TryParsePane(args?.Pane, out var pane, out var rejection))
            return rejection;
        var update = new ControlSessionRestoreUpdate(pin, Pane: pane, PaneId: args?.PaneId);
        return _actions.SetSessionRestore(request.Target, args?.Window, update);
    }

    /// <summary>
    /// The shared --pane role selector (session.status, session.restore): pane is null when the
    /// selector was absent, the parsed pane when valid; returns false with the pinned rejection when
    /// the token names no pane.
    /// </summary>
    private static bool TryParsePane(string? raw, out StatusPane? pane,
                                     out ControlResponse rejection)
    {
        rejection = Err("--pane must be left, right, or scratch");
        pane = null;
        if (raw is null) return true;
        var parsed = StatusPanes.FromWire(raw);
        if (parsed is null) return false;
        pane = parsed;
        return true;
    }

    private ControlResponse DispatchSessionMove(IReadOnlyList<string> targets, string? window,
                                                ControlSessionMove move)
    {
        if (targets.Count == 0)
            return Err("session.move requires at least one --target");
        if (targets.Count == 1)
            return _actions.MoveSession(targets[0], window, move);
        return _actions.MoveSessions(targets, window, move);
    }

    private ControlResponse DispatchWorkspaceCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.WorkspaceNew:
                return _actions.CreateWorkspace(request.Args?.Window, request.Args?.Name,
                    request.Args?.Collapsed ?? false);
            case Command.WorkspaceSelect:
                return _actions.SelectWorkspace(request.Target, request.Args?.Window);
            case Command.WorkspaceRename:
            {
                if (Session.TrimmedOrNull(request.Args?.Name) is not { } name)
                    return Err("workspace.rename requires a name");
                return _actions.RenameWorkspace(request.Target, request.Args?.Window, name);
            }
            case Command.WorkspaceTheme:
                return _actions.SetWorkspaceTheme(request.Target, request.Args?.Window, request.Args?.Name);
            case Command.WorkspaceDelete:
                return _actions.DeleteWorkspace(request.Target, request.Args?.Window);
            case Command.WorkspaceMove:
            {
                if (request.Args?.To is not { } to)
                    return Err("workspace.move requires --to");
                var direction = ReorderDirections.FromWire(to);
                if (direction is null)
                    return Err("workspace.move --to must be up|down|top|bottom");
                return _actions.MoveWorkspace(request.Target, request.Args?.Window, direction.Value);
            }
            case Command.WorkspaceFocus:
                return _actions.FocusWorkspace(request.Target, request.Args?.Window, request.Args?.Mode);
            case Command.WorkspaceCollapse:
                return _actions.SetWorkspaceExpansion(request.Target, request.Args?.Window, expanded: false);
            case Command.WorkspaceExpand:
                return _actions.SetWorkspaceExpansion(request.Target, request.Args?.Window, expanded: true);
            default:
                throw new InvalidOperationException($"unexpected workspace command: {request.Cmd}");
        }
    }

    private async Task<ControlResponse> DispatchSessionSurfaceCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.SessionSplit:
                return _actions.SplitSession(request.Target, request.Args?.Window, request.Args?.Mode);
            case Command.SessionScratch:
                return _actions.ScratchSession(request.Target, request.Args?.Window, request.Args?.Mode,
                    request.Args?.Command);
            case Command.SessionFocus:
                return _actions.FocusSessionPane(request.Target, request.Args?.Window, request.Args?.Pane);
            case Command.SessionResize:
                return (request.Args?.Ratio, request.Args?.RatioDelta) switch
                {
                    (null, null) => Err("session.resize requires --split-ratio, --grow-left, or --grow-right"),
                    (not null, not null) =>
                        Err("session.resize: --split-ratio is mutually exclusive with --grow-left/--grow-right"),
                    ({ } ratio, null) => _actions.ResizeSplit(request.Target, request.Args?.Window,
                        new ControlSplitResize.Ratio(ratio)),
                    (null, { } delta) => _actions.ResizeSplit(request.Target, request.Args?.Window,
                        new ControlSplitResize.Delta(delta)),
                };
            case Command.SurfaceZoom:
            {
                var mode = ControlToggleModes.Parse(request.Args?.Mode, onToken: "show", offToken: "hide");
                if (mode is null)
                    return Err($"invalid surface zoom mode: {request.Args?.Mode ?? "toggle"}");
                return _actions.SetSurfaceZoom(request.Target, request.Args?.Window, mode.Value);
            }
            case Command.SessionType:
            {
                if (request.Args?.Text is not { } text)
                    return Err("session.type requires text");
                if (NulRejection(text) is { } nul)
                    return nul;
                if (SurfacePaneRejection(request.Args?.Pane) is { } badPane)
                    return badPane;
                return await _actions.TypeSession(request.Target, request.Args?.Window,
                    new ControlSessionTypeOptions(text,
                        Select: request.Args?.Select ?? false,
                        Pane: request.Args?.Pane));
            }
            case Command.SessionCopy:
                return _actions.CopySessionSelection(request.Target, request.Args?.Window);
            case Command.SessionPaste:
                return _actions.PasteSession(request.Target, request.Args?.Window);
            case Command.SessionSelectAll:
                return _actions.SelectAllSession(request.Target, request.Args?.Window);
            case Command.SessionSearch:
                return await _actions.SearchSession(request.Target, request.Args?.Window,
                    request.Args?.Text, request.Args?.To);
            case Command.SessionOverlayOpen:
            {
                if (request.Args?.Command is not { Length: > 0 } command)
                    return Err("session.overlay.open requires a command");
                if (request.Args?.Color is { } color && !WatermarkConfig.IsValidColorHex(color))
                    return Err($"invalid color: {color} (#rrggbb)");
                if (request.Args?.SizePercent is < 1 or > 100)
                    return Err("session.overlay.open: --size-percent must be 1...100");
                return _actions.OpenSessionOverlay(request.Target, request.Args?.Window,
                    new ControlSessionOverlayOpenOptions(command,
                        Cwd: request.Args?.Cwd,
                        Wait: request.Args?.Wait ?? false,
                        SizePercent: request.Args?.SizePercent,
                        BackgroundColor: request.Args?.Color,
                        Follow: request.Args?.Follow ?? false));
            }
            case Command.SessionOverlayClose:
                return _actions.CloseSessionOverlay(request.Target, request.Args?.Window);
            case Command.SessionOverlayResize:
            {
                var wantsFull = request.Args?.Full == true;
                var percent = request.Args?.SizePercent;
                if (wantsFull && percent is not null)
                    return Err("session.overlay.resize: --full is mutually exclusive with --size-percent");
                if (!wantsFull && percent is null)
                    return Err("session.overlay.resize requires --size-percent or --full");
                if (percent is < 1 or > 100)
                    return Err("session.overlay.resize: --size-percent must be 1...100");
                return _actions.ResizeSessionOverlay(request.Target, request.Args?.Window,
                    wantsFull ? null : percent);
            }
            case Command.SessionOverlayResult:
                return _actions.SessionOverlayResult(request.Target, request.Args?.Window);
            case Command.SessionBackground:
                return DispatchSessionBackground(request);
            case Command.SessionText:
                return DispatchSessionText(request);
            default:
                throw new InvalidOperationException($"unexpected session surface command: {request.Cmd}");
        }
    }

    private ControlResponse DispatchAppCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.FontInc:
                return DispatchFont(request, "increase_font_size:1");
            case Command.FontDec:
                return DispatchFont(request, "decrease_font_size:1");
            case Command.FontReset:
                return DispatchFont(request, "reset_font_size");
            case Command.Quick:
                return _actions.SetQuickTerminal(request.Args?.Mode);
            case Command.KeymapReload:
                return _actions.ReloadKeymap();
            case Command.ConfigReload:
                return _actions.ReloadGhosttyConfig();
            case Command.Notify:
            {
                if (request.Args?.Body is not { Length: > 0 } body)
                    return Err("notify requires a body");
                return _actions.SendNotification(request.Target, request.Args?.Window,
                    request.Args?.Title, body);
            }
            case Command.ThemeSet:
                return _actions.SetTheme(request.Args);
            case Command.ThemeList:
                return _actions.ListThemes();
            case Command.Sidebar:
            {
                var mode = ControlToggleModes.Parse(request.Args?.Mode, onToken: "show", offToken: "hide");
                if (mode is null)
                    return Err($"invalid sidebar mode: {request.Args?.Mode ?? "toggle"}");
                return _actions.SetSidebarVisibility(mode.Value);
            }
            case Command.SidebarMode:
            {
                var mode = ControlSidebarViewModes.Parse(request.Args?.Mode);
                if (mode is null)
                    return Err($"invalid sidebar mode: {request.Args?.Mode ?? "toggle"}");
                return _actions.SetSidebarViewMode(mode.Value);
            }
            case Command.SidebarExpand:
                return _actions.ExpandSidebar(request.Args?.Window);
            case Command.SidebarCollapse:
                return _actions.CollapseSidebar(request.Args?.Window);
            case Command.RestoreClear:
                return _actions.ClearRestoreCommands();
            default:
                throw new InvalidOperationException($"unexpected app command: {request.Cmd}");
        }
    }

    /// <summary>The quick-terminal input/read commands, async because the app side polls briefly for
    /// the surface to mount + realize after quick show (the twin of session.type/session.text).</summary>
    private async Task<ControlResponse> DispatchQuickCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.QuickType:
            {
                if (request.Args?.Text is not { } text)
                    return Err("quick.type requires text");
                if (NulRejection(text) is { } nul)
                    return nul;
                return await _actions.TypeQuick(text);
            }
            case Command.QuickText:
            {
                var all = request.Args?.All ?? false;
                var lines = request.Args?.Lines;
                if (all && lines is not null)
                    return Err("use either --all or --lines, not both");
                if (lines is <= 0)
                    return Err("--lines must be greater than 0");
                return await _actions.ReadQuickText(all, lines);
            }
            default:
                throw new InvalidOperationException($"unexpected quick command: {request.Cmd}");
        }
    }

    private ControlResponse DispatchSessionBackground(ControlRequest request)
    {
        // The args bag is normalized into the option struct here so the app-side adapter stays a small
        // fixed-arity signature rather than a 10-parameter dispatch.
        if (request.Args?.Fit is { } fit && !WatermarkConfig.IsValidFit(fit))
            return Err($"invalid fit: {fit} (contain|cover|stretch|none)");
        if (request.Args?.Position is { } position && !WatermarkConfig.IsValidPosition(position))
            return Err($"invalid position: {position}");
        if (request.Args?.Opacity is { } opacity && !WatermarkConfig.IsValidOpacity(opacity))
            return Err($"invalid opacity: {SwiftDouble(opacity)} (0.0-1.0)");
        BackgroundWatermark? watermark;
        switch (request.Args?.Mode)
        {
            case "image":
            {
                if (request.Args?.Path is not { Length: > 0 } path)
                    return Err("session.background image requires a path");
                if (!WatermarkConfig.IsValidImagePath(path))
                    return Err("image path must not contain control characters");
                watermark = new BackgroundWatermark
                {
                    Kind = WatermarkKind.Image, ImagePath = path, Opacity = request.Args?.Opacity,
                    Fit = request.Args?.Fit is { } f ? WatermarkFits.FromWire(f) : null,
                    Position = request.Args?.Position is { } p ? WatermarkPositions.FromWire(p) : null,
                    Repeats = request.Args?.Repeats,
                };
                break;
            }
            case "text":
            {
                if (request.Args?.Text is not { Length: > 0 } text)
                    return Err("session.background text requires text");
                if (WatermarkConfig.TextElementCount(text) > WatermarkConfig.MaxTextLength)
                    return Err($"session.background text too long (max {WatermarkConfig.MaxTextLength} characters)");
                if (request.Args?.Color is { } textColor && !WatermarkConfig.IsValidColorHex(textColor))
                    return Err($"invalid color: {textColor} (#rrggbb)");
                watermark = new BackgroundWatermark
                {
                    Kind = WatermarkKind.Text, Text = text, ColorHex = request.Args?.Color,
                    Opacity = request.Args?.Opacity,
                    Fit = request.Args?.Fit is { } f ? WatermarkFits.FromWire(f) : null,
                    Position = request.Args?.Position is { } p ? WatermarkPositions.FromWire(p) : null,
                };
                break;
            }
            case "color":
            {
                // No per-call opacity: a solid color honors the window translucency set in Settings.
                if (request.Args?.Color is not { Length: > 0 } color)
                    return Err("session.background color requires a color");
                if (!WatermarkConfig.IsValidColorHex(color))
                    return Err($"invalid color: {color} (#rrggbb)");
                watermark = new BackgroundWatermark { Kind = WatermarkKind.Color, ColorHex = color };
                break;
            }
            case "clear":
            case null:
                watermark = null;
                break;
            default:
                return Err($"invalid background mode: {request.Args?.Mode} (image|text|color|clear)");
        }
        return _actions.SetSessionBackground(request.Target, request.Args?.Window,
            new ControlSessionBackgroundOptions(watermark));
    }

    private ControlResponse DispatchFont(ControlRequest request, string action)
    {
        if (SurfacePaneRejection(request.Args?.Pane) is { } badPane)
            return badPane;
        return _actions.Font(request.Target, request.Args?.Window, request.Args?.Pane, action);
    }

    private ControlResponse DispatchSessionText(ControlRequest request)
    {
        var all = request.Args?.All ?? false;
        var lines = request.Args?.Lines;
        if (all && lines is not null)
            return Err("use either --all or --lines, not both");
        if (lines is <= 0)
            return Err("--lines must be greater than 0");
        if (SurfacePaneRejection(request.Args?.Pane) is { } badPane)
            return badPane;
        return _actions.ReadSessionText(request.Target, request.Args?.Window,
            new ControlSessionTextOptions(request.Args?.Pane, all, lines));
    }

    private async Task<ControlResponse> DispatchWindowCommand(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case Command.WindowNew:
                return _actions.WindowNew(request.Args?.Name);
            case Command.WindowList:
                return _actions.WindowList();
            case Command.WindowSelect:
                return await _actions.WindowSelect(request.Target);
            case Command.WindowClose:
                return await _actions.WindowClose(request.Target);
            case Command.WindowRename:
            {
                if (Session.TrimmedOrNull(request.Args?.Name) is not { } name)
                    return Err("window.rename requires a name");
                return _actions.WindowRename(request.Target, name);
            }
            case Command.WindowDelete:
                return _actions.WindowDelete(request.Target);
            case Command.WindowResize:
            {
                if (request.Args?.Width is not { } width || request.Args?.Height is not { } height
                    || width <= 0 || height <= 0)
                    return Err("window.resize requires positive width and height");
                return _actions.WindowResize(request.Target, width, height);
            }
            case Command.WindowMove:
            {
                if (request.Args?.X is not { } x || request.Args?.Y is not { } y)
                    return Err("window.move requires x and y");
                return _actions.WindowMove(request.Target, x, y, request.Args?.Display);
            }
            case Command.WindowZoom:
                return _actions.WindowZoom(request.Target);
            case Command.WindowFullscreen:
                return _actions.WindowFullscreen(request.Target);
            default:
                throw new InvalidOperationException($"unexpected window command: {request.Cmd}");
        }
    }

    /// <summary>
    /// The dashboard overlay is host-free-validated here. The open path needs at least one id (or
    /// --mru) and at most one font flag; --close takes no id, --mru, or font flag; a --font-size must
    /// be finite and positive; --mru cannot be combined with explicit ids (but composes with the font
    /// flags). The 9-cell cap is NOT applied here: the cap counts PANES, which needs the store, so it
    /// lives host-side behind SetDashboard; this only forwards the raw ids.
    /// </summary>
    private ControlResponse DispatchDashboard(ControlRequest request)
    {
        var args = request.Args;
        var targets = args?.Targets ?? Array.Empty<string>();
        var fontSize = args?.FontSize;
        var autoSize = args?.AutoSize ?? false;
        var mru = args?.Mru ?? false;

        if (args?.Close == true)
        {
            if (targets.Length != 0 || mru || fontSize is not null || autoSize)
                return Err("dashboard --close takes no ids, --mru, or font options");
            return _actions.SetDashboard(Array.Empty<string>(), args?.Window, close: true,
                new DashboardFontMode.Untouched(), mru: false);
        }

        if (fontSize is not null && autoSize)
            return Err("dashboard: --font-size is mutually exclusive with --auto-size");
        if (fontSize is { } size && (!double.IsFinite(size) || size <= 0))
            return Err("dashboard --font-size must be a positive number");
        DashboardFontMode fontMode = autoSize
            ? new DashboardFontMode.Auto()
            : fontSize is { } fixedSize
                ? new DashboardFontMode.Fixed(fixedSize)
                : new DashboardFontMode.Untouched();
        if (mru)
        {
            // --mru supplies the members host-side from the window's recency, so it takes no explicit
            // ids; the font flags still apply.
            if (targets.Length != 0)
                return Err("dashboard --mru cannot be combined with explicit session ids");
            return _actions.SetDashboard(Array.Empty<string>(), args?.Window, close: false, fontMode, mru: true);
        }
        if (targets.Length == 0)
            return Err("dashboard requires at least one session id");
        return _actions.SetDashboard(targets, args?.Window, close: false, fontMode, mru: false);
    }

    /// <summary>Swift's default Double interpolation for error messages: an integral value keeps a
    /// trailing .0 (2 → "2.0"), a fractional one prints the shortest round-trip form.</summary>
    private static string SwiftDouble(double value)
    {
        if (double.IsFinite(value) && value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.InvariantCulture) + ".0";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
