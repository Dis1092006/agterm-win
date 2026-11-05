using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

// msg.* on the application host (port of ControlServer+Messages.swift). The bus is app-wide, so an address
// resolves its session across every open window, never the frontmost default for an id, and a broadcast
// without --workspace spans every open window. Each window still resolves the pane in its own store.

public sealed partial class LiveApplicationHost
{
    private MessageDispatcher? _messages;

    private MessageDispatcher Messages => _messages ??= new MessageDispatcher(new AppMessageHost(this));

    private sealed class AppMessageHost(LiveApplicationHost app) : IMessagePaneHost
    {
        public MessagePaneResult<MessageRecipient> ResolveMessagePane(MessageAddress address)
        {
            var (windows, active) = app.OpenWindowHosts();
            var owners = windows
                .SelectMany(window => window.CaptureSessionIds().Select(session => (Window: window, Session: session)))
                .ToArray();
            Guid? activeSession = null;
            if (active is { } activeWindow)
                activeWindow.RunLocked(() => activeSession = activeWindow.Store.SelectedSessionId);
            var resolution = ControlResolve.Resolve(address.Session, owners.Select(o => o.Session).ToArray(), activeSession);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return new MessagePaneError(ControlResolve.ErrorMessage("session", address.Session, resolution));
            var owner = owners.First(o => o.Session == resolution.Id).Window;
            var canonical = address with { Session = resolution.Id.ToString("D") };
            MessagePaneResult<MessageRecipient> result = default;
            owner.RunLocked(() => result = owner.Actions.ResolveMessagePane(canonical));
            return result;
        }

        public MessagePaneResult<IReadOnlyList<MessageRecipient>> MessagePanes(string? workspace, string? window)
        {
            var (windows, active) = app.OpenWindowHosts();
            if (workspace is null)
            {
                var all = new List<MessageRecipient>();
                foreach (var open in windows) open.RunLocked(() => all.AddRange(open.Store.MessageRecipients()));
                return MessagePaneResult<IReadOnlyList<MessageRecipient>>.Ok(all);
            }

            var owner = app.MessageWorkspaceOwner(workspace, window, windows, active);
            if (owner.Error is { } error) return error;
            MessagePaneResult<IReadOnlyList<MessageRecipient>> result = default;
            owner.Window!.RunLocked(() => result = owner.Window.Actions.MessagePanes(workspace, null));
            return result;
        }

        public void MessageQueued(Guid messageId, string? from, IReadOnlyList<MessageRecipient> recipients)
        {
            var (windows, _) = app.OpenWindowHosts();
            foreach (var recipient in recipients)
            {
                foreach (var window in windows)
                {
                    var emitted = false;
                    window.RunLocked(() => emitted = window.Store.EmitMessageQueued(messageId, from, recipient));
                    if (emitted) break;
                }
            }
        }
    }

    /// <summary>The open windows in window order, and the active one.</summary>
    private (LiveWindowHost[] Windows, LiveWindowHost? Active) OpenWindowHosts()
    {
        lock (_gate)
        {
            var windows = _windows
                .Where(entry => entry.IsOpen && _openWindows.ContainsKey(entry.Id))
                .Select(entry => _openWindows[entry.Id])
                .ToArray();
            return (windows, ActiveWindowIdLocked() is { } id && _openWindows.TryGetValue(id, out var active) ? active : null);
        }
    }

    /// <summary>The open window holding a workspace: the one --window names, else whichever holds the
    /// workspace id, with "active" meaning the active window's.</summary>
    private (LiveWindowHost? Window, MessagePaneError? Error) MessageWorkspaceOwner(
        string workspace, string? window, LiveWindowHost[] windows, LiveWindowHost? active)
    {
        var selector = window?.Trim();
        if (!string.IsNullOrEmpty(selector))
        {
            LiveWindowEntry[] entries;
            Guid? activeId;
            lock (_gate)
            {
                entries = _windows.ToArray();
                activeId = ActiveWindowIdLocked();
            }
            var resolution = ControlResolve.Resolve(selector, entries.Select(entry => entry.Id).ToArray(), activeId);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return (null, new MessagePaneError(ControlResolve.ErrorMessage("window", selector, resolution)));
            var named = windows.FirstOrDefault(open => open.WindowId == resolution.Id);
            return named is null ? (null, new MessagePaneError(LiveWindowHost.WindowNotOpenError)) : (named, null);
        }

        if (workspace.Trim() == "active")
            return active is null ? (null, new MessagePaneError("no such window: active")) : (active, null);
        var owners = windows
            .SelectMany(open => open.CaptureWorkspaceIds().Select(id => (Window: open, Workspace: id)))
            .ToArray();
        var found = ControlResolve.Resolve(workspace.Trim(), owners.Select(o => o.Workspace).ToArray(), active: null);
        if (found.Kind != TargetResolution.Outcome.Resolved)
            return (null, new MessagePaneError(ControlResolve.ErrorMessage("workspace", workspace, found)));
        return (owners.First(o => o.Workspace == found.Id).Window, null);
    }
}
