namespace AgTerm.Xterm.Tests;

public sealed class HooksNoticesTests
{
    [Theory]
    [InlineData(1, "1 line in hooks.conf was skipped. Run agtermctl hooks list for details.")]
    [InlineData(3, "3 lines in hooks.conf were skipped. Run agtermctl hooks list for details.")]
    public void Diagnostics_notice_counts_the_skipped_lines(int count, string message)
    {
        var notice = HooksNotices.Diagnostics(count);

        Assert.Equal("Hooks have issues", notice.Title);
        Assert.Equal(message, notice.Message);
    }

    [Fact]
    public void Failure_notice_names_the_event_and_the_detail()
    {
        var notice = HooksNotices.Failure("session.created", "echo hi", "could not start: boom");

        Assert.Equal("Hook for session.created failed", notice.Title);
        Assert.Equal("echo hi: could not start: boom", notice.Message);
    }
}
