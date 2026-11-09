using AgTerm.App;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

public sealed class AskDialogRulesTests
{
    private const int Tab = 0x09, Enter = 0x0D, Escape = 0x1B, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;

    [Theory]
    [InlineData(Tab, false)]
    [InlineData(Right, false)]
    public void TabAndRightMoveForward(int key, bool shift) =>
        Assert.Equal(new AskIntent.Forward(), AskKeys.Intent(key, shift, false, false, false));

    [Theory]
    [InlineData(Tab, true)]
    [InlineData(Left, false)]
    public void ShiftTabAndLeftMoveBack(int key, bool shift) =>
        Assert.Equal(new AskIntent.Backward(), AskKeys.Intent(key, shift, false, false, false));

    // a list takes them as its cursor and toggle; without a list Up and Down move the highlight
    [Fact]
    public void UpDownAndSpaceAreListIntents()
    {
        Assert.Equal(new AskIntent.Up(), AskKeys.Intent(Up, false, false, false, false));
        Assert.Equal(new AskIntent.Down(), AskKeys.Intent(Down, false, false, false, false));
        Assert.Equal(new AskIntent.Toggle(), AskKeys.Intent(0x20, false, false, false, false));
        Assert.Null(AskKeys.Intent(0x20, false, true, false, false));
    }

    [Fact]
    public void EnterActivatesAndEscapeCancels()
    {
        Assert.Equal(new AskIntent.Activate(), AskKeys.Intent(Enter, false, false, false, false));
        Assert.Equal(new AskIntent.Cancel(), AskKeys.Intent(Escape, false, false, false, false));
    }

