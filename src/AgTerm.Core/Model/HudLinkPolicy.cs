namespace AgTerm.Core.Model;

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
