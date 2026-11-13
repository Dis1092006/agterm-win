using AgTerm.Core.Keymap;

namespace AgTerm.Xterm.Tests;

public sealed class HtmlPageKeyFilterTests
{
    private const int Tab = 0x09, Escape = 0x1B, F1 = 0x70, F12 = 0x7B, N = 0x4E, X = 0x58;
    private const int Ctrl = 0x11, LeftCtrl = 0xA2, RightCtrl = 0xA3, Alt = 0x12, LeftShift = 0xA0;

    private static readonly PhysicalChord AltN = new(false, true, false, "n");

    private static HtmlPageKeyFilter Filter() => new(chord => chord == AltN);

    [Fact]
    public void AnAppChordIsTakenFromThePage()
    {
        var key = Filter().Down(N, ctrl: false, alt: true, shift: false, switcherActive: false);

        Assert.Equal(new HtmlPageKey(true, HtmlPageKeyCommand.Chord, AltN), key);
    }

    [Fact]
    public void AKeyTheAppDoesNotBindGoesToThePageAsUserInput()
    {
        var filter = Filter();

        Assert.Equal(new HtmlPageKey(false, HtmlPageKeyCommand.None, PageInput: true),
            filter.Down(X, ctrl: false, alt: true, shift: false, switcherActive: false));
        Assert.Equal(new HtmlPageKey(false, HtmlPageKeyCommand.None, PageInput: true),
            filter.Down(F12, ctrl: true, alt: false, shift: false, switcherActive: false));
    }

    [Fact]
    public void AModifierAloneIsNotUserInput()
    {
        var filter = Filter();

        foreach (var modifier in new[] { Ctrl, LeftCtrl, Alt, LeftShift, 0x5B })
            Assert.Equal(new HtmlPageKey(false, HtmlPageKeyCommand.None),
                filter.Down(modifier, ctrl: false, alt: false, shift: false, switcherActive: false));
    }

    [Fact]
    public void TheReleaseOfATakenKeyIsTakenOnce()
    {
        var filter = Filter();
        filter.Down(N, ctrl: false, alt: true, shift: false, switcherActive: false);

        Assert.True(filter.Up(N, switcherActive: false).Swallow);
        Assert.False(filter.Up(N, switcherActive: false).Swallow);
        Assert.False(filter.Up(X, switcherActive: false).Swallow);
    }

    [Fact]
    public void CtrlTabDrivesTheSwitcher()
    {
        var filter = Filter();

        Assert.Equal(new HtmlPageKey(true, HtmlPageKeyCommand.SwitcherTab),
            filter.Down(Tab, ctrl: true, alt: false, shift: false, switcherActive: false));
        Assert.Equal(new HtmlPageKey(true, HtmlPageKeyCommand.SwitcherReverseTab),
            filter.Down(Tab, ctrl: true, alt: false, shift: true, switcherActive: true));
        Assert.Equal(new HtmlPageKey(false, HtmlPageKeyCommand.None, PageInput: true),
            filter.Down(Tab, ctrl: false, alt: false, shift: false, switcherActive: false));
    }

    [Fact]
    public void EscapeIsTakenOnlyWhileTheSwitcherIsOpen()
    {
        var filter = Filter();

        Assert.Equal(new HtmlPageKey(true, HtmlPageKeyCommand.SwitcherEscape),
            filter.Down(Escape, ctrl: true, alt: false, shift: false, switcherActive: true));
        Assert.Equal(new HtmlPageKey(false, HtmlPageKeyCommand.None, PageInput: true),
            filter.Down(Escape, ctrl: false, alt: false, shift: false, switcherActive: false));
    }

    [Fact]
    public void ReleasingCtrlEndsTheSwitcherAndStillReachesThePage()
    {
        var filter = Filter();

        foreach (var ctrl in new[] { Ctrl, LeftCtrl, RightCtrl })
            Assert.Equal(new HtmlPageKey(false, HtmlPageKeyCommand.SwitcherControlReleased),
                filter.Up(ctrl, switcherActive: true));
        Assert.Equal(new HtmlPageKey(false, HtmlPageKeyCommand.None), filter.Up(Ctrl, switcherActive: false));
    }

    [Fact]
    public void AfterItsOwnCtrlTabTheFilterEndsTheSwitchBeforeTheWindowReportsIt()
    {
        var filter = Filter();
        filter.Down(Tab, ctrl: true, alt: false, shift: false, switcherActive: false);
        filter.Up(Tab, switcherActive: false);

        Assert.Equal(HtmlPageKeyCommand.SwitcherControlReleased, filter.Up(LeftCtrl, switcherActive: false).Command);
        Assert.Equal(HtmlPageKeyCommand.None, filter.Up(LeftCtrl, switcherActive: false).Command);
    }

    [Fact]
    public void AfterItsOwnCtrlTabTheFilterTakesEscapeBeforeTheWindowReportsTheSwitch()
    {
        var filter = Filter();
        filter.Down(Tab, ctrl: true, alt: false, shift: false, switcherActive: false);

        Assert.Equal(HtmlPageKeyCommand.SwitcherEscape,
            filter.Down(Escape, ctrl: true, alt: false, shift: false, switcherActive: false).Command);
        Assert.Equal(HtmlPageKeyCommand.None,
            filter.Down(Escape, ctrl: true, alt: false, shift: false, switcherActive: false).Command);
    }

    [Fact]
    public void CtrlShiftF1OpensKeyboardShortcuts()
    {
        var filter = Filter();

        Assert.Equal(new HtmlPageKey(true, HtmlPageKeyCommand.KeyboardShortcuts),
            filter.Down(F1, ctrl: true, alt: false, shift: true, switcherActive: false));
        Assert.False(filter.Down(F1, ctrl: true, alt: true, shift: true, switcherActive: false).Swallow);
    }
}
