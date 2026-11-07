using System.Text.Json;
using System.Text.RegularExpressions;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Agents;

/// <summary>Which agent hook fired: the session started (or changed id) or ended.</summary>
public enum AgentHookEvent { Start, End }

public enum AgentResumeVerb { Ignore, Pin, ClearIfPinned }

/// <summary>What a resume hook does with the pane's restore command: pin Command, clear the pin if
/// it still is Command (or LegacyCommand, the same session's pin from an earlier version), or nothing.</summary>
public readonly record struct AgentResumeDecision(AgentResumeVerb Verb, string? Command, string? LegacyCommand = null)
{
    public static AgentResumeDecision Ignore => default;

    public static AgentResumeDecision Pin(string command) => new(AgentResumeVerb.Pin, command);

    public static AgentResumeDecision ClearIfPinned(string command, string? legacyCommand = null) =>
        new(AgentResumeVerb.ClearIfPinned, command, legacyCommand);
}

/// <summary>A pane's restore command as tree reports it; Found is false when the session is not in
/// the tree.</summary>
public readonly record struct AgentResumePane(bool Found, string? RestoreCommand);

/// <summary>How one agent CLI reports its session in a hook payload, and the shell line that
/// resumes it.</summary>
/// <param name="IdField">The payload field that carries the session id.</param>
/// <param name="PinPrefix">The resume command without the id.</param>
/// <param name="ClearReasons">End reasons that mean the user left the agent; null when every end
/// clears.</param>
/// <param name="SkipsSubagents">Whether a payload with agentId or parentAgentId comes from a
/// subagent that runs inside its parent's session.</param>
/// <param name="LegacyPinPrefix">The resume command an earlier version pinned, which end still clears.</param>
/// <param name="IgnoredWhenSet">An environment variable that marks a hook run outside the agent's pane.</param>
public sealed record AgentResumeProfile(
    string Name,
    string DisplayName,
    string IdField,
    string PinPrefix,
    IReadOnlyList<string>? ClearReasons,
    bool SkipsSubagents,
    string? LegacyPinPrefix = null,
    string? IgnoredWhenSet = null)
{
    public static IReadOnlyList<AgentResumeProfile> All { get; } =
    [
        // SessionEnd reason "other" is the pane closing, which an app quit does to every pane.
        new("claude", "Claude Code", "session_id", "claude --resume ", ["prompt_input_exit", "logout"], SkipsSubagents: false),
        // sessionEnd reason user_exit covers /exit, /clear, Ctrl+C, Ctrl+D and the pane closing;
        // complete is a copilot -p run.
        new("copilot", "Copilot CLI", "sessionId", "copilot --resume=", ["user_exit", "complete"], SkipsSubagents: true),
        // Codex sends SessionEnd only with reason "other". Codex 0.167 runs sessions and hooks in a shared
        // app-server that carries the AGTERM_* of the pane that started it, so a hook from it would pin
        // another pane (measured with 0.167.0). --no-daemon keeps a resumed session in its own pane.
        new("codex", "Codex", "session_id", "codex --no-daemon resume ", ClearReasons: null, SkipsSubagents: false,
            LegacyPinPrefix: "codex resume ", IgnoredWhenSet: "CODEX_DAEMON_SHUTDOWN_SOCKET"),
    ];

    public static AgentResumeProfile? Find(string name) => All.FirstOrDefault(agent => agent.Name == name);

    public string PinFor(string sessionId) => PinPrefix + sessionId;
}

public static partial class AgentResumeHook
{
    // The pin is shell code typed on every launch: only a UUID-shaped id goes in it.
    [GeneratedRegex("^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    private static partial Regex Uuid();

    public static AgentResumeDecision Decide(AgentResumeProfile profile, AgentHookEvent hookEvent, string payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return AgentResumeDecision.Ignore;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return AgentResumeDecision.Ignore;
            if (profile.SkipsSubagents && (HasValue(root, "agentId") || HasValue(root, "parentAgentId")))
                return AgentResumeDecision.Ignore;
            if (String(root, profile.IdField) is not { } id || !Uuid().IsMatch(id))
                return AgentResumeDecision.Ignore;
            var pin = profile.PinFor(id);
            if (hookEvent == AgentHookEvent.Start) return AgentResumeDecision.Pin(pin);
            if (profile.ClearReasons is { } reasons && !reasons.Contains(String(root, "reason")))
                return AgentResumeDecision.Ignore;
            return AgentResumeDecision.ClearIfPinned(pin, profile.LegacyPinPrefix is { } legacy ? legacy + id : null);
        }
    }

    /// <summary>The restore command of the pane a hook runs in. The pane token wins over the pane
    /// name, because a promoted-then-re-split pane keeps its token while its AGTERM_PANE is stale.
    /// The scratch pane is never restored, so it has none.</summary>
    public static AgentResumePane PaneRestoreCommand(ControlTree tree, string sessionId, string? paneId, string? pane)
    {
        var node = tree.Workspaces.SelectMany(workspace => workspace.Sessions)
            .FirstOrDefault(session => string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        if (node is null) return new AgentResumePane(false, null);
        var kind = pane;
        if (!string.IsNullOrEmpty(paneId) && node.Surfaces?.FirstOrDefault(surface => surface.PaneId == paneId) is { } surface)
            kind = surface.Kind;
        return new AgentResumePane(true, kind switch
        {
            "right" => node.SplitRestoreCommand,
            "scratch" => null,
            _ => node.RestoreCommand,
        });
    }

    private static bool HasValue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && !(value.ValueKind == JsonValueKind.String && value.GetString() == "");

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
