using AgTerm.Core.Protocol;

namespace AgTerm.Cli;

/// <summary>
/// Response rendering — the port of the upstream SocketClient print/format helpers (the transport
/// itself lives in AgTerm.ControlServer.ControlPipeClient). Human-readable output is byte-for-byte
/// the upstream format; --json prints the raw response line.
/// </summary>
internal static class ClientOutput
{
    /// <summary>Print a response: the raw JSON line with json: true, otherwise a human-readable
    /// summary. An error response (ok == false, non---json) goes to stderr; everything else to stdout.</summary>
    public static void PrintResponse(CliEnvironment env, ControlResponse response, bool json,
                                     bool echoId = false)
    {
        if (!json && !response.Ok)
        {
            env.Error.WriteLine(FormatResponse(response, json: false));
            return;
        }
        env.Out.WriteLine(FormatResponse(response, json, echoId));
    }

    /// <summary>Render a response to a single string (no trailing newline): the raw JSON line with
    /// json: true, otherwise a human-readable summary — an error: line, the tree listing, the
    /// selected text, the affected id (only when echoId, i.e. for the create commands), or a bare ok.</summary>
    public static string FormatResponse(ControlResponse response, bool json, bool echoId = false)
    {
        if (json)
        {
            return ControlJson.Serialize(response);
        }
        if (!response.Ok)
        {
            return "error: " + (response.Error ?? "unknown error");
        }
        if (response.Result?.Tree is { } tree)
        {
            return FormatTree(tree);
        }
        if (response.Result?.Windows is { } windows)
        {
            return FormatWindows(windows);
        }
        if (response.Result?.Themes is { } themes)
        {
            return FormatThemes(themes, current: response.Result?.Theme,
                sync: response.Result?.Sync ?? false,
                light: response.Result?.Light, dark: response.Result?.Dark);
        }
        if (response.Result?.Keymap is { } keymap)
        {
            return FormatKeymap(keymap);
        }
        if (response.Result?.Text is { } text)
        {
            return text;
        }
        if (response.Result?.ExitCode is { } exitCode)
        {
            return $"exit {exitCode}";
        }
        if (response.Result?.Affected is { } affected)
        {
            return affected == 1 ? "1 session" : $"{affected} sessions";
        }
        if (response.Result?.Count is { } count)
        {
            // keymap.reload reports its parse-diagnostic count; 0 reads as a clean reload.
            return count == 0 ? "ok" : $"{count} diagnostic(s)";
        }
        if (response.Result?.Ratio is { } ratio)
        {
            // session.resize echoes the applied (clamped) left-pane fraction, scriptable as a bare number.
            return ratio.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (echoId && response.Result?.Id is { } id)
        {
            return id;
        }
        return "ok";
    }

    /// <summary>Render the keymap.list payload: the file path, one row per action with its chord
    /// (`*` marks one the keymap file moved), then the custom commands and the diagnostics, each block
    /// left out when empty (no trailing newline).</summary>
    public static string FormatKeymap(ControlKeymap keymap)
    {
        var lines = new List<string> { $"keymap: {keymap.Path}", "", "actions:" };
        var width = keymap.Actions.Select(a => a.Action.Length).DefaultIfEmpty(0).Max();
        foreach (var action in keymap.Actions)
        {
            var mark = action.Overridden == true ? "*" : " ";
            lines.Add($"  {mark} {action.Action.PadRight(width)}  {action.Chord ?? "-"}");
        }
        if (keymap.Commands.Length > 0)
        {
            lines.Add("");
            lines.Add("commands:");
            lines.AddRange(keymap.Commands.Select(c => $"    {c.Name}  {c.Shortcut ?? "(palette only)"}"));
        }
        if (keymap.Diagnostics.Length > 0)
        {
            lines.Add("");
            lines.Add("diagnostics:");
            // Line 0 stands for the whole file, not a real line, so no number is printed for it.
            lines.AddRange(keymap.Diagnostics.Select(d => d.Line > 0 ? $"    line {d.Line}: {d.Message}" : $"    {d.Message}"));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Render the theme.list payload as one theme name per line, the active theme(s)
    /// marked with "* " and a leading "default terminal" entry for the no-theme case (no trailing
    /// newline). When sync is on, both the light and dark themes are marked and a header notes the
    /// appearance pair; otherwise the single current theme is marked.</summary>
    public static string FormatThemes(IReadOnlyList<string> themes, string? current, bool sync = false,
                                      string? light = null, string? dark = null)
    {
        Func<string?, bool> active = sync
            ? name => name is not null && (name == light || name == dark)
            : name => name == current;
        string Line(string? name, string label) => (active(name) ? "* " : "  ") + label;
        var body = string.Join("\n",
            new[] { Line(null, "default terminal") }.Concat(themes.Select(t => Line(t, t))));
        if (!sync) return body;
        var header = "syncing with Windows appearance — light: " + (light ?? "default terminal")
            + ", dark: " + (dark ?? "default terminal");
        return header + "\n" + body;
    }

    /// <summary>Render the window.list payload as one "id  name  [open]  [active]" line per window
    /// (no trailing newline). Closed/inactive windows still list, with the bracket tag absent.</summary>
    public static string FormatWindows(IReadOnlyList<ControlWindowNode> windows) =>
        string.Join("\n", windows.Select(window =>
        {
            var tags = (window.Open ? " [open]" : "") + (window.Active ? " [active]" : "");
            return $"{window.Id}  {window.Name}{tags}";
        }));

    /// <summary>Render a tree as an indented workspace → session listing (no trailing newline).</summary>
    public static string FormatTree(ControlTree tree)
    {
        var lines = new List<string>();
        foreach (var workspace in tree.Workspaces)
        {
            var mark = workspace.Active ? "*" : " ";
            lines.Add($"{mark} {workspace.Name}  [{workspace.Id}]");
            foreach (var session in workspace.Sessions)
            {
                var smark = session.Active ? "*" : " ";
                var tags = (session.Split ? " (split)" : "") + (session.Overlay ? " (overlay)" : "")
                    + (session.Scratch ? " (scratch)" : "");
                var titleSuffix = session.Title is { } title ? $"  title: {title}" : "";
                lines.Add($"  {smark} {session.Name}{tags}  [{session.Id}]  {session.Cwd}{titleSuffix}");
            }
        }
        return string.Join("\n", lines);
    }
}
