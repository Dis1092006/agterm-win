using System.ComponentModel;
using System.Diagnostics;
using AgTerm.ControlServer;
using AgTerm.Core.Install;
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
    string Selection = "",
    Guid? SessionId = null)
{
    public static CustomCommandExecutionRequest FromHotkey(CustomHotkeyEvent hit) =>
        new(hit.CommandId, CustomCommandSource.Hotkey, hit.Dashboard ? null : hit.PaneId, hit.Generation);
}

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
    IReadOnlyDictionary<string, string> Environment,
    Guid? SessionId = null,
    bool ErrorHud = false,
    HudPosition ErrorPosition = HudPositions.Default,
    OverlayPane? ErrorPane = null);

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
                },
                sessionId: null);
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
            },
            session.Id);
    }

    private static (Session Session, CommandContext.Pane Pane)? ResolveSession(
        AppStore store,
        CustomCommandExecutionRequest request)
    {
        if (request.SessionId is { } sessionId)
        {
            if (store.SessionWithId(sessionId) is not { } requested)
                return null;
            return (requested, FocusedPrimaryOrSplit(requested));
        }

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

        return (active, FocusedPrimaryOrSplit(active));
    }

    private static CommandContext.Pane FocusedPrimaryOrSplit(Session session) =>
        session.SplitFocused && session.SplitSurface is not null
            ? CommandContext.Pane.Right
            : CommandContext.Pane.Left;

    private static CustomCommandInvocation CreateInvocation(
        CustomCommand command,
        CommandContext context,
        Guid? sessionId) =>
        new(
            command.Id,
            command.Name,
            context.Expand(command.Command),
            context.SessionPwd,
            new Dictionary<string, string>(context.Environment(), StringComparer.Ordinal),
            sessionId,
            command.ErrorHud,
            command.ErrorPosition,
            command.ErrorPane);

    private static string Upper(Guid value) => value.ToString("D").ToUpperInvariant();
}

/// <summary>How a custom command ended. Detail is the last stderr line, and only a command that opted
/// into a failure panel has its stderr captured.</summary>
public sealed record CustomCommandExit(int ExitCode, string? Detail);

public sealed class CustomCommandProcessHandle
{
    public CustomCommandProcessHandle(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        ProcessId = process.Id;
        Finished = WaitForExitAndDisposeAsync(process);
        Exited = Finished;
    }

    public CustomCommandProcessHandle(StderrCapturingProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        ProcessId = process.ProcessId;
        Finished = ExitOfAsync(process);
        Exited = Finished;
    }

    public int ProcessId { get; }

    /// <summary>Completes when the process exits; faults the same way as <see cref="Finished"/>.</summary>
    public Task Exited { get; }

    public Task<CustomCommandExit> Finished { get; }

    private static async Task<CustomCommandExit> WaitForExitAndDisposeAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return new CustomCommandExit(process.ExitCode, null);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task<CustomCommandExit> ExitOfAsync(StderrCapturingProcess process)
    {
        var captured = await process.Exited.ConfigureAwait(false);
        return new CustomCommandExit(captured.ExitCode, CommandFailure.Detail(captured.StderrTail));
    }
}

/// <summary>A custom command that never started; the message is the reason shown to the user.</summary>
public sealed class CustomCommandNotStartedException(string reason) : Exception(reason);

public static class CustomCommandFailureNotice
{
    public static LiveFailureNotice NotStarted(string name, string reason) =>
        new("Command failed", $"{name} ({reason})");
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
            return Start(invocation);
        }, cancellationToken);

    /// <summary>Starts the process on the calling thread, for a caller that cannot wait on another
    /// thread (keymap.run runs inside the host gate).</summary>
    public CustomCommandProcessHandle Start(CustomCommandInvocation invocation)
    {
        // Process.Start would refuse it too, with "The directory name is invalid" and no path.
        if (!string.IsNullOrWhiteSpace(invocation.WorkingDirectory) && !Path.Exists(invocation.WorkingDirectory))
            throw new CustomCommandNotStartedException(
                $"session directory {TerminalText.Sanitized(invocation.WorkingDirectory)} no longer exists");
        var startInfo = CreateStartInfo(invocation, _which, _env);
        try
        {
            if (invocation.ErrorHud && TryStartCapturing(startInfo, invocation.Name) is { } captured)
                return new CustomCommandProcessHandle(captured);
            var process = Process.Start(startInfo);
            if (process is null)
                throw new InvalidOperationException($"Failed to start custom command '{invocation.Name}'.");
            return new CustomCommandProcessHandle(process);
        }
        catch (Win32Exception ex)
        {
            throw new CustomCommandNotStartedException(ex.Message);
        }
    }

    /// <summary>A full or read-only TEMP must cost the failure panel, not the command: upstream runs it
    /// without capture too.</summary>
    private static StderrCapturingProcess? TryStartCapturing(ProcessStartInfo startInfo, string name)
    {
        try
        {
            return StderrCapturingProcess.Start(startInfo);
        }
        catch (StderrCaptureUnavailableException ex)
        {
            Debug.WriteLine($"custom command '{name}': stderr capture unavailable: {ex.InnerException?.Message}");
            return null;
        }
    }

    public static ProcessStartInfo CreateStartInfo(
        CustomCommandInvocation invocation,
        Func<string, string?>? which = null,
        Func<string, string?>? env = null,
        string? processPath = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        which ??= FindExecutableOnPath;
        env ??= Environment.GetEnvironmentVariable;

        var shell = ShellResolver.ResolveCommandLineShell(which, env);
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

        // Custom commands get AGT_*; an AGTERM_* value here would come from another instance.
        foreach (var name in SurfaceEnvironment.OwnedVariables)
            startInfo.Environment.Remove(name);
        foreach (var pair in invocation.Environment)
            startInfo.Environment[pair.Key] = pair.Value;

        processPath ??= Environment.ProcessPath;
        var directory = Path.GetDirectoryName(processPath);
        var bundledCliDirectory = !string.IsNullOrEmpty(directory) &&
            File.Exists(Path.Join(directory, CliInstall.ToolFileName))
            ? directory : null;
        startInfo.Environment.TryGetValue("PATH", out var inheritedPath);
        startInfo.Environment["PATH"] = CommandPath.Widened(inheritedPath, bundledCliDirectory);
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