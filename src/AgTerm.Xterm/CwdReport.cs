using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Normalizes a shell cwd report payload into a local path for Session.CurrentCwd. Accepts the
/// upstream OSC 7 form (a file:// URI) and the Windows-native ConEmu/Windows-Terminal OSC 9;9 form
/// (a raw, possibly quoted path) — PLATFORM-NOTES. Returns null for anything that is not a local
/// path (blank, foreign host).
/// </summary>
public static class CwdReport
{
    public static string? Parse(string payload)
    {
        var text = payload.Trim();
        if (text.Length == 0) return null;

        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
            if (uri.Host is not ("" or "localhost")) return null;
            // an explicit localhost authority would render as a \\localhost UNC — drop it
            if (uri.Host == "localhost" &&
                !Uri.TryCreate("file://" + uri.PathAndQuery, UriKind.Absolute, out uri!))
                return null;
            // LocalPath renders a drive URI as C:\x and a unix-shaped one as /home/u (both decoded)
            return TerminalText.Sanitized(uri.LocalPath);
        }

        // OSC 9;9 raw path; Windows Terminal's documented prompt wraps it in quotes
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1].Trim();
        text = TerminalText.Sanitized(text);
        return text.Length == 0 ? null : text;
    }
}
