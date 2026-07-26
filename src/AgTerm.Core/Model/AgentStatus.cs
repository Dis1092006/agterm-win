using System.Text.Json.Serialization;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>
/// The per-session agent state driven over the control channel (session.status). Idle means nothing
/// is shown; the other states each render a tinted sidebar glyph.
/// </summary>
[JsonConverter(typeof(AgentStatusConverter))]
public enum AgentStatus { Idle, Active, Completed, Blocked }

public sealed class AgentStatusConverter : WireEnumConverter<AgentStatus>
{
    public AgentStatusConverter() : base(
        (AgentStatus.Idle, "idle"), (AgentStatus.Active, "active"),
        (AgentStatus.Completed, "completed"), (AgentStatus.Blocked, "blocked"))
    {
    }
}

public static class AgentStatuses
{
    /// <summary>The Swift raw-value parse: the wire token, or null for an unknown string.</summary>
    public static AgentStatus? FromWire(string wire) => wire switch
    {
        "idle" => AgentStatus.Idle,
        "active" => AgentStatus.Active,
        "completed" => AgentStatus.Completed,
        "blocked" => AgentStatus.Blocked,
        _ => null,
    };
}

public static class StatusPanes
{
    /// <summary>The Swift raw-value parse: the wire token, or null for an unknown string.</summary>
    public static StatusPane? FromWire(string wire) => wire switch
    {
        "left" => StatusPane.Left,
        "right" => StatusPane.Right,
        "scratch" => StatusPane.Scratch,
        _ => null,
    };
}

public static class AgentStatusExtensions
{
    /// <summary>True for the states that need user attention (a blocked prompt or a completed run) —
    /// the set attention navigation steps through, excluding idle (no glyph) and active (still working).</summary>
    public static bool NeedsAttention(this AgentStatus status) =>
        status is AgentStatus.Blocked or AgentStatus.Completed;

    /// <summary>Sort priority for the attention list: lower comes first. Blocked (0) is most urgent,
    /// then active (1), then completed (2); idle (3) is filtered out before the list is built.</summary>
    public static int AttentionRank(this AgentStatus status) => status switch
    {
        AgentStatus.Blocked => 0,
        AgentStatus.Active => 1,
        AgentStatus.Completed => 2,
        _ => 3,
    };

    /// <summary>
    /// Whether a keystroke in the session's terminal should clear this glyph back to idle: blocked and
    /// completed clear on ANY key; active clears ONLY on an interrupt keystroke (Escape or Ctrl-C), so
    /// ordinary typing while the agent works does not wipe the "working" glyph. Idle has no glyph.
    /// </summary>
    internal static bool ClearedByKeystroke(this AgentStatus status, bool isInterrupt) => status switch
    {
        AgentStatus.Blocked or AgentStatus.Completed => true,
        AgentStatus.Active => isInterrupt,
        _ => false,
    };

    /// <summary>
    /// The sound to play for a session.status call, or null for none. An explicit per-call sound always
    /// wins; otherwise the configured blocked default plays, but ONLY for the blocked state. Empty
    /// strings count as unset. The host resolves the returned name; this is just the precedence decision.
    /// </summary>
    public static string? EffectiveSound(this AgentStatus status, string? perCall, string? blockedDefault)
    {
        if (!string.IsNullOrEmpty(perCall)) return perCall;
        if (status == AgentStatus.Blocked && !string.IsNullOrEmpty(blockedDefault)) return blockedDefault;
        return null;
    }
}

/// <summary>
/// Which pane of a session set the current agent status, using the same left|right|scratch vocabulary
/// as the --pane control argument (left = main, right = split).
/// </summary>
[JsonConverter(typeof(StatusPaneConverter))]
public enum StatusPane { Left, Right, Scratch }

public sealed class StatusPaneConverter : WireEnumConverter<StatusPane>
{
    public StatusPaneConverter() : base(
        (StatusPane.Left, "left"), (StatusPane.Right, "right"), (StatusPane.Scratch, "scratch"))
    {
    }
}

/// <summary>
/// The silhouette a status glyph draws; circle is what an unset status draws. Raw values ride the
/// wire as "circle"|"square"|… .
/// </summary>
[JsonConverter(typeof(StatusShapeConverter))]
public enum StatusShape { Circle, Square, Triangle, Diamond, Capsule, Star }

public sealed class StatusShapeConverter : WireEnumConverter<StatusShape>
{
    public StatusShapeConverter() : base(
        (StatusShape.Circle, "circle"), (StatusShape.Square, "square"), (StatusShape.Triangle, "triangle"),
        (StatusShape.Diamond, "diamond"), (StatusShape.Capsule, "capsule"), (StatusShape.Star, "star"))
    {
    }
}

public static class StatusShapes
{
    private static readonly string[] ValidNames = { "circle", "square", "triangle", "diamond", "capsule", "star" };

    /// <summary>The accepted names pipe-joined — the compact form the control server's rejection
    /// message uses.</summary>
    public static string ValidNamesList => string.Join("|", ValidNames);

    /// <summary>The accepted names comma-joined — the prose form the CLI help text uses.</summary>
    public static string ValidNamesPhrase => string.Join(", ", ValidNames);

    /// <summary>The Swift raw-value parse: the wire token, or null for an unknown string.</summary>
    public static StatusShape? FromWire(string wire) => wire switch
    {
        "circle" => StatusShape.Circle,
        "square" => StatusShape.Square,
        "triangle" => StatusShape.Triangle,
        "diamond" => StatusShape.Diamond,
        "capsule" => StatusShape.Capsule,
        "star" => StatusShape.Star,
        _ => null,
    };
}

/// <summary>
/// The per-session agent status value: the state plus optional blink (pulse for attention),
/// autoReset (clear back to idle once the session is visited), per-call color and shape overrides,
/// and the pane that set the status. Ephemeral (never persisted); set only via the control API.
/// </summary>
public readonly record struct AgentIndicator(
    AgentStatus Status = AgentStatus.Idle,
    bool Blink = false,
    bool AutoReset = false,
    string? Color = null,
    StatusShape? Shape = null,
    StatusPane? StatusPane = null)
{
    /// <summary>
    /// Whether a keystroke from the pane should clear this indicator back to idle: only when the
    /// keystroke's own pane owns the current status (a null StatusPane is treated as Left) AND the
    /// status itself is clearable by that keystroke. Keeps foreground typing from wiping a status set
    /// by a background pane.
    /// </summary>
    public bool ClearedBy(StatusPane pane, bool isInterrupt) =>
        (StatusPane ?? Model.StatusPane.Left) == pane && Status.ClearedByKeystroke(isInterrupt);
}
