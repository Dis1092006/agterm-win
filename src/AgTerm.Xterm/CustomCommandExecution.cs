using System.Diagnostics;
using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Terminal;

namespace AgTerm.Xterm;

public enum CustomCommandSource
{
    Palette,
    Hotkey,
}

public sealed record CustomCommandExecutionRequest(
    Guid CommandId,
    CustomCommandSource Source,
    string? PaneId = null,
    long? HotkeyGeneration = null,
    string Selection = "");

public sealed record CustomCommandWindowContext(
    Guid WindowId,
    string WindowName,
    string PipeName,
    long HotkeyGeneration);

public sealed record CustomCommandInvocation(
    Guid CommandId,
    string Name,
    string ShellLine,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

public static class CustomCommandInvocationResolver
{
    public static CustomCommandInvocation? Resolve(
        AppStore store,
        Keymap keymap,
        CustomCommandExecutionRequest request,
        CustomCommandWindowContext window)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(keymap);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(window);

        if (request.Source == CustomCommandSource.Hotkey &&
            request.HotkeyGeneration != window.HotkeyGeneration)
        {
            return null;
        }

        var command = keymap.Commands.FirstOrDefault(candidate => candidate.Id == request.CommandId);
        if (command is null)
            return null;

        var resolved = ResolveSession(store, request);
        if (resolved is null)
        {
            if (request.Source == CustomCommandSource.Palette ||
                CommandContext.ReferencesSessionScopedContext(command.Command))
            {
                return null;
            }

            return CreateInvocation(
                command,
                new CommandContext
                {
                    WindowId = Upper(window.WindowId),
                    WindowName = window.WindowName,
                    Socket = window.PipeName,
                });
        }

        var (session, pane) = resolved.Value;
        var workspace = store.WorkspaceForSession(session.Id);
        return CreateInvocation(
            command,
            new CommandContext
            {
                SessionId = Upper(session.Id),
                SessionName = session.DisplayName,
                SessionPwd = session.CwdFor(pane),
                WorkspaceId = workspace is null ? string.Empty : Upper(workspace.Id),
                WorkspaceName = workspace?.Name ?? string.Empty,
                WindowId = Upper(window.WindowId),
                WindowName = window.WindowName,
                FocusedPane = pane,
                Selection = request.Selection,
                Socket = window.PipeName,
            });
    }

    private static (Session Session, CommandContext.Pane Pane)? ResolveSession(
        AppStore store,
        CustomCommandExecutionRequest request)
    {
        if (request.Source == CustomCommandSource.Hotkey && request.PaneId is { } paneId)
        {
            foreach (var session in store.Workspaces.SelectMany(workspace => workspace.Sessions))
            {
                if (session.Surface?.PaneToken == paneId)
                    return (session, CommandContext.Pane.Left);
                if (session.SplitSurface?.PaneToken == paneId)
                    return (session, CommandContext.Pane.Right);
                if (session.ScratchSurface?.PaneToken == paneId)
                    return (session, CommandContext.Pane.Scratch);
            }
        }

        if (store.ActiveSession is not { } active)
            return null;

        var onSplit = active.SplitFocused && active.SplitSurface is not null;
        return onSplit
            ? (active, CommandContext.Pane.Right)
            : (active, CommandContext.Pane.Left);
    }

    private static CustomCommandInvocation CreateInvocation(
        CustomCommand command,
        CommandContext context) =>
        new(
            command.Id,
            command.Name,
            context.Expand(command.Command),
            context.SessionPwd,
            new Dictionary<string, string>(context.Environment(), StringComparer.Ordinal));

    private static string Upper(Guid value) => value.ToString("D").ToUpperInvariant();
}

public sealed class CustomCommandProcessHandle
{
    public CustomCommandProcessHandle(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        ProcessId = process.Id;
        Exited = WaitForExitAndDisposeAsync(process);
    }

    public int ProcessId { get; }
    public Task Exited { get; }

    private static async Task WaitForExitAndDisposeAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        finally
        {
            process.Dispose();
        }
    }
}

public sealed class CustomCommandProcessRunner
{
    private readonly Func<string, string?>? _which;
    private readonly Func<string, string?>? _env;

    public CustomCommandProcessRunner(
        Func<string, string?>? which = null,
        Func<string, string?>? env = null)
    {
        _which = which;
        _env = env;
    }

    public Task<CustomCommandProcessHandle> StartAsync(
        CustomCommandInvocation invocation,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = Process.Start(CreateStartInfo(invocation, _which, _env));
            if (process is null)
                throw new InvalidOperationException($"Failed to start custom command '{invocation.Name}'.");
            return new CustomCommandProcessHandle(process);
        }, cancellationToken);

    public static ProcessStartInfo CreateStartInfo(
        CustomCommandInvocation invocation,
        Func<string, string?>? which = null,
        Func<string, string?>? env = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        which ??= FindExecutableOnPath;
        env ??= Environment.GetEnvironmentVariable;

        var shell = ShellResolver.Resolve(null, which, env);
        var isPowerShell = string.Equals(
            Path.GetFileName(shell),
            "pwsh.exe",
            StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrWhiteSpace(invocation.WorkingDirectory))
            startInfo.WorkingDirectory = invocation.WorkingDirectory;

        if (isPowerShell)
        {
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
        }
        else
        {
            startInfo.Arguments = $"/d /c \"{invocation.ShellLine}\"";
        }
        if (isPowerShell)
            startInfo.ArgumentList.Add(invocation.ShellLine);

        foreach (var pair in invocation.Environment)
            startInfo.Environment[pair.Key] = pair.Value;
        return startInfo;
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}