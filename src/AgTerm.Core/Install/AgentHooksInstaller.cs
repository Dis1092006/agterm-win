using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgTerm.Core.Install;

public static class AgentHooksInstaller
{
    public const string WrapperName = "agterm-agent-status.cmd";
    public const string PiName = "agterm-status.ts";
    public const string WrapperMarker = "agterm-win-status-hook";
    public const string PiMarker = "// agterm-win-pi-status-extension";

    private static readonly (string Event, string? Matcher, string State, string? Flag)[] ClaudeEvents =
    [
        ("UserPromptSubmit", null, "active", "--blink"),
        ("PostToolUse", null, "active", "--blink"),
        ("Stop", null, "completed", "--auto-reset"),
        ("PermissionRequest", null, "blocked", null),
        ("Notification", "permission_prompt", "blocked", null),
    ];

    /// <summary>The Copilot CLI hooks file agterm-win owns, in <c>~/.copilot/hooks</c>. Windows-only extension.</summary>
    public const string CopilotHooksName = "agterm-win-status.json";

    // permissionRequest is left out: Copilot CLI fires it for auto-approved tool calls too.
    private static readonly (string Event, string? Matcher, string State, string? Flag)[] CopilotEvents =
    [
        ("userPromptSubmitted", null, "active", "--blink"),
        ("postToolUse", null, "active", "--blink"),
        ("postToolUseFailure", null, "active", "--blink"),
        ("agentStop", null, "completed", "--auto-reset"),
        ("notification", "permission_prompt", "blocked", null),
        ("notification", "elicitation_dialog", "blocked", null),
    ];

    private static JsonArray StatusArgs(string wrapperPath, string state, string? flag)
    {
        var args = new JsonArray("/d", "/v:on", "/c", "call", wrapperPath, state);
        if (flag is not null) args.Add(flag);
        args.Add("agterm-win");
        return args;
    }

