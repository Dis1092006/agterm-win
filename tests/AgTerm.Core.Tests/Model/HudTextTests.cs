using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore ControlDispatcher+Pick.swift containsControlCharacters and
// ControlDispatcher+Hud.swift containsMarkdownControlCharacters (upstream ed831ab).
public class HudTextTests
{
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\rb")]
    [InlineData("\u0001")]
    [InlineData("\u001B[2J")]
    [InlineData("\u001F")]
    [InlineData("\u007F")]
    public void PlainTextRefusesEveryScalarBelowSpaceAndDelete(string text) => Assert.True(HudText.HasControlCharacters(text));

    [Theory]
    [InlineData("")]
    [InlineData("ok")]
    [InlineData("\u0080")]
    [InlineData("\u0085")]
    [InlineData("\u00A0")]
    [InlineData("\U0001F98A")]
    public void PlainTextAcceptsEverythingElse(string text) => Assert.False(HudText.HasControlCharacters(text));

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("# T\n\n\t- x")]
    public void MarkdownKeepsLineFeedAndTab(string text) => Assert.False(HudText.HasMarkdownControlCharacters(text));

    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\r\nb")]
    [InlineData("\u000B")]
    [InlineData("\u000C")]
    [InlineData("\u001B")]
    [InlineData("\u0007")]
    [InlineData("\u007F")]
    public void MarkdownRefusesEveryOtherControlCharacter(string text) => Assert.True(HudText.HasMarkdownControlCharacters(text));
}
