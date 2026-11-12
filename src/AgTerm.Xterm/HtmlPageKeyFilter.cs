using AgTerm.Core.Keymap;

namespace AgTerm.Xterm;

public enum HtmlPageKeyCommand
{
    None,
    Chord,
    SwitcherTab,
    SwitcherReverseTab,
    SwitcherEscape,
    SwitcherControlReleased,
    KeyboardShortcuts,
}

/// <summary>What happens to one key while an HTML page has the keyboard. Swallow keeps the key from
/// the page; PageInput marks a key the page gets, which counts as real user input.</summary>
public readonly record struct HtmlPageKey(
    bool Swallow,
    HtmlPageKeyCommand Command,
    PhysicalChord? Chord = null,
    bool PageInput = false);

/// <summary>
/// Decides which keys the window takes from a focused HTML page. WebView2 handles a page's keys in
/// its own process, so the window never sees them as key events. The window takes the same keys it
/// takes when the focus is outside the terminal: the session switcher's Ctrl+Tab, Escape and Ctrl
/// release, Ctrl+Shift+F1, and a chord the keymap binds to an app action. Upstream's menu and key
/// monitors see these keys before the page does.
/// </summary>
public sealed class HtmlPageKeyFilter
{
    private const int Tab = 0x09, Escape = 0x1B, F1 = 0x70;

    private readonly Func<PhysicalChord, bool> _isAppChord;
    private readonly HashSet<int> _taken = [];
    // the window opens the switcher after the hook returns, so the Ctrl release or Escape can come first
    private bool _switching;

    /// <param name="isAppChord">Whether a chord fires an app action now.</param>
    public HtmlPageKeyFilter(Func<PhysicalChord, bool> isAppChord)
    {
        ArgumentNullException.ThrowIfNull(isAppChord);
        _isAppChord = isAppChord;
    }

    public HtmlPageKey Down(int virtualKey, bool ctrl, bool alt, bool shift, bool switcherActive)
    {
        var command = Command(virtualKey, ctrl, alt, shift, switcherActive, out var chord);
        if (command == HtmlPageKeyCommand.None)
            return new HtmlPageKey(false, command, PageInput: !IsModifier(virtualKey));
        _taken.Add(virtualKey);
        _switching = command switch
        {
            HtmlPageKeyCommand.SwitcherTab or HtmlPageKeyCommand.SwitcherReverseTab => true,
            HtmlPageKeyCommand.SwitcherEscape => false,
            _ => _switching,
        };
        return new HtmlPageKey(true, command, chord);
    }

    /// <summary>The release of a key the window took goes nowhere, so the page never sees half a press.</summary>
    public HtmlPageKey Up(int virtualKey, bool switcherActive)
    {
        if (_taken.Remove(virtualKey)) return new HtmlPageKey(true, HtmlPageKeyCommand.None);
        if (!IsCtrl(virtualKey) || !(switcherActive || _switching)) return new HtmlPageKey(false, HtmlPageKeyCommand.None);
        _switching = false;
        return new HtmlPageKey(false, HtmlPageKeyCommand.SwitcherControlReleased);
    }

    private HtmlPageKeyCommand Command(int virtualKey, bool ctrl, bool alt, bool shift, bool switcherActive,
                                       out PhysicalChord? chord)
    {
        chord = null;
        if (virtualKey == F1 && ctrl && shift && !alt) return HtmlPageKeyCommand.KeyboardShortcuts;
        if (virtualKey == Tab && ctrl)
            return shift ? HtmlPageKeyCommand.SwitcherReverseTab : HtmlPageKeyCommand.SwitcherTab;
        if (virtualKey == Escape && (switcherActive || _switching)) return HtmlPageKeyCommand.SwitcherEscape;
        if (WindowsVirtualKeyNames.KeyName(virtualKey) is not { } name) return HtmlPageKeyCommand.None;
        var physical = new PhysicalChord(ctrl, alt, shift, name);
        if (!_isAppChord(physical)) return HtmlPageKeyCommand.None;
        chord = physical;
        return HtmlPageKeyCommand.Chord;
    }

    private static bool IsCtrl(int virtualKey) => virtualKey is 0x11 or 0xA2 or 0xA3;

    private static bool IsModifier(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
}
