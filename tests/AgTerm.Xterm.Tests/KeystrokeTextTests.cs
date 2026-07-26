using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// session.type keystroke semantics (§6): every \n, \r or \r\n in the payload is ONE Enter, and a
/// terminal Enter is CR — never LF, never a paste (bracketed-paste markers would wrap it).
/// </summary>
public class KeystrokeTextTests
{
    [Theory]
    [InlineData("ls\n", "ls\r")]
    [InlineData("ls\r\n", "ls\r")]
    [InlineData("ls\r", "ls\r")]
    [InlineData("a\nb\r\nc\rd", "a\rb\rc\rd")]
    [InlineData("no newline", "no newline")]
    [InlineData("", "")]
    public void Every_newline_flavor_becomes_one_CR(string typed, string expected)
    {
        Assert.Equal(expected, KeystrokeText.ToPtyInput(typed));
    }
}