    /// <summary>The full contents of the owned Copilot CLI hooks file for <paramref name="wrapperPath"/>.</summary>
    public static string BuildCopilot(string wrapperPath)
    {
        var hooks = new JsonObject();
        foreach (var (eventName, matcher, state, flag) in CopilotEvents)
        {
            var entry = new JsonObject
            {
                ["type"] = "command",
                ["exec"] = "cmd.exe",
                ["args"] = StatusArgs(wrapperPath, state, flag),
                ["timeoutSec"] = 10,
            };
            if (matcher is not null) entry["matcher"] = matcher;
            if (hooks[eventName] is not JsonArray entries) hooks[eventName] = entries = new JsonArray();
            entries.Add(entry);
        }
        var root = new JsonObject { ["version"] = 1, ["hooks"] = hooks };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static bool IsOwnedCopilot(string contents)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(contents); }
        catch (JsonException) { return false; }
        if (root is not JsonObject file || file["hooks"] is not JsonObject hooks) return false;
        return hooks.All(property => property.Value is JsonArray entries && entries.All(entry =>
            entry is JsonObject handler && handler["type"]?.ToString() == "command" &&
            handler["exec"]?.ToString() == "cmd.exe" && handler["args"] is JsonArray args && IsOwnedArgs(args)));
    }

    public static (string Contents, bool Changed) MergeClaude(string? existing, string wrapperPath)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(existing))
            root = new JsonObject();
        else
        {
            try { root = JsonNode.Parse(existing) as JsonObject ?? throw new InvalidDataException("Claude settings must be a JSON object."); }
            catch (JsonException ex) { throw new InvalidDataException("Claude settings are not valid JSON.", ex); }
        }

        if (root.ContainsKey("hooks") && root["hooks"] is not JsonObject)
            throw new InvalidDataException("Claude hooks must be a JSON object.");
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        foreach (var (eventName, _, _, _) in ClaudeEvents)
        {
            if (hooks.ContainsKey(eventName) && hooks[eventName] is not JsonArray)
                throw new InvalidDataException($"Claude {eventName} hooks must be an array.");
            if (hooks[eventName] is JsonArray groups && groups.Any(group =>
                group is not JsonObject entry || entry["hooks"] is not JsonArray handlers ||
                handlers.Any(handler => handler is not JsonObject)))
                throw new InvalidDataException($"Claude {eventName} contains an invalid hook group.");
        }

        var changed = false;
        foreach (var (eventName, matcher, state, flag) in ClaudeEvents)
        {
            var entries = hooks[eventName] as JsonArray ?? new JsonArray();
            var args = StatusArgs(wrapperPath, state, flag);
            var entry = new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = "cmd.exe", ["args"] = args }),
            };
            if (matcher is not null) entry["matcher"] = matcher;
            var owned = entries.Select((node, index) => (node, index)).Where(item => IsOwned(item.node)).ToArray();
            if (entries.Any(node => MentionsWrapper(node) && !IsOwned(node)))
                throw new InvalidDataException($"An unrecognized agterm-win {eventName} hook exists; left unchanged.");
            if (owned.Length > 1)
                throw new InvalidDataException($"More than one agterm-win {eventName} hook exists; left unchanged.");
            if (owned.Length == 1)
            {
                if (JsonNode.DeepEquals(entries[owned[0].index], entry)) continue;
                entries[owned[0].index] = entry;
            }
            else entries.Add(entry);
            hooks[eventName] = entries;
            changed = true;
        }
        if (!changed) return (existing ?? "", false);
        root["hooks"] = hooks;
        return (root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", true);
    }

    private static bool MentionsWrapper(JsonNode? entry) =>
        entry is JsonObject group && group["hooks"] is JsonArray handlers &&
        handlers.OfType<JsonObject>().Any(handler =>
            handler["args"] is JsonArray args &&
            args.Any(arg => arg is JsonValue value && value.TryGetValue<string>(out var text) &&
                Path.GetFileName(text) == WrapperName));

    private static bool IsOwned(JsonNode? entry)
    {
        if (entry is not JsonObject group || group["hooks"] is not JsonArray handlers || handlers.Count != 1 ||
            handlers[0] is not JsonObject handler || handler["args"] is not JsonArray args)
            return false;
        return handler["type"]?.ToString() == "command" && handler["command"]?.ToString() == "cmd.exe" &&
            IsOwnedArgs(args);
    }

    private static bool IsOwnedArgs(JsonArray args)
    {
        var values = args.Select(arg => arg is JsonValue value && value.TryGetValue<string>(out var text) ? text : null).ToArray();
        return values.Length is 7 or 8 && values[0] == "/d" && values[1] == "/v:on" &&
            values[2] == "/c" && values[3] == "call" &&
            Path.GetFileName(values[4]) == WrapperName && values[^1] == "agterm-win";
    }

    public static InstallOutcome Run(string home, string wrapperTemplate, string piTemplate, string toolPath)
    {
        if (!File.Exists(toolPath) || !wrapperTemplate.Contains(WrapperMarker, StringComparison.Ordinal) ||
            !piTemplate.Contains(PiMarker, StringComparison.Ordinal))
            return new InstallOutcome("Install Failed", "The agent-status scripts or agtermctl are not bundled in this build.", true);
        if (new[] { home, toolPath }.Any(path => path.IndexOfAny(['%', '!', '^', '&', '"', '\r', '\n']) >= 0))
            return new InstallOutcome("Install Failed",
                "The install path contains a character that cannot be quoted safely by cmd.exe (%, !, ^, &, or \").", true);

        var installed = new List<string>();
        var skipped = new List<string>();
        var directory = Path.Join(home, ".config", "agterm", "agent-status");
        var wrapper = Path.Join(directory, WrapperName);
        try
        {
            EnsureOwnedIfPresent(wrapper, WrapperMarker);
            WriteOwned(wrapper, wrapperTemplate.Replace("__AGTERMCTL_PATH__", toolPath, StringComparison.Ordinal), WrapperMarker);
            installed.Add($"Status script: {wrapper}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new InstallOutcome("Install Failed", $"Status scripts could not be fully installed: {ex.Message}", true);
        }

        var settings = Path.Join(home, ".claude", "settings.json");
        try
        {
            var existing = File.Exists(settings)
                ? File.ReadAllText(settings, new UTF8Encoding(false, true))
                : null;
            var merged = MergeClaude(existing, wrapper);
            if (merged.Changed) WriteConfig(settings, existing, merged.Contents);
            installed.Add($"Claude Code: {settings}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or DecoderFallbackException)
        {
            skipped.Add($"Claude Code: {ex.Message}");
        }

        if (Directory.Exists(Path.Join(home, ".copilot")))
        {
            var copilotHooks = Path.Join(home, ".copilot", "hooks", CopilotHooksName);
            try
            {
                var existing = File.Exists(copilotHooks)
                    ? File.ReadAllText(copilotHooks, new UTF8Encoding(false, true))
                    : null;
                if (existing is not null && !IsOwnedCopilot(existing))
                    throw new InvalidDataException($"'{copilotHooks}' is not an agterm-win file; left untouched.");
                var contents = BuildCopilot(wrapper);
                if (existing != contents) WriteConfig(copilotHooks, existing, contents);
                installed.Add($"Copilot CLI: {copilotHooks}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or DecoderFallbackException)
            {
                skipped.Add($"Copilot CLI: {ex.Message}");
            }
        }
        else skipped.Add("Copilot CLI: not installed (no .copilot directory)");

        var piAgent = Path.Join(home, ".pi", "agent");
        if (Directory.Exists(piAgent))
        {
            var destination = Path.Join(piAgent, "extensions", PiName);
            try
            {
                var contents = piTemplate.Replace("__AGTERM_STATUS_WRAPPER__",
                    JsonSerializer.Serialize(wrapper), StringComparison.Ordinal);
                WriteOwned(destination, contents, PiMarker);
                installed.Add($"Pi: {destination} (restart Pi or run /reload)");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                skipped.Add($"Pi: {ex.Message}");
            }
        }
        else skipped.Add("Pi: not installed (no .pi\\agent directory)");

        var warning = skipped.Any(line => !line.StartsWith("Pi: not installed", StringComparison.Ordinal) &&
            !line.StartsWith("Copilot CLI: not installed", StringComparison.Ordinal));
        return new InstallOutcome(warning ? "Agent Status Hooks Installed — with a warning" : "Agent Status Hooks Installed",
            string.Join("\n", installed.Concat(skipped)), warning);
    }

    private static void WriteOwned(string path, string contents, string marker)
    {
        EnsureOwnedIfPresent(path, marker);
        if (File.Exists(path))
        {
            if (File.ReadAllText(path, Encoding.UTF8) == contents) return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, contents);
    }

    private static void EnsureOwnedIfPresent(string path, string marker)
    {
        if (!File.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"'{path}' is a link; left untouched.");
        if (!File.ReadAllText(path, Encoding.UTF8).Contains(marker, StringComparison.Ordinal))
            throw new InvalidDataException($"'{path}' is not an agterm-win file; left untouched.");
    }

    private static void WriteConfig(string path, string? existing, string contents)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"'{path}' is a link; left untouched.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (existing is not null)
        {
            var backup = path + ".bak";
            for (var suffix = 1; File.Exists(backup); suffix++) backup = path + $".bak.{suffix}";
            File.Copy(path, backup);
        }
        WriteAtomic(path, contents);
    }

    private static void WriteAtomic(string path, string contents)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
