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

    [Theory]
    [InlineData("echo hi\n", "echo hi", true)]
    [InlineData("echo hi\r\n", "echo hi", true)]
    [InlineData("one\ntwo\n", "one\rtwo", true)]
    [InlineData("echo hi\n\n", "echo hi\r", true)]
    [InlineData("echo hi", "echo hi", false)]
    [InlineData("one\ntwo", "one\rtwo", false)]
    [InlineData("\n", "\r", false)]
    [InlineData("\n\n", "\r\r", false)]
    [InlineData("", "", false)]
    public void Only_a_final_Return_after_text_is_paced(string typed, string head, bool pacedReturn)
    {
        Assert.Equal((head, pacedReturn), KeystrokeText.Paced(typed));
    }

    [Fact]
    public void The_paced_Return_is_written_after_the_submit_gap()
    {
        var log = new List<string>();
        KeystrokeText.Write("echo hi\n", s => log.Add("write " + s), d => log.Add($"sleep {d.TotalMilliseconds}"));
        Assert.Equal(new[] { "write echo hi", "sleep 10", "write \r" }, log);
    }

    [Fact]
    public void Text_without_a_final_Return_is_one_write()
    {
        var log = new List<string>();
        KeystrokeText.Write("one\ntwo", s => log.Add("write " + s), d => log.Add("sleep"));
        Assert.Equal(new[] { "write one\rtwo" }, log);
    }
}
