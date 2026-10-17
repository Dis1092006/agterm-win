namespace AgTerm.Xterm;

/// <summary>
/// Which CoreWebView2 process failures take the terminal page down with them, and what the window
/// says when one happens (PLATFORM-NOTES: no upstream analogue — libghostty renders in-process, so
/// there is no renderer to lose).
///
/// The kind arrives as its enum NAME rather than the enum itself: this assembly has no WinUI or
/// WebView2 reference, and the mapping is the part worth testing without a browser.
///
/// A dead page keeps its CoreWebView2 object alive, so every later ExecuteScriptAsync throws and
/// each session reports its own spawn failure. Naming the real cause once is the point of this
/// table; the window suppresses the per-session notices that follow.
/// </summary>
public static class WebViewProcessFailure
{
    private const string FatalTitle = "Terminal rendering process stopped";

    private const string FatalMessage =
        "The terminal view is no longer running. Restart " + ProductIdentity.Name + " to use the terminals again.";

    /// <summary>
    /// Whether this kind leaves the page unusable. Unrecognized kinds read as survivable: a newer
    /// runtime may name a kind this build does not know, and claiming the terminals are gone when
    /// they still work is the worse error.
    /// </summary>
    public static bool IsFatal(string? kind) => kind switch
    {
        // The whole browser process is gone: nothing about this WebView2 works again.
        "BrowserProcessExited" => true,
        // The page's own renderer. Recovering it would need a reload plus a fresh page object;
        // until then the panes are as dead as a lost browser process.
        "RenderProcessExited" or "RenderProcessUnresponsive" => true,
        // Everything else — GPU, utility, sandbox helper, PPAPI, an out-of-process iframe's
        // renderer (this page hosts none), or a kind this build does not know — leaves the page
        // running. WebView2 recovers the process on its own.
        _ => false,
    };

    /// <summary>The notice to show for this kind, or null when the page survives it.</summary>
    public static LiveFailureNotice? Notice(string? kind) =>
        IsFatal(kind) ? new LiveFailureNotice(FatalTitle, FatalMessage) : null;
}
