using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public class TerminalThemeTests
{
    [Theory]
    [InlineData("#000000", true)]
    [InlineData("#282a36", true)]
    [InlineData("#ffffff", false)]
    [InlineData("#f5f5f5", false)]
    [InlineData("#808080", false)]
    [InlineData("red", true)]
    [InlineData("#12345", true)]
    public void IsDarkColor_uses_luminance_below_one_half_and_treats_an_unreadable_color_as_dark(
        string color, bool dark)
    {
        Assert.Equal(dark, TerminalTheme.IsDarkColor(color));
    }

    [Fact]
    public void IsDark_reads_the_background()
    {
        var theme = new TerminalTheme("t", "#000000", "#ffffff", "#000000", "#ffffff", "#cccccc", "#000000",
            Enumerable.Repeat("#000000", 16).ToArray());

        Assert.False(theme.IsDark);
    }
}
