using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The WebView2 process can die under a running window (PLATFORM-NOTES: no upstream analogue —
/// libghostty renders in-process). These map CoreWebView2ProcessFailedKind names to the notice the
/// window shows, so the cause is stated once instead of every session reporting a spawn failure.
/// </summary>
public sealed class WebViewProcessFailureTests
{
    [Theory]
    [InlineData("BrowserProcessExited")]
    [InlineData("RenderProcessExited")]
    [InlineData("RenderProcessUnresponsive")]
    public void Kinds_that_kill_the_page_report_a_restart_notice(string kind)
    {
        var notice = WebViewProcessFailure.Notice(kind);

        Assert.NotNull(notice);
        Assert.Equal("Terminal rendering process stopped", notice.Title);
        Assert.Equal(
            "The terminal view is no longer running. Restart agterm-win to use the terminals again.",
            notice.Message);
    }

    [Theory]
    [InlineData("GpuProcessExited")]
    [InlineData("UtilityProcessExited")]
    [InlineData("SandboxHelperProcessExited")]
    [InlineData("PpapiPluginProcessExited")]
    [InlineData("PpapiBrokerProcessExited")]
    [InlineData("UnknownProcessExited")]
    // Only an out-of-process iframe dies here; this page hosts none, so its panes keep working.
    [InlineData("FrameRenderProcessExited")]
    public void Kinds_the_page_survives_report_nothing(string kind)
    {
        Assert.Null(WebViewProcessFailure.Notice(kind));
    }

    [Fact]
    public void An_unrecognized_kind_reports_nothing()
    {
        // A newer runtime can name a kind this build does not know. Staying silent keeps a
        // survivable failure from claiming the terminals are gone.
        Assert.Null(WebViewProcessFailure.Notice("SomeFutureProcessExited"));
    }

    [Fact]
    public void Fatal_kinds_are_reported_as_fatal()
    {
        Assert.True(WebViewProcessFailure.IsFatal("BrowserProcessExited"));
        Assert.False(WebViewProcessFailure.IsFatal("GpuProcessExited"));
    }
}
