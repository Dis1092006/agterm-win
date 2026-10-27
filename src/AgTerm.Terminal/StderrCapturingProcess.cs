using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AgTerm.Core.Keymap;
using Microsoft.Win32.SafeHandles;

namespace AgTerm.Terminal;

/// <summary>What a captured process left behind: its exit code and the last bytes of its stderr.</summary>
public sealed record CapturedExit(int ExitCode, byte[] StderrTail);

/// <summary>The stderr file could not be created, so nothing was started. The caller can still run the
/// command without capture.</summary>
public sealed class StderrCaptureUnavailableException(Exception inner)
    : Exception("The stderr capture file could not be created.", inner);

/// <summary>
/// A background process with stdin and stdout on NUL and stderr in a temp file, started without a
/// console window. The file is read once the process exits and then deleted.
/// The stderr goes to a file and not to a pipe because a pipe's read end lives only as long as agterm:
/// a background process the command started would fail its next write after the app quits, while a
/// file needs no reader. For the same reason <see cref="Exited"/> waits on the process handle only,
/// never on the file, so a descendant that keeps the file open does not delay it.
/// </summary>
public sealed class StderrCapturingProcess
{
    private readonly TaskCompletionSource<CapturedExit> _exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _stderrPath;
    private readonly IntPtr _process;
    private readonly WaitHandle _processWait;

    private StderrCapturingProcess(int processId, IntPtr process, string stderrPath)
    {
        ProcessId = processId;
        _process = process;
        _stderrPath = stderrPath;
        // the process handle is closed once, in OnExited, so the wait handle must not own it
        _processWait = new ProcessWaitHandle(process);
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(IntPtr process) =>
            SafeWaitHandle = new SafeWaitHandle(process, ownsHandle: false);
    }

    public int ProcessId { get; }

    /// <summary>Completes when the process itself exits, with its exit code and the stderr tail.</summary>
    public Task<CapturedExit> Exited => _exited.Task;

