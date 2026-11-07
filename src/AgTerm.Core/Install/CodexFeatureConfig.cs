using System.Text.RegularExpressions;

namespace AgTerm.Core.Install;

/// <summary>Who sets <c>features.daemon_auto_start</c> in Codex <c>config.toml</c>.</summary>
public enum CodexDaemonSetting { Unset, Ours, UserOff, UserOn }

/// <summary>Turns Codex's shared app-server off for the resume setting by one owned line in
/// <c>config.toml</c>. Codex 0.167 runs hooks in that server with the AGTERM_* of another pane; with
/// <c>daemon_auto_start = false</c> a plain <c>codex</c> runs in its pane. A line edit, not a TOML
/// rewrite, so the user's file keeps its comments and layout. Windows-only extension.</summary>
public static partial class CodexFeatureConfig
{
    public const string OwnedLine = "daemon_auto_start = false # agterm-win";

    private const string InlineRefusal = "config.toml sets features inline, so agterm-win left it unchanged.";

    [GeneratedRegex(@"^\s*\[")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^\s*\[\s*features\s*\]\s*(#.*)?$")]
    private static partial Regex FeaturesHeader();

    [GeneratedRegex(@"^\s*daemon_auto_start\s*=\s*(?<value>true|false)\b")]
    private static partial Regex FeatureKey();

    [GeneratedRegex(@"^\s*features\s*\.\s*daemon_auto_start\s*=\s*(?<value>true|false)\b")]
    private static partial Regex DottedKey();

    [GeneratedRegex(@"^\s*features\s*=")]
    private static partial Regex InlineFeatures();

    public static CodexDaemonSetting Inspect(string? toml) => Scan(Lines(toml)).Setting;

    /// <returns>The new contents, or null when nothing changes.</returns>
    public static string? Enable(string? toml)
    {
        var lines = Lines(toml);
        var scan = Scan(lines);
        if (scan.Setting != CodexDaemonSetting.Unset) return null;
        if (scan.FeaturesHeader is { } header)
        {
            lines.Insert(header + 1, OwnedLine);
            return Join(lines, toml!);
        }
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
        if (lines.Count > 0) lines.Add("");
        lines.AddRange(["[features]", OwnedLine, ""]);
        return Join(lines, toml);
    }

    /// <returns>The new contents, or null when nothing changes.</returns>
    public static string? Disable(string? toml)
    {
        var lines = Lines(toml);
        var scan = Scan(lines);
        if (scan.Setting != CodexDaemonSetting.Ours) return null;
        lines.RemoveAt(scan.SettingLine!.Value);
        return Join(lines, toml!);
    }

    private readonly record struct ScanResult(CodexDaemonSetting Setting, int? SettingLine, int? FeaturesHeader);

    private static ScanResult Scan(List<string> lines)
    {
        string? table = null;
        int? header = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (TableHeader().IsMatch(line))
            {
                table = FeaturesHeader().IsMatch(line) ? "features" : "other";
                if (table == "features") header ??= i;
                continue;
            }
            Match? key = null;
            if (table is null)
            {
                if (DottedKey().Match(line) is { Success: true } dotted) key = dotted;
                else if (InlineFeatures().IsMatch(line)) throw new InvalidDataException(InlineRefusal);
            }
            else if (table == "features" && FeatureKey().Match(line) is { Success: true } plain) key = plain;
            if (key is null) continue;
            var setting = line.Trim() == OwnedLine ? CodexDaemonSetting.Ours
                : key.Groups["value"].Value == "true" ? CodexDaemonSetting.UserOn : CodexDaemonSetting.UserOff;
            return new ScanResult(setting, i, header);
        }
        return new ScanResult(CodexDaemonSetting.Unset, null, header);
    }

    // A file that ends with a newline splits into a last empty line, so Join puts the newline back.
    private static List<string> Lines(string? toml) =>
        string.IsNullOrEmpty(toml) ? [] : toml.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

    private static string Join(List<string> lines, string? original) =>
        string.Join(original?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n", lines);
}
