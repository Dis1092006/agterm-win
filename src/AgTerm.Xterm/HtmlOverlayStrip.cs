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

    /// <summary>The widths the identity and the title get in a label of the given width, from their
    /// natural widths. The title gives up width first (upstream layoutPriority) but keeps its minimum,
    /// room for an ellipsis, while the identity is cut. The separator is the dot with its spacing.</summary>
    public static (double Identity, double Title) LabelWidths(
        double available, double identity, double? title, double separator, double titleMinimum)
    {
        if (title is not { } natural) return (Math.Clamp(identity, 0, Math.Max(0, available)), 0);
        var reserve = separator + Math.Min(natural, titleMinimum);
        var identityWidth = Math.Min(identity, Math.Max(0, available - reserve));
        return (identityWidth, Math.Min(natural, Math.Max(0, available - identityWidth - separator)));
    }
}
