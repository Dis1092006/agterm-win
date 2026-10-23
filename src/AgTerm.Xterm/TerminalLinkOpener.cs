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
        var end = target.Length;
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
        }
        return validated ? new LiveFailureNotice("Path not found", $"Path not found: {target}") : null;
    }
}
