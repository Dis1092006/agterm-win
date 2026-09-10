namespace AgTerm.Xterm;

/// <summary>
/// Decides which failure notices reach the window's single error banner.
///
/// The banner holds one notice, so the newest replaces the last. That is right for unrelated
/// failures, and wrong right after the renderer dies: every session then fails to spawn and the
/// last of them would bury the one notice that explains why. Once a fatal notice is shown, later
/// notices are dropped.
///
/// UI-thread confined, like the banner it guards (ShowFailure runs on the dispatcher).
/// </summary>
public sealed class FailureNoticeGate
{
    private bool _fatalShown;

    /// <summary>
    /// Whether to put this notice on the banner. Pass fatal: true for a notice that leaves the
    /// window unusable — it claims the banner and nothing displaces it.
    /// </summary>
    public bool ShouldShow(LiveFailureNotice notice, bool fatal = false)
    {
        ArgumentNullException.ThrowIfNull(notice);

        // Already explaining a dead window. Every later notice is a consequence of it, including a
        // repeat of the fatal notice itself: WebView2 can raise ProcessFailed more than once.
        if (_fatalShown) return false;

        _fatalShown = fatal;
        return true;
    }
}
