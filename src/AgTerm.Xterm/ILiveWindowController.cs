using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record LiveWindowPresentation(
    ControlWindowFrame Geometry,
    bool Fullscreen,
    bool Zoomed);

public sealed record LiveWindowMoveResult(bool Applied, int DisplayCount);

public interface ILiveWindowController
{
    Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken);

    Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken);

    Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken);

    Task<LiveWindowPresentation?> CaptureWindowAsync(
        Guid windowId,
        CancellationToken cancellationToken);

    Task<int> GetDisplayCountAsync(CancellationToken cancellationToken);

    Task ResizeWindowAsync(
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
}
