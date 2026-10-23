namespace AgTerm.Core.Model;

public enum WindowShowState
{
    Normal,
    Maximized,
    Minimized,
    FullScreen,
}

/// <summary>Follows a window's moves and state changes to keep the frame it would save. A maximized,
/// minimized or full screen window reports bounds that are not its own, so only a normal window
/// updates the bounds; full screen is not restored and leaves the frame from before it.</summary>
public sealed class WindowFrameTracker
{
    private PixelRect? _normalBounds;
    private bool _maximized;
    private bool _enteringFullScreen;

    public SavedWindowFrame? Frame =>
        _normalBounds is { } bounds ? new SavedWindowFrame(bounds, _maximized) : null;

    /// <summary>AppWindow reports the full screen bounds while its presenter still looks normal, before
    /// the presenter change, so nothing counts until the window reports full screen.</summary>
    public void EnteringFullScreen() => _enteringFullScreen = true;

    public void FullScreenCancelled() => _enteringFullScreen = false;

    public void Observe(WindowShowState state, PixelRect bounds)
    {
        if (_enteringFullScreen)
        {
            if (state == WindowShowState.FullScreen)
                _enteringFullScreen = false;
            return;
        }

        switch (state)
        {
            case WindowShowState.Normal:
                _normalBounds = bounds;
                _maximized = false;
                break;
            case WindowShowState.Maximized:
                _maximized = true;
                break;
        }
    }
}
