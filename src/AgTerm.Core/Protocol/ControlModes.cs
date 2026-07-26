using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

/// <summary>Host-facing events.read options after dispatcher validation and normalization.</summary>
public sealed class ControlEventReadOptions : IEquatable<ControlEventReadOptions>
{
    public ControlEventReadOptions(ControlEventCursor? cursor, IReadOnlyCollection<ControlEventKind>? kinds, int limit)
    {
        Cursor = cursor;
        Kinds = kinds is null ? null : new HashSet<ControlEventKind>(kinds);
        Limit = limit;
    }

    public ControlEventCursor? Cursor { get; }

    /// <summary>The kind filter as a SET (order-free, like the Swift Set), or null = no filter.</summary>
    public IReadOnlySet<ControlEventKind>? Kinds { get; }

    public int Limit { get; }

    public bool Equals(ControlEventReadOptions? other) =>
        other is not null
        && Nullable.Equals(Cursor, other.Cursor)
        && (Kinds is null ? other.Kinds is null : other.Kinds is not null && Kinds.SetEquals(other.Kinds))
        && Limit == other.Limit;

    public override bool Equals(object? obj) => Equals(obj as ControlEventReadOptions);

    public override int GetHashCode() => HashCode.Combine(Cursor, Limit, Kinds?.Count ?? -1);
}

/// <summary>Parsed binary control mode with the shared default/toggle semantics used by
/// mode-bearing commands.</summary>
public enum ControlToggleMode { On, Off, Toggle }

public static class ControlToggleModes
{
    /// <summary>Parse a mode string, defaulting null to toggle. Callers keep their command-specific
    /// tokens and error strings by choosing the true/false spellings they already expose on the wire.</summary>
    public static ControlToggleMode? Parse(string? mode, string onToken = "on", string offToken = "off")
    {
        var value = mode ?? "toggle";
        if (value == onToken) return ControlToggleMode.On;
        if (value == offToken) return ControlToggleMode.Off;
        if (value == "toggle") return ControlToggleMode.Toggle;
        return null;
    }

