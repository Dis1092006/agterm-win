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

    public void Open(string target)
    {
        switch (LinkPolicy.ActionFor(target))
        {
            case HudLinkAction.Open open:
                _start(new ProcessStartInfo(open.Url) { UseShellExecute = true });
                break;
            case HudLinkAction.Reveal reveal when _exists(reveal.Path):
                _start(WindowsExplorer.StartInfo(HudLinkPolicy.ExplorerSelectArguments(reveal.Path)));
                break;
        }
    }
}