    [Fact]
    public void ALetterKeyIsAHotkeyByVirtualKeySoACyrillicLayoutStillHitsIt()
    {
        // on the Russian layout the key at Latin Y types U+043D, but WinUI still reports VK_Y (0x59)
        Assert.Equal(new AskIntent.Hotkey("y"), AskKeys.Intent(0x59, false, false, false, false));
        Assert.Equal(new AskIntent.Hotkey("a"), AskKeys.Intent(0x41, true, false, false, false));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void CtrlAltAndWinLeaveEveryKeyToTheWindow(bool control, bool alt, bool windows)
    {
        Assert.Null(AskKeys.Intent(0x59, false, control, alt, windows));
        Assert.Null(AskKeys.Intent(Enter, false, control, alt, windows));
        Assert.Null(AskKeys.Intent(Tab, false, control, alt, windows));
    }

    [Fact]
    public void OtherKeysDoNothing()
    {
        Assert.Null(AskKeys.Intent(0x31, false, false, false, false));
        Assert.Null(AskKeys.Intent(0x70, false, false, false, false));
    }

    [Theory]
    [InlineData(ControlAskInputMode.Line)]
    [InlineData(ControlAskInputMode.Multiline)]
    public void InTheFieldTabMovesAndEscapeCancels(ControlAskInputMode mode)
    {
        Assert.Equal(new AskIntent.Forward(), AskKeys.FieldIntent(Tab, false, false, false, false, mode));
        Assert.Equal(new AskIntent.Backward(), AskKeys.FieldIntent(Tab, true, false, false, false, mode));
        Assert.Equal(new AskIntent.Cancel(), AskKeys.FieldIntent(Escape, false, false, false, false, mode));
    }

    [Fact]
    public void InALineFieldEnterAnswers()
    {
        Assert.Equal(new AskIntent.Activate(), AskKeys.FieldIntent(Enter, false, false, false, false, ControlAskInputMode.Line));
        Assert.Null(AskKeys.FieldIntent(Enter, false, true, false, false, ControlAskInputMode.Line));
    }

    [Fact]
    public void InAMultilineFieldCtrlEnterAnswersAndEnterTypesANewline()
    {
        Assert.Equal(new AskIntent.Activate(), AskKeys.FieldIntent(Enter, false, true, false, false, ControlAskInputMode.Multiline));
        Assert.Null(AskKeys.FieldIntent(Enter, false, false, false, false, ControlAskInputMode.Multiline));
        Assert.Null(AskKeys.FieldIntent(Enter, true, true, false, false, ControlAskInputMode.Multiline));
        Assert.Null(AskKeys.FieldIntent(Enter, false, true, true, false, ControlAskInputMode.Multiline));
    }

    [Theory]
    [InlineData(0x59)]
    [InlineData(Left)]
    [InlineData(Down)]
    [InlineData(0x20)]
    public void InTheFieldLettersArrowsAndSpaceStayWithTheText(int key) =>
        Assert.Null(AskKeys.FieldIntent(key, false, false, false, false, ControlAskInputMode.Line));

    // a choice list's free-form row: plain Up and Down go back to the list, as upstream's moveUp/moveDown
    [Fact]
    public void InTheFreeFormRowUpAndDownGoToTheListAndSpaceStaysText()
    {
        Assert.Equal(new AskIntent.Up(), AskKeys.FieldIntent(Up, false, false, false, false, ControlAskInputMode.Line, routesVertical: true));
        Assert.Equal(new AskIntent.Down(), AskKeys.FieldIntent(Down, false, false, false, false, ControlAskInputMode.Line, routesVertical: true));
        Assert.Null(AskKeys.FieldIntent(Up, true, false, false, false, ControlAskInputMode.Line, routesVertical: true));
        Assert.Null(AskKeys.FieldIntent(Down, false, true, false, false, ControlAskInputMode.Line, routesVertical: true));
        Assert.Null(AskKeys.FieldIntent(0x20, false, false, false, false, ControlAskInputMode.Line, routesVertical: true));
        Assert.Equal(new AskIntent.Activate(), AskKeys.FieldIntent(Enter, false, false, false, false, ControlAskInputMode.Line, routesVertical: true));
    }

    [Theory]
    [InlineData(0x43)]
    [InlineData(0x56)]
    [InlineData(0x58)]
    [InlineData(0x5A)]
    [InlineData(0x59)]
    [InlineData(0x41)]
    [InlineData(0x2D)]
    [InlineData(0x08)]
    [InlineData(0x2E)]
    [InlineData(Left)]
    [InlineData(Right)]
    [InlineData(Up)]
    [InlineData(Down)]
    [InlineData(0x24)]
    [InlineData(0x23)]
    public void CtrlEditKeysBelongToTheField(int key)
    {
        Assert.True(AskKeys.IsFieldEditChord(key, control: true, alt: false));
        Assert.False(AskKeys.IsFieldEditChord(key, control: true, alt: true));
        Assert.False(AskKeys.IsFieldEditChord(key, control: false, alt: false));
    }

    [Fact]
    public void OtherCtrlKeysAreWindowChords() =>
        Assert.False(AskKeys.IsFieldEditChord(0x57, control: true, alt: false));

    [Theory]
    [InlineData(1000, 13, null, 576)]
    [InlineData(1000, 20, null, 864)]
    [InlineData(400, 20, null, 360)]
    [InlineData(1000, 13, 50, 500)]
    [InlineData(1000, 13, 100, 1000)]
    public void ThePanelWidthCapsAtNinetyPercentAndSeventyTwoCellsUnlessFixed(double anchor, double font, int? percent, double expected) =>
        Assert.Equal(expected, AskPanelLayout.MaxPanelWidth(anchor, font, percent), 3);

    [Fact]
    public void ATinyFontStillUsesAnEightPixelCell() => Assert.Equal(8, AskPanelLayout.TerminalCell(6));

    [Fact]
    public void ThePanelHeightLeavesACellAboveAndBelow() => Assert.Equal(284, AskPanelLayout.MaxPanelHeight(300));

    [Fact]
    public void ButtonsShareTheWidestWidthInARowWhenItFits() =>
        Assert.Equal(new AskButtonArrangement(true, 80), AskPanelLayout.Arrange(new[] { 50.0, 80.0, 60.0 }, 256));

    [Fact]
    public void ButtonsFallBackToAColumnCappedAtTheAvailableWidth()
    {
        Assert.Equal(new AskButtonArrangement(false, 80), AskPanelLayout.Arrange(new[] { 50.0, 80.0, 60.0 }, 255));
        Assert.Equal(new AskButtonArrangement(false, 70), AskPanelLayout.Arrange(new[] { 50.0, 200.0 }, 70));
    }

    [Fact]
    public void AnAnchorWithoutAShownSplitIsTheWholeArea()
    {
        var area = new AskRect(10, 20, 804, 600);
        Assert.Equal(area, AskAnchorGeometry.Pane(area, OverlayPane.Right, splitShown: false, ratio: 0.5));
    }

    [Fact]
    public void AShownSplitDividesTheAreaLikeThePage()
    {
        var area = new AskRect(10, 20, 804, 600);
        Assert.Equal(new AskRect(10, 20, 320, 600), AskAnchorGeometry.Pane(area, OverlayPane.Left, splitShown: true, ratio: 0.4));
        Assert.Equal(new AskRect(334, 20, 480, 600), AskAnchorGeometry.Pane(area, OverlayPane.Right, splitShown: true, ratio: 0.4));
    }

    [Fact]
    public void TheDividerWidthMatchesThePage()
    {
        var bridge = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.Xterm", "Assets", "bridge.js"));
        Assert.Contains($"const SPLIT_DIVIDER_WIDTH_PX = {AskAnchorGeometry.SplitDividerWidth};", bridge, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
