using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgTerm.Terminal;

/// <summary>How to spawn a pseudoconsole child (§10: shell in cwd, or a --command argv directly).</summary>
public sealed record ConPtyStartInfo
{
    /// <summary>The child's argv. argv[0] is the program (execed directly — no shell wrapping,
    /// per the §6 --command contract); the list is turned into a CreateProcessW command line with
    /// CommandLineToArgvW-inverse quoting.</summary>
    public required IReadOnlyList<string> Argv { get; init; }

    /// <summary>Working directory for the child; null = inherit the host's.</summary>
    public string? Cwd { get; init; }

    public int Cols { get; init; } = 80;

    public int Rows { get; init; } = 24;

    /// <summary>Raw VT output chunks from the pty, invoked on the reader thread. Wired at start so
    /// no output can race a later event subscription.</summary>
    public Action<byte[]>? OnOutput { get; init; }

    /// <summary>Variables added on top of the host's inherited environment (e.g. the surface's
    /// AGTERM_* set). Null = plain inheritance.</summary>
    public IReadOnlyDictionary<string, string>? ExtraEnvironment { get; init; }

    /// <summary>Inherited variables to drop from the child (e.g. a launcher's TERM_PROGRAM, which
    /// would misidentify the terminal). Removal applies before ExtraEnvironment, so naming a
    /// variable in both keeps the explicit value. Null = remove nothing.</summary>
    public IReadOnlyList<string>? RemoveEnvironment { get; init; }

    /// <summary>Start the pseudoconsole at the terminal's current cursor instead of clearing the
    /// screen (PSEUDOCONSOLE_INHERIT_CURSOR). Used when the terminal already shows restored output.
    /// ConPTY then sends a cursor-position request (ESC[6n) and waits for the terminal's reply on
    /// the input pipe before it renders anything.</summary>
    public bool InheritCursor { get; init; }
}

