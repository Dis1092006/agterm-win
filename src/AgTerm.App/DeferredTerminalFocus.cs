namespace AgTerm.App;

/// <summary>Terminal focus that waits for the window's activation while the window is in the
/// background. Focusing the WebView2 of a background window makes WinUI report the window active
/// without giving Chromium the keyboard, so the user's next activation is not seen as one and keys
/// never reach the page.</summary>
public sealed class DeferredTerminalFocus
{
    public bool Pending { get; private set; }

    /// <summary>An active window focuses at once, which also satisfies a focus still waiting for an
    /// activation; a background window or an open modal keeps the request pending.</summary>
    public void Request(bool windowIsActive, Action focus, bool modalOpen = false)
    {
        if (!windowIsActive || modalOpen)
        {
            Pending = true;
            return;
        }
        Pending = false;
        focus();
    }

    /// <summary>Runs a waiting focus once. An open modal keeps the activation's focus, and the
    /// focus keeps waiting.</summary>
    public void Activated(bool modalOpen, Action restore)
    {
        if (!Pending || modalOpen)
            return;
        Pending = false;
        restore();
    }

    /// <summary>A modal opened in the background takes the focus; its close restores the terminal.</summary>
    public void Cancel() => Pending = false;
}
