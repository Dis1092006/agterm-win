using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// What a page's panel draws above it (upstream HtmlOverlayView, c63e5158): the identity strip with
/// its buttons, and the failure view over a page that could not load. The app builds it from the live
/// page's own report, which is newer than the overlay copy in the snapshot.
/// </summary>
/// <param name="Shown">Whether the strip is drawn; a chromeless page has none.</param>
/// <param name="Navigation">Whether back, forward, reload, browser and Explorer show; close always does.</param>
/// <param name="Identity">The file shown, named by the app. The title is the page's own text and stays apart.</param>
/// <param name="IdentityTip">The full path of the page shown.</param>
public sealed record HtmlOverlayStrip(bool Shown, bool Navigation, bool CanGoBack, bool CanGoForward,
                                      string Identity, string IdentityTip, string? Title, bool Failed, string? Error)
{
    public static HtmlOverlayStrip From(HtmlOverlay overlay, HtmlLoadState state, string? error, HtmlPageInfo? current) =>
        new(!overlay.Chromeless, overlay.Navigation, current?.CanGoBack == true, current?.CanGoForward == true,
            overlay.IdentityAt(current), overlay.PageAt(current), current?.Title,
            state == HtmlLoadState.Failed, error);

    /// <summary>The panel's place in an area of the given size: all of it for a full page, else centered at
    /// the size percent, with point axes and the strip as OverlayPointSize.PanelSize measures them.</summary>
    public static (double X, double Y, double Width, double Height) PanelFrame(
        (double Width, double Height) bounds, int? sizePercent, OverlayPointSize points, double chrome)
    {
        if (sizePercent is not { } percent) return (0, 0, bounds.Width, bounds.Height);
        var (width, height) = OverlayPointSize.PanelSize(bounds, percent, points, chrome);
        return ((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
    }
}
