using System.Runtime.InteropServices;
using AgTerm.Core.Model;

namespace AgTerm.App;

/// <summary>UI-thread-owned notification attention, separate from refused-window-raise feedback.</summary>
internal sealed class TaskbarNotificationAttention(
    Func<bool> isApplicationForeground,
    Action<nint, uint, uint, uint> flash) : IDisposable
{
    private nint _pendingWindow;
    private readonly HashSet<nint> _refusedRaiseWindows = [];
    private bool _enabled;
    private bool _disposed;

    public void ApplySettings(AppSettings settings)
    {
        if (_disposed) return;
        _enabled = settings.TaskbarFlashEnabled();
        if (!_enabled) Stop();
    }

    public void Request(nint window)
    {
        if (_disposed || !_enabled) return;
        if (isApplicationForeground())
        {
            Stop();
            return;
        }
        if (_pendingWindow != 0 || _refusedRaiseWindows.Contains(window)) return;
        flash(window, 0x2, 1, 0);
        _pendingWindow = window;
    }

    public void Activated(nint window)
    {
        _refusedRaiseWindows.Remove(window);
        Stop();
    }

    public void WindowClosed(nint window)
    {
        _refusedRaiseWindows.Remove(window);
        if (_pendingWindow == window) Stop();
    }

    public void RelinquishForRaise(nint window)
    {
        // A refused raise replaces this HWND's attention with its own continuous flash.
        _refusedRaiseWindows.Add(window);
        if (_pendingWindow == window) _pendingWindow = 0;
    }

    private void Stop()
    {
        if (_pendingWindow == 0) return;
        flash(_pendingWindow, 0, 0, 0);
        _pendingWindow = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}

internal static class WindowsTaskbarFlash
{
    public static bool IsForegroundWindow(nint window) => GetForegroundWindow() == window;

    public static bool IsApplicationForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        return process == Environment.ProcessId;
    }

    public static void Apply(nint window, uint flags, uint count, uint timeout)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = window,
            Flags = flags,
            Count = count,
            Timeout = timeout,
        };
        // The return value reports the previous caption state, not success.
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
