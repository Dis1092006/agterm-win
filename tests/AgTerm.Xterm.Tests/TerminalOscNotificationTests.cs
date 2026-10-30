using System.Text;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class TerminalOscNotificationTests
{
    [Theory]
    [InlineData(9, "Finished", "", "Finished")]
    [InlineData(9, "", "", "")]
    [InlineData(9, "42;hello", "", "42;hello")]
    [InlineData(9, "4;bogus", "", "4;bogus")]
    [InlineData(9, "10x", "", "10x")]
    [InlineData(777, "notify;Build;Finished;with;details", "Build", "Finished;with;details")]
    [InlineData(777, "notify;;", "", "")]
    [InlineData(777, "notify; ;", " ", "")]
    public void Parses_pinned_notification_forms(int identifier, string payload, string title, string body)
    {
        var result = TerminalOscNotification.Parse(identifier, payload, out var error);

        Assert.Null(error);
        Assert.Equal(new TerminalOscNotification(title, body), result);
    }

    [Theory]
    [InlineData("1;100")]
    [InlineData("2;message")]
    [InlineData("3;title")]
    [InlineData("4;0")]
    [InlineData("4;1;50")]
    [InlineData("4;2;bad")]
    [InlineData("4;3junk")]
    [InlineData("4;4;100")]
    [InlineData("5")]
    [InlineData("5 o'clock")]
    [InlineData("6;macro")]
    [InlineData("7;process")]
    [InlineData("8;ENV")]
    [InlineData("9;C:\\work")]
    [InlineData("10")]
    [InlineData("10;1suffix")]
    [InlineData("11;comment")]
    [InlineData("12")]
    [InlineData("12anything")]
    public void Recognized_ConEmu_commands_do_not_notify_or_execute(string payload)
    {
        Assert.Null(TerminalOscNotification.Parse(9, payload, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(777, "notify;missing-body-separator")]
    [InlineData(777, "notify")]
    public void Rejects_incomplete_notify_forms(int identifier, string payload)
    {
        Assert.Null(TerminalOscNotification.Parse(identifier, payload, out var error));
        Assert.NotEmpty(error!);
    }

    [Theory]
    [InlineData(777, "Notify;Title;Body")]
    [InlineData(777, "other;Title;Body")]
    [InlineData(7777, "agterm-readline=1")]
    public void Ignores_other_protocols_and_extensions(int identifier, string payload)
    {
        Assert.Null(TerminalOscNotification.Parse(identifier, payload, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Accepts_2047_payload_bytes_but_rejects_2048_before_truncation()
    {
        Assert.NotNull(TerminalOscNotification.Parse(9, new string('a', 2047), out _));
        Assert.Null(TerminalOscNotification.Parse(9, new string('a', 2048), out var error));
        Assert.NotEmpty(error!);
        Assert.Null(TerminalOscNotification.Parse(777, "notify;t;" + new string('a', 2040), out error));
        Assert.NotEmpty(error!);
    }

    [Fact]
    public void Counts_UTF8_bytes_and_truncates_at_rune_boundaries()
    {
        var result = TerminalOscNotification.Parse(777,
            "notify;" + new string('a', 61) + "🙂;" + new string('b', 253) + "🙂", out _);

        Assert.NotNull(result);
        Assert.Equal(new string('a', 61), result.Title);
        Assert.Equal(new string('b', 253), result.Body);
        Assert.Equal(new string('x', 63), TerminalOscNotification.Parse(
            777, "notify;" + new string('x', 64) + ";" + new string('y', 256), out _)?.Title);
        Assert.Equal(255, Encoding.UTF8.GetByteCount(TerminalOscNotification.Parse(
            9, new string('y', 256), out _)!.Body));
        Assert.Null(TerminalOscNotification.Parse(9, new string('é', 1024), out _));
    }

    [Fact]
    public void Strips_control_and_bidi_formatting_but_preserves_body_line_breaks_and_unicode()
    {
        var result = TerminalOscNotification.Parse(777,
            "notify;\0Build\n\u202E\u0085;done\0\u001b\u007f\u2066\n\tשלום🙂", out _);

        Assert.Equal(new TerminalOscNotification("Build", "done\n\tשלום🙂"), result);
    }

    [Fact]
    public void Only_an_empty_program_title_uses_the_sanitized_63_byte_session_name()
    {
        Assert.Equal("   ", new TerminalOscNotification("   ", "").EffectiveTitle("name"));
        Assert.Equal(new string('a', 62), new TerminalOscNotification("", "").EffectiveTitle(
            "\u202E" + new string('a', 62) + "🙂"));
        Assert.Equal("session", new TerminalOscNotification("", "").EffectiveTitle("ses\0sion"));
    }

    [Fact]
    public void Limiter_is_global_uses_one_second_and_last_content_five_second_boundaries()
    {
        var now = TimeSpan.Zero;
        var limiter = new TerminalNotificationLimiter(() => now);
        var a = new TerminalOscNotification("a", "body");
        var b = new TerminalOscNotification("b", "body");

        Assert.True(limiter.Allow(a));
        now = TimeSpan.FromMilliseconds(999);
        Assert.False(limiter.Allow(b));
        now = TimeSpan.FromSeconds(1);
        Assert.True(limiter.Allow(b));
        now = TimeSpan.FromSeconds(2);
        Assert.True(limiter.Allow(a));
        now = TimeSpan.FromMilliseconds(6999);
        Assert.False(limiter.Allow(a));
        now = TimeSpan.FromSeconds(7);
        Assert.True(limiter.Allow(a));
    }

    [Fact]
    public void Limiter_hashes_title_then_body_without_a_separator_or_session_name_fallback()
    {
        var now = TimeSpan.Zero;
        var limiter = new TerminalNotificationLimiter(() => now);
        Assert.True(limiter.Allow(new TerminalOscNotification("a", "bc")));
        now = TimeSpan.FromSeconds(1);
        Assert.False(limiter.Allow(new TerminalOscNotification("ab", "c")));
        now = TimeSpan.FromSeconds(5);
        Assert.True(limiter.Allow(new TerminalOscNotification("ab", "c")));
    }
}
