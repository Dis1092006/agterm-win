using System.Runtime.InteropServices;
using AgTerm.Core.Keymap;
using AgTerm.Xterm;

namespace AgTerm.App;

/// <summary>
/// A low-level keyboard hook that runs only while an HTML page has the keys and the window is in the
/// foreground. WebView2 handles a page's keys in the browser process, so the window gets no key event
/// for them; the hook takes the app's own keys from the page first (upstream's menu and key monitors
/// see them before the web view). The window installs it when a page takes the focus and removes it
/// when the page loses it or the window is deactivated.
/// </summary>
internal sealed class HtmlPageKeyboardHook : IDisposable
{
    private const int KeyboardLowLevel = 13;
    private const int KeyDown = 0x100, KeyUp = 0x101, SysKeyDown = 0x104, SysKeyUp = 0x105;
    private const int Shift = 0x10, Control = 0x11, Menu = 0x12;

    private readonly IntPtr _window;
    private readonly Func<PhysicalChord, bool> _isAppChord;
    private readonly Func<bool> _switcherActive;
    private readonly Action<HtmlPageKey> _handle;
    private readonly HookProc _callback;
    private HtmlPageKeyFilter _filter;
    private IntPtr _hook;

    /// <param name="handle">Called inside the hook for a key the window acts on; it must return at once.</param>
    public HtmlPageKeyboardHook(IntPtr window, Func<PhysicalChord, bool> isAppChord, Func<bool> switcherActive,
                                Action<HtmlPageKey> handle)
    {
        (_window, _isAppChord, _switcherActive, _handle) = (window, isAppChord, switcherActive, handle);
        _filter = new HtmlPageKeyFilter(isAppChord);
        _callback = Callback;
    }

    public bool Installed => _hook != IntPtr.Zero;

    public void Install()
    {
        if (Installed) return;
        // a release the old hook swallowed the press of is not this hook's to swallow
        _filter = new HtmlPageKeyFilter(_isAppChord);
        _hook = SetWindowsHookEx(KeyboardLowLevel, _callback, GetModuleHandle(null), 0);
    }

    public void Uninstall()
    {
        if (!Installed) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Uninstall();

    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && GetForegroundWindow() == _window)
        {
            var virtualKey = Marshal.ReadInt32(data);
            HtmlPageKey? key = (int)message switch
            {
                KeyDown or SysKeyDown => _filter.Down(virtualKey, IsDown(Control), IsDown(Menu), IsDown(Shift),
                    _switcherActive()),
                KeyUp or SysKeyUp => _filter.Up(virtualKey, _switcherActive()),
                _ => null,
            };
            if (key is { } decided)
            {
                if (decided.Command != HtmlPageKeyCommand.None || decided.PageInput) _handle(decided);
                if (decided.Swallow) return 1;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
