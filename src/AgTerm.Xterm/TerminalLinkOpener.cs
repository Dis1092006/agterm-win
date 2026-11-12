using System.Diagnostics;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public sealed class TerminalLinkOpener
{
    private readonly Action<ProcessStartInfo> _start;
    private readonly Func<string, bool> _exists;

    public TerminalLinkOpener(Action<ProcessStartInfo>? start = null, Func<string, bool>? exists = null)
    {
        _start = start ?? (info => Process.Start(info)?.Dispose());
        _exists = exists ?? (path => File.Exists(path) || Directory.Exists(path));
    }

    public LiveFailureNotice? Open(string target, bool allowPathFallback = false)
    {
        if (LinkPolicy.ActionFor(target) is HudLinkAction.Open open)
        {
            _start(new ProcessStartInfo(open.Url) { UseShellExecute = true });
            return null;
        }

        var tryPrefixes = allowPathFallback && target.Length >= 3
            && char.IsAsciiLetter(target[0]) && target[1] == ':' && target[2] is '/' or '\\';
        var validated = false;
        var end = tryPrefixes ? TrimFallbackEnd(target, target.Length) : target.Length;
        while (end >= 3)
        {
            if (LinkPolicy.ActionFor(target[..end]) is HudLinkAction.Reveal reveal)
            {
                validated = true;
                if (_exists(reveal.Path))
                {
                    _start(WindowsExplorer.StartInfo(HudLinkPolicy.ExplorerSelectArguments(reveal.Path)));
                    return null;
                }
            }
            if (!tryPrefixes)
                break;
            end--;
            while (end >= 3 && !char.IsWhiteSpace(target[end]))
                end--;
            while (end >= 3 && char.IsWhiteSpace(target[end - 1]))
                end--;
            end = TrimFallbackEnd(target, end);
        }
        return validated ? new LiveFailureNotice("Path not found", $"Path not found: {target}") : null;
    }

    private static int TrimFallbackEnd(string target, int end)
    {
        int openParens = 0, closeParens = 0, openBrackets = 0, closeBrackets = 0;
        for (var i = 0; i < end; i++)
        {
            switch (target[i])
            {
                case '(': openParens++; break;
                case ')': closeParens++; break;
                case '[': openBrackets++; break;
                case ']': closeBrackets++; break;
            }
        }
        while (end > 3)
        {
            var last = target[end - 1];
            if (char.IsWhiteSpace(last) || last is ',' or '.' or ';' or '!' or '?')
            {
                end--;
            }
            else if (last == ')' && closeParens > openParens)
            {
                closeParens--;
                end--;
            }
            else if (last == ']' && closeBrackets > openBrackets)
            {
                closeBrackets--;
                end--;
            }
            else
            {
                break;
            }
        }
        return end;
    }
}
