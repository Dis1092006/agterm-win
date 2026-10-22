namespace AgTerm.Core.Model;

/// <summary>The pinned upstream terminal-link policy, with local Windows drive paths also revealed.
/// Shares the HUD's action types and URL validation; classification never accesses the filesystem.</summary>
public static class LinkPolicy
{
    private static readonly IReadOnlySet<string> LocalHosts = HostNames(["localhost", Environment.MachineName]);

    public static HudLinkAction? ActionFor(string? target, IEnumerable<string>? localHosts = null)
    {
        if (string.IsNullOrEmpty(target))
            return null;
        if (IsDrivePath(target))
            return LocalPath(target) is { } plainPath ? new HudLinkAction.Reveal(plainPath) : null;
        if (!HudLinkPolicy.IsPrintableAscii(target))
            return null;
        if (!target.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return HudLinkPolicy.ActionFor(target);
        if (!ValidEscapes(target))
            return null;
        var urlPath = HudLinkPolicy.LocalFilePath(target, localHosts is null ? LocalHosts : HostNames(localHosts));
        if (urlPath is null || urlPath.Length < 4 || urlPath[0] != '/' || !IsDrivePath(urlPath[1..]))
            return null;
        return LocalPath(urlPath[1..]) is { } path ? new HudLinkAction.Reveal(path) : null;
    }

    private static bool IsDrivePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\';

    private static string? LocalPath(string path)
    {
        if (path.Length > 32767 || path.Any(char.IsControl) || path.IndexOfAny(['"', '<', '>', '|', '?', '*']) >= 0
            || path.IndexOf(':', 2) >= 0)
            return null;
        var parts = new List<string>();
        foreach (var part in path[3..].Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }
            if (part.EndsWith(' ') || part.EndsWith('.') || IsDeviceName(part))
                return null;
            parts.Add(part);
        }
        return path[..2] + "\\" + string.Join("\\", parts);
    }

    private static bool IsDeviceName(string part)
    {
        var name = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
                && name[3] is >= '1' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3');
    }

    private static IReadOnlySet<string> HostNames(IEnumerable<string> names)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in names)
        {
            var name = raw.ToLowerInvariant();
            if (name.EndsWith('.'))
                name = name[..^1];
            if (name.Length == 0)
                continue;
            result.Add(name);
            if (name.EndsWith(".local", StringComparison.Ordinal) && name.Length > 6)
                result.Add(name[..^6]);
        }
        return result;
    }

    private static bool ValidEscapes(string target)
    {
        for (var i = 0; i < target.Length; i++)
        {
            if (target[i] != '%')
                continue;
            if (i + 2 >= target.Length || !Uri.IsHexDigit(target[i + 1]) || !Uri.IsHexDigit(target[i + 2]))
                return false;
            i += 2;
        }
        return true;
    }
}
