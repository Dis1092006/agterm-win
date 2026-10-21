using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// One InfoBar shows every failure, so the last notice wins. When the renderer dies, each session
/// then reports its own spawn failure and would bury the real cause. The gate keeps the fatal
/// notice on screen.
/// </summary>
public sealed class FailureNoticeGateTests
{
    private static readonly LiveFailureNotice RendererDied =
        new("Terminal rendering process stopped", "The terminal view is no longer running.");

    private static readonly LiveFailureNotice SurfaceFailed =
        new("Terminal failed to start", "WebView2 unavailable");

    [Fact]
    public void An_ordinary_notice_is_shown()
    {
        var gate = new FailureNoticeGate();

        Assert.True(gate.ShouldShow(SurfaceFailed));
    }

    [Fact]
    public void Ordinary_notices_keep_replacing_each_other()
    {
        var gate = new FailureNoticeGate();

        Assert.True(gate.ShouldShow(SurfaceFailed));
        Assert.True(gate.ShouldShow(new LiveFailureNotice("Config reload failed", "bad keymap")));
    }

    [Fact]
    public void A_surface_failure_after_a_fatal_notice_is_suppressed()
    {
        var gate = new FailureNoticeGate();

        Assert.True(gate.ShouldShow(RendererDied, fatal: true));
        Assert.False(gate.ShouldShow(SurfaceFailed));
    }

    [Fact]
    public void Every_later_surface_failure_stays_suppressed()
    {
        var gate = new FailureNoticeGate();
        gate.ShouldShow(RendererDied, fatal: true);

        // One per restored session: all of them report the same dead page.
        Assert.False(gate.ShouldShow(SurfaceFailed));
        Assert.False(gate.ShouldShow(SurfaceFailed));
        Assert.False(gate.ShouldShow(SurfaceFailed));
    }

    [Fact]
    public void A_repeated_fatal_notice_is_suppressed_once_it_is_already_shown()
    {
        var gate = new FailureNoticeGate();

        Assert.True(gate.ShouldShow(RendererDied, fatal: true));
        // WebView2 can raise ProcessFailed more than once; the banner already says this.
        Assert.False(gate.ShouldShow(RendererDied, fatal: true));
    }

    [Fact]
    public void Reload_allows_a_new_failure_after_the_previous_page_is_replaced()
    {
        var gate = new FailureNoticeGate();
        gate.ShouldShow(RendererDied, fatal: true);

        gate.Reset();

        Assert.True(gate.ShouldShow(SurfaceFailed));
        Assert.True(gate.ShouldShow(RendererDied, fatal: true));
    }
}
