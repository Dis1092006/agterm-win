using System.Runtime.InteropServices;
using AgTerm.Core.Keymap;

namespace AgTerm.App;

public sealed class GlobalHotkeyBinding(
    Func<int, uint, int> register, Action unregister, Action<string?> report)
{
    public Chord? RegisteredChord { get; private set; }

    public void Apply(Chord? chord)
    {
        if (chord is not null && chord == RegisteredChord) return;
        if (RegisteredChord is not null) unregister();
        RegisteredChord = null;
        if (chord is not { } value) { report(null); return; }
        if (WindowsVirtualKeyNames.KeyCode(value.Key) is not { } key)
            throw new ArgumentException("Global hotkey has no supported key position.", nameof(chord));
        var physical = WindowsChordDecoder.Effective(value);
        var modifiers = (physical.Alt ? 1u : 0) | (physical.Ctrl ? 2u : 0) | (physical.Shift ? 4u : 0);
        var error = register(key, modifiers | 0x4000);
        if (error == 0)
        {
            RegisteredChord = chord;
            report(null);
        }
        else report($"Global hotkey '{value.DisplayString}' could not be registered (Win32 error {error}). Another application may own this chord.");
    }
}

internal sealed class GlobalHotkey : IDisposable
{
    private const uint Id = 0x514B;
    private readonly IntPtr _window;
    private readonly SubclassProc _procedure;
    private readonly GlobalHotkeyBinding _binding;

    public GlobalHotkey(IntPtr window, Action toggle, Action<string?> report, Action<uint> windowChanged)
    {
        _window = window;
        _binding = new((key, modifiers) =>
            RegisterHotKey(window, (int)Id, modifiers, (uint)key) ? 0 : Marshal.GetLastWin32Error(),
            () =>
            {
                if (!UnregisterHotKey(window, (int)Id))
                    report($"Global hotkey could not be unregistered (Win32 error {Marshal.GetLastWin32Error()}).");
            }, report);
        _procedure = (hwnd, message, wParam, lParam, _, _) =>
        {
            if (message == 0x0312 && (uint)wParam == Id) { toggle(); return IntPtr.Zero; }
            if (message is 0x02E0 or 0x007E or 0x001A) windowChanged(message);
            return DefSubclassProc(hwnd, message, wParam, lParam);
        };
        if (!SetWindowSubclass(window, _procedure, Id, UIntPtr.Zero))
            throw new InvalidOperationException("Could not install the global-hotkey message handler.");
    }

    public void Apply(Chord? chord) => _binding.Apply(chord);

    public void Dispose()
    {
        _binding.Apply(null);
        if (!RemoveWindowSubclass(_window, _procedure, Id))
            System.Diagnostics.Trace.TraceError("Could not remove the global-hotkey message handler.");
    }

    private delegate IntPtr SubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam,
        UIntPtr subclassId, UIntPtr data);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
