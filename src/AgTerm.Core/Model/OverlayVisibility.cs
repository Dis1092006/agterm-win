namespace AgTerm.Core.Model;

/// <summary>What session.overlay.hide|show|toggle asks of the scoped overlays (upstream
/// OverlayVisibilityMode).</summary>
public enum OverlayVisibilityMode
{
    Hide,
    Show,
    Toggle,
}

/// <summary>Why a hide, show or toggle of a session's overlays refused. Upstream also has a remote
/// case; the port has no remote sessions.</summary>
public enum OverlayVisibilityFailure
{
    UnknownSession,
    NoOverlay,
}