    /// <summary>Throws <see cref="Win32Exception"/> when the process cannot be created, and
    /// <see cref="StderrCaptureUnavailableException"/> when the stderr file cannot.</summary>
    public static StderrCapturingProcess Start(ProcessStartInfo startInfo)
    {
        var commandLine = CommandLine(startInfo);
        var stderrPath = Path.Combine(StderrDirectory(), $"agterm-cmd-{Guid.NewGuid():N}.stderr");
        try
        {
            using var nul = File.OpenHandle("NUL", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var stderr = CreateStderrFile(stderrPath);
            var process = Create(startInfo, commandLine, nul, stderr, out var processId);
            var captured = new StderrCapturingProcess(processId, process, stderrPath);
            captured.WatchForExit();
            return captured;
        }
        catch
        {
            DeleteQuietly(stderrPath);
            throw;
        }
    }

    private static SafeFileHandle CreateStderrFile(string path)
    {
        try
        {
            return File.OpenHandle(
                path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StderrCaptureUnavailableException(ex);
        }
    }

    private static string CommandLine(ProcessStartInfo startInfo)
    {
        var program = WindowsCommandLine.FromArgv([startInfo.FileName]);
        if (startInfo.ArgumentList.Count > 0)
            return program + " " + WindowsCommandLine.FromArgv(startInfo.ArgumentList);
        return startInfo.Arguments.Length > 0 ? program + " " + startInfo.Arguments : program;
    }

    private static IntPtr Create(
        ProcessStartInfo startInfo, string commandLine, SafeFileHandle nul, SafeFileHandle stderr, out int processId)
    {
        var nulHandle = Inheritable(nul);
        var stderrHandle = Inheritable(stderr);
        var inherited = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var attributeList = IntPtr.Zero;
        var environmentBlock = IntPtr.Zero;
        try
        {
            Marshal.WriteIntPtr(inherited, 0, nulHandle);
            Marshal.WriteIntPtr(inherited, IntPtr.Size, stderrHandle);
            attributeList = HandleListAttributes(inherited, IntPtr.Size * 2);
            environmentBlock = EnvironmentBlock.Encode(Variables(startInfo));
            var startup = new ConPtyNative.StartupInfoEx
            {
                StartupInfo =
                {
                    Cb = Marshal.SizeOf<ConPtyNative.StartupInfoEx>(),
                    Flags = ConPtyNative.StartfUseStdHandles,
                    StdInput = nulHandle,
                    StdOutput = nulHandle,
                    StdError = stderrHandle,
                },
                AttributeList = attributeList,
            };
            var directory = string.IsNullOrEmpty(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory;
            if (!ConPtyNative.CreateProcessW(
                    null, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles: true,
                    ConPtyNative.ExtendedStartupInfoPresent | ConPtyNative.CreateUnicodeEnvironment
                        | ConPtyNative.CreateNoWindow,
                    environmentBlock, directory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            ConPtyNative.CloseHandle(info.Thread);
            processId = info.ProcessId;
            return info.Process;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                ConPtyNative.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
            Marshal.FreeHGlobal(inherited);
        }
    }

    private static IntPtr Inheritable(SafeFileHandle handle)
    {
        var raw = handle.DangerousGetHandle();
        if (!ConPtyNative.SetHandleInformation(raw, ConPtyNative.HandleFlagInherit, ConPtyNative.HandleFlagInherit))
            throw new Win32Exception();
        return raw;
    }

    /// <summary>Restricts inheritance to the listed handles: without the list, every inheritable handle
    /// in agterm (a session's pipes, for one) would leak into the command.</summary>
    private static IntPtr HandleListAttributes(IntPtr handles, int byteCount)
    {
        var listSize = IntPtr.Zero;
        ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
        var attributeList = Marshal.AllocHGlobal(listSize);
        if (!ConPtyNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize))
        {
            var error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(error);
        }
        if (!ConPtyNative.UpdateProcThreadAttribute(
                attributeList, 0, ConPtyNative.ProcThreadAttributeHandleList,
                handles, new IntPtr(byteCount), IntPtr.Zero, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            ConPtyNative.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(error);
        }
        return attributeList;
    }

    private static Dictionary<string, string> Variables(ProcessStartInfo startInfo)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in startInfo.Environment)
            variables[key] = value ?? "";
        return variables;
    }

    private void WatchForExit() =>
        ThreadPool.RegisterWaitForSingleObject(
            _processWait, (_, _) => OnExited(), null, Timeout.Infinite, executeOnlyOnce: true);

    private void OnExited()
    {
        CapturedExit? exit = null;
        Exception? failure = null;
        try
        {
            try
            {
                exit = new CapturedExit(ExitCode(), ReadTailOrEmpty());
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            // before completing: an awaiter that sees Exited must find the file gone
            DeleteQuietly(_stderrPath);
        }
        finally
        {
            ConPtyNative.CloseHandle(_process);
            _processWait.Dispose();
        }
        if (exit is not null) _exited.TrySetResult(exit);
        else _exited.TrySetException(failure!);
    }

    /// <summary>A zero from a failed query would read as a clean exit, so a failure is an error.</summary>
    private int ExitCode()
    {
        if (!ConPtyNative.GetExitCodeProcess(_process, out var code))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return unchecked((int)code);
    }

    /// <summary>The stderr is a diagnostic extra: a scanner or a temp cleaner holding the file must not
    /// cost the caller the exit code.</summary>
    private byte[] ReadTailOrEmpty()
    {
        try
        {
            return TailReader(_stderrPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Replaceable so a test can make the read fail; a real file cannot be locked against a
    /// reader that shares delete access.</summary>
    internal static Func<string, byte[]> TailReader { get; set; } = ReadTail;

    /// <summary>Replaceable so a test can make the temp file uncreatable without touching the real TEMP.</summary>
    internal static Func<string> StderrDirectory { get; set; } = Path.GetTempPath;

    private static byte[] ReadTail(string path)
    {
        using var file = File.OpenHandle(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var size = RandomAccess.GetLength(file);
        var length = (int)Math.Min(size, CommandFailure.TailLimit);
        var tail = new byte[length];
        var offset = size - length;
        var filled = 0;
        while (filled < length)
        {
            var read = RandomAccess.Read(file, tail.AsSpan(filled), offset + filled);
            if (read == 0) break;
            filled += read;
        }
        return filled == length ? tail : tail[..filled];
    }

    /// <summary>A leftover temp file is harmless; a failed delete must not turn into a failed exit report.</summary>
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
