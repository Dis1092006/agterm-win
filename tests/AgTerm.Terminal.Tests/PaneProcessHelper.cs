using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AgTerm.Terminal.Tests;

/// <summary>
/// A process the pane tests run inside a pseudoconsole (the test assembly started as an isolated
/// scenario). It reports its own pid and its console host pid, starts the child that
/// AGTERM_TEST_PANE_HELPER names, prints READY and waits to be ended.
/// </summary>
internal static class PaneProcessHelper
{
    internal const string Scenario = "pane-helper";
    internal const string ModeVariable = "AGTERM_TEST_PANE_HELPER";
    internal const string MarkerVariable = "AGTERM_TEST_MARKER";

    private const uint CreateSuspended = 0x00000004;
    private const uint DetachedProcess = 0x00000008;
    private const uint CreateNewConsole = 0x00000010;
    internal const uint CreateBreakawayFromJob = 0x01000000;
    private const int StartfUseShowWindow = 0x00000001;
    private const uint CtrlCloseEvent = 2;

    private static readonly string Ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
    private static readonly string LongPing = $"\"{Ping}\" -n 30 127.0.0.1";

    private static CtrlHandler? _handler;

    internal static Task RunAsync()
    {
        var mode = Environment.GetEnvironmentVariable(ModeVariable) ?? "";
        if (mode == "freeconsole-child")
        {
            if (!FreeConsole()) throw new Win32Exception();
            Thread.Sleep(Timeout.Infinite);
        }
        Console.WriteLine($"HELPER pid={Environment.ProcessId}");
        Console.WriteLine($"CONHOST pid={ConsoleHostPid()}");
        switch (mode)
        {
            case "suspended":
                Report(Spawn(LongPing, CreateSuspended));
                break;
            case "detached":
                Report(Spawn(LongPing, DetachedProcess, waitStarted: true));
                break;
            case "newconsole":
                Report(Spawn(LongPing, CreateNewConsole, hidden: true, waitStarted: true));
                break;
            case "gui":
                var script = Path.Combine(Path.GetTempPath(), $"agterm-pane-helper-{Environment.ProcessId}.vbs");
                File.WriteAllText(script, "WScript.Sleep 30000\r\n");
                Report(Spawn($"\"{Path.Combine(Environment.SystemDirectory, "wscript.exe")}\" //B //Nologo \"{script}\"", 0, waitStarted: true));
                break;
            case "freeconsole":
                // a copy of this helper that starts on the pane console and then leaves it
                Environment.SetEnvironmentVariable(ModeVariable, "freeconsole-child");
                var self = $"\"{Environment.ProcessPath}\" \"{typeof(PaneProcessHelper).Assembly.Location}\" {Scenario}";
                Report(Spawn(self, 0, waitFor: value => value == 1));
                break;
            case "ctrlclose":
            case "ctrlclose-spawn":
                var spawnOnClose = mode == "ctrlclose-spawn";
                var marker = Environment.GetEnvironmentVariable(MarkerVariable)!;
                _handler = type =>
                {
                    if (type != CtrlCloseEvent) return false;
                    var line = "ctrl-close";
                    if (spawnOnClose) line += $" spawned={Spawn(LongPing, 0)}";
                    File.WriteAllText(marker, line);
                    // the child attaches while this process still lives, after the console closed
                    if (spawnOnClose) Thread.Sleep(3000);
                    return true;
                };
                if (!SetConsoleCtrlHandler(_handler, true)) throw new Win32Exception();
                break;
            case "breakaway":
                try
                {
                    Spawn($"\"{Ping}\" -n 1 127.0.0.1", CreateBreakawayFromJob);
                    Console.WriteLine("BREAKAWAY ok");
                }
                catch (Win32Exception ex)
                {
                    Console.WriteLine($"BREAKAWAY error={ex.NativeErrorCode}");
                }
                break;
            default:
                throw new ArgumentException($"Unknown pane helper mode: {mode}");
        }
        Console.WriteLine("READY");
        Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
        return Task.CompletedTask;
    }

    private static void Report(int pid) => Console.WriteLine($"SPAWNED pid={pid}");

    /// <summary>Starts a child the way a program in the pane would; it inherits this process's
    /// console unless the flags say otherwise.</summary>
    internal static int Spawn(
        string commandLine, uint flags, bool hidden = false, bool waitStarted = false,
        Func<long, bool>? waitFor = null)
    {
        var startup = new StartupInfo { Cb = Marshal.SizeOf<StartupInfo>() };
        if (hidden)
        {
            startup.Flags = StartfUseShowWindow;
            startup.ShowWindow = 0;
        }
        if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                flags, IntPtr.Zero, null, ref startup, out var info))
            throw new Win32Exception();
        // a child that has not started yet still shows its creator with flag 2
        if (waitStarted) waitFor ??= value => (value & 3) != 2;
        var deadline = Environment.TickCount64 + 10_000;
        while (waitFor is not null && Environment.TickCount64 < deadline
               && !waitFor(QueryConsoleHost(info.Process)))
            Thread.Sleep(10);
        CloseHandle(info.Thread);
        CloseHandle(info.Process);
        return info.ProcessId;
    }

    private static long ConsoleHostPid() => QueryConsoleHost(GetCurrentProcess()) is var value and >= 0
        ? value & ~3L
        : -1;

    private static long QueryConsoleHost(IntPtr process)
    {
        var value = IntPtr.Zero;
        var status = NtQueryInformationProcess(process, 49, ref value, IntPtr.Size, out _);
        return status == 0 ? (long)value : -1;
    }

    private delegate bool CtrlHandler(uint type);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved3;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(CtrlHandler handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process, int infoClass, ref IntPtr info, int length, out int returnLength);
}
