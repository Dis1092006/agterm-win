namespace AgTerm.Core.Model;

/// <summary>Routes the metrics the xterm page posts to the session it measured. The page does not
/// know pane roles: a hidden split with the right pane focused shows that pane alone, so a single
/// shown pane can carry either token, and the token decides the role its rect belongs to.</summary>
public static class HudMetricsTarget
{
    public static bool Apply(AppStore store, string paneToken, HudSessionMetrics metrics, HudRect? singlePane)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(metrics);
        if (string.IsNullOrEmpty(paneToken))
            return false;
        foreach (var session in store.Workspaces.SelectMany(workspace => workspace.Sessions))
        {
            if (session.Surface?.PaneToken == paneToken)
                return store.SetHudMetrics(session.Id, singlePane is { } left ? metrics with { Left = left } : metrics);
            if (session.SplitSurface?.PaneToken == paneToken)
                return singlePane is { } right && store.SetHudMetrics(session.Id, metrics with { Right = right });
        }
        return false;
    }
}
