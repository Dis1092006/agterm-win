using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgTerm.Core.Agents;

namespace AgTerm.Core.Install;

public enum AgentResumeState { On, Off, NotFound, Failed }

/// <param name="Reason">Why a failed agent failed, or a note on an agent that is on or off.</param>
public sealed record AgentResumeAgentStatus(string DisplayName, AgentResumeState State, string? Reason = null);

public sealed record AgentResumeReport(IReadOnlyList<AgentResumeAgentStatus> Agents, string? Error = null)
{
    public bool HasFailure => Error is not null || Agents.Any(agent => agent.State == AgentResumeState.Failed);

    public string Describe()
    {
        if (Error is not null) return $"The resume hooks could not be installed: {Error}";
        var parts = new List<string>();
        foreach (var (state, label) in new[]
        {
            (AgentResumeState.On, "on"), (AgentResumeState.Off, "off"), (AgentResumeState.NotFound, "not found"),
        })
        {
            var names = Agents.Where(agent => agent.State == state).Select(agent => agent.DisplayName).ToArray();
            if (names.Length > 0) parts.Add($"{string.Join(", ", names)}: {label}.");
        }
        parts.AddRange(Agents.Where(agent => agent.State == AgentResumeState.Failed)
            .Select(agent => agent.Reason is null ? $"{agent.DisplayName} failed." : $"{agent.DisplayName} failed: {agent.Reason}"));
        parts.AddRange(Agents.Where(agent => agent.State != AgentResumeState.Failed && agent.Reason is not null)
            .Select(agent => agent.Reason!));
        if (Agents.Any(agent => agent.DisplayName == "Codex" && agent.State == AgentResumeState.On))
            parts.Add("Codex asks you to trust the new hooks the next time it starts. "
                + "If Codex's background server is running, stop it once with codex app-server daemon stop.");
        return string.Join(" ", parts);
    }
}