    public static bool DesiredValue(this ControlToggleMode mode, bool current) => mode switch
    {
        ControlToggleMode.On => true,
        ControlToggleMode.Off => false,
        ControlToggleMode.Toggle => !current,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

/// <summary>Parsed pane selector for session.focus, including the default/toggle aliases.</summary>
public enum ControlPaneFocusMode { Primary, Split, Toggle }

public static class ControlPaneFocusModes
{
    public static ControlPaneFocusMode? Parse(string? pane) => (pane ?? "other") switch
    {
        "left" or "primary" => ControlPaneFocusMode.Primary,
        "right" or "split" => ControlPaneFocusMode.Split,
        "other" or "toggle" => ControlPaneFocusMode.Toggle,
        _ => null,
    };

    public static bool WantsSplit(this ControlPaneFocusMode mode, bool currentSplitFocused) => mode switch
    {
        ControlPaneFocusMode.Primary => false,
        ControlPaneFocusMode.Split => true,
        ControlPaneFocusMode.Toggle => !currentSplitFocused,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

/// <summary>Parsed view mode for sidebar.mode.</summary>
public enum ControlSidebarViewMode { Tree, Flagged, Toggle }

public static class ControlSidebarViewModes
{
    public static ControlSidebarViewMode? Parse(string? mode) => (mode ?? "toggle") switch
    {
        "tree" => ControlSidebarViewMode.Tree,
        "flagged" => ControlSidebarViewMode.Flagged,
        "toggle" => ControlSidebarViewMode.Toggle,
        _ => null,
    };
}

/// <summary>The mutually exclusive move forms accepted by session.move.</summary>
public abstract record ControlSessionMove
{
    private ControlSessionMove()
    {
    }

    public sealed record Reorder(ReorderDirection Direction) : ControlSessionMove;

    public sealed record Workspace(string Name) : ControlSessionMove;

    /// <summary>Relocate + position relative to an anchor session (id / prefix / "active").
    /// After == false places before the anchor. The anchor carries its own workspace, so this form
    /// self-identifies the destination and never reads the workspace parameter.</summary>
    public sealed record Place(string Anchor, bool After) : ControlSessionMove;
}

/// <summary>Parsed resize request for session.resize.</summary>
public abstract record ControlSplitResize
{
    private ControlSplitResize()
    {
    }

    public sealed record Ratio(double Value) : ControlSplitResize;

    public sealed record Delta(double Value) : ControlSplitResize;
}

/// <summary>Host-facing session.new options after dispatcher-level guard validation.</summary>
/// <param name="Wait">Whether a --command session HOLDS its surface after the command exits (--wait)
/// instead of closing immediately. Meaningful only with a command; the dispatcher rejects --wait
/// without a --command.</param>
/// <param name="After">Anchor session to place the new session right AFTER (id / prefix / "active");
/// the anchor carries its own workspace, so this bypasses workspace/workspaceName. Mutually
/// exclusive with Before.</param>
/// <param name="Before">Anchor session to place the new session right BEFORE, the mirror of After.</param>
/// <param name="NoSelect">Create the session in the background: skip selecting and focusing it,
/// leaving the current selection untouched (the CLI's --no-select).</param>
public sealed record ControlSessionCreateOptions(
    string? Window, string? Cwd, string? Workspace, string? WorkspaceName,
    bool? CreateWorkspace, string? Command, bool? Wait = null, string? Name = null,
    string? After = null, string? Before = null, bool NoSelect = false);

/// <summary>
/// Parsed session.status payload. Sound validation and playback stay host-side; Color is the
/// per-call #rrggbb glyph-tint override and Shape the per-call silhouette override (both validated
/// in the dispatcher), threaded onto the ephemeral AgentIndicator.
/// </summary>
/// <param name="Pane">Which pane set the status (left=main, right=split, scratch), or null when
/// unspecified.</param>
/// <param name="PaneId">The surface's STABLE spawn token (the shell's baked AGTERM_PANE_ID). Resolved
/// app-side against the session's live surfaces; when it resolves it OVERRIDES the stale role Pane.
/// The resolution stays app-side because the dispatcher has no session — this only carries the token
/// through.</param>
public sealed record ControlSessionStatusUpdate(
    AgentStatus Status, bool? Blink, bool? AutoReset, string? Sound,
    string? Color = null, StatusShape? Shape = null,
    StatusPane? Pane = null, string? PaneId = null);

/// <summary>
/// The three forms of the session.restore per-pane restore-command override, parsed from the wire
/// tokens set / none / clear.
/// </summary>
public abstract record ControlRestoreOverride
{
    /// <summary>The storage bound on a pinned command, in UTF-8 BYTES (not graphemes) — the value
    /// persists in the window snapshot, so the cap guards the snapshot, not the display width.</summary>
    public const int MaxCommandBytes = 1024;

    private ControlRestoreOverride()
    {
    }

    /// <summary>wire "set" — pin this shell line, run verbatim on the next launch.</summary>
    public sealed record Pin(string Command) : ControlRestoreOverride;

    /// <summary>wire "none" — pin the pane to nothing, so it restores a plain shell.</summary>
    public sealed record PinNone : ControlRestoreOverride;

    /// <summary>wire "clear" — drop the pin, back to the captured-foreground auto-restore.</summary>
    public sealed record Unpin : ControlRestoreOverride;
}

/// <summary>
/// Parsed session.restore payload. The pane resolution stays host-side: PaneId is carried through
/// opaquely and resolved app-side against the session's LIVE surfaces, falling back to the baked
/// role Pane — and, unlike session.status, an unresolvable token with no explicit pane is an error
/// rather than a silent main-pane default.
/// </summary>
public sealed record ControlSessionRestoreUpdate(
    ControlRestoreOverride Pin, StatusPane? Pane = null, string? PaneId = null);