/// <summary>
/// One live pseudoconsole: a ConPTY pair + a spawned child process. Owns the reader thread
/// (pty output → OnOutput), the input stream (WriteText — keystroke bytes, Enter is CR),
/// resize, and the exit watcher (Exited completes with the child's exit code).
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private readonly Lock _writeGate = new();
    private readonly TaskCompletionSource<int> _exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _outputCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FileStream _input;
    private readonly IntPtr _console;
    private readonly IntPtr _process;
    private int _consoleClosed;
    private bool _disposed;

    private ConPtySession(IntPtr console, IntPtr process, FileStream input)
    {
        _console = console;
        _process = process;
        _input = input;
    }

    /// <summary>Task that completes with the child's exit code.</summary>
    public Task<int> Exited => _exited.Task;

    /// <summary>Task that completes after output reader reaches EOF and all OnOutput callbacks return.</summary>
    public Task OutputCompleted => _outputCompleted.Task;

    public static ConPtySession Start(ConPtyStartInfo info)
    {
        var commandLine = WindowsCommandLine.FromArgv(info.Argv);

        if (!ConPtyNative.CreatePipe(out var ptyInputRead, out var ourInputWrite, IntPtr.Zero, 0))
            throw new Win32Exception();
        if (!ConPtyNative.CreatePipe(out var ourOutputRead, out var ptyOutputWrite, IntPtr.Zero, 0))
            throw new Win32Exception();

        var size = new ConPtyNative.Coord { X = (short)info.Cols, Y = (short)info.Rows };
        var hr = ConPtyNative.CreatePseudoConsole(
            size, ptyInputRead, ptyOutputWrite,
            info.InheritCursor ? ConPtyNative.PseudoConsoleInheritCursor : 0u, out var console);
        if (hr != 0) throw new Win32Exception(hr, "CreatePseudoConsole failed");
        // the pseudoconsole holds its own duplicates of the child-side pipe ends
        ptyInputRead.Dispose();
        ptyOutputWrite.Dispose();

        var attributeList = IntPtr.Zero;
        try
        {
            var listSize = IntPtr.Zero;
            ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            attributeList = Marshal.AllocHGlobal(listSize);
            if (!ConPtyNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize))
                throw new Win32Exception();
            if (!ConPtyNative.UpdateProcThreadAttribute(
                    attributeList, 0, ConPtyNative.ProcThreadAttributePseudoConsole,
                    console, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            var environmentBlock = BuildEnvironmentBlock(info.ExtraEnvironment, info.RemoveEnvironment);
            var startup = new ConPtyNative.StartupInfoEx
            {
                // STARTF_USESTDHANDLES with null handles: without it CreateProcessW duplicates the
                // host's own (possibly redirected) std handles into the child, and child output
                // bypasses the pseudoconsole into the host's stdout (the Windows Terminal
                // ConptyConnection workaround). Null std handles are replaced by conhost with
                // handles to the pseudoconsole itself.
                StartupInfo =
                {
                    Cb = Marshal.SizeOf<ConPtyNative.StartupInfoEx>(),
                    Flags = ConPtyNative.StartfUseStdHandles,
                },
                AttributeList = attributeList,
            };
            bool created;
            int createError;
            ConPtyNative.ProcessInformation processInfo;
            try
            {
                created = ConPtyNative.CreateProcessW(
                    null, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles: false,
                    ConPtyNative.ExtendedStartupInfoPresent | ConPtyNative.CreateUnicodeEnvironment,
                    environmentBlock, info.Cwd, ref startup, out processInfo);
                createError = Marshal.GetLastWin32Error();
            }
            finally
            {
                if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
            }
            if (!created)
            {
                ConPtyNative.ClosePseudoConsole(console);
                throw new Win32Exception(createError, $"CreateProcessW failed for: {commandLine}");
            }
            ConPtyNative.CloseHandle(processInfo.Thread);

            // unbuffered: a write that fails once the console is closed must not leave its bytes
            // in a buffer that Dispose would flush into the closed pipe, and Write flushes anyway
            var input = new FileStream(ourInputWrite, FileAccess.Write, bufferSize: 0);
            var session = new ConPtySession(console, processInfo.Process, input);
            session.StartReader(ourOutputRead, info.OnOutput);
            session.StartExitWatcher();
            return session;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                ConPtyNative.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
        }
    }

    /// <summary>A CREATE_UNICODE_ENVIRONMENT block: the inherited process environment with the
    /// removals applied and the extras overlaid (case-insensitive keys, like the environment
    /// itself), sorted per the CreateProcessW contract, double-NUL-terminated. Zero when there is
    /// nothing to change (null = plain inheritance, cheaper and identical).</summary>
    private static IntPtr BuildEnvironmentBlock(
        IReadOnlyDictionary<string, string>? extra, IReadOnlyList<string>? remove)
    {
        if ((extra is null || extra.Count == 0) && (remove is null || remove.Count == 0))
            return IntPtr.Zero;
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            merged[(string)entry.Key] = (string?)entry.Value ?? "";
        // removal first: an explicit extra of the same name is a declaration, and wins
        if (remove is not null)
            foreach (var key in remove)
                merged.Remove(key);
        if (extra is not null)
            foreach (var (key, value) in extra)
                merged[key] = value;
        var block = new StringBuilder();
        foreach (var (key, value) in merged)
            block.Append(key).Append('=').Append(value).Append('\0');
        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    private void StartReader(SafeFileHandle outputRead, Action<byte[]>? onOutput)
    {
        var reader = new Thread(() =>
        {
            using var output = new FileStream(outputRead, FileAccess.Read);
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    var read = output.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    if (onOutput is null) continue;
                    var chunk = new byte[read];
                    Array.Copy(buffer, chunk, read);
                    onOutput(chunk);
                }
            }
            catch (IOException)
            {
                // pipe broken — the pseudoconsole was closed; treated as EOF
            }
            catch (Exception ex)
            {
                _outputCompleted.TrySetException(ex);
            }
            finally
            {
                _outputCompleted.TrySetResult();
            }
        })
        { IsBackground = true, Name = "conpty-output" };
        reader.Start();
    }

    private void StartExitWatcher()
    {
        var watcher = new Thread(() =>
        {
            ConPtyNative.WaitForSingleObject(_process, ConPtyNative.Infinite);
            ConPtyNative.GetExitCodeProcess(_process, out var code);
            // closing the pseudoconsole releases conhost, which EOFs the reader after the
            // remaining buffered output has been drained
            CloseConsoleOnce();
            _exited.TrySetResult(unchecked((int)code));
        })
        { IsBackground = true, Name = "conpty-exit" };
        watcher.Start();
    }

    /// <summary>Send text to the child as keystrokes (raw bytes into the pty input; the caller
    /// maps Enter to CR per session.type semantics).</summary>
    public void WriteText(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Write(byte[] data)
    {
        lock (_writeGate)
        {
            if (_disposed) return;
            try
            {
                _input.Write(data, 0, data.Length);
                _input.Flush();
            }
            catch (IOException)
            {
                // child gone mid-write — the exit watcher reports it
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        if (Volatile.Read(ref _consoleClosed) != 0) return;
        ConPtyNative.ResizePseudoConsole(
            _console, new ConPtyNative.Coord { X = (short)cols, Y = (short)rows });
    }

    private void CloseConsoleOnce()
    {
        if (Interlocked.Exchange(ref _consoleClosed, 1) == 0)
            ConPtyNative.ClosePseudoConsole(_console);
    }

    /// <summary>Kills the child (if still alive), closes the pseudoconsole and the input pipe.</summary>
    public void Dispose()
    {
        lock (_writeGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (!_exited.Task.IsCompleted)
            ConPtyNative.TerminateProcess(_process, 1);
        // the exit watcher observes the kill, closes the console, and completes Exited;
        // closing here too covers the already-exited path (both are once-guarded)
        ConPtyNative.WaitForSingleObject(_process, 5000);
        CloseConsoleOnce();
        _input.Dispose();
        ConPtyNative.CloseHandle(_process);
    }
}