/// <summary>Where each agent keeps its configuration; null means the default under <paramref name="Home"/>.</summary>
public sealed record AgentResumePaths(string Home, string? ClaudeConfigDirectory = null, string? CopilotHome = null,
    string? CodexHome = null)
{
    public static AgentResumePaths FromEnvironment(string home, Func<string, string?> getenv) =>
        new(home, NonEmpty(getenv("CLAUDE_CONFIG_DIR")), NonEmpty(getenv("COPILOT_HOME")), NonEmpty(getenv("CODEX_HOME")));

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>Installs or removes the SessionStart/SessionEnd hooks that run <c>agtermctl agent-hook resume</c>
/// for Claude Code, Copilot CLI and Codex. Windows-only extension.</summary>
public static partial class AgentResumeInstaller
{
    public const string WrapperName = "agterm-agent-resume.cmd";
    public const string WrapperMarker = "agterm-win-resume-hook";
    public const string CopilotHooksName = "agterm-win-resume.json";
    public const string LogName = "agent-resume.log";

    private const string CopilotRecipeName = "copilot-session-resume.json";
    private const string CopilotRecipeScript = "copilot-resume-hook.ps1";
    private static readonly string[] ClaudeRecipeScripts = ["claude-resume-hook.ps1", "pin-claude-resume.ps1"];
    private static readonly string[] Verbs = ["start", "end"];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static AgentResumeReport Apply(AgentResumePaths paths, bool enabled, string toolPath, string wrapperTemplate)
    {
        var directory = Path.Join(paths.Home, ".config", "agterm", "agent-resume");
        var wrapper = Path.Join(directory, WrapperName);
        if (enabled)
        {
            if (!File.Exists(toolPath)) return Failed($"agtermctl was not found at '{toolPath}'.");
            if (!wrapperTemplate.Contains(WrapperMarker, StringComparison.Ordinal))
                return Failed("The resume hook script is not bundled in this build.");
            if (new[] { paths.Home, toolPath }.Any(path => path.IndexOfAny(['%', '!', '^', '&', '"', '\r', '\n']) >= 0))
                return Failed("The install path contains a character that cmd.exe cannot quote safely (%, !, ^, & or \").");
            try
            {
                AgentHooksInstaller.WriteOwned(wrapper,
                    wrapperTemplate.Replace("__AGTERMCTL_PATH__", toolPath, StringComparison.Ordinal), WrapperMarker);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return Failed(ex.Message);
            }
        }

        var agents = new List<AgentResumeAgentStatus>();
        foreach (var profile in AgentResumeProfile.All)
        {
            var state = AgentResumeState.Off;
            string? reason = null;
            try
            {
                var found = profile.Name switch
                {
                    "claude" => ApplyEvents(ClaudeDirectory(paths), "settings.json", ClaudeFormat, enabled, wrapper),
                    "copilot" => ApplyCopilot(paths, enabled, wrapper),
                    "codex" => ApplyEvents(CodexDirectory(paths), "hooks.json", CodexFormat, enabled, wrapper),
                    _ => throw new InvalidOperationException(profile.Name),
                };
                if (found && profile.Name == "codex") reason = ApplyCodexConfig(CodexDirectory(paths), enabled);
                state = !found ? AgentResumeState.NotFound : enabled ? AgentResumeState.On : AgentResumeState.Off;
            }
            catch (Exception ex) when (IsFileError(ex) || ex is DecoderFallbackException)
            {
                state = AgentResumeState.Failed;
                reason = ex.Message;
            }
            agents.Add(new AgentResumeAgentStatus(profile.DisplayName, state, reason));
        }

        if (enabled) MigrateRecipes(paths, agents);
        else if (agents.All(agent => agent.State != AgentResumeState.Failed)) RemoveWrapper(directory, wrapper);
        return new AgentResumeReport(agents);
    }

    private static AgentResumeReport Failed(string error) => new([], error);

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException;

    private static string ClaudeDirectory(AgentResumePaths paths) => paths.ClaudeConfigDirectory ?? Path.Join(paths.Home, ".claude");

    private static string CodexDirectory(AgentResumePaths paths) => paths.CodexHome ?? Path.Join(paths.Home, ".codex");

    // Codex 0.167 runs hooks in its shared app-server, which carries another pane's AGTERM_*, so the
    // setting turns that server off. A daemon_auto_start the user set stays as it is.
    /// <returns>A note for the hint when the user's own setting keeps Codex out of resume.</returns>
    private static string? ApplyCodexConfig(string directory, bool enabled)
    {
        var path = Path.Join(directory, "config.toml");
        var existing = File.Exists(path) ? ReadText(path) : null;
        var updated = enabled ? CodexFeatureConfig.Enable(existing) : CodexFeatureConfig.Disable(existing);
        if (updated is not null) AgentHooksInstaller.WriteConfig(path, existing, updated);
        return enabled && CodexFeatureConfig.Inspect(updated ?? existing) == CodexDaemonSetting.UserOn
            ? "daemon_auto_start = true in Codex config.toml keeps Codex sessions in its background server, so they are not resumed."
            : null;
    }

    private static string ReadText(string path) => File.ReadAllText(path, new UTF8Encoding(false, true));

    // Claude Code and Codex keep their hooks in a shared file: { "hooks": { "SessionStart": [ group ] } }.

    private sealed record EventFormat(
        string Agent,
        Func<string, string, JsonObject> Build,
        Func<JsonObject, bool> IsOurs,
        Func<JsonObject, bool> Mentions,
        Func<JsonObject, JsonObject?>? StripRecipe = null,
        Func<string, string?>? Refuse = null);

    private static readonly EventFormat ClaudeFormat = new(
        "claude",
        (wrapper, verb) => Group(new JsonObject
        {
            ["type"] = "command",
            ["command"] = "cmd.exe",
            ["args"] = new JsonArray("/d", "/c", "call", wrapper, "claude", verb),
            ["timeout"] = 10,
        }),
        group => SingleHandler(group) is { } handler && handler["type"]?.ToString() == "command" &&
            handler["command"]?.ToString() == "cmd.exe" && handler["args"] is JsonArray args &&
            Strings(args) is [ "/d", "/c", "call", var path, "claude", "start" or "end" ] &&
            IsWrapperPath(path),
        group => Handlers(group).Any(handler => Words(handler).Any(word => word.Contains(WrapperName, StringComparison.OrdinalIgnoreCase))),
        StripClaudeRecipe);

    private static readonly EventFormat CodexFormat = new(
        "codex",
        (wrapper, verb) => Group(new JsonObject
        {
            ["type"] = "command",
            // Codex runs the command through cmd.exe /c and escapes inner quotes with backslashes,
            // which cmd.exe does not understand, so the command carries no quotes.
            ["command"] = $"{wrapper} codex {verb}",
            ["timeout"] = 10,
        }),
        group => SingleHandler(group) is { } handler && handler["type"]?.ToString() == "command" &&
            handler["command"] is JsonValue value && value.TryGetValue<string>(out var command) &&
            CodexCommand().Match(command) is { Success: true } match && IsWrapperPath(match.Groups["path"].Value),
        group => Handlers(group).Any(handler => Words(handler).Any(word => word.Contains(WrapperName, StringComparison.OrdinalIgnoreCase))),
        Refuse: wrapper => wrapper.Any(char.IsWhiteSpace)
            ? $"The hook path '{wrapper}' has a space, and Codex cannot run a quoted command on Windows."
            : null);

    [GeneratedRegex("^(?<path>.+) codex (start|end)$")]
    private static partial Regex CodexCommand();

    private static bool IsWrapperPath(string? path) => string.Equals(Path.GetFileName(path), WrapperName, StringComparison.OrdinalIgnoreCase);

    private static JsonObject Group(JsonObject handler) => new() { ["hooks"] = new JsonArray(handler) };

    private static IEnumerable<JsonObject> Handlers(JsonObject group) =>
        group["hooks"] is JsonArray handlers ? handlers.OfType<JsonObject>() : [];

    private static JsonObject? SingleHandler(JsonObject group) =>
        group["hooks"] is JsonArray { Count: 1 } handlers ? handlers[0] as JsonObject : null;

    private static string?[] Strings(JsonArray array) =>
        array.Select(node => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null).ToArray();

    private static IEnumerable<string> Words(JsonObject handler)
    {
        foreach (var key in new[] { "command", "exec" })
            if (handler[key] is JsonValue value && value.TryGetValue<string>(out var text)) yield return text;
        if (handler["args"] is JsonArray args)
            foreach (var arg in Strings(args))
                if (arg is not null) yield return arg;
    }

    private static bool IsClaudeRecipeHandler(JsonObject handler) =>
        Words(handler).Any(word => ClaudeRecipeScripts.Any(script =>
            word.Trim('"').EndsWith(script, StringComparison.OrdinalIgnoreCase)));

    // The claude-session-resume recipe this setting replaces registered its script as one handler in a group.
    private static JsonObject? StripClaudeRecipe(JsonObject group)
    {
        var clone = (JsonObject)group.DeepClone();
        if (clone["hooks"] is not JsonArray handlers) return clone;
        foreach (var handler in handlers.OfType<JsonObject>().Where(IsClaudeRecipeHandler).ToArray())
            handlers.Remove(handler);
        return handlers.Count == 0 ? null : clone;
    }

    /// <returns>False when the agent's configuration directory does not exist.</returns>
    private static bool ApplyEvents(string directory, string fileName, EventFormat format, bool enabled, string wrapper)
    {
        if (!Directory.Exists(directory)) return false;
        var path = Path.Join(directory, fileName);
        if (enabled && format.Refuse?.Invoke(wrapper) is { } refusal) throw new InvalidDataException(refusal);
        var existing = File.Exists(path) ? ReadText(path) : null;
        if (existing is null && !enabled) return true;
        var merged = MergeEvents(existing, fileName, format, enabled, wrapper);
        if (merged is not null) AgentHooksInstaller.WriteConfig(path, existing, merged);
        return true;
    }

    /// <returns>The new contents, or null when nothing changes.</returns>
    private static string? MergeEvents(string? existing, string fileName, EventFormat format, bool enabled, string wrapper)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(existing)) root = new JsonObject();
        else
        {
            try { root = JsonNode.Parse(existing) as JsonObject ?? throw new InvalidDataException($"{fileName} is not a JSON object."); }
            catch (JsonException) { throw new InvalidDataException($"{fileName} is not valid JSON."); }
        }
        if (root.ContainsKey("hooks") && root["hooks"] is not JsonObject)
            throw new InvalidDataException($"The hooks in {fileName} are not a JSON object.");
        var hooks = root["hooks"] as JsonObject;
        var events = Verbs.Select(verb => (Event: verb == "start" ? "SessionStart" : "SessionEnd", Verb: verb)).ToArray();
        foreach (var (eventName, _) in events)
        {
            if (hooks is null || !hooks.ContainsKey(eventName)) continue;
            if (hooks[eventName] is not JsonArray groups || groups.Any(group =>
                group is not JsonObject entry || entry["hooks"] is not JsonArray handlers ||
                handlers.Any(handler => handler is not JsonObject)))
                throw new InvalidDataException($"The {eventName} hooks in {fileName} are not valid.");
        }

        var result = (JsonObject)root.DeepClone();
        var newHooks = hooks?.DeepClone() as JsonObject ?? new JsonObject();
        foreach (var (eventName, verb) in events)
        {
            var groups = hooks?[eventName] as JsonArray;
            var updated = new JsonArray();
            var placed = false;
            foreach (var group in groups?.OfType<JsonObject>() ?? [])
            {
                if (format.IsOurs(group))
                {
                    if (enabled && !placed) updated.Add(format.Build(wrapper, verb));
                    placed = true;
                    continue;
                }
                if (format.Mentions(group))
                    throw new InvalidDataException($"An unrecognized agterm-win {eventName} hook in {fileName} was left unchanged.");
                var kept = enabled && format.StripRecipe is not null ? format.StripRecipe(group) : group.DeepClone();
                if (kept is not null) updated.Add(kept);
            }
            if (enabled && !placed) updated.Add(format.Build(wrapper, verb));
            if (updated.Count > 0 || groups is { Count: 0 }) newHooks[eventName] = updated;
            else newHooks.Remove(eventName);
        }
        if (newHooks.Count > 0 || hooks is { Count: 0 }) result["hooks"] = newHooks;
        else result.Remove("hooks");

        return JsonNode.DeepEquals(root, result) ? null : result.ToJsonString(Indented) + "\n";
    }

    // Copilot CLI loads every *.json in its hooks directory, so agterm-win owns one file there.

    public static string BuildCopilot(string wrapper)
    {
        var hooks = new JsonObject();
        foreach (var verb in Verbs)
        {
            hooks[verb == "start" ? "sessionStart" : "sessionEnd"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["exec"] = "cmd.exe",
                ["args"] = new JsonArray("/d", "/c", "call", wrapper, "copilot", verb),
                ["timeoutSec"] = 10,
            });
        }
        return new JsonObject { ["version"] = 1, ["hooks"] = hooks }.ToJsonString(Indented) + "\n";
    }

    private static bool IsOwnedCopilot(string contents)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(contents); }
        catch (JsonException) { return false; }
        return root is JsonObject file && file["hooks"] is JsonObject { Count: > 0 } hooks &&
            hooks.All(property => property.Value is JsonArray entries && entries.All(entry =>
                entry is JsonObject handler && handler["exec"]?.ToString() == "cmd.exe" && handler["args"] is JsonArray args &&
                Strings(args) is [ "/d", "/c", "call", var path, "copilot", "start" or "end" ] && IsWrapperPath(path)));
    }

    private static bool ApplyCopilot(AgentResumePaths paths, bool enabled, string wrapper)
    {
        var directory = SkillInstall.CopilotDirectory(paths.Home, paths.CopilotHome);
        if (!Directory.Exists(directory)) return false;
        var path = Path.Join(directory, "hooks", CopilotHooksName);
        var existing = File.Exists(path) ? ReadText(path) : null;
        if (existing is not null && !IsOwnedCopilot(existing))
            throw new InvalidDataException($"'{path}' is not an agterm-win file and was left unchanged.");
        if (!enabled)
        {
            if (existing is not null) File.Delete(path);
            return true;
        }
        var contents = BuildCopilot(wrapper);
        if (existing == contents) return true;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AgentHooksInstaller.WriteAtomic(path, contents);
        return true;
    }

    private static void RemoveWrapper(string directory, string wrapper)
    {
        try
        {
            if (File.Exists(wrapper))
            {
                AgentHooksInstaller.EnsureOwnedIfPresent(wrapper, WrapperMarker);
                File.Delete(wrapper);
            }
            File.Delete(Path.Join(directory, LogName));
            DeleteIfEmpty(directory);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // A foreign file at our path stays, and so does its directory.
        }
    }

    // The claude-session-resume and copilot-session-resume cookbook recipes did the same job by hand.
    // Their hooks are gone once ours are in place, so their scripts go too.
    private static void MigrateRecipes(AgentResumePaths paths, IReadOnlyList<AgentResumeAgentStatus> agents)
    {
        bool IsOn(string name) => agents.Any(agent =>
            agent.DisplayName == AgentResumeProfile.Find(name)!.DisplayName && agent.State == AgentResumeState.On);
        var config = Path.Join(paths.Home, ".config", "agterm");
        try
        {
            if (IsOn("claude"))
                DeleteScripts(Path.Join(config, "claude-restore"), ClaudeRecipeScripts);
            if (IsOn("copilot"))
            {
                var recipe = Path.Join(SkillInstall.CopilotDirectory(paths.Home, paths.CopilotHome), "hooks", CopilotRecipeName);
                if (File.Exists(recipe) && ReadText(recipe).Contains(CopilotRecipeScript, StringComparison.OrdinalIgnoreCase))
                    File.Delete(recipe);
                if (!File.Exists(recipe))
                    DeleteScripts(Path.Join(config, "copilot-restore"), [CopilotRecipeScript]);
            }
        }
        catch (Exception ex) when (IsFileError(ex) || ex is DecoderFallbackException)
        {
            // The recipe files are left for the user; the new hooks work either way.
        }
    }

    private static void DeleteScripts(string directory, IEnumerable<string> names)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var name in names) File.Delete(Path.Join(directory, name));
        DeleteIfEmpty(directory);
    }

    private static void DeleteIfEmpty(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }
}
