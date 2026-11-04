using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AgTerm.Terminal;

/// <summary>Raw kernel32 surface for CreatePseudoConsole + CreateProcessW-with-attribute-list.
/// Hand-written (no CsWin32): seven imports, kept together and internal.</summary>
internal static class ConPtyNative
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal static readonly IntPtr ProcThreadAttributePseudoConsole = new(0x00020016);
    internal const uint Infinite = 0xFFFFFFFF;
    internal const int StartfUseStdHandles = 0x00000100;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint HandleFlagInherit = 0x1;
    internal static readonly IntPtr ProcThreadAttributeHandleList = new(0x00020002);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
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
    internal struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(
        out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr pipeAttributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(
        Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

    internal const uint PseudoConsoleInheritCursor = 0x1;

    [DllImport("kernel32.dll")]
    internal static extern int ResizePseudoConsole(IntPtr console, Coord size);

    [DllImport("kernel32.dll")]
    internal static extern void ClosePseudoConsole(IntPtr console);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList, uint flags, IntPtr attribute,
        IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessW(
        string? applicationName, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    internal const uint CreateSuspended = 0x00000004;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int GetProcessId(IntPtr process);

    internal const int ErrorMoreData = 234;

    internal const int JobObjectBasicProcessIdList = 3;
    internal const int JobObjectExtendedLimitInformationClass = 9;
    internal const uint JobObjectLimitBreakawayOk = 0x00000800;

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool QueryInformationJobObject(
        IntPtr job, int infoClass, ref JobObjectExtendedLimitInformation info, int length,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetInformationJobObject(
        IntPtr job, int infoClass, ref JobObjectExtendedLimitInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool QueryInformationJobObject(
        IntPtr job, int infoClass, IntPtr info, int length, out int returnLength);

    internal const uint ProcessTerminate = 0x0001;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint Synchronize = 0x00100000;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    internal const uint DuplicateSameAccess = 0x2;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DuplicateHandle(
        IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess, out IntPtr targetHandle,
        uint access, bool inheritHandle, uint options);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr process, uint flags, char[] name, ref int size);

    /// <summary>ProcessConsoleHostProcess: not documented (phnt). The low two bits are flags:
    /// 1 = attached to the console of the host pid in the other bits; 2 = created, not started
    /// yet, with the creator's pid in the other bits.</summary>
    internal const int ProcessConsoleHostProcess = 49;

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryInformationProcess(
        IntPtr process, int infoClass, ref IntPtr info, int length, out int returnLength);

    /// <summary>The raw ProcessConsoleHostProcess value of a process, or null if the query fails.
    /// </summary>
    internal static long? QueryConsoleHost(IntPtr process)
    {
        var value = IntPtr.Zero;
        var status = NtQueryInformationProcess(
            process, ProcessConsoleHostProcess, ref value, IntPtr.Size, out _);
        return status == 0 ? (long)value : null;
    }
}
