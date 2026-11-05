using System.Diagnostics;
using AgTerm.Core.Hooks;
using AgTerm.Core.Install;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

/// <summary>
/// The app-side <see cref="IHookLauncher"/>. Runs the hook line in cmd.exe with no window, the event
/// as one JSON line on stdin and the AGT_* variables set. Output is discarded. onExit is delivered
/// once the child has exited and the stdin writer has finished, never inline from Start, so no input
/// write outlives the run it belongs to.
/// </summary>
public sealed class HookProcessRunner : IHookLauncher
{
    private readonly Func<string> _socketProvider;
    private readonly string? _bundledCliDirectory;
    private readonly string _shellPath;
    private readonly Func<ControlEvent, string> _encode;

    /// <param name="socketProvider">The value of AGT_SOCKET, the pipe agtermctl --socket accepts.</param>
    /// <param name="bundledCliDirectory">Put first on PATH so a hook finds agtermctl.</param>
    /// <param name="shellPath">Injection point for tests; production uses ComSpec.</param>
    /// <param name="encode">Injection point for tests; production uses the control wire JSON.</param>
    public HookProcessRunner(
        Func<string> socketProvider,
        string? bundledCliDirectory = null,
        string? shellPath = null,
        Func<ControlEvent, string>? encode = null)
    {
        _socketProvider = socketProvider;
        _bundledCliDirectory = bundledCliDirectory;
        _shellPath = shellPath ?? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        _encode = encode ?? (e => ControlJson.Serialize(e));
    }

    public int Start(HookEntry entry, ControlEvent controlEvent, Action<string> onDeliveryFailure, Action<int> onExit)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _shellPath,
            Arguments = $"{HookShell.CommandSwitch} \"{entry.Command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        ApplyEnvironment(startInfo, controlEvent);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) =>
        {
            try { exited.TrySetResult(process.ExitCode); }
            catch (Exception) { exited.TrySetResult(-1); }
        };
        process.OutputDataReceived += static (_, _) => { };
        process.ErrorDataReceived += static (_, _) => { };

        process.Start();
        var pid = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var writer = Task.Run(() => WriteEvent(process, controlEvent, onDeliveryFailure));
        _ = Task.Run(async () =>
        {
            var code = await exited.Task.ConfigureAwait(false);
            await writer.ConfigureAwait(false);
            process.Dispose();
            onExit(code);
        });
        return pid;
    }

    private void WriteEvent(Process process, ControlEvent controlEvent, Action<string> onDeliveryFailure)
    {
        try
        {
            string line;
            try { line = _encode(controlEvent) + "\n"; }
            catch (Exception ex)
            {
                onDeliveryFailure($"encode: {ex.Message}");
                return;
            }
            // a script that exits without reading closes the pipe: that is not a failure
            try
            {
                process.StandardInput.Write(line);
                process.StandardInput.Flush();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch (Exception ex) { onDeliveryFailure(ex.Message); }
        }
        finally
        {
            try { process.StandardInput.Close(); }
            catch (Exception) { }
        }
    }

    /// <summary>Every fixed variable is set explicitly, empty when the event lacks the field, so an
    /// inherited value can never point a script at the wrong session.</summary>
    private void ApplyEnvironment(ProcessStartInfo startInfo, ControlEvent controlEvent)
    {
        var environment = startInfo.Environment;
        foreach (var name in SurfaceEnvironment.OwnedVariables)
            environment.Remove(name);
        environment["AGT_EVENT_KIND"] = controlEvent.Kind.ToWire();
        environment["AGT_EVENT_STATUS"] = controlEvent.Payload.Status ?? "";
        environment["AGT_EVENT_HOST"] = controlEvent.Payload.Host ?? "";
        environment["AGT_SESSION_ID"] = controlEvent.Session ?? "";
        environment["AGT_WORKSPACE_ID"] = controlEvent.Workspace ?? "";
        environment["AGT_WINDOW_ID"] = controlEvent.Window ?? "";
        environment["AGT_SOCKET"] = _socketProvider();
        environment.TryGetValue("PATH", out var inheritedPath);
        environment["PATH"] = CommandPath.Widened(inheritedPath, _bundledCliDirectory);
    }
}
