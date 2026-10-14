namespace AgTerm.Core.Model;

/// <summary>What a click on a HUD link does on Windows: open the URL with the system handler, or
/// select a local file in Explorer.</summary>
public abstract record HudLinkAction
{
    private HudLinkAction()
    {
    }

    public sealed record Open(string Url) : HudLinkAction;

    public sealed record Reveal(string Path) : HudLinkAction;
}

/// <summary>Port of upstream LinkPolicy.disposition plus HudMarkdown.linkTarget (ed831ab) as spec 5.4
/// states it. A HUD shows program-controlled text, so a link may name any scheme; only web and mail
/// schemes and a file on this machine are clickable. A URL must be printable ASCII as written, the
/// form a page attribute and the system handler both take without re-encoding.</summary>
public static class HudLinkPolicy
{
    private static readonly HashSet<string> OpenedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "ftp",
    };

    /// <summary>The URL a click may act on, or null when the link is a plain label.</summary>
    public static string? Target(string? url)
    {
        if (string.IsNullOrEmpty(url) || !IsPrintableAscii(url) || Scheme(url) is not { } scheme)
            return null;
        if (OpenedSchemes.Contains(scheme))
            return Uri.TryCreate(url, UriKind.Absolute, out _) ? url : null;
        return scheme.Equals("file", StringComparison.OrdinalIgnoreCase) && LocalFilePath(url) is not null ? url : null;
    }

    /// <summary>The click's action, or null for a plain label. A file link is revealed, never opened:
    /// opening a file can run it.</summary>
    public static HudLinkAction? ActionFor(string url)
    {
        if (Target(url) is not { } target)
            return null;
        if (!target.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return new HudLinkAction.Open(target);
        return WindowsPath(LocalFilePath(target)!) is { } path ? new HudLinkAction.Reveal(path) : null;
    }

    /// <summary>The Explorer command line that selects a revealed path. Windows argument splitting reads
    /// a backslash right before a closing quote as an escaped quote, so a quoted path never ends in one:
    /// a folder drops its trailing backslash, and a drive root, which holds no space, goes unquoted.</summary>
    public static string ExplorerSelectArguments(string path)
    {
        var trimmed = path.TrimEnd('\\');
        return IsDriveName(trimmed) ?$"/select,{trimmed}\\" : $"/select,\"{trimmed}\"";
    }

    private static bool IsDriveName(string path) => path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    /// <summary>file:///C:/x carries the drive after the first slash. A path with no drive stays rooted,
    /// and GetFullPath puts it on the current drive without touching the disk.</summary>
    private static string? WindowsPath(string urlPath)
    {
        var path = urlPath.Length >= 3 && char.IsAsciiLetter(urlPath[1]) && urlPath[2] == ':' ? urlPath[1..] : urlPath;
        // C:x is relative to the app's working directory on C:, and spec 5.4 reveals only an absolute path
        if (path != urlPath && path.Length > 2 && path[2] != '/')
            return null;
        if (path.Length == 2)
            path += "/";
        var full = Path.GetFullPath(path.Replace('/', '\\'));
        // Explorer takes the path inside quotes on its command line. A colon after the drive names an
        // NTFS stream, never a path to reveal.
        return full.StartsWith(@"\\", StringComparison.Ordinal) || full.Contains('"') || full.IndexOf(':', 2) >= 0
            ? null
            : full;
    }

    private static bool IsPrintableAscii(string url) => url.All(c => c is >= '\u0021' and <= '\u007E');

    private static string? Scheme(string url)
    {
        var colon = url.IndexOf(':');
        if (colon < 1)
            return null;
        var scheme = url[..colon];
        return char.IsAsciiLetter(scheme[0]) && scheme.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')
            ? scheme
            : null;
    }

    /// <summary>The percent-decoded URL path of a file link on this machine, or null.</summary>
    internal static string? LocalFilePath(string url)
    {
        var rest = url["file:".Length..];
        var end = rest.IndexOfAny(new[] { '?', '#' });
        if (end >= 0)
            rest = rest[..end];
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            var slash = rest.IndexOf('/', 2);
            if (slash < 0)
                return null;
            var host = WithoutTrailingDot(rest[2..slash]);
            if (host.Length > 0 && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return null;
            rest = rest[slash..];
        }

        var path = Uri.UnescapeDataString(rest);
        // a second slash or a backslash right after the first names a network share the host check never saw
        if (!path.StartsWith('/') || (path.Length > 1 && path[1] is '/' or '\\') || path.Any(char.IsControl))
            return null;
        return path;
    }

    // one dot only, as upstream normalizedHost: "file://./x" names an empty host, "file://../x" does not
    private static string WithoutTrailingDot(string host) => host.EndsWith('.') ? host[..^1] : host;
}
