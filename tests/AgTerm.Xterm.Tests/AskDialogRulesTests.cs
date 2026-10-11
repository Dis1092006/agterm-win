using AgTerm.App;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class AskDialogRulesTests
{
    private const int Tab = 0x09, Enter = 0x0D, Escape = 0x1B, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;

    [Theory]
    [InlineData(Tab, false)]
    [InlineData(Right, false)]
    [InlineData(Down, false)]
    public void TabRightAndDownMoveForward(int key, bool shift) =>
        Assert.Equal(new AskIntent.Forward(), AskKeys.Intent(key, shift, false, false, false));

    [Theory]
    [InlineData(Tab, true)]
    [InlineData(Left, false)]
    [InlineData(Up, false)]
    public void ShiftTabLeftAndUpMoveBack(int key, bool shift) =>
        Assert.Equal(new AskIntent.Backward(), AskKeys.Intent(key, shift, false, false, false));

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
        Assert.Null(AskKeys.Intent(0x20, false, false, false, false));
    }

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
