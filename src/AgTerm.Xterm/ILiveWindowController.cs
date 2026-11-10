using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record LiveWindowPresentation(
    ControlWindowFrame Geometry,
    bool Fullscreen,
    bool Zoomed,
    bool Minimized = false);

public sealed record LiveWindowMoveResult(bool Applied, int DisplayCount);

/// <summary>The frame size a resize applied, in integer points, read back after the clamp.</summary>
public sealed record LiveWindowSize(int Width, int Height);

public enum LiveWindowMinimizeStatus { NotOpen, FullScreen, Applied }

/// <summary>What a minimize request did. <see cref="Minimized"/> is the state the mode asked for.</summary>
public sealed record LiveWindowMinimizeResult(LiveWindowMinimizeStatus Status, bool Minimized);

public interface ILiveWindowController
{
    Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken);

    Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken);

    Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken);

    Task<LiveWindowPresentation?> CaptureWindowAsync(
        Guid windowId,
        CancellationToken cancellationToken);

    Task<int> GetDisplayCountAsync(CancellationToken cancellationToken);

    /// <summary>Returns the applied size, or null when no window is registered for the id.</summary>
    Task<LiveWindowSize?> ResizeWindowAsync(
        Guid windowId,
        int width,
        int height,
        CancellationToken cancellationToken);

    Task<LiveWindowMoveResult> MoveWindowAsync(
        Guid windowId,
        int x,
        int y,
        int? display,
        CancellationToken cancellationToken);

    Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken);

    Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken);

    /// <summary>Minimizes the window to the taskbar or restores it, without activating it. A window
    /// already in the asked state answers Applied; a full-screen one answers FullScreen.</summary>
    Task<LiveWindowMinimizeResult> MinimizeWindowAsync(
        Guid windowId,
        ControlToggleMode mode,
        CancellationToken cancellationToken);
}
