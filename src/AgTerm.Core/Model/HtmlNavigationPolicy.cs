namespace AgTerm.Core.Model;

/// <summary>Where a navigation would land (upstream HtmlNavigationTarget).</summary>
public enum HtmlNavigationTarget { MainFrame, Subframe, NewWindow }

/// <summary>A navigation the page asks for: the URL, where it lands, and whether the user's input
/// started it.</summary>
public sealed record HtmlNavigationAction(Uri Url, HtmlNavigationTarget Target, bool UserActivated);

/// <summary>What the page does with a navigation. OpenExternal needs the user's confirmation,
/// because a page can fake a click.</summary>
public enum HtmlNavigationDecision { Allow, OpenExternal, Cancel }

/// <summary>Bounds file navigation by its grant and URL main-frame navigation by its origin; a
/// browsing page's main frame may go to any http or https origin (upstream HtmlNavigationPolicy,
/// c63e5158).</summary>
public static class HtmlNavigationPolicy
{
    public static HtmlNavigationDecision Decide(HtmlNavigationAction action, HtmlOverlay overlay)
    {
        var scheme = action.Url.IsAbsoluteUri ? action.Url.Scheme.ToLowerInvariant() : null;
        var web = scheme is "http" or "https";
        if (action.Target == HtmlNavigationTarget.NewWindow)
            return web && action.UserActivated ? HtmlNavigationDecision.OpenExternal : HtmlNavigationDecision.Cancel;
        if (scheme == "about") return HtmlNavigationDecision.Allow;
        switch (overlay.Source)
        {
            case HtmlSource.File file:
                if (web) return action.UserActivated ? HtmlNavigationDecision.OpenExternal : HtmlNavigationDecision.Cancel;
                if (scheme != "file" || file.GrantRoot is null) return HtmlNavigationDecision.Cancel;
                return HtmlOverlay.Contains(file.GrantRoot, action.Url.LocalPath)
                    ? HtmlNavigationDecision.Allow : HtmlNavigationDecision.Cancel;
            case HtmlSource.Url url:
                if (!web) return HtmlNavigationDecision.Cancel;
                if (HtmlSource.SameOrigin(action.Url, url.Address) || overlay.Browse) return HtmlNavigationDecision.Allow;
                if (action.Target == HtmlNavigationTarget.Subframe && !overlay.Chromeless) return HtmlNavigationDecision.Allow;
                return action.UserActivated ? HtmlNavigationDecision.OpenExternal : HtmlNavigationDecision.Cancel;
            default:
                return HtmlNavigationDecision.Cancel;
        }
    }
}